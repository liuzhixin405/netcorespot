using CryptoSpot.Core.Interfaces.Trading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 自动交易后台服务 - 纯粹的后台服务，只负责生命周期管理
    /// </summary>
    public class AutoTradingService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<AutoTradingService> _logger;
        private IAutoTradingService? _autoTradingLogicService;

        public AutoTradingService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<AutoTradingService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }


        #region BackgroundService Implementation

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            
            try
            {
                // 获取自动交易逻辑服务
                _autoTradingLogicService = scope.ServiceProvider.GetRequiredService<IAutoTradingService>();
                
                if (_autoTradingLogicService == null)
                {
                    _logger.LogError("Failed to resolve IAutoTradingService");
                    return;
                }

                // 启动自动交易
                await _autoTradingLogicService.StartAutoTradingAsync();
                _logger.LogInformation("Auto trading service started");

                // 保持服务运行直到收到取消请求
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Auto trading service is being cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in auto trading service execution");
            }
            finally
            {
                // 停止自动交易
                if (_autoTradingLogicService != null)
                {
                    try
                    {
                        await _autoTradingLogicService.StopAutoTradingAsync();
                        _logger.LogInformation("Auto trading service stopped");
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogWarning("ServiceProvider disposed during auto trading service shutdown");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error stopping auto trading service");
                    }
                }
            }
        }

        #endregion
    }
}
