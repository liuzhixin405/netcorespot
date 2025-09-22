using CryptoSpot.Core.Events;
using CryptoSpot.Core.Events.Trading;
using CryptoSpot.Core.Interfaces.Caching;
using CryptoSpot.Core.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.EventHandlers.Trading
{
    /// <summary>
    /// 交易事件处理器 - 处理交易相关的领域事件
    /// </summary>
    public class TradingEventHandler : 
        IDomainEventHandler<OrderCreatedEvent>,
        IDomainEventHandler<OrderStatusChangedEvent>,
        IDomainEventHandler<TradeExecutedEvent>,
        IDomainEventHandler<PriceUpdatedEvent>,
        IDomainEventHandler<KLineDataUpdatedEvent>,
        IDomainEventHandler<AssetBalanceChangedEvent>
    {
        private readonly ICacheService _cacheService;
        private readonly ILogger<TradingEventHandler> _logger;

        public TradingEventHandler(
            ICacheService cacheService,
            ILogger<TradingEventHandler> logger)
        {
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task HandleAsync(OrderCreatedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Order created: {OrderId} for user {UserId} on {Symbol}", 
                    domainEvent.OrderId, domainEvent.UserId, domainEvent.Symbol);

                // 更新缓存中的订单信息
                await _cacheService.InvalidateUserOrdersCacheAsync(domainEvent.UserId);
                
                // 可以添加其他业务逻辑，如通知、审计等
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OrderCreatedEvent for order {OrderId}", domainEvent.OrderId);
            }
        }

        public async Task HandleAsync(OrderStatusChangedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Order status changed: {OrderId} from {OldStatus} to {NewStatus}", 
                    domainEvent.OrderId, domainEvent.OldStatus, domainEvent.NewStatus);

                // 更新缓存
                await _cacheService.InvalidateUserOrdersCacheAsync(domainEvent.UserId);
                
                // 如果订单完全成交，更新用户资产缓存
                if (domainEvent.NewStatus == OrderStatus.Filled)
                {
                    await _cacheService.InvalidateUserAssetsCacheAsync(domainEvent.UserId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OrderStatusChangedEvent for order {OrderId}", domainEvent.OrderId);
            }
        }

        public async Task HandleAsync(TradeExecutedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Trade executed: {TradeId} on {Symbol} - {Quantity} @ {Price}", 
                    domainEvent.TradeId, domainEvent.Symbol, domainEvent.Quantity, domainEvent.Price);

                // 更新交易对缓存
                await _cacheService.InvalidateTradingPairCacheAsync(domainEvent.Symbol);
                
                // 更新用户资产缓存
                await _cacheService.InvalidateUserAssetsCacheAsync(domainEvent.BuyerId);
                await _cacheService.InvalidateUserAssetsCacheAsync(domainEvent.SellerId);
                
                // 更新用户交易历史缓存
                await _cacheService.InvalidateUserTradesCacheAsync(domainEvent.BuyerId);
                await _cacheService.InvalidateUserTradesCacheAsync(domainEvent.SellerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling TradeExecutedEvent for trade {TradeId}", domainEvent.TradeId);
            }
        }

        public async Task HandleAsync(PriceUpdatedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Price updated: {Symbol} = {Price} (24h: {Change24h:F2}%)", 
                    domainEvent.Symbol, domainEvent.Price, domainEvent.Change24h);

                // 更新交易对价格缓存
                await _cacheService.UpdateTradingPairPriceAsync(
                    domainEvent.Symbol,
                    domainEvent.Price,
                    domainEvent.Change24h,
                    domainEvent.Volume24h,
                    domainEvent.High24h,
                    domainEvent.Low24h);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling PriceUpdatedEvent for {Symbol}", domainEvent.Symbol);
            }
        }

        public async Task HandleAsync(KLineDataUpdatedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("KLine data updated: {Symbol} {TimeFrame} @ {Timestamp}", 
                    domainEvent.Symbol, domainEvent.TimeFrame, domainEvent.Timestamp);

                // 更新K线数据缓存
                var klineData = new KLineData
                {
                    TimeFrame = domainEvent.TimeFrame,
                    OpenTime = domainEvent.Timestamp,
                    Open = domainEvent.Open,
                    High = domainEvent.High,
                    Low = domainEvent.Low,
                    Close = domainEvent.Close,
                    Volume = domainEvent.Volume
                };
                await _cacheService.UpdateKLineDataCacheAsync(klineData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling KLineDataUpdatedEvent for {Symbol} {TimeFrame}", 
                    domainEvent.Symbol, domainEvent.TimeFrame);
            }
        }

        public async Task HandleAsync(AssetBalanceChangedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Asset balance changed: User {UserId} {AssetSymbol} {OldBalance} -> {NewBalance} ({Reason})", 
                    domainEvent.UserId, domainEvent.AssetSymbol, domainEvent.OldBalance, domainEvent.NewBalance, domainEvent.ChangeReason);

                // 更新用户资产缓存
                await _cacheService.InvalidateUserAssetsCacheAsync(domainEvent.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling AssetBalanceChangedEvent for user {UserId} asset {AssetSymbol}", 
                    domainEvent.UserId, domainEvent.AssetSymbol);
            }
        }
    }
}
