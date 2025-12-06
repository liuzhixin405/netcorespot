using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CryptoSpot.Infrastructure.Services;

/// <summary>
/// 撮合引擎 HTTP 客户端
/// </summary>
public class HttpMatchEngineClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpMatchEngineClient> _logger;
    private readonly string _baseUrl;

    public HttpMatchEngineClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<HttpMatchEngineClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration["MatchEngine:BaseUrl"] ?? "http://localhost:5001";
        
        // BaseAddress 和 Timeout 已在 AddHttpClient 配置中设置
        // 如果未设置，则使用默认值
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
    }

    /// <summary>
    /// 提交订单到撮合引擎
    /// </summary>
    public async Task<MatchOrderResult> SubmitOrderAsync(
        long userId,
        string symbol,
        string side,
        string type,
        decimal price,
        decimal quantity)
    {
        try
        {
            var request = new
            {
                userId,
                symbol,
                side,
                type,
                price,
                quantity
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/match/orders", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MatchEngineResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Success == true && result.Data != null)
            {
                return new MatchOrderResult
                {
                    OrderId = result.Data.OrderId,
                    Symbol = result.Data.Symbol,
                    Status = result.Data.Status,
                    ExecutedQuantity = result.Data.ExecutedQuantity,
                    Trades = result.Data.Trades?.Select(t => new TradeInfo
                    {
                        Id = t.TradeId,
                        Price = t.Price,
                        Quantity = t.Quantity,
                        BuyOrderId = t.BuyOrderId,
                        SellOrderId = t.SellOrderId,
                        Timestamp = t.Timestamp
                    }).ToList()
                };
            }

            throw new Exception($"撮合引擎返回错误: {result?.Error ?? "未知错误"}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用撮合引擎失败: Symbol={Symbol}, Side={Side}", symbol, side);
            throw;
        }
    }

    /// <summary>
    /// 取消订单
    /// </summary>
    public async Task<bool> CancelOrderAsync(long orderId, long userId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/match/orders/{orderId}?userId={userId}");
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MatchEngineCancelResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result?.Success == true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消订单失败: OrderId={OrderId}", orderId);
            return false;
        }
    }

    /// <summary>
    /// 查询用户资产
    /// </summary>
    public async Task<List<AssetBalance>> GetUserAssetsAsync(long userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/match/assets/{userId}");
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MatchEngineAssetsResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result?.Data?.Select(a => new AssetBalance
            {
                Asset = a.Asset,
                Available = a.Available,
                Frozen = a.Frozen
            }).ToList() ?? new List<AssetBalance>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询用户资产失败: UserId={UserId}", userId);
            return new List<AssetBalance>();
        }
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/match/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

// DTO 类
public class MatchEngineResponse
{
    public bool Success { get; set; }
    public MatchOrderData? Data { get; set; }
    public string? Error { get; set; }
}

public class MatchOrderData
{
    public long OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal ExecutedQuantity { get; set; }
    public List<TradeData>? Trades { get; set; }
}

public class TradeData
{
    public long TradeId { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public long BuyOrderId { get; set; }
    public long SellOrderId { get; set; }
    public long Timestamp { get; set; }
}

public class MatchEngineCancelResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class MatchEngineAssetsResponse
{
    public bool Success { get; set; }
    public List<AssetData>? Data { get; set; }
}

public class AssetData
{
    public string Asset { get; set; } = string.Empty;
    public decimal Available { get; set; }
    public decimal Frozen { get; set; }
    public decimal Total { get; set; }
}

public class MatchOrderResult
{
    public long OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal ExecutedQuantity { get; set; }
    public List<TradeInfo>? Trades { get; set; }
}

public class TradeInfo
{
    public long Id { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public long BuyOrderId { get; set; }
    public long SellOrderId { get; set; }
    public long Timestamp { get; set; }
}

public class AssetBalance
{
    public string Asset { get; set; } = string.Empty;
    public decimal Available { get; set; }
    public decimal Frozen { get; set; }
}
