using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.MatchEngine.Events;
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
        private readonly IServiceProvider _sp;
        private readonly IMatchEngineEventBus _bus;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pending = new();

        public OrderBookSnapshotWorker(ILogger<OrderBookSnapshotWorker> logger, IServiceProvider sp, IMatchEngineEventBus bus)
        {
            _logger = logger;
            _sp = sp;
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

                    using var scope = _sp.CreateScope();
                    var redisOrders = scope.ServiceProvider.GetRequiredService<RedisOrderRepository>();
                    var push = scope.ServiceProvider.GetService<CryptoSpot.Application.Abstractions.Services.RealTime.IRealTimeDataPushService>();
                    if (push == null) continue;

                    var (bids, asks) = await redisOrders.GetOrderBookDepthAsync(symbol, 20);
                    var bidDtos = bids.Select(x => new CryptoSpot.Application.DTOs.Trading.OrderBookLevelDto { Price = x.price, Quantity = x.quantity }).ToList();
                    var askDtos = asks.Select(x => new CryptoSpot.Application.DTOs.Trading.OrderBookLevelDto { Price = x.price, Quantity = x.quantity }).ToList();
                    await push.PushExternalOrderBookSnapshotAsync(symbol, bidDtos, askDtos, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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
