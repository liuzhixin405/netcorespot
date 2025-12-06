using CryptoSpot.MatchEngine.Core;
using CryptoSpot.MatchEngine.Services;

var builder = WebApplication.CreateBuilder(args);

// ==================== 服务配置 ====================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CryptoSpot MatchEngine API", Version = "v1" });
});

// ==================== HTTP 客户端 ====================
builder.Services.AddHttpClient<ApiServiceClient>(client =>
{
    var apiBaseUrl = builder.Configuration["ApiService:BaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ==================== 撮合引擎核心服务 ====================
builder.Services.AddSingleton<InMemoryMatchEngineService>();
builder.Services.AddSingleton<InMemoryAssetStore>();
builder.Services.AddSingleton<IMatchingAlgorithm, PriceTimePriorityMatchingAlgorithm>();
builder.Services.AddSingleton<ITradingPairParser, TradingPairParserService>();
builder.Services.AddSingleton<System.Collections.Concurrent.ConcurrentDictionary<string, IOrderBook>>();

// ==================== 后台服务 ====================
builder.Services.AddHostedService<MatchEngineDataService>();

var app = builder.Build();

// ==================== HTTP 管道配置 ====================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
