using System;

namespace CryptoSpot.Core.Events
{
    /// <summary>
    /// 领域事件接口
    /// </summary>
    public interface IDomainEvent
    {
        Guid Id { get; }
        DateTime OccurredOn { get; }
        string EventType { get; }
    }

    /// <summary>
    /// 领域事件处理器接口
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
    {
        Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 领域事件发布器接口
    /// </summary>
    public interface IDomainEventPublisher
    {
        Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent;
        Task PublishAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
    }
}
