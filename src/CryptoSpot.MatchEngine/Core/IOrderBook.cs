using CryptoSpot.Domain.Entities;
using System.Collections.Generic;

namespace CryptoSpot.MatchEngine.Core
{
    /// <summary>
    /// OrderBook 抽象：隐藏内部数据结构便于后续替换实现（内存 / Redis / Hybrid）。
    /// 先提供最小接口，保持与现有 InMemory 行为一致。
    /// </summary>
    public interface IOrderBook
    {
        string Symbol { get; }
        void Add(Order order);
        Order? GetBestOpposite(OrderSide side);
        void Remove(Order order);
        IReadOnlyList<(decimal price, decimal quantity)> GetDepth(OrderSide side, int depth);
        object SyncRoot { get; }
    }
}
