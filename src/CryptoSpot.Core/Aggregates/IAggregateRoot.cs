using CryptoSpot.Core.Events;

namespace CryptoSpot.Core.Aggregates
{
    /// <summary>
    /// 聚合根接口
    /// </summary>
    public interface IAggregateRoot
    {
        /// <summary>
        /// 领域事件集合
        /// </summary>
        IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

        /// <summary>
        /// 清除领域事件
        /// </summary>
        void ClearDomainEvents();
    }
}
