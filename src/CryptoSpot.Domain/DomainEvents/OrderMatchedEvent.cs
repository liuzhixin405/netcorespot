using CryptoSpot.Domain.Common;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Domain.DomainEvents
{
    /// <summary>
    /// 订单撮合成交事件
    /// </summary>
    public class OrderMatchedEvent : IDomainEvent
    {
        public Trade Trade { get; }
        public Order BuyOrder { get; }
        public Order SellOrder { get; }
        public DateTime OccurredOn { get; }

        public OrderMatchedEvent(Trade trade, Order buyOrder, Order sellOrder)
        {
            Trade = trade;
            BuyOrder = buyOrder;
            SellOrder = sellOrder;
            OccurredOn = DateTime.UtcNow;
        }
    }
}
