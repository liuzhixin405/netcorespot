using CryptoSpot.Domain.Common;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Domain.DomainEvents
{
    /// <summary>
    /// 订单取消事件
    /// </summary>
    public class OrderCancelledEvent : IDomainEvent
    {
        public Order Order { get; }
        public DateTime OccurredOn { get; }

        public OrderCancelledEvent(Order order)
        {
            Order = order;
            OccurredOn = DateTime.UtcNow;
        }
    }
}
