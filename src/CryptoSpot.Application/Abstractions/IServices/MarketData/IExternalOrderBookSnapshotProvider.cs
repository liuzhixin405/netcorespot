using CryptoSpot.Application.DTOs.Trading;

namespace CryptoSpot.Application.Abstractions.Services.MarketData;

public interface IExternalOrderBookSnapshotProvider
{
    ExternalOrderBookSnapshot? GetSnapshot(string symbol);
}

public sealed record ExternalOrderBookSnapshot(
    string Symbol,
    IReadOnlyList<OrderBookLevelDto> Bids,
    IReadOnlyList<OrderBookLevelDto> Asks,
    long Timestamp,
    long Sequence);
