using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Events;
using CryptoSpot.Core.Events.Trading;
using CryptoSpot.Core.ValueObjects;

namespace CryptoSpot.Core.Aggregates
{
    /// <summary>
    /// 交易聚合根 - 管理订单和交易的完整生命周期
    /// </summary>
    public class TradingAggregate : IAggregateRoot
    {
        private readonly List<IDomainEvent> _domainEvents = new();
        private readonly List<Order> _orders = new();
        private readonly List<Trade> _trades = new();

        public int Id { get; private set; }
        public string Symbol { get; private set; } = string.Empty;
        public int TradingPairId { get; private set; }
        public long CreatedAt { get; private set; }
        public long UpdatedAt { get; private set; }

        /// <summary>
        /// 订单集合
        /// </summary>
        public IReadOnlyCollection<Order> Orders => _orders.AsReadOnly();

        /// <summary>
        /// 交易集合
        /// </summary>
        public IReadOnlyCollection<Trade> Trades => _trades.AsReadOnly();

        /// <summary>
        /// 领域事件集合
        /// </summary>
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        private TradingAggregate() { }

        /// <summary>
        /// 创建交易聚合根
        /// </summary>
        public static TradingAggregate Create(string symbol, int tradingPairId)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("交易对符号不能为空", nameof(symbol));

            if (tradingPairId <= 0)
                throw new ArgumentException("交易对ID必须大于0", nameof(tradingPairId));

            var now = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
            var aggregate = new TradingAggregate
            {
                Symbol = symbol,
                TradingPairId = tradingPairId,
                CreatedAt = now,
                UpdatedAt = now
            };

            return aggregate;
        }

        /// <summary>
        /// 从现有数据重建聚合根
        /// </summary>
        public static TradingAggregate FromExisting(int id, string symbol, int tradingPairId, 
            IEnumerable<Order> orders, IEnumerable<Trade> trades)
        {
            var now = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
            var aggregate = new TradingAggregate
            {
                Id = id,
                Symbol = symbol,
                TradingPairId = tradingPairId,
                CreatedAt = now,
                UpdatedAt = now
            };

            aggregate._orders.AddRange(orders);
            aggregate._trades.AddRange(trades);

            return aggregate;
        }

        /// <summary>
        /// 创建订单
        /// </summary>
        public Order CreateOrder(int? userId, string orderId, OrderSide side, OrderType type, 
            decimal quantity, decimal? price = null)
        {
            // 业务规则验证
            ValidateOrderCreation(userId, side, type, quantity, price);

            var order = Order.CreateOrder(
                userId,
                TradingPairId,
                orderId,
                side,
                type,
                quantity,
                price);

            _orders.Add(order);
            UpdatedAt = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();

            // 添加聚合事件
            AddDomainEvent(new OrderCreatedEvent(order));

            return order;
        }

        /// <summary>
        /// 匹配订单
        /// </summary>
        public Trade? MatchOrders(Order buyOrder, Order sellOrder)
        {
            if (buyOrder == null || sellOrder == null)
                throw new ArgumentNullException("订单不能为空");

            if (!buyOrder.CanMatchWith(sellOrder))
                return null;

            var matchQuantity = buyOrder.CalculateMatchQuantity(sellOrder);
            var matchPrice = buyOrder.CalculateMatchPrice(sellOrder);

            if (matchQuantity <= 0)
                return null;

            // 创建交易
            var trade = CreateTrade(buyOrder, sellOrder, matchQuantity, matchPrice);

            // 更新订单状态
            buyOrder.PartialFill(matchQuantity, matchPrice);
            sellOrder.PartialFill(matchQuantity, matchPrice);

            // 如果订单完全成交，从活跃订单列表中移除
            if (buyOrder.IsFilled())
            {
                _orders.Remove(buyOrder);
            }
            if (sellOrder.IsFilled())
            {
                _orders.Remove(sellOrder);
            }

            _trades.Add(trade);
            UpdatedAt = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();

            // 添加聚合事件
            AddDomainEvent(new TradeExecutedEvent(trade));

            return trade;
        }

        /// <summary>
        /// 取消订单
        /// </summary>
        public bool CancelOrder(int orderId)
        {
            var order = _orders.FirstOrDefault(o => o.Id == orderId);
            if (order == null)
                return false;

            if (!order.CanCancel())
                return false;

            order.Cancel();
            _orders.Remove(order);
            UpdatedAt = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();

            return true;
        }

        /// <summary>
        /// 获取活跃订单
        /// </summary>
        public IEnumerable<Order> GetActiveOrders()
        {
            return _orders.Where(o => o.IsActive());
        }

        /// <summary>
        /// 获取买单
        /// </summary>
        public IEnumerable<Order> GetBuyOrders()
        {
            return _orders.Where(o => o.Side == OrderSide.Buy && o.IsActive())
                         .OrderByDescending(o => o.Price);
        }

        /// <summary>
        /// 获取卖单
        /// </summary>
        public IEnumerable<Order> GetSellOrders()
        {
            return _orders.Where(o => o.Side == OrderSide.Sell && o.IsActive())
                         .OrderBy(o => o.Price);
        }

        /// <summary>
        /// 获取最佳买价
        /// </summary>
        public decimal? GetBestBidPrice()
        {
            return GetBuyOrders().FirstOrDefault()?.Price;
        }

        /// <summary>
        /// 获取最佳卖价
        /// </summary>
        public decimal? GetBestAskPrice()
        {
            return GetSellOrders().FirstOrDefault()?.Price;
        }

        /// <summary>
        /// 获取买卖价差
        /// </summary>
        public decimal? GetSpread()
        {
            var bestBid = GetBestBidPrice();
            var bestAsk = GetBestAskPrice();

            if (bestBid.HasValue && bestAsk.HasValue)
            {
                return bestAsk.Value - bestBid.Value;
            }

            return null;
        }

        /// <summary>
        /// 获取24小时交易量
        /// </summary>
        public decimal Get24HourVolume()
        {
            var yesterday = DateTime.UtcNow.AddDays(-1);
            var yesterdayTimestamp = ((DateTimeOffset)yesterday).ToUnixTimeMilliseconds();
            return _trades.Where(t => t.ExecutedAt >= yesterdayTimestamp)
                         .Sum(t => t.Quantity * t.Price);
        }

        /// <summary>
        /// 获取24小时交易次数
        /// </summary>
        public int Get24HourTradeCount()
        {
            var yesterday = DateTime.UtcNow.AddDays(-1);
            var yesterdayTimestamp = ((DateTimeOffset)yesterday).ToUnixTimeMilliseconds();
            return _trades.Count(t => t.ExecutedAt >= yesterdayTimestamp);
        }

        /// <summary>
        /// 清除领域事件
        /// </summary>
        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }

        private void AddDomainEvent(IDomainEvent domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }

        private void ValidateOrderCreation(int? userId, OrderSide side, OrderType type, 
            decimal quantity, decimal? price)
        {
            if (quantity <= 0)
                throw new ArgumentException("订单数量必须大于0");

            if (type == OrderType.Limit && (!price.HasValue || price.Value <= 0))
                throw new ArgumentException("限价单必须指定有效价格");

            // 可以添加更多业务规则验证
            // 例如：用户权限检查、交易对状态检查等
        }

        private Trade CreateTrade(Order buyOrder, Order sellOrder, decimal quantity, decimal price)
        {
            var trade = new Trade
            {
                TradingPairId = TradingPairId,
                BuyOrderId = buyOrder.Id,
                SellOrderId = sellOrder.Id,
                BuyerId = buyOrder.UserId ?? 0,
                SellerId = sellOrder.UserId ?? 0,
                Quantity = quantity,
                Price = price,
                Fee = CalculateFee(quantity, price),
                ExecutedAt = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds(),
                CreatedAt = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds()
            };

            return trade;
        }

        private decimal CalculateFee(decimal quantity, decimal price)
        {
            // 简单的费率计算，实际应该根据用户等级、交易对等计算
            var tradeValue = quantity * price;
            return tradeValue * 0.001m; // 0.1% 费率
        }
    }
}
