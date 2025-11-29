namespace CryptoSpot.Domain.Matching;

/// <summary>
/// 订单簿 - 纯内存撮合核心
/// </summary>
public sealed class OrderBook
{
    private readonly TradingProduct _product;
    private readonly Depth _bids;
    private readonly Depth _asks;
    private readonly HashSet<long> _processedOrderIds;
    private long _tradeSeq;
    private long _logSeq;

    public OrderBook(TradingProduct product)
    {
        _product = product;
        _bids = Depth.CreateBidDepth();
        _asks = Depth.CreateAskDepth();
        _processedOrderIds = new HashSet<long>();
    }

    public string Symbol => _product.Symbol;
    public long CurrentLogSeq => _logSeq;
    public long CurrentTradeSeq => _tradeSeq;

    private Depth GetDepth(Side side) => side == Side.Buy ? _bids : _asks;
    private long NextLogSeq() => ++_logSeq;
    private long NextTradeSeq() => ++_tradeSeq;

    /// <summary>
    /// 应用新订单到订单簿，执行撮合
    /// </summary>
    public IReadOnlyList<LogBase> ApplyOrder(MatchOrder order)
    {
        // 防止重复处理
        if (!_processedOrderIds.Add(order.Id))
            return Array.Empty<LogBase>();

        var logs = new List<LogBase>();
        var taker = BookOrder.From(order);
        var timestamp = DateTime.UtcNow;

        // 市价单特殊处理
        if (taker.Type == OrderType.Market)
        {
            taker.Price = taker.Side == Side.Buy ? decimal.MaxValue : decimal.Zero;
        }

        var makerDepth = taker.Side == Side.Buy ? _asks : _bids;

        // 撮合循环
        foreach (var maker in makerDepth.IterateInMatchOrder())
        {
            // 价格不匹配则退出
            if (taker.Side == Side.Buy && taker.Price < maker.Price) break;
            if (taker.Side == Side.Sell && taker.Price > maker.Price) break;

            var price = maker.Price;
            decimal size;

            // 计算成交量
            if (taker.Type == OrderType.Limit ||
                (taker.Type == OrderType.Market && taker.Side == Side.Sell))
            {
                if (taker.Size <= 0) break;
                size = decimal.Min(taker.Size, maker.Size);
                taker.Size -= size;
            }
            else // 市价买单，按资金计算
            {
                if (taker.Funds <= 0) break;

                var scale = (decimal)Math.Pow(10, _product.BaseScale);
                var takerSize = Math.Truncate((double)(taker.Funds / price * scale)) / (double)scale;
                var takerSizeDec = (decimal)takerSize;

                if (takerSizeDec <= 0) break;

                size = decimal.Min(takerSizeDec, maker.Size);
                var funds = size * price;
                taker.Funds -= funds;
            }

            // 减少 Maker 订单数量
            makerDepth.DecreaseSize(maker.OrderId, size);

            // 记录成交日志
            logs.Add(new MatchLog(
                NextLogSeq(), 
                _product.Symbol, 
                timestamp,
                NextTradeSeq(),
                taker, 
                maker, 
                price, 
                size));

            // Maker 完全成交
            if (maker.Size == 0)
            {
                logs.Add(new DoneLog(
                    NextLogSeq(), 
                    _product.Symbol, 
                    timestamp,
                    maker, 
                    0m, 
                    DoneReason.Filled));
            }
        }

        // Taker 处理
        if (taker.Type == OrderType.Limit && taker.Size > 0)
        {
            // 限价单未完全成交，挂单
            GetDepth(taker.Side).Add(taker);
            logs.Add(new OpenLog(NextLogSeq(), _product.Symbol, timestamp, taker));
        }
        else
        {
            // 市价单或限价单完全成交
            var remainingSize = taker.Size;
            var reason = DoneReason.Filled;

            if (taker.Type == OrderType.Market)
            {
                taker.Price = 0m;
                remainingSize = 0m;
                if ((taker.Side == Side.Sell && taker.Size > 0) ||
                    (taker.Side == Side.Buy && taker.Funds > 0))
                {
                    reason = DoneReason.Cancelled;
                }
            }

            logs.Add(new DoneLog(
                NextLogSeq(), 
                _product.Symbol, 
                timestamp,
                taker, 
                remainingSize, 
                reason));
        }

        return logs;
    }

    /// <summary>
    /// 取消订单
    /// </summary>
    public IReadOnlyList<LogBase> CancelOrder(MatchOrder order)
    {
        _processedOrderIds.Add(order.Id);

        var depth = GetDepth(order.Side);
        if (!depth.TryGet(order.Id, out var bookOrder) || bookOrder == null)
        {
            return Array.Empty<LogBase>();
        }

        var remainingSize = bookOrder.Size;
        depth.DecreaseSize(order.Id, bookOrder.Size);

        var doneLog = new DoneLog(
            NextLogSeq(), 
            _product.Symbol, 
            DateTime.UtcNow,
            bookOrder, 
            remainingSize, 
            DoneReason.Cancelled);
        
        return new LogBase[] { doneLog };
    }

    /// <summary>
    /// 获取订单簿快照
    /// </summary>
    public OrderBookSnapshot GetSnapshot()
    {
        var bids = _bids.AllOrders()
            .GroupBy(o => o.Price)
            .Select(g => new PriceLevel 
            { 
                Price = g.Key, 
                Size = g.Sum(o => o.Size),
                Count = g.Count()
            })
            .OrderByDescending(p => p.Price)
            .Take(20)
            .ToList();

        var asks = _asks.AllOrders()
            .GroupBy(o => o.Price)
            .Select(g => new PriceLevel 
            { 
                Price = g.Key, 
                Size = g.Sum(o => o.Size),
                Count = g.Count()
            })
            .OrderBy(p => p.Price)
            .Take(20)
            .ToList();

        return new OrderBookSnapshot
        {
            Symbol = _product.Symbol,
            Bids = bids,
            Asks = asks,
            LogSeq = _logSeq,
            TradeSeq = _tradeSeq,
            Timestamp = DateTime.UtcNow
        };
    }
}

public class OrderBookSnapshot
{
    public required string Symbol { get; set; }
    public List<PriceLevel> Bids { get; set; } = new();
    public List<PriceLevel> Asks { get; set; } = new();
    public long LogSeq { get; set; }
    public long TradeSeq { get; set; }
    public DateTime Timestamp { get; set; }
}

public class PriceLevel
{
    public decimal Price { get; set; }
    public decimal Size { get; set; }
    public int Count { get; set; }
}
