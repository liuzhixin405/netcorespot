using CryptoSpot.Domain.Common;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Domain.DomainEvents
{
    /// <summary>
    /// 资产更新事件
    /// </summary>
    public class AssetUpdatedEvent : IDomainEvent
    {
        public Asset Asset { get; }
        public decimal OldAvailable { get; }
        public decimal NewAvailable { get; }
        public decimal OldFrozen { get; }
        public decimal NewFrozen { get; }
        public string Reason { get; }
        public DateTime OccurredOn { get; }

        public AssetUpdatedEvent(Asset asset, decimal oldAvailable, decimal newAvailable, 
            decimal oldFrozen, decimal newFrozen, string reason)
        {
            Asset = asset;
            OldAvailable = oldAvailable;
            NewAvailable = newAvailable;
            OldFrozen = oldFrozen;
            NewFrozen = newFrozen;
            Reason = reason;
            OccurredOn = DateTime.UtcNow;
        }
    }
}
