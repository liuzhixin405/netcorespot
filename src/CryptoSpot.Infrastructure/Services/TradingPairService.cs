using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories; // replaced Core.Interfaces.Repositories
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;

namespace CryptoSpot.Infrastructure.Services
{
    public class TradingPairService : ITradingPairService, IDisposable
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly RedisCacheService _cacheService;
        private readonly ILogger<TradingPairService> _logger;
        private readonly IDtoMappingService _mapping;

        public TradingPairService(
            ITradingPairRepository tradingPairRepository,
            RedisCacheService cacheService,
            ILogger<TradingPairService> logger,
            IDtoMappingService mapping)
        {
            _tradingPairRepository = tradingPairRepository;
            _cacheService = cacheService;
            _logger = logger;
            _mapping = mapping;
        }

        public async Task<ApiResponseDto<TradingPairDto?>> GetTradingPairAsync(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return ApiResponseDto<TradingPairDto?>.CreateError("Symbol 不能为空");
            try
            {
                var cached = await _cacheService.GetTradingPairAsync(symbol);
                if (cached != null)
                {
                    return ApiResponseDto<TradingPairDto?>.CreateSuccess(_mapping.MapToDto(cached));
                }

                var entity = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (entity == null)
                    return ApiResponseDto<TradingPairDto?>.CreateError("交易对不存在", "TRADING_PAIR_NOT_FOUND");

                await _cacheService.SetTradingPairAsync(entity); // 缓存原始实体
                return ApiResponseDto<TradingPairDto?>.CreateSuccess(_mapping.MapToDto(entity));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易对失败: {Symbol}", symbol);
                return ApiResponseDto<TradingPairDto?>.CreateError("获取交易对失败", "TRADING_PAIR_ERROR");
            }
        }

        public async Task<ApiResponseDto<TradingPairDto?>> GetTradingPairByIdAsync(int tradingPairId)
        {
            if (tradingPairId <= 0)
                return ApiResponseDto<TradingPairDto?>.CreateError("TradingPairId 无效");
            try
            {
                var entity = await _tradingPairRepository.GetByIdAsync(tradingPairId);
                if (entity == null)
                    return ApiResponseDto<TradingPairDto?>.CreateError("交易对不存在", "TRADING_PAIR_NOT_FOUND");
                await _cacheService.SetTradingPairAsync(entity);
                return ApiResponseDto<TradingPairDto?>.CreateSuccess(_mapping.MapToDto(entity));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据ID获取交易对失败: {TradingPairId}", tradingPairId);
                return ApiResponseDto<TradingPairDto?>.CreateError("获取交易对失败", "TRADING_PAIR_ERROR");
            }
        }

        public async Task<ApiResponseDto<int>> GetTradingPairIdAsync(string symbol)
        {
            try
            {
                var resp = await GetTradingPairAsync(symbol);
                if (!resp.Success || resp.Data == null)
                    return ApiResponseDto<int>.CreateError(resp.Error ?? "交易对不存在", resp.ErrorCode);
                return ApiResponseDto<int>.CreateSuccess(resp.Data.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易对ID失败: {Symbol}", symbol);
                return ApiResponseDto<int>.CreateError("获取交易对ID失败", "TRADING_PAIR_ERROR");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetActiveTradingPairsAsync()
        {
            try
            {
                // 先尝试从缓存批量取
                var cachedList = await _cacheService.GetAllTradingPairsAsync();
                IEnumerable<TradingPair> entities;
                if (cachedList.Any())
                {
                    entities = cachedList.Where(tp => tp.IsActive);
                }
                else
                {
                    entities = await _tradingPairRepository.FindAsync(tp => tp.IsActive);
                    foreach (var e in entities)
                        await _cacheService.SetTradingPairAsync(e);
                }
                var dtos = entities.Select(_mapping.MapToDto).ToList();
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateSuccess(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃交易对失败");
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateError("获取活跃交易对失败", "TRADING_PAIR_ERROR");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetTopTradingPairsAsync(int count = 10)
        {
            if (count <= 0) count = 10;
            try
            {
                // 复用 Active 集合
                var activeResp = await GetActiveTradingPairsAsync();
                if (!activeResp.Success || activeResp.Data == null)
                    return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateError(activeResp.Error ?? "获取交易对失败", activeResp.ErrorCode);
                var list = activeResp.Data.OrderByDescending(p => p.Volume24h).Take(count).ToList();
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateSuccess(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取热门交易对失败");
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateError("获取热门交易对失败", "TRADING_PAIR_ERROR");
            }
        }

        public async Task<ApiResponseDto<bool>> UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            try
            {
                var entity = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (entity == null)
                    return ApiResponseDto<bool>.CreateError("交易对不存在", "TRADING_PAIR_NOT_FOUND");

                entity.Price = price;
                entity.Change24h = change24h;
                entity.Volume24h = volume24h;
                entity.High24h = high24h;
                entity.Low24h = low24h;
                entity.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                await _tradingPairRepository.UpdateAsync(entity);
                await _cacheService.SetTradingPairAsync(entity);
                return ApiResponseDto<bool>.CreateSuccess(true, "更新成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新交易对价格失败: {Symbol}", symbol);
                return ApiResponseDto<bool>.CreateError("更新价格失败", "TRADING_PAIR_ERROR");
            }
        }

        public void Dispose()
        {
            // nothing
        }
    }
}
