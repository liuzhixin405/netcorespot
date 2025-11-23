using CryptoSpot.Bus.Core;
using CryptoSpot.MatchEngine.Commands;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.MatchEngine.CommandHandlers
{
    /// <summary>
    /// 订单已放置命令处理器 - 记录事件日志（实时推送可在后续扩展）
    /// </summary>
    public class OrderPlacedCommandHandler : ICommandHandler<OrderPlacedCommand, bool>
    {
        private readonly ILogger<OrderPlacedCommandHandler> _logger;

        public OrderPlacedCommandHandler(ILogger<OrderPlacedCommandHandler> logger)
        {
            _logger = logger;
        }

        public Task<bool> HandleAsync(OrderPlacedCommand command, CancellationToken ct)
        {
            try
            {
                _logger.LogInformation(
                    "Order placed: Symbol={Symbol}, OrderId={OrderId}, Side={Side}, Price={Price}, Quantity={Quantity}", 
                    command.Symbol, 
                    command.Order.Id,
                    command.Order.Side,
                    command.Order.Price,
                    command.Order.Quantity);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Failed to process OrderPlaced event: Symbol={Symbol}, OrderId={OrderId}", 
                    command.Symbol, 
                    command.Order.Id);
                return Task.FromResult(false);
            }
        }
    }

    /// <summary>
    /// 交易已执行命令处理器 - 记录事件日志和指标
    /// </summary>
    public class TradeExecutedCommandHandler : ICommandHandler<TradeExecutedCommand, bool>
    {
        private readonly ILogger<TradeExecutedCommandHandler> _logger;

        public TradeExecutedCommandHandler(ILogger<TradeExecutedCommandHandler> logger)
        {
            _logger = logger;
        }

        public Task<bool> HandleAsync(TradeExecutedCommand command, CancellationToken ct)
        {
            try
            {
                _logger.LogInformation(
                    "Trade executed: Symbol={Symbol}, TradeId={TradeId}, Price={Price}, Quantity={Quantity}, Maker={MakerId}, Taker={TakerId}", 
                    command.Symbol, 
                    command.Trade.Id,
                    command.Trade.Price,
                    command.Trade.Quantity,
                    command.MakerOrder.UserId,
                    command.TakerOrder.UserId);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Failed to process TradeExecuted event: Symbol={Symbol}, TradeId={TradeId}", 
                    command.Symbol, 
                    command.Trade.Id);
                return Task.FromResult(false);
            }
        }
    }

    /// <summary>
    /// 订单簿变更命令处理器
    /// 注意：订单簿快照由 InMemoryMatchEngineService 直接调用 IOrderBookSnapshotService 推送
    /// 此处仅作为事件记录和监控点
    /// </summary>
    public class OrderBookChangedCommandHandler : ICommandHandler<OrderBookChangedCommand, bool>
    {
        private readonly ILogger<OrderBookChangedCommandHandler> _logger;

        public OrderBookChangedCommandHandler(ILogger<OrderBookChangedCommandHandler> logger)
        {
            _logger = logger;
        }

        public Task<bool> HandleAsync(OrderBookChangedCommand command, CancellationToken ct)
        {
            // 订单簿快照由 Worker 定时推送，此处仅记录日志
            _logger.LogTrace("OrderBook changed: Symbol={Symbol}", command.Symbol);
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 订单取消命令处理器
    /// </summary>
    public class OrderCancelledCommandHandler : ICommandHandler<OrderCancelledCommand, bool>
    {
        private readonly ILogger<OrderCancelledCommandHandler> _logger;

        public OrderCancelledCommandHandler(ILogger<OrderCancelledCommandHandler> logger)
        {
            _logger = logger;
        }

        public Task<bool> HandleAsync(OrderCancelledCommand command, CancellationToken ct)
        {
            _logger.LogDebug(
                "Order cancelled event processed: Symbol={Symbol}, OrderId={OrderId}", 
                command.Symbol, 
                command.OrderId);
            return Task.FromResult(true);
        }
    }
}
