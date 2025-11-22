using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.MatchEngine.Events;
using CryptoSpot.MatchEngine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.MatchEngine
{
    /// <summary>
    /// 统一监听 OrderBookChangedEvent 并推送快照，替代多个重复来源。
    /// 目前实现为事件触发后立即生成快照（无去抖）；后续可加节流。
    /// </summary>
    public class OrderBookSnapshotWorker : BackgroundService
    {
        private readonly ILogger<OrderBookSnapshotWorker> _logger;
        private readonly IOrderBookSnapshotService _snapshotService;
        private readonly IMatchEngineEventBus _bus;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pending = new();

        public OrderBookSnapshotWorker(
            ILogger<OrderBookSnapshotWorker> logger, 
            IOrderBookSnapshotService snapshotService, 
            IMatchEngineEventBus bus)
        {
            _logger = logger;
            _snapshotService = snapshotService;
            _bus = bus;
            _bus.Subscribe<OrderBookChangedEvent>(OnOrderBookChangedAsync);
        }

        private Task OnOrderBookChangedAsync(OrderBookChangedEvent evt)
        {
            _pending.Enqueue(evt.Symbol);
            return Task.CompletedTask;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OrderBookSnapshotWorker started");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_pending.TryDequeue(out var symbol))
                    {
                        await Task.Delay(100, stoppingToken);
                        continue;
                    }

                    // 使用统一的快照服务
                    await _snapshotService.PushSnapshotAsync(symbol, 20, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OrderBookSnapshotWorker loop error");
                    await Task.Delay(500, stoppingToken);
                }
            }
        }
    }
}
