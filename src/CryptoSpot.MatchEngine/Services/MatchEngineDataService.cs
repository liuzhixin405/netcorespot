namespace CryptoSpot.MatchEngine.Services;

/// <summary>
/// 撮合引擎数据持久化服务
/// 启动时从 API 服务加载数据，关闭时推送数据到 API 服务
/// </summary>
public class MatchEngineDataService : IHostedService
{
    private readonly InMemoryAssetStore _assetStore;
    private readonly ApiServiceClient _apiClient;
    private readonly ILogger<MatchEngineDataService> _logger;

    public MatchEngineDataService(
        InMemoryAssetStore assetStore,
        ApiServiceClient apiClient,
        ILogger<MatchEngineDataService> logger)
    {
        _assetStore = assetStore;
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("⏳ 撮合引擎数据服务启动中...");

        try
        {
            // 1. 从 API 服务获取活跃交易对
            var tradingPairs = await _apiClient.GetActiveTradingPairsAsync();
            _logger.LogInformation("✅ 从 API 加载了 {Count} 个活跃交易对", tradingPairs.Count);

            // 2. 从 API 服务获取所有用户资产并加载到内存
            var allAssets = await _apiClient.GetAllUserAssetsAsync();
            foreach (var asset in allAssets)
            {
                // 初始化时,Available 就是总余额,Frozen 另外记录
                await _assetStore.InitializeBalanceAsync(asset.UserId, asset.Asset, asset.Available);
                
                // 如果有冻结余额,需要从 Available 中扣除并添加到 Frozen
                if (asset.Frozen > 0)
                {
                    await _assetStore.FreezeAssetAsync(asset.UserId, asset.Asset, asset.Frozen);
                }
            }
            _logger.LogInformation("✅ 从 API 加载了 {Count} 条用户资产记录", allAssets.Count);

            _logger.LogInformation("✅ 撮合引擎数据加载完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 撮合引擎数据加载失败");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("⏳ 撮合引擎正在关闭，推送数据到 API 服务中...");

        try
        {
            // 获取所有内存中的资产
            var allBalances = _assetStore.GetAllBalances().ToList();
            _logger.LogInformation("⏳ 准备推送 {Count} 条资产记录", allBalances.Count);

            // 转换为 API 格式并批量推送
            var updates = allBalances.Select(balance => new AssetUpdateInfo
            {
                UserId = balance.UserId,
                Asset = balance.Currency,
                Available = balance.Available,
                Frozen = balance.Frozen
            }).ToList();

            var success = await _apiClient.PushAssetUpdatesAsync(updates);
            
            if (success)
            {
                _logger.LogInformation("✅ 成功推送所有资产更新到 API 服务");
            }
            else
            {
                _logger.LogWarning("⚠️ 推送资产更新失败，数据可能未保存");
            }

            // 清空内存
            _assetStore.Clear();
            _logger.LogInformation("✅ 内存数据已清空");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 推送数据到 API 服务失败");
        }

        _logger.LogInformation("✅ 撮合引擎已关闭");
    }
}
