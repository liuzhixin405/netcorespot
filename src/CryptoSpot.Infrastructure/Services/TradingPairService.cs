using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.Mapping;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services;

/// <summary>
/// 交易对服务实现（简化版 - 纯数据库）
/// </summary>
public class TradingPairService : ITradingPairService
{
    private readonly ITradingPairRepository _repository;
    private readonly IDtoMappingService _mapping;
    private readonly ILogger<TradingPairService> _logger;

    public TradingPairService(
        ITradingPairRepository repository,
        IDtoMappingService mapping,
        ILogger<TradingPairService> logger)
    {
        _repository = repository;
        _mapping = mapping;
        _logger = logger;
    }

    public async Task<ApiResponseDto<TradingPairDto?>> GetTradingPairAsync(string symbol)
    {
        try
        {
            var pair = await _repository.GetBySymbolAsync(symbol);
            if (pair == null)
            {
                return ApiResponseDto<TradingPairDto?>.CreateError($"交易对 {symbol} 不存在");
            }

            var dto = _mapping.MapToDto(pair);
            return ApiResponseDto<TradingPairDto?>.CreateSuccess(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取交易对失败: {Symbol}", symbol);
            return ApiResponseDto<TradingPairDto?>.CreateError("获取交易对失败");
        }
    }

    public async Task<ApiResponseDto<TradingPairDto?>> GetTradingPairByIdAsync(long tradingPairId)
    {
        try
        {
            var pair = await _repository.GetByIdAsync(tradingPairId);
            if (pair == null)
            {
                return ApiResponseDto<TradingPairDto?>.CreateError($"交易对 ID {tradingPairId} 不存在");
            }

            var dto = _mapping.MapToDto(pair);
            return ApiResponseDto<TradingPairDto?>.CreateSuccess(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取交易对失败: {TradingPairId}", tradingPairId);
            return ApiResponseDto<TradingPairDto?>.CreateError("获取交易对失败");
        }
    }

    public async Task<ApiResponseDto<long>> GetTradingPairIdAsync(string symbol)
    {
        try
        {
            var pair = await _repository.GetBySymbolAsync(symbol);
            if (pair == null)
            {
                return ApiResponseDto<long>.CreateError($"交易对 {symbol} 不存在");
            }

            return ApiResponseDto<long>.CreateSuccess(pair.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取交易对ID失败: {Symbol}", symbol);
            return ApiResponseDto<long>.CreateError("获取交易对ID失败");
        }
    }

    public async Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetActiveTradingPairsAsync()
    {
        try
        {
            var pairs = await _repository.GetActiveTradingPairsAsync();
            var dtos = pairs.Select(p => _mapping.MapToDto(p)).ToList();
            return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateSuccess(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取活跃交易对失败");
            return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateError("获取活跃交易对失败");
        }
    }

    public async Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetTopTradingPairsAsync(int count = 10)
    {
        try
        {
            var pairs = await _repository.GetTopTradingPairsAsync(count);
            var dtos = pairs.Select(p => _mapping.MapToDto(p)).ToList();
            return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateSuccess(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取热门交易对失败");
            return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateError("获取热门交易对失败");
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
            var pair = await _repository.GetBySymbolAsync(symbol);
            if (pair == null)
            {
                return ApiResponseDto<bool>.CreateError($"交易对 {symbol} 不存在");
            }

            pair.Price = price;
            pair.Change24h = change24h;
            pair.Volume24h = volume24h;
            pair.High24h = high24h;
            pair.Low24h = low24h;
            pair.Touch();

            await _repository.UpdateAsync(pair);
            return ApiResponseDto<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新交易对价格失败: {Symbol}", symbol);
            return ApiResponseDto<bool>.CreateError("更新交易对价格失败");
        }
    }
}
