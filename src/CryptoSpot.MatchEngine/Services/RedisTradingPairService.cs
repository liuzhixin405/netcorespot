using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Redis;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.MatchEngine.Services
{
    /// <summary>
    /// 简化的基于 Redis 缓存的交易对服务
    /// 撮合引擎专用，用于从 Redis 读取交易对配置
    /// </summary>
    public class RedisTradingPairService : ITradingPairService
    {
        private readonly IRedisCache _redisCache;
        private readonly ILogger<RedisTradingPairService> _logger;
        private const string TradingPairCacheKeyPrefix = "CryptoSpot:trading_pair:";
        private const string TradingPairSymbolIndexKey = "CryptoSpot:trading_pair:symbol_index";

        public RedisTradingPairService(
            IRedisCache redisCache,
            ILogger<RedisTradingPairService> logger)
        {
            _redisCache = redisCache;
            _logger = logger;
        }

        public async Task<ApiResponseDto<TradingPairDto?>> GetTradingPairAsync(string symbol)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    return ApiResponseDto<TradingPairDto?>.CreateFailure("交易对符号不能为空");
                }

                // 从 Redis Hash 获取交易对 ID
                var tradingPairId = await _redisCache.HGetAsync<long>(TradingPairSymbolIndexKey, symbol);
                
                if (tradingPairId == 0)
                {
                    _logger.LogWarning("交易对未找到: {Symbol}", symbol);
                    return ApiResponseDto<TradingPairDto?>.CreateFailure($"交易对 {symbol} 不存在");
                }

                // 获取交易对详细信息
                var cacheKey = $"{TradingPairCacheKeyPrefix}{tradingPairId}";
                var tradingPairData = await _redisCache.HGetAllAsync(cacheKey);

                if (tradingPairData == null || tradingPairData.Count == 0)
                {
                    _logger.LogWarning("交易对数据未找到: {Symbol} (ID: {TradingPairId})", symbol, tradingPairId);
                    return ApiResponseDto<TradingPairDto?>.CreateFailure($"交易对 {symbol} 数据不存在");
                }

                var dto = MapToDto(tradingPairData);
                return ApiResponseDto<TradingPairDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易对失败: {Symbol}", symbol);
                return ApiResponseDto<TradingPairDto?>.CreateFailure($"获取交易对失败: {ex.Message}");
            }
        }

        public async Task<ApiResponseDto<TradingPairDto?>> GetTradingPairByIdAsync(long tradingPairId)
        {
            try
            {
                if (tradingPairId <= 0)
                {
                    return ApiResponseDto<TradingPairDto?>.CreateFailure("交易对ID无效");
                }

                var cacheKey = $"{TradingPairCacheKeyPrefix}{tradingPairId}";
                var tradingPairData = await _redisCache.HGetAllAsync(cacheKey);

                if (tradingPairData == null || tradingPairData.Count == 0)
                {
                    _logger.LogWarning("交易对数据未找到: ID {TradingPairId}", tradingPairId);
                    return ApiResponseDto<TradingPairDto?>.CreateFailure($"交易对 ID {tradingPairId} 不存在");
                }

                var dto = MapToDto(tradingPairData);
                return ApiResponseDto<TradingPairDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易对失败: ID {TradingPairId}", tradingPairId);
                return ApiResponseDto<TradingPairDto?>.CreateFailure($"获取交易对失败: {ex.Message}");
            }
        }

        public async Task<ApiResponseDto<long>> GetTradingPairIdAsync(string symbol)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    return ApiResponseDto<long>.CreateFailure("交易对符号不能为空");
                }

                var tradingPairId = await _redisCache.HGetAsync<long>(TradingPairSymbolIndexKey, symbol);
                
                if (tradingPairId == 0)
                {
                    _logger.LogWarning("交易对ID未找到: {Symbol}", symbol);
                    return ApiResponseDto<long>.CreateFailure($"交易对 {symbol} 不存在");
                }

                return ApiResponseDto<long>.CreateSuccess(tradingPairId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易对ID失败: {Symbol}", symbol);
                return ApiResponseDto<long>.CreateFailure($"获取交易对ID失败: {ex.Message}");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetActiveTradingPairsAsync()
        {
            try
            {
                // 简化实现：返回空列表
                // 实际环境中需要实现 SetMembers 方法或使用其他方式
                _logger.LogWarning("GetActiveTradingPairsAsync 未完全实现，返回空列表");
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateSuccess(new List<TradingPairDto>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃交易对列表失败");
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateFailure($"获取活跃交易对失败: {ex.Message}");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetTopTradingPairsAsync(int count = 10)
        {
            try
            {
                // 简化实现：返回空列表
                // 实际环境中需要实现 SortedSetRangeByRank 方法
                _logger.LogWarning("GetTopTradingPairsAsync 未完全实现，返回空列表");
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateSuccess(new List<TradingPairDto>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取热门交易对失败");
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateFailure($"获取热门交易对失败: {ex.Message}");
            }
        }

        public async Task<ApiResponseDto<bool>> UpdatePriceAsync(
            string symbol, 
            decimal price, 
            decimal change24h, 
            decimal volume24h, 
            decimal high24h, 
            decimal low24h)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    return ApiResponseDto<bool>.CreateFailure("交易对符号不能为空");
                }

                // 获取交易对 ID
                var tradingPairId = await _redisCache.HGetAsync<long>(TradingPairSymbolIndexKey, symbol);
                
                if (tradingPairId == 0)
                {
                    _logger.LogWarning("交易对未找到，无法更新价格: {Symbol}", symbol);
                    return ApiResponseDto<bool>.CreateFailure($"交易对 {symbol} 不存在");
                }

                // 更新价格信息
                var cacheKey = $"{TradingPairCacheKeyPrefix}{tradingPairId}";
                var keyValues = new object[]
                {
                    "Price", price.ToString("F8"),
                    "Change24h", change24h.ToString("F2"),
                    "Volume24h", volume24h.ToString("F8"),
                    "High24h", high24h.ToString("F8"),
                    "Low24h", low24h.ToString("F8"),
                    "LastUpdated", DateTime.UtcNow.ToString("O")
                };

                await _redisCache.HMSetAsync(cacheKey, keyValues);

                _logger.LogDebug("更新交易对价格成功: {Symbol}, Price={Price}", symbol, price);
                return ApiResponseDto<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新交易对价格失败: {Symbol}", symbol);
                return ApiResponseDto<bool>.CreateFailure($"更新价格失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从 Redis Hash 数据映射到 DTO
        /// </summary>
        private TradingPairDto MapToDto(Dictionary<string, string> data)
        {
            return new TradingPairDto
            {
                Id = long.TryParse(data.GetValueOrDefault("Id"), out var id) ? id : 0,
                Symbol = data.GetValueOrDefault("Symbol") ?? string.Empty,
                BaseAsset = data.GetValueOrDefault("BaseAsset") ?? string.Empty,
                QuoteAsset = data.GetValueOrDefault("QuoteAsset") ?? string.Empty,
                IsActive = bool.TryParse(data.GetValueOrDefault("IsActive"), out var isActive) && isActive,
                MinQuantity = decimal.TryParse(data.GetValueOrDefault("MinQuantity"), out var minQty) ? minQty : 0,
                MaxQuantity = decimal.TryParse(data.GetValueOrDefault("MaxQuantity"), out var maxQty) ? maxQty : 0,
                PricePrecision = int.TryParse(data.GetValueOrDefault("PricePrecision"), out var pricePrecision) ? pricePrecision : 8,
                QuantityPrecision = int.TryParse(data.GetValueOrDefault("QuantityPrecision"), out var qtyPrecision) ? qtyPrecision : 8,
                Price = decimal.TryParse(data.GetValueOrDefault("Price"), out var price) ? price : 0,
                Change24h = decimal.TryParse(data.GetValueOrDefault("Change24h"), out var change) ? change : 0,
                Change24hPercent = decimal.TryParse(data.GetValueOrDefault("Change24hPercent"), out var changePercent) ? changePercent : 0,
                Volume24h = decimal.TryParse(data.GetValueOrDefault("Volume24h"), out var volume) ? volume : 0,
                High24h = decimal.TryParse(data.GetValueOrDefault("High24h"), out var high) ? high : 0,
                Low24h = decimal.TryParse(data.GetValueOrDefault("Low24h"), out var low) ? low : 0,
                LastUpdated = DateTime.TryParse(data.GetValueOrDefault("LastUpdated"), out var lastUpdated) ? lastUpdated : DateTime.UtcNow
            };
        }
    }
}
