using CryptoSpot.Core.Events;
using CryptoSpot.Core.Events.Trading;
using CryptoSpot.Core.ValueObjects;

namespace CryptoSpot.Core.Entities
{
    /// <summary>
    /// 增强的订单实体 - 包含业务逻辑和行为
    /// </summary>
    public partial class Order
    {
        private readonly List<IDomainEvent> _domainEvents = new();

        /// <summary>
        /// 领域事件集合
        /// </summary>
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        /// <summary>
        /// 清除领域事件
        /// </summary>
        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }

        /// <summary>
        /// 添加领域事件
        /// </summary>
        protected void AddDomainEvent(IDomainEvent domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }

        /// <summary>
        /// 创建新订单
        /// </summary>
        public static Order CreateOrder(
            int? userId,
            int tradingPairId,
            string orderId,
            OrderSide side,
            OrderType type,
            decimal quantity,
            decimal? price = null)
        {
            if (quantity <= 0)
                throw new ArgumentException("订单数量必须大于0", nameof(quantity));

            if (type == OrderType.Limit && (!price.HasValue || price.Value <= 0))
                throw new ArgumentException("限价单必须指定有效价格", nameof(price));

            var now = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
            var order = new Order
            {
                UserId = userId,
                TradingPairId = tradingPairId,
                OrderId = orderId,
                Side = side,
                Type = type,
                Quantity = quantity,
                Price = price,
                Status = OrderStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };

            // 添加订单创建事件
            order.AddDomainEvent(new OrderCreatedEvent(order));

            return order;
        }

        /// <summary>
        /// 更新订单状态
        /// </summary>
        public void UpdateStatus(OrderStatus newStatus)
        {
            if (Status == newStatus)
                return;

            var oldStatus = Status;
            Status = newStatus;
            UpdatedAt = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();

            // 添加状态变更事件
            AddDomainEvent(new OrderStatusChangedEvent(this, oldStatus));
        }

        /// <summary>
        /// 部分成交
        /// </summary>
        public void PartialFill(decimal fillQuantity, decimal fillPrice)
        {
            if (fillQuantity <= 0)
                throw new ArgumentException("成交数量必须大于0", nameof(fillQuantity));

            if (fillPrice <= 0)
                throw new ArgumentException("成交价格必须大于0", nameof(fillPrice));

            if (fillQuantity > RemainingQuantity)
                throw new ArgumentException("成交数量不能超过剩余数量");

            var oldStatus = Status;
            FilledQuantity += fillQuantity;
            
            // 计算平均价格
            if (FilledQuantity > 0)
            {
                var totalValue = (FilledQuantity - fillQuantity) * AveragePrice + fillQuantity * fillPrice;
                AveragePrice = totalValue / FilledQuantity;
            }

            // 检查是否完全成交
            if (FilledQuantity >= Quantity)
            {
                Status = OrderStatus.Filled;
            }
            else
            {
                Status = OrderStatus.PartiallyFilled;
            }

            UpdatedAt = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();

            // 添加状态变更事件
            if (oldStatus != Status)
            {
                AddDomainEvent(new OrderStatusChangedEvent(this, oldStatus));
            }
        }

        /// <summary>
        /// 取消订单
        /// </summary>
        public void Cancel()
        {
            if (Status == OrderStatus.Filled)
                throw new InvalidOperationException("已完全成交的订单不能取消");

            if (Status == OrderStatus.Cancelled)
                throw new InvalidOperationException("订单已经取消");

            UpdateStatus(OrderStatus.Cancelled);
        }

        /// <summary>
        /// 拒绝订单
        /// </summary>
        public void Reject(string reason = "")
        {
            if (Status != OrderStatus.Pending)
                throw new InvalidOperationException("只能拒绝待处理状态的订单");

            UpdateStatus(OrderStatus.Rejected);
        }

        /// <summary>
        /// 检查订单是否可以取消
        /// </summary>
        public bool CanCancel()
        {
            return Status == OrderStatus.Pending || Status == OrderStatus.PartiallyFilled;
        }

        /// <summary>
        /// 检查订单是否活跃（可以匹配）
        /// </summary>
        public bool IsActive()
        {
            return Status == OrderStatus.Pending || Status == OrderStatus.PartiallyFilled;
        }

        /// <summary>
        /// 检查订单是否完全成交
        /// </summary>
        public bool IsFilled()
        {
            return Status == OrderStatus.Filled;
        }

        /// <summary>
        /// 检查订单是否已取消
        /// </summary>
        public bool IsCancelled()
        {
            return Status == OrderStatus.Cancelled;
        }

        /// <summary>
        /// 检查订单是否被拒绝
        /// </summary>
        public bool IsRejected()
        {
            return Status == OrderStatus.Rejected;
        }

        /// <summary>
        /// 获取订单总价值
        /// </summary>
        public decimal GetTotalValue()
        {
            if (Type == OrderType.Market)
                return 0; // 市价单无法计算总价值

            return Quantity * (Price ?? 0);
        }

        /// <summary>
        /// 获取已成交价值
        /// </summary>
        public decimal GetFilledValue()
        {
            return FilledQuantity * AveragePrice;
        }

        /// <summary>
        /// 获取剩余价值
        /// </summary>
        public decimal GetRemainingValue()
        {
            if (Type == OrderType.Market)
                return 0;

            return RemainingQuantity * (Price ?? 0);
        }

        /// <summary>
        /// 检查订单是否匹配
        /// </summary>
        public bool CanMatchWith(Order otherOrder)
        {
            if (otherOrder == null)
                return false;

            // 不能与自己匹配
            if (Id == otherOrder.Id)
                return false;

            // 必须是不同的方向
            if (Side == otherOrder.Side)
                return false;

            // 必须是同一个交易对
            if (TradingPairId != otherOrder.TradingPairId)
                return false;

            // 两个订单都必须是活跃状态
            if (!IsActive() || !otherOrder.IsActive())
                return false;

            // 价格匹配检查
            if (Side == OrderSide.Buy)
            {
                // 买单价格必须 >= 卖单价格
                return (Price ?? decimal.MaxValue) >= (otherOrder.Price ?? 0);
            }
            else
            {
                // 卖单价格必须 <= 买单价格
                return (Price ?? 0) <= (otherOrder.Price ?? decimal.MaxValue);
            }
        }

        /// <summary>
        /// 计算与另一个订单的匹配数量
        /// </summary>
        public decimal CalculateMatchQuantity(Order otherOrder)
        {
            if (!CanMatchWith(otherOrder))
                return 0;

            return Math.Min(RemainingQuantity, otherOrder.RemainingQuantity);
        }

        /// <summary>
        /// 计算与另一个订单的匹配价格
        /// </summary>
        public decimal CalculateMatchPrice(Order otherOrder)
        {
            if (!CanMatchWith(otherOrder))
                return 0;

            // 价格优先原则：先到达的订单价格优先
            if (CreatedAt <= otherOrder.CreatedAt)
            {
                return Price ?? 0;
            }
            else
            {
                return otherOrder.Price ?? 0;
            }
        }
    }
}
