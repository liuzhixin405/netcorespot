// removed old Core interface usings
using CryptoSpot.Application.Abstractions.Repositories; // IDatabaseCoordinator
using CryptoSpot.Application.Abstractions.Services.Auth;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure;
using CryptoSpot.Infrastructure.BgService;
using CryptoSpot.Infrastructure.BgServices;
using CryptoSpot.Infrastructure.ExternalServices;
using CryptoSpot.Infrastructure.Services;
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

// 配置日志记录，避免EventLog问题
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
builder.Services.AddControllers(options => { })
    .AddJsonOptions(o =>
    {
        // 统一输出/输入字段为 camelCase，便于前端直接使用 data.token / data.user.username
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        // 允许前端传递小写/驼峰枚举 (buy/sell, limit/market)
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        // 兼容旧格式: 额外开启大小写不敏感读取（仅影响反序列化）
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database & Persistence
builder.Services.AddPersistence(builder.Configuration);

// 添加 Redis
builder.Services.AddRedis(builder.Configuration.GetSection("Redis"));

// Clean Architecture (命令总线 / 匹配引擎 / 映射)
builder.Services.AddCleanArchitecture();

// Database Coordinator (Singleton)
builder.Services.AddSingleton<IDatabaseCoordinator, DatabaseCoordinator>();

// Redis Cache Service (Singleton)
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
    var connection = new PooledConnectionMultiplexer(redisConfig.ConfigurationOptions);
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
    var connection = new PooledConnectionMultiplexer(redisConfig.ConfigurationOptions);
    return new RedisService(provider.GetRequiredService<ILogger<RedisService>>(), connection, redisConfig, serializer);
});
builder.Services.AddSingleton<RedisCacheService>();

// 已在 AddPersistence 中统一注册的服务此处不再重复注册 (ITradingPairService / IKLineDataService / ITradingService / IOrderService / ITradeService / IAssetService / IUserService)
// 仅补充未在 AddPersistence 中的额外服务
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPriceDataService, PriceDataService>(); // 价格聚合 (依赖 TradingPairService)

// Redis-First 架构：撮合引擎注册
builder.Services.AddSingleton<RedisOrderMatchingEngine>();
builder.Services.AddScoped<IOrderMatchingEngine, RedisOrderMatchingEngineAdapter>();

// 实时推送与缓存
builder.Services.AddScoped<IRealTimeDataPushService,SignalRDataPushService>();
builder.Services.AddSingleton<IOrderBookSnapshotCache, OrderBookSnapshotCache>();

// 行情流 Provider & 自动交易
builder.Services.AddSingleton<IMarketDataStreamProvider, OkxMarketDataStreamProvider>();
builder.Services.AddScoped<IAutoTradingService, AutoTradingLogicService>();

// Background Services
builder.Services.AddHostedService<AutoTradingService>();
builder.Services.AddHostedService<AssetFlushBackgroundService>();
builder.Services.AddHostedService<MarketDataStreamRelayService>();

builder.Services.AddMemoryCache();

// HttpClient for Binance API with proxy support
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

// JWT Authentication
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

// CORS
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

// SignalR
builder.Services.AddSignalR(options => { options.EnableDetailedErrors = true; });

var app = builder.Build();

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<CryptoSpot.Infrastructure.Hubs.TradingHub>("/tradingHub");
await app.Services.InitDbContext();
await app.RunAsync();

