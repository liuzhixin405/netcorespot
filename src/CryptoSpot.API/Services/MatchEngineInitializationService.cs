using CryptoSpot.MatchEngine;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.API.Services;

/// <summary>
/// 初始化撮合引擎 - 加载交易对到内存
/// </summary>
public class MatchEngineInitializationService : IHostedService
{
    private readonly ChannelMatchEngineService _matchEngine;
    private readonly InMemoryAssetStore _assetStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MatchEngineInitializationService> _logger;

    public MatchEngineInitializationService(
        ChannelMatchEngineService matchEngine,
        InMemoryAssetStore assetStore,
        IServiceProvider serviceProvider,
        ILogger<MatchEngineInitializationService> logger)
    {
        _matchEngine = matchEngine;
        _assetStore = assetStore;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing matching engine...");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. 加载所有交易对
            var tradingPairs = await dbContext.TradingPairs
                .Where(tp => tp.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var pair in tradingPairs)
            {
                _matchEngine.InitializeSymbol(pair.Symbol);
            }

            _logger.LogInformation("Initialized {Count} trading pairs", tradingPairs.Count);

            // 2. 加载用户资产到内存
            var assets = await dbContext.Assets.ToListAsync(cancellationToken);
            
            foreach (var asset in assets)
            {
                if (asset.UserId.HasValue)
                {
                    await _assetStore.InitializeBalanceAsync(
                        asset.UserId.Value,
                        asset.Symbol,
                        asset.Available);
                }
            }

            _logger.LogInformation("Loaded {Count} user assets", assets.Count);
            _logger.LogInformation("✅ Matching engine initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize matching engine");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping matching engine...");
        return Task.CompletedTask;
    }
}
