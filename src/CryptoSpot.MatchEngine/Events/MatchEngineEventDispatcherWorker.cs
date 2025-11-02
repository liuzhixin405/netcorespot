using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.MatchEngine.Events
{
    /// <summary>
    /// 从异步事件总线 Channel 读取并调度事件处理。
    /// </summary>
    public class MatchEngineEventDispatcherWorker : BackgroundService
    {
        private readonly AsyncMatchEngineEventBus _bus;
        private readonly ILogger<MatchEngineEventDispatcherWorker> _logger;

        public MatchEngineEventDispatcherWorker(AsyncMatchEngineEventBus bus, ILogger<MatchEngineEventDispatcherWorker> logger)
        {
            _bus = bus;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MatchEngineEventDispatcherWorker started");
            var reader = _bus.Reader;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var evt = await reader.ReadAsync(stoppingToken);
                    try
                    {
                        await _bus.DispatchAsync(evt);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Dispatch error for event {EventType}", evt.GetType().Name);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dispatcher loop error");
                }
            }
            _logger.LogInformation("MatchEngineEventDispatcherWorker stopped");
        }
    }
}
