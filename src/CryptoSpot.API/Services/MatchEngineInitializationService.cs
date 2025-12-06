using CryptoSpot.MatchEngine.Services;
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

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping matching engine and persisting data...");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 将内存中的资产数据写回数据库
            var balances = _assetStore.GetAllBalances().ToList();
            _logger.LogInformation("Persisting {Count} asset balances to database", balances.Count);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var (userId, currency, available, frozen) in balances)
            {
                var asset = await dbContext.Assets
                    .FirstOrDefaultAsync(a => a.UserId == userId && a.Symbol == currency, cancellationToken);

                if (asset != null)
                {
                    asset.Available = available;
                    asset.Frozen = frozen;
                    asset.UpdatedAt = now;
                }
                else
                {
                    // 如果数据库中不存在，创建新记录
                    dbContext.Assets.Add(new Domain.Entities.Asset
                    {
                        UserId = userId,
                        Symbol = currency,
                        Available = available,
                        Frozen = frozen,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("✅ Asset balances persisted successfully");

            // 清空内存数据
            _assetStore.Clear();
            _logger.LogInformation("Memory cleared");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist data during shutdown");
            // 即使失败也不抛出异常，避免影响应用程序关闭
        }

        _logger.LogInformation("Matching engine stopped");
    }
}
