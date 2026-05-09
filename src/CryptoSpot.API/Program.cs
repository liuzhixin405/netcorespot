using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Infrastructure;
using CryptoSpot.Infrastructure.BackgroundServices;
using CryptoSpot.Infrastructure.ExternalServices;
using CryptoSpot.Infrastructure.Extensions;
using CryptoSpot.Infrastructure.Identity;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.API.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ==================== 日志配置 ====================
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// ==================== 控制器配置 ====================
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        o.JsonSerializerOptions.Converters.Add(new LongToStringJsonConverter());
        o.JsonSerializerOptions.Converters.Add(new NullableLongToStringJsonConverter());
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ==================== Infrastructure 层服务 ====================
builder.Services.AddInfrastructure(builder.Configuration);

// ==================== Application 层服务 ====================
builder.Services.AddCleanArchitecture();

// ==================== HTTP 服务 ====================
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

// 数据库协调器
builder.Services.AddSingleton<IDatabaseCoordinator, DatabaseCoordinator>();

// 强类型配置绑定
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));

// 实时数据服务与市场数据流由 Infrastructure 层统一注册

// Binance 市场数据提供者（带代理支持 + Resilience）
builder.Services.AddHttpClient<BinanceMarketDataProvider>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var proxyUrl = config["Binance:ProxyUrl"];
    client.Timeout = TimeSpan.FromSeconds(60);
    if (!string.IsNullOrEmpty(proxyUrl))
        Console.WriteLine($"Configuring Binance API proxy: {proxyUrl}");
}).ConfigurePrimaryHttpMessageHandler(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var proxyUrl = config["Binance:ProxyUrl"];
    var handler = new HttpClientHandler();
    if (!string.IsNullOrEmpty(proxyUrl))
    {
        try
        {
            handler.Proxy = new WebProxy(proxyUrl);
            handler.UseProxy = true;
            Console.WriteLine($"✅ Proxy configured: {proxyUrl}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Proxy configuration failed: {ex.Message}");
        }
    }
    return handler;
}).AddStandardResilienceHandler();

// ==================== 速率限制 ====================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", config =>
    {
        config.PermitLimit = 5;
        config.Window = TimeSpan.FromMinutes(1);
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });
});

// ==================== 认证与授权 ====================
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"]
    ?? throw new InvalidOperationException(
        "JWT SecretKey is not configured. Set 'JwtSettings:SecretKey' in configuration.");
var issuer = jwtSettings["Issuer"] ?? "CryptoSpot";
var audience = jwtSettings["Audience"] ?? "CryptoSpotUsers";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
        };
    });

builder.Services.AddAuthorization();

// ==================== CORS 配置 ====================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:3001")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ==================== SignalR 配置 ====================
builder.Services.AddSignalR(options => options.EnableDetailedErrors = true);

// ==================== 健康检查 ====================
builder.Services.AddCryptoSpotHealthChecks(builder.Configuration);

// ==================== 构建应用 ====================
var app = builder.Build();

// 全局异常处理中间件（必须在最前面）
app.UseGlobalExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<CryptoSpot.Infrastructure.Hubs.TradingHub>("/tradingHub");
app.MapCryptoSpotHealthChecks();

// ==================== 初始化与启动 ====================
await app.Services.InitDbContext();
await app.PerformStartupHealthChecks(builder.Configuration);
await app.RunAsync();

public sealed class LongToStringJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String when long.TryParse(reader.GetString(), out var value) => value,
            JsonTokenType.Number => reader.GetInt64(),
            _ => throw new JsonException("Expected a long value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

public sealed class NullableLongToStringJsonConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        return reader.TokenType switch
        {
            JsonTokenType.String when long.TryParse(reader.GetString(), out var value) => value,
            JsonTokenType.Number => reader.GetInt64(),
            _ => throw new JsonException("Expected a nullable long value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString());
            return;
        }

        writer.WriteNullValue();
    }
}
