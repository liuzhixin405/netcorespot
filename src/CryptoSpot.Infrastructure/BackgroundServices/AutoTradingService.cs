using CryptoSpot.Application.Abstractions.Services.Trading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.BackgroundServices
{
    public class AutoTradingService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<AutoTradingService> _logger;

        public AutoTradingService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<AutoTradingService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            IAutoTradingService? autoTradingLogicService = null;

            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    autoTradingLogicService = scope.ServiceProvider.GetRequiredService<IAutoTradingService>();
                }

                if (autoTradingLogicService == null)
                {
                    _logger.LogError("Failed to resolve IAutoTradingService");
                    return;
                }

                await autoTradingLogicService.StartAutoTradingAsync();
                _logger.LogInformation("Auto trading service started");

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
                _logger.LogError(ex, "Auto trading service startup failed: {Message}", ex.Message);
            }
            finally
            {
                if (autoTradingLogicService != null)
                {
                    try
                    {
                        await autoTradingLogicService.StopAutoTradingAsync();
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
    }
}
