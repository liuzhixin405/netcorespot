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

    public Task<ApiResponseDto<TradingPairDto?>> GetTradingPairAsync(string symbol)
    {
        return ServiceHelper.ExecuteAsync<TradingPairDto?>(
            async () =>
            {
                var pair = await _repository.GetBySymbolAsync(symbol);
                return pair == null ? throw new InvalidOperationException($"交易对 {symbol} 不存在") : _mapping.MapToDto(pair);
            },
            _logger, "获取交易对失败");
    }

    public Task<ApiResponseDto<TradingPairDto?>> GetTradingPairByIdAsync(long tradingPairId)
    {
        return ServiceHelper.ExecuteAsync<TradingPairDto?>(
            async () =>
            {
                var pair = await _repository.GetByIdAsync(tradingPairId);
                return pair == null ? throw new InvalidOperationException($"交易对 ID {tradingPairId} 不存在") : _mapping.MapToDto(pair);
            },
            _logger, "获取交易对失败");
    }

    public Task<ApiResponseDto<long>> GetTradingPairIdAsync(string symbol)
    {
        return ServiceHelper.ExecuteAsync(
            async () =>
            {
                var pair = await _repository.GetBySymbolAsync(symbol);
                return pair == null ? throw new InvalidOperationException($"交易对 {symbol} 不存在") : pair.Id;
            },
            _logger, "获取交易对ID失败");
    }

    public Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetActiveTradingPairsAsync()
    {
        return ServiceHelper.ExecuteAsync(
            async () =>
            {
                var pairs = await _repository.GetActiveTradingPairsAsync();
                return pairs.Select(p => _mapping.MapToDto(p));
            },
            _logger, "获取活跃交易对失败");
    }

    public Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetTopTradingPairsAsync(int count = 10)
    {
        return ServiceHelper.ExecuteAsync(
            async () =>
            {
                var pairs = await _repository.GetTopTradingPairsAsync(count);
                return pairs.Select(p => _mapping.MapToDto(p));
            },
            _logger, "获取热门交易对失败");
    }

    public Task<ApiResponseDto<bool>> UpdatePriceAsync(
        string symbol, 
        decimal price, 
        decimal change24h, 
        decimal volume24h, 
        decimal high24h, 
        decimal low24h)
    {
        return ServiceHelper.ExecuteAsync(
            async () =>
            {
                var pair = await _repository.GetBySymbolAsync(symbol) 
                    ?? throw new InvalidOperationException($"交易对 {symbol} 不存在");
                pair.Price = price;
                pair.Change24h = change24h;
                pair.Volume24h = volume24h;
                pair.High24h = high24h;
                pair.Low24h = low24h;
                pair.Touch();
                await _repository.UpdateAsync(pair);
                return true;
            },
            _logger, "更新交易对价格失败");
    }
}
