namespace CryptoSpot.Domain.Matching;

public class TradingProduct
{
    public required string Symbol { get; set; }
    public int BaseScale { get; set; } = 8;
    public int QuoteScale { get; set; } = 2;
}

public class MatchOrder
{
    public long Id { get; set; }
    public required string Symbol { get; set; }
    public long UserId { get; set; }
    public Side Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Size { get; set; }
    public decimal Funds { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = "New";
}

public sealed class BookOrder
{
    public long OrderId { get; set; }
    public long UserId { get; set; }
    public decimal Size { get; set; }
    public decimal Funds { get; set; }
    public decimal Price { get; set; }
    public Side Side { get; set; }
    public OrderType Type { get; set; }

    public static BookOrder From(MatchOrder order) => new()
    {
        OrderId = order.Id,
        UserId = order.UserId,
        Size = order.Size,
        Funds = order.Funds,
        Price = order.Price,
        Side = order.Side,
        Type = order.Type
    };
}
