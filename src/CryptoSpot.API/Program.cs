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
using CryptoSpot.MatchEngine;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

builder.Services.AddCleanArchitecture();

builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<IDatabaseCoordinator, DatabaseCoordinator>();

builder.Services.AddInfrastructureServices();

builder.Services.AddSingleton<CryptoSpot.MatchEngine.ChannelMatchEngineService>();
builder.Services.AddSingleton<CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService>(
    provider => provider.GetRequiredService<CryptoSpot.MatchEngine.ChannelMatchEngineService>());

builder.Services.AddScoped<IRealTimeDataPushService,SignalRDataPushService>();

builder.Services.AddSingleton<IMarketDataStreamProvider, OkxMarketDataStreamProvider>();
builder.Services.AddScoped<IAutoTradingService, AutoTradingLogicService>();

builder.Services.AddSingleton<InMemoryAssetStore>();
builder.Services.AddHostedService<CryptoSpot.API.Services.MatchEngineInitializationService>();

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

