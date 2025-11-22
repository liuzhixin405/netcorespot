using CryptoSpot.Domain.Common;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Domain.DomainEvents
{
    /// <summary>
    /// 订单创建事件
    /// </summary>
    public class OrderPlacedEvent : IDomainEvent
    {
        public Order Order { get; }
        public DateTime OccurredOn { get; }

        public OrderPlacedEvent(Order order)
        {
            Order = order;
            OccurredOn = DateTime.UtcNow;
        }
    }
}
