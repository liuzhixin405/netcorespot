using System.Collections.Generic;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Infrastructure.MatchEngine.Core
{
    public record MatchSlice(Order Maker, Order Taker, decimal Price, decimal Quantity);

    public interface IMatchingAlgorithm
    {
        IEnumerable<MatchSlice> Match(IOrderBook book, Order taker);
    }
}
