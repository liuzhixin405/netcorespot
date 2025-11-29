namespace CryptoSpot.Domain.Matching;

/// <summary>
/// 撮合日志基类
/// </summary>
public abstract record LogBase(long Seq, string Symbol, DateTime Timestamp);

/// <summary>
/// 订单开仓日志（挂单成功）
/// </summary>
public sealed record OpenLog(long Seq, string Symbol, DateTime Timestamp, BookOrder Order)
    : LogBase(Seq, Symbol, Timestamp);

/// <summary>
/// 订单成交日志
/// </summary>
public sealed record MatchLog(
    long Seq, 
    string Symbol, 
    DateTime Timestamp,
    long TradeSeq,
    BookOrder Taker, 
    BookOrder Maker, 
    decimal Price, 
    decimal Size)
    : LogBase(Seq, Symbol, Timestamp);

/// <summary>
/// 订单完成日志（成交完成或取消）
/// </summary>
public sealed record DoneLog(
    long Seq, 
    string Symbol, 
    DateTime Timestamp,
    BookOrder Order,
    decimal RemainingSize, 
    DoneReason Reason)
    : LogBase(Seq, Symbol, Timestamp);
