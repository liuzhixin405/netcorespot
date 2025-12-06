using System.Text.Json;

namespace CryptoSpot.MatchEngine.Services;

/// <summary>
/// API 服务客户端 - 用于撮合引擎调用主 API 服务
/// </summary>
public class ApiServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiServiceClient> _logger;

    public ApiServiceClient(HttpClient httpClient, ILogger<ApiServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有活跃交易对
    /// </summary>
    public async Task<List<TradingPairInfo>> GetActiveTradingPairsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/internal/trading-pairs");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ApiResponse<List<TradingPairInfo>>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result?.Data ?? new List<TradingPairInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取交易对失败");
            return new List<TradingPairInfo>();
        }
    }

    /// <summary>
    /// 获取所有用户资产
    /// </summary>
    public async Task<List<AssetInfo>> GetAllUserAssetsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/internal/assets");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ApiResponse<List<AssetInfo>>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result?.Data ?? new List<AssetInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取用户资产失败");
            return new List<AssetInfo>();
        }
    }

    /// <summary>
    /// 推送资产变更到 API 服务（批量）
    /// </summary>
    public async Task<bool> PushAssetUpdatesAsync(List<AssetUpdateInfo> updates)
    {
        try
        {
            var json = JsonSerializer.Serialize(updates);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/internal/assets/batch-update", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("✅ 成功推送 {Count} 条资产更新", updates.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推送资产更新失败");
            return false;
        }
    }
}

// DTO 类
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
}

public class TradingPairInfo
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string BaseAsset { get; set; } = string.Empty;
    public string QuoteAsset { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class AssetInfo
{
    public long UserId { get; set; }
    public string Asset { get; set; } = string.Empty;
    public decimal Available { get; set; }
    public decimal Frozen { get; set; }
}

public class AssetUpdateInfo
{
    public long UserId { get; set; }
    public string Asset { get; set; } = string.Empty;
    public decimal Available { get; set; }
    public decimal Frozen { get; set; }
}
