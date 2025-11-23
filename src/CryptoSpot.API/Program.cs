using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Infrastructure.Extensions;
using CryptoSpot.Application.Abstractions.Services.Auth;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.DependencyInjection;
using CryptoSpot.Infrastructure;
using CryptoSpot.Infrastructure.BgService;
using CryptoSpot.Infrastructure.BgServices;
using CryptoSpot.Infrastructure.ExternalServices;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.API.Middleware;
using CryptoSpot.Redis;
using CryptoSpot.Redis.Configuration;
using CryptoSpot.Redis.Serializer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers(options => { })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddPersistence(builder.Configuration);

builder.Services.AddRedis(builder.Configuration.GetSection("Redis"));

builder.Services.AddCleanArchitecture();

builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<IDatabaseCoordinator, DatabaseCoordinator>();

builder.Services.AddSingleton<IRedisCache>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var logger = provider.GetRequiredService<ILogger<RedisCache>>();
    var serializer = new CryptoSpot.Redis.Serializer.JsonSerializer(null);
    var redisConfig = new CryptoSpot.Redis.Configuration.RedisConfiguration
    {
        Hosts = connectionString.Split(',').Select(c => c.Split(':')).Where(c => c.Length == 2)
            .Select(c => new CryptoSpot.Redis.Configuration.RedisHost { Host = c[0], Port = int.Parse(c[1]) }).ToArray()
    };
    var connection = ConnectionMultiplexer.Connect(redisConfig.ConfigurationOptions);
    return new CryptoSpot.Redis.RedisCache(logger, connection, redisConfig, serializer);
});
builder.Services.AddSingleton<IRedisService>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var redisConfig = new CryptoSpot.Redis.Configuration.RedisConfiguration
    {
        Hosts = connectionString.Split(',').Select(c => c.Split(':')).Where(c => c.Length == 2)
           .Select(c => new CryptoSpot.Redis.Configuration.RedisHost { Host = c[0], Port = int.Parse(c[1]) }).ToArray()
    };
    var serializer = new MsgPackSerializer();
    var connection = ConnectionMultiplexer.Connect(redisConfig.ConfigurationOptions);
    return new RedisService(provider.GetRequiredService<ILogger<RedisService>>(), connection, redisConfig, serializer);
});
builder.Services.AddSingleton<RedisCacheService>();
builder.Services.AddInfrastructureServices();

builder.Services.AddScoped<CryptoSpot.Application.Abstractions.Services.Trading.IOrderMatchingEngine, CryptoSpot.Infrastructure.Services.MatchEngineAdapter>();

builder.Services.AddScoped<CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService, CryptoSpot.API.Services.ApiOrderPublisherService>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPriceDataService, PriceDataService>();

builder.Services.AddScoped<IRealTimeDataPushService,SignalRDataPushService>();
builder.Services.AddSingleton<IOrderBookSnapshotCache, OrderBookSnapshotCache>();

builder.Services.AddSingleton<IMarketDataStreamProvider, OkxMarketDataStreamProvider>();
builder.Services.AddScoped<IAutoTradingService, AutoTradingLogicService>();

builder.Services.AddHostedService<AutoTradingService>();
builder.Services.AddHostedService<AssetFlushBackgroundService>();
builder.Services.AddHostedService<MarketDataStreamRelayService>();
builder.Services.AddHostedService<CryptoSpot.Infrastructure.Services.CacheFlushHostedService>();

builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<BinanceMarketDataProvider>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var proxyUrl = configuration["Binance:ProxyUrl"];
    client.Timeout = TimeSpan.FromSeconds(60);
    if (!string.IsNullOrEmpty(proxyUrl)) { Console.WriteLine($"Configuring Binance API proxy: {proxyUrl}"); }
}).ConfigurePrimaryHttpMessageHandler((serviceProvider) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var proxyUrl = configuration["Binance:ProxyUrl"];
    var handler = new HttpClientHandler();
    if (!string.IsNullOrEmpty(proxyUrl))
    {
        try { var proxy = new WebProxy(proxyUrl); handler.Proxy = proxy; handler.UseProxy = true; Console.WriteLine($"✅ Proxy configured successfully: {proxyUrl}"); }
        catch (Exception ex) { Console.WriteLine($"❌ Failed to configure proxy: {ex.Message}"); }
    }
    else { Console.WriteLine("ℹ️ No proxy configured, using direct connection"); }
    return handler;
});

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

builder.Services.AddSignalR(options => { options.EnableDetailedErrors = true; });

builder.Services.AddCryptoSpotHealthChecks(builder.Configuration);

var app = builder.Build();

// ✅ 全局异常处理中间件（必须在最前面）
app.UseGlobalExceptionHandling();

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<CryptoSpot.Infrastructure.Hubs.TradingHub>("/tradingHub");
app.MapCryptoSpotHealthChecks();

// 初始化数据库
await app.Services.InitDbContext();

// 启动健康检查验证
await app.PerformStartupHealthChecks(builder.Configuration);

await app.RunAsync();

