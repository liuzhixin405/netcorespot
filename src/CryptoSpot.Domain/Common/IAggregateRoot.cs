namespace CryptoSpot.Domain.Common
{
    /// <summary>
    /// 聚合根标记接口
    /// </summary>
    public interface IAggregateRoot
    {
        IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
        void ClearDomainEvents();
    }
}
