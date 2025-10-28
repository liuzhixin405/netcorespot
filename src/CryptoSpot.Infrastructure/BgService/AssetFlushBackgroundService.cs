using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Application.Abstractions.Services.Users;

namespace CryptoSpot.Infrastructure.BgService
{
    /// <summary>
    /// 每3分钟批量将 Redis 中标记为 dirty 的资产写回数据库；在优雅关闭时执行最后一次 flush。
    /// </summary>
    public class AssetFlushBackgroundService : BackgroundService
    {
        private readonly ILogger<AssetFlushBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(3);

        public AssetFlushBackgroundService(ILogger<AssetFlushBackgroundService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Asset flush background service started");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, stoppingToken);
                    await FlushAsync(stoppingToken);
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Asset flush cycle failed");
                }
            }
        }

        private async Task FlushAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var commandBus = scope.ServiceProvider.GetService<CryptoSpot.Bus.Core.ICommandBus>();
                if (commandBus == null)
                {
                    _logger.LogWarning("ICommandBus 未注册，跳过资产同步调度");
                    return;
                }

                // 触发异步批量同步任务（短超时以防阻塞）
                var command = new CryptoSpot.Application.DomainCommands.DataSync.SyncAssetsCommand { BatchSize = 500 };
                // 不等待命令完成以避免阻塞本周期（CommandBus 内部处理并发）
                await commandBus.SendAsync<CryptoSpot.Application.DomainCommands.DataSync.SyncAssetsCommand, CryptoSpot.Application.DomainCommands.DataSync.SyncAssetsResult>(command, ct);
                _logger.LogDebug("已调度 SyncAssetsCommand 来刷新资产到 MySQL");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Asset flush canceled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FlushAsync failed");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Asset flush background service stopping - final flush");
            try
            {
                await FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Final asset flush failed");
            }
            await base.StopAsync(cancellationToken);
        }
    }
}
