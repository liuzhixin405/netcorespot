using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using CryptoSpot.Application.Abstractions.Services.Trading;

namespace CryptoSpot.Application.Mapping
{
    /// <summary>
    /// 实体映射服务接口
    /// </summary>
    public interface IDtoMappingService
    {
        // Order mappings
        OrderDto MapToDto(Order order);
        Order MapToDomain(CreateOrderRequestDto orderRequest, long userId, long tradingPairId);
        IEnumerable<OrderDto> MapToDto(IEnumerable<Order> orders);

        // Trade mappings
        TradeDto MapToDto(Trade trade);
        IEnumerable<TradeDto> MapToDto(IEnumerable<Trade> trades);

        // TradingPair mappings
        TradingPairDto MapToDto(TradingPair tradingPair);
        TradingPairSummaryDto MapToSummaryDto(TradingPair tradingPair);
        IEnumerable<TradingPairDto> MapToDto(IEnumerable<TradingPair> tradingPairs);
        IEnumerable<TradingPairSummaryDto> MapToSummaryDto(IEnumerable<TradingPair> tradingPairs);

        // KLineData mappings
        KLineDataDto MapToDto(KLineData klineData, string symbol);
        IEnumerable<KLineDataDto> MapToDto(IEnumerable<KLineData> klineData, string symbol);

        // User mappings
        UserDto MapToDto(User user);
        UserSummaryDto MapToSummaryDto(User user);
        User MapToDomain(CreateUserRequestDto userRequest);
        IEnumerable<UserDto> MapToDto(IEnumerable<User> users);

        // Asset mappings
        AssetDto MapToDto(Asset asset);
        IEnumerable<AssetDto> MapToDto(IEnumerable<Asset> assets);

        // OrderBook mappings
        OrderBookDepthDto MapToDto(OrderBookDepth orderBook);
        OrderBookLevelDto MapToDto(OrderBookLevel orderBookLevel);
        // 新增: OrderBook 批量/增量映射辅助
        IEnumerable<OrderBookLevelDto> MapToDto(IEnumerable<OrderBookLevel> levels);
        (IEnumerable<OrderBookLevelDto> bids, IEnumerable<OrderBookLevelDto> asks) MapOrderBookLevels(
            IEnumerable<OrderBookLevel> bidLevels,
            IEnumerable<OrderBookLevel> askLevels);
    }
}
