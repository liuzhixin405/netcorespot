using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Interfaces.Auth;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Interfaces;
using CryptoSpot.Core.Interfaces.Caching;
using CryptoSpot.Infrastructure.Data;
using CryptoSpot.Infrastructure.ExternalServices;
using CryptoSpot.Infrastructure.Repositories;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.Application.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;
using CryptoSpot.API.Services;
using CryptoSpot.Application.DependencyInjection;
using Common.Redis.Extensions; // added for Redis

var builder = WebApplication.CreateBuilder(args);

// 配置日志记录，避免EventLog问题
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database - 使用连接池以处理高并发
builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
{
    options.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"), ServerVersion.Parse("8.0"), mysqlOptions =>
    {
        mysqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
        mysqlOptions.CommandTimeout(60);
    });
    options.EnableThreadSafetyChecks(false); // 禁用线程安全检查
    // options.EnableServiceProviderCaching(false); // 注释掉，使用默认缓存
}, poolSize: 20); // 适中的连接池大小 

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



// Ensure database is created and up-to-date
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        // First try to create database if it doesn't exist
        context.Database.EnsureCreated();
        Console.WriteLine("✅ Database schema created/verified successfully");
        
        // Test connection by querying a simple table
        var userCount = await context.Users.CountAsync();
        Console.WriteLine($"📊 Current user count: {userCount}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Database setup failed: {ex.Message}");
        // Don't throw - let the app continue and fail gracefully on first DB operation
    }
}

// Initialize data
try
{
    using (var scope = app.Services.CreateScope())
    {
        var dataInitService = scope.ServiceProvider.GetRequiredService<DataInitializationService>();
        if (await dataInitService.NeedsInitializationAsync())
        {
            await dataInitService.InitializeDataAsync();
            Console.WriteLine("✅ Data initialization completed");
        }
        else
        {
            Console.WriteLine("✅ Data already initialized");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Data initialization failed: {ex.Message}");
    // Don't throw - let the app continue
}

app.Run();

