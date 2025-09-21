using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Interfaces.Auth;
using CryptoSpot.Core.Interfaces.System;
using CryptoSpot.Core.Interfaces.Repositories;
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
using CryptoSpot.Core.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();



// Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"), 
        new MySqlServerVersion(new Version(8, 0, 0))));

// Repository Layer
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITradingPairRepository, TradingPairRepository>();
builder.Services.AddScoped<IKLineDataRepository, KLineDataRepository>();

// Infrastructure Services (Data Access & External Services)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPriceDataService, PriceDataService>();
builder.Services.AddScoped<IKLineDataService, KLineDataService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<ITradeService, TradeService>();
builder.Services.AddScoped<ISystemAssetService, SystemAssetService>();
builder.Services.AddScoped<ISystemAccountService, SystemAccountService>();

// Application Services (Business Logic)
builder.Services.AddScoped<ITradingService, TradingService>();
builder.Services.AddScoped<IOrderMatchingEngine, OrderMatchingEngine>();


// SignalR Data Push Service
builder.Services.AddScoped<IRealTimeDataPushService,SignalRDataPushService>();

// Business Services
builder.Services.AddScoped<IMarketDataProvider, BinanceMarketDataProvider>();
builder.Services.AddScoped<IAutoTradingService, AutoTradingLogicService>();

// Background Services
builder.Services.AddHostedService<AutoTradingService>();
builder.Services.AddHostedService<BinanceService>();
builder.Services.AddHostedService<OrderBookPushService>();



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
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              //.AllowAnyOrigin();
              .AllowCredentials();
              //.WithExposedHeaders("Content-Type");
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

app.Run();

