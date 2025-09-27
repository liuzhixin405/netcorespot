using CryptoSpot.Domain.Entities;
// removed old Core interface usings
using CryptoSpot.Application.Abstractions.MarketData; // IKLineDataService, IPriceDataService
using CryptoSpot.Application.Abstractions.Trading;   // trading abstractions
using CryptoSpot.Application.Abstractions.Users;
using CryptoSpot.Application.Abstractions.Auth;
using CryptoSpot.Application.Abstractions.Repositories; // IDatabaseCoordinator
using CryptoSpot.Application.Abstractions.Caching; // ICacheService, ICacheEventService
using CryptoSpot.Application.Abstractions.RealTime; // IRealTimeDataPushService
using CryptoSpot.Infrastructure.ExternalServices;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.Application.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;
using CryptoSpot.API.Services;
using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;
using CryptoSpot.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// 配置日志记录，避免EventLog问题
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
builder.Services.AddControllers(options => { })
    .AddJsonOptions(o =>
    {
        // 允许前端传递小写/驼峰枚举 (buy/sell, limit/market)
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        // 兼容旧格式: 额外开启大小写不敏感
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 移除直接 AddDbContextPool<ApplicationDbContext> (旧 Infrastructure) 改为 Persistence 封装
// Database - 使用连接池以处理高并发 由 AddPersistence 负责
builder.Services.AddPersistence(builder.Configuration);

// 添加 Redis (开发环境本地) - 放在数据库之后
builder.Services.AddRedis(builder.Configuration.GetSection("Redis"));

// Add Clean Architecture services (必须在数据库配置之后)
builder.Services.AddCleanArchitecture();

// Repository Layer - 已在Application层注册，这里只注册Infrastructure特有的服务

// Database Coordinator (Singleton for thread safety)
builder.Services.AddSingleton<IDatabaseCoordinator, DatabaseCoordinator>();

// Cache Services (Singleton for global cache management)
builder.Services.AddSingleton<ICacheEventService, CacheEventService>();
builder.Services.AddSingleton<ICacheService, CacheService>();

// Infrastructure Services (Data Access & External Services)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPriceDataService, PriceDataService>();
builder.Services.AddScoped<IKLineDataService, KLineDataService>();
builder.Services.AddScoped<ITradingPairService, TradingPairService>();
// builder.Services.AddScoped<IOrderService, OrderService>(); // 已由 AddCleanArchitecture 注册 RefactoredOrderService
builder.Services.AddScoped<IAssetService, AssetService>();
// 移除对 ITradeService 的覆盖注册, 以使用 CleanArchitecture 中的 RefactoredTradeService
// builder.Services.AddScoped<ITradeService, TradeService>();

// Data Initialization Service
builder.Services.AddScoped<DataInitializationService>();

// Application Services (Business Logic)
// ITradingService is registered in ServiceCollectionExtensions.cs
builder.Services.AddScoped<IOrderMatchingEngine, OrderMatchingEngine>();

// Use Cases
builder.Services.AddScoped<CryptoSpot.Application.UseCases.Auth.LoginUseCase>();
builder.Services.AddScoped<CryptoSpot.Application.UseCases.Auth.RegisterUseCase>();

// SignalR Data Push Service
builder.Services.AddScoped<IRealTimeDataPushService,SignalRDataPushService>();
// 订单簿快照缓存 (内存) 后续可替换为 Redis
builder.Services.AddSingleton<IOrderBookSnapshotCache, OrderBookSnapshotCache>();

// Business Services
//builder.Services.AddScoped<IMarketDataProvider, BinanceMarketDataProvider>();
// 新增 OKX WebSocket 行情流
builder.Services.AddSingleton<IMarketDataStreamProvider, OkxMarketDataStreamProvider>();
builder.Services.AddScoped<IAutoTradingService, AutoTradingLogicService>();

// Background Services
builder.Services.AddHostedService<CacheInitializationService>();
builder.Services.AddHostedService<AutoTradingService>();
//builder.Services.AddHostedService<OrderBookPushService>(); // 已由外部流式+增量推送接管, 关闭周期性全量快照避免前端闪烁
//builder.Services.AddHostedService<MarketDataSyncService>();
builder.Services.AddHostedService<MarketDataStreamRelayService>();

builder.Services.AddMemoryCache();

// HttpClient for Binance API with proxy support
builder.Services.AddHttpClient<BinanceMarketDataProvider>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var proxyUrl = configuration["Binance:ProxyUrl"];
    
    // 设置超时 - 增加到60秒处理慢响应
    client.Timeout = TimeSpan.FromSeconds(60);
    
    if (!string.IsNullOrEmpty(proxyUrl))
    {
        Console.WriteLine($"Configuring Binance API proxy: {proxyUrl}");
    }
}).ConfigurePrimaryHttpMessageHandler((serviceProvider) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var proxyUrl = configuration["Binance:ProxyUrl"];
    
    var handler = new HttpClientHandler();
    
    if (!string.IsNullOrEmpty(proxyUrl))
    {
        try
        {
            var proxy = new WebProxy(proxyUrl);
            handler.Proxy = proxy;
            handler.UseProxy = true;
            Console.WriteLine($"✅ Proxy configured successfully: {proxyUrl}");
        }
        catch (Exception ex)
        {
            // 如果代理配置失败，记录错误但继续使用默认配置
            Console.WriteLine($"❌ Failed to configure proxy: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("ℹ️ No proxy configured, using direct connection");
    }
    
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
// Add SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection(); // Commented out for HTTP development

// 在开发环境使用 AllowAllWithCredentials 策略，生产环境使用 AllowReactApp 策略

    app.UseCors("AllowReactApp");

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Map SignalR Hub
app.MapHub<CryptoSpot.API.Hubs.TradingHub>("/tradingHub");

//Db and DataInit
await app.Services.InitDbContext();


await app.RunAsync();

