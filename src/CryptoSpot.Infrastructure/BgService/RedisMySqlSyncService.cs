using CryptoSpot.Application.DomainCommands.DataSync;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.BgService;

/// <summary>
/// Redis → MySQL 数据同步服务（使用 CommandBus 高性能批量处理）
/// </summary>
public class RedisMySqlSyncService : BackgroundService
{
    private readonly ICommandBus _commandBus;
    private readonly ILogger<RedisMySqlSyncService> _logger;
    private const int SYNC_INTERVAL_SECONDS = 10; // 每 10 秒同步一次

    public RedisMySqlSyncService(
        ICommandBus commandBus,
        ILogger<RedisMySqlSyncService> logger)
    {
        _commandBus = commandBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("✅ Redis → MySQL 同步服务已启动（间隔: {Interval}秒）[使用 CommandBus]", SYNC_INTERVAL_SECONDS);

        // 等待 30 秒让系统完全启动
        await Task.Delay(30000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // 并行通过 CommandBus 调度三个同步任务（BatchDataflowCommandBus 会自动批处理和并发控制）
                await _commandBus.SendAsync<SyncOrdersCommand, SyncOrdersResult>(
                    new SyncOrdersCommand { BatchSize = 500 }, stoppingToken);
                await _commandBus.SendAsync<SyncTradesCommand, SyncTradesResult>(
                    new SyncTradesCommand { BatchSize = 500 }, stoppingToken);
                await _commandBus.SendAsync<SyncAssetsCommand, SyncAssetsResult>(
                    new SyncAssetsCommand { BatchSize = 500 }, stoppingToken);

                stopwatch.Stop();
                
                _logger.LogInformation("✅ 同步完成，耗时={Ms}ms", stopwatch.ElapsedMilliseconds);

                await Task.Delay(SYNC_INTERVAL_SECONDS * 1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("⛔ Redis → MySQL 同步服务正在停止...");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 同步失败");
                await Task.Delay(5000, stoppingToken); // 错误后等待 5 秒
            }
        }

        _logger.LogInformation("⏹️ RedisMySqlSyncService 已停止");
    }
}
