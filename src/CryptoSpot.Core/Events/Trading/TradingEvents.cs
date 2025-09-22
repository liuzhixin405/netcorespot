using CryptoSpot.Core.Entities;
using CryptoSpot.Core.ValueObjects;

namespace CryptoSpot.Core.Events.Trading
{
    /// <summary>
    /// 订单创建事件
    /// </summary>
    public class OrderCreatedEvent : DomainEvent
    {
        public int OrderId { get; }
        public int UserId { get; }
        public string Symbol { get; }
        public OrderSide Side { get; }
        public OrderType Type { get; }
        public decimal Quantity { get; }
        public decimal? Price { get; }

        public OrderCreatedEvent(Order order)
        {
            OrderId = order.Id;
            UserId = order.UserId ?? 0;
            Symbol = order.TradingPair?.Symbol ?? string.Empty;
            Side = order.Side;
            Type = order.Type;
            Quantity = order.Quantity;
            Price = order.Price;
        }
    }

    /// <summary>
    /// 订单状态变更事件
    /// </summary>
    public class OrderStatusChangedEvent : DomainEvent
    {
        public int OrderId { get; }
        public int UserId { get; }
        public string Symbol { get; }
        public OrderStatus OldStatus { get; }
        public OrderStatus NewStatus { get; }
        public decimal FilledQuantity { get; }
        public decimal AveragePrice { get; }

        public OrderStatusChangedEvent(Order order, OrderStatus oldStatus)
        {
            OrderId = order.Id;
            UserId = order.UserId ?? 0;
            Symbol = order.TradingPair?.Symbol ?? string.Empty;
            OldStatus = oldStatus;
            NewStatus = order.Status;
            FilledQuantity = order.FilledQuantity;
            AveragePrice = order.AveragePrice;
        }
    }

    /// <summary>
    /// 交易执行事件
    /// </summary>
    public class TradeExecutedEvent : DomainEvent
    {
        public int TradeId { get; }
        public int BuyOrderId { get; }
        public int SellOrderId { get; }
        public int BuyerId { get; }
        public int SellerId { get; }
        public string Symbol { get; }
        public decimal Quantity { get; }
        public decimal Price { get; }
        public decimal Fee { get; }

        public TradeExecutedEvent(Trade trade)
        {
            TradeId = trade.Id;
            BuyOrderId = trade.BuyOrderId;
            SellOrderId = trade.SellOrderId;
            BuyerId = trade.BuyerId;
            SellerId = trade.SellerId;
            Symbol = trade.TradingPair?.Symbol ?? string.Empty;
            Quantity = trade.Quantity;
            Price = trade.Price;
            Fee = trade.Fee;
        }
    }

    /// <summary>
    /// 价格更新事件
    /// </summary>
    public class PriceUpdatedEvent : DomainEvent
    {
        public string Symbol { get; }
        public decimal Price { get; }
        public decimal Change24h { get; }
        public decimal Volume24h { get; }
        public decimal High24h { get; }
        public decimal Low24h { get; }

        public PriceUpdatedEvent(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            Symbol = symbol;
            Price = price;
            Change24h = change24h;
            Volume24h = volume24h;
            High24h = high24h;
            Low24h = low24h;
        }
    }

    /// <summary>
    /// K线数据更新事件
    /// </summary>
    public class KLineDataUpdatedEvent : DomainEvent
    {
        public string Symbol { get; }
        public string TimeFrame { get; }
        public long Timestamp { get; }
        public decimal Open { get; }
        public decimal High { get; }
        public decimal Low { get; }
        public decimal Close { get; }
        public decimal Volume { get; }
        public bool IsNewKLine { get; }

        public KLineDataUpdatedEvent(string symbol, string timeFrame, KLineData klineData, bool isNewKLine = false)
        {
            Symbol = symbol;
            TimeFrame = timeFrame;
            Timestamp = klineData.Timestamp;
            Open = klineData.Open;
            High = klineData.High;
            Low = klineData.Low;
            Close = klineData.Close;
            Volume = klineData.Volume;
            IsNewKLine = isNewKLine;
        }
    }

    /// <summary>
    /// 资产余额变更事件
    /// </summary>
    public class AssetBalanceChangedEvent : DomainEvent
    {
        public int UserId { get; }
        public string AssetSymbol { get; }
        public decimal OldBalance { get; }
        public decimal NewBalance { get; }
        public decimal OldFrozen { get; }
        public decimal NewFrozen { get; }
        public string ChangeReason { get; }

        public AssetBalanceChangedEvent(int userId, string assetSymbol, decimal oldBalance, decimal newBalance, 
            decimal oldFrozen, decimal newFrozen, string changeReason)
        {
            UserId = userId;
            AssetSymbol = assetSymbol;
            OldBalance = oldBalance;
            NewBalance = newBalance;
            OldFrozen = oldFrozen;
            NewFrozen = newFrozen;
            ChangeReason = changeReason;
        }
    }
}
