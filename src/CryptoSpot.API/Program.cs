using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Infrastructure;
using CryptoSpot.Infrastructure.BackgroundServices;
using CryptoSpot.Infrastructure.ExternalServices;
using CryptoSpot.Infrastructure.Extensions;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.API.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

// 撮合引擎 HTTP 客户端
builder.Services.AddHttpClient<HttpMatchEngineClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var matchEngineUrl = config["MatchEngine:BaseUrl"] ?? "http://localhost:5001";
    client.BaseAddress = new Uri(matchEngineUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ==================== 实时数据服务 ====================
builder.Services.AddScoped<IRealTimeDataPushService, SignalRDataPushService>();
builder.Services.AddSingleton<IMarketDataStreamProvider, OkxMarketDataStreamProvider>();
builder.Services.AddScoped<IAutoTradingService, AutoTradingLogicService>();

// Binance 市场数据提供者（带代理支持）
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
});

// ==================== 认证与授权 ====================
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";
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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<CryptoSpot.Infrastructure.Hubs.TradingHub>("/tradingHub");
app.MapCryptoSpotHealthChecks();

// ==================== 初始化与启动 ====================
await app.Services.InitDbContext();
await app.PerformStartupHealthChecks(builder.Configuration);
await app.RunAsync();