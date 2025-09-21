using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Interfaces.Auth;
using CryptoSpot.Core.Interfaces.System;
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

var builder = WebApplication.CreateBuilder(args);

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
    options.EnableServiceProviderCaching(false); // 禁用服务提供者缓存
}, poolSize: 100); // 增加连接池大小到100 
// Repository Layer
builder.Services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddTransient<IUserRepository, UserRepository>();
builder.Services.AddTransient<ITradingPairRepository, TradingPairRepository>();
builder.Services.AddTransient<IKLineDataRepository, KLineDataRepository>();

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
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<ITradeService, TradeService>();
builder.Services.AddScoped<ISystemAssetService, SystemAssetService>();
builder.Services.AddScoped<ISystemAccountService, SystemAccountService>();

// Data Initialization Service
builder.Services.AddScoped<DataInitializationService>();

// Application Services (Business Logic)
builder.Services.AddScoped<ITradingService, TradingService>();
builder.Services.AddScoped<IOrderMatchingEngine, OrderMatchingEngine>();

// Use Cases
builder.Services.AddScoped<CryptoSpot.Application.UseCases.Auth.LoginUseCase>();
builder.Services.AddScoped<CryptoSpot.Application.UseCases.Auth.RegisterUseCase>();


// SignalR Data Push Service
builder.Services.AddScoped<IRealTimeDataPushService,SignalRDataPushService>();

// Business Services
builder.Services.AddScoped<IMarketDataProvider, BinanceMarketDataProvider>();
builder.Services.AddScoped<IAutoTradingService, AutoTradingLogicService>();

// Background Services
builder.Services.AddHostedService<CacheInitializationService>();
builder.Services.AddHostedService<AutoTradingService>();
builder.Services.AddHostedService<OrderBookPushService>();
builder.Services.AddHostedService<MarketDataSyncService>();
builder.Services.AddHostedService<DatabaseHealthService>();



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

