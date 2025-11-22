namespace CryptoSpot.Domain.Common
{
    /// <summary>
    /// 领域事件标记接口
    /// </summary>
    public interface IDomainEvent
    {
        DateTime OccurredOn { get; }
    }
}
