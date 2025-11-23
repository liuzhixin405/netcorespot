using CryptoSpot.Bus.Core;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.MatchEngine.Commands
{
    /// <summary>
    /// 撮合引擎命令基类
    /// </summary>
    public abstract record MatchEngineCommand : ICommand<bool>
    {
        public string Symbol { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 订单已放置事件命令
    /// </summary>
    public record OrderPlacedCommand : MatchEngineCommand
    {
        public Order Order { get; init; } = null!;
        
        public OrderPlacedCommand(string symbol, Order order)
        {
            Symbol = symbol;
            Order = order;
        }
    }

    /// <summary>
    /// 交易已执行事件命令
    /// </summary>
    public record TradeExecutedCommand : MatchEngineCommand
    {
        public Trade Trade { get; init; } = null!;
        public Order MakerOrder { get; init; } = null!;
        public Order TakerOrder { get; init; } = null!;
        
        public TradeExecutedCommand(string symbol, Trade trade, Order makerOrder, Order takerOrder)
        {
            Symbol = symbol;
            Trade = trade;
            MakerOrder = makerOrder;
            TakerOrder = takerOrder;
        }
    }

    /// <summary>
    /// 订单簿已变更事件命令
    /// </summary>
    public record OrderBookChangedCommand : MatchEngineCommand
    {
        public OrderBookChangedCommand(string symbol)
        {
            Symbol = symbol;
        }
    }

    /// <summary>
    /// 订单已取消事件命令
    /// </summary>
    public record OrderCancelledCommand : MatchEngineCommand
    {
        public long OrderId { get; init; }
        
        public OrderCancelledCommand(string symbol, long orderId)
        {
            Symbol = symbol;
            OrderId = orderId;
        }
    }
}
