using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using CryptoSpot.Application.Abstractions.Services.Trading;

namespace CryptoSpot.Application.Mapping
{
    /// <summary>
    /// DTO映射服务实现
    /// </summary>
    public class DtoMappingService : IDtoMappingService
    {
        #region Order Mappings

        public OrderDto MapToDto(Order order)
        {
            return new OrderDto
            {
                Id = order.Id,
                OrderId = order.OrderId,
                ClientOrderId = order.ClientOrderId,
                UserId = order.UserId,
                Symbol = order.TradingPair?.Symbol ?? string.Empty,
                TradingPairId = order.TradingPairId,
                Side = order.Side, // 直接赋值 Domain 枚举
                Type = order.Type,
                Quantity = order.Quantity,
                Price = order.Price,
                FilledQuantity = order.FilledQuantity,
                RemainingQuantity = order.RemainingQuantity,
                AveragePrice = order.AveragePrice,
                Status = order.Status,
                CreatedAt = DateTimeExtensions.FromUnixTimeMilliseconds(order.CreatedAt),
                UpdatedAt = DateTimeExtensions.FromUnixTimeMilliseconds(order.UpdatedAt),
                TotalValue = order.TotalValue
            };
        }

        public Order MapToDomain(CreateOrderRequestDto orderRequest, long userId, long tradingPairId)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            return new Order
            {
                UserId = userId,
                TradingPairId = tradingPairId,
                OrderId = Guid.NewGuid().ToString("N")[..16], // 生成16位订单号
                ClientOrderId = orderRequest.ClientOrderId,
                Side = orderRequest.Side,
                Type = orderRequest.Type,
                Quantity = orderRequest.Quantity,
                Price = orderRequest.Price,
                Status = OrderStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        public IEnumerable<OrderDto> MapToDto(IEnumerable<Order> orders)
        {
            return orders.Select(MapToDto);
        }

        #endregion

        #region Trade Mappings

        public TradeDto MapToDto(Trade trade)
        {
            return new TradeDto
            {
                Id = trade.Id,
                TradeId = trade.TradeId,
                BuyOrderId = trade.BuyOrderId,
                SellOrderId = trade.SellOrderId,
                BuyerId = trade.BuyerId,
                SellerId = trade.SellerId,
                Symbol = trade.TradingPair?.Symbol ?? string.Empty,
                Price = trade.Price,
                Quantity = trade.Quantity,
                Fee = trade.Fee,
                FeeAsset = trade.FeeAsset,
                TotalValue = trade.TotalValue,
                ExecutedAt = trade.ExecutedDateTime
            };
        }

        public IEnumerable<TradeDto> MapToDto(IEnumerable<Trade> trades)
        {
            return trades.Select(MapToDto);
        }

        #endregion

        #region TradingPair Mappings

        public TradingPairDto MapToDto(TradingPair tradingPair)
        {
            return new TradingPairDto
            {
                Id = tradingPair.Id,
                Symbol = tradingPair.Symbol,
                BaseAsset = tradingPair.BaseAsset,
                QuoteAsset = tradingPair.QuoteAsset,
                Price = tradingPair.Price,
                Change24h = tradingPair.Change24h,
                Change24hPercent = tradingPair.Price > 0 ? (tradingPair.Change24h / tradingPair.Price) * 100 : 0,
                Volume24h = tradingPair.Volume24h,
                High24h = tradingPair.High24h,
                Low24h = tradingPair.Low24h,
                LastUpdated = tradingPair.LastUpdatedDateTime,
                IsActive = tradingPair.IsActive,
                MinQuantity = tradingPair.MinQuantity,
                MaxQuantity = tradingPair.MaxQuantity,
                PricePrecision = tradingPair.PricePrecision,
                QuantityPrecision = tradingPair.QuantityPrecision
            };
        }

        public TradingPairSummaryDto MapToSummaryDto(TradingPair tradingPair)
        {
            return new TradingPairSummaryDto
            {
                Symbol = tradingPair.Symbol,
                BaseAsset = tradingPair.BaseAsset,
                QuoteAsset = tradingPair.QuoteAsset,
                Price = tradingPair.Price,
                Change24hPercent = tradingPair.Price > 0 ? (tradingPair.Change24h / tradingPair.Price) * 100 : 0,
                IsActive = tradingPair.IsActive
            };
        }

        public IEnumerable<TradingPairDto> MapToDto(IEnumerable<TradingPair> tradingPairs)
        {
            return tradingPairs.Select(MapToDto);
        }

        public IEnumerable<TradingPairSummaryDto> MapToSummaryDto(IEnumerable<TradingPair> tradingPairs)
        {
            return tradingPairs.Select(MapToSummaryDto);
        }

        #endregion

        #region KLineData Mappings

        public KLineDataDto MapToDto(KLineData klineData, string symbol)
        {
            return new KLineDataDto
            {
                Id = klineData.Id,
                Symbol = symbol,
                TimeFrame = klineData.TimeFrame,
                OpenTime = klineData.OpenTime,
                CloseTime = klineData.CloseTime,
                Open = klineData.Open,
                High = klineData.High,
                Low = klineData.Low,
                Close = klineData.Close,
                Volume = klineData.Volume,
                OpenDateTime = klineData.OpenDateTime,
                CloseDateTime = klineData.CloseDateTime
            };
        }

        public IEnumerable<KLineDataDto> MapToDto(IEnumerable<KLineData> klineData, string symbol)
        {
            return klineData.Select(k => MapToDto(k, symbol));
        }

        #endregion

        #region User Mappings

        public UserDto MapToDto(User user)
        {
            return new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Type = (UserTypeDto)user.Type,
                Description = user.Description,
                IsAutoTradingEnabled = user.IsAutoTradingEnabled,
                MaxRiskRatio = user.MaxRiskRatio,
                DailyTradingLimit = user.DailyTradingLimit,
                DailyTradedAmount = user.DailyTradedAmount,
                IsSystemAccount = user.IsSystemAccount,
                LastLoginAt = user.LastLoginAt.HasValue ? DateTimeExtensions.FromUnixTimeMilliseconds(user.LastLoginAt.Value) : null,
                CreatedAt = DateTimeExtensions.FromUnixTimeMilliseconds(user.CreatedAt),
                UpdatedAt = DateTimeExtensions.FromUnixTimeMilliseconds(user.UpdatedAt)
            };
        }

        public User MapToDomain(CreateUserRequestDto userRequest)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            return new User
            {
                Username = userRequest.Username,
                Email = userRequest.Email,
                Type = (UserType)userRequest.Type,
                Description = userRequest.Description,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        public IEnumerable<UserDto> MapToDto(IEnumerable<User> users)
        {
            return users.Select(MapToDto);
        }

        #endregion

        #region Asset Mappings

        public AssetDto MapToDto(Asset asset)
        {
            return new AssetDto
            {
                Id = asset.Id,
                UserId = asset.UserId,
                Symbol = asset.Symbol,
                Available = asset.Available,
                Frozen = asset.Frozen,
                Total = asset.Total,
                MinReserve = asset.MinReserve,
                TargetBalance = asset.TargetBalance,
                AutoRefillEnabled = asset.AutoRefillEnabled,
                UsableBalance = asset.UsableBalance,
                IsSystemAsset = asset.IsSystemAsset,
                UpdatedAt = DateTimeExtensions.FromUnixTimeMilliseconds(asset.UpdatedAt)
            };
        }

        public IEnumerable<AssetDto> MapToDto(IEnumerable<Asset> assets)
        {
            return assets.Select(MapToDto);
        }

        #endregion

        #region OrderBook Mappings

        public OrderBookDepthDto MapToDto(OrderBookDepth orderBook)
        {
            return new OrderBookDepthDto
            {
                Symbol = orderBook.Symbol,
                Bids = orderBook.Bids.Select(MapToDto).ToList(),
                Asks = orderBook.Asks.Select(MapToDto).ToList(),
                Timestamp = orderBook.Timestamp
            };
        }

        public OrderBookLevelDto MapToDto(OrderBookLevel orderBookLevel)
        {
            return new OrderBookLevelDto
            {
                Price = orderBookLevel.Price,
                Quantity = orderBookLevel.Quantity,
                Total = orderBookLevel.Total,
                OrderCount = orderBookLevel.OrderCount
            };
        }

        // 新增: 批量层级映射
        public IEnumerable<OrderBookLevelDto> MapToDto(IEnumerable<OrderBookLevel> levels)
        {
            return levels.Select(MapToDto);
        }

        // 新增: 组合快捷方法，返回 bids / asks 两组 DTO
        public (IEnumerable<OrderBookLevelDto> bids, IEnumerable<OrderBookLevelDto> asks) MapOrderBookLevels(
            IEnumerable<OrderBookLevel> bidLevels,
            IEnumerable<OrderBookLevel> askLevels)
        {
            return (MapToDto(bidLevels), MapToDto(askLevels));
        }

        #endregion
    }
}
