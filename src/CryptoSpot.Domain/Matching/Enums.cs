namespace CryptoSpot.Domain.Matching;

public enum Side
{
    Buy = 1,
    Sell = 2
}

public enum OrderType
{
    Limit = 1,
    Market = 2
}

public enum DoneReason
{
    Filled = 1,
    Cancelled = 2
}
