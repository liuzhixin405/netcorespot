using CryptoSpot.Domain.Matching;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Persistence.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Matching;

/// <summary>
/// 撮合引擎后台服务
/// 启动时初始化所有交易对的订单簿，并处理撮合日志
/// </summary>
public class MatchingEngineHostedService : BackgroundService
{
    private readonly InMemoryMatchingEngine _matchingEngine;
    private readonly MatchLogPersister _logPersister;
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILogger<MatchingEngineHostedService> _logger;

    public MatchingEngineHostedService(
        InMemoryMatchingEngine matchingEngine,
        MatchLogPersister logPersister,
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<MatchingEngineHostedService> logger)
    {
        _matchingEngine = matchingEngine;
        _logPersister = logPersister;
        _contextFactory = contextFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting matching engine hosted service");

        // 初始化所有交易对
        await InitializeTradingPairsAsync(stoppingToken);

        // 订阅撮合日志事件
        _matchingEngine.OnLogGenerated += async (log) =>
        {
            await _logPersister.ProcessLogAsync(log);
        };

        _logger.LogInformation("Matching engine is ready");

        // 保持服务运行
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task InitializeTradingPairsAsync(CancellationToken ct)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            
            var tradingPairs = await context.TradingPairs
                .Where(tp => tp.IsActive)
                .ToListAsync(ct);

            foreach (var pair in tradingPairs)
            {
                _matchingEngine.InitializeSymbol(
                    pair.Symbol, 
                    pair.QuantityPrecision, 
                    pair.PricePrecision);
            }

            _logger.LogInformation("Initialized {Count} trading pairs", tradingPairs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize trading pairs");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping matching engine hosted service");
        await base.StopAsync(cancellationToken);
    }
}
