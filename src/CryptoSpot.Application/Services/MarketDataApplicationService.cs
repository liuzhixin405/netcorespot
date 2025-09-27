using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 市场数据应用服务 - 协调市场数据相关的用例
    /// </summary>
    public class MarketDataApplicationService
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IKLineDataRepository _klineDataRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<MarketDataApplicationService> _logger;

        public MarketDataApplicationService(
            ITradingPairRepository tradingPairRepository,
            IKLineDataRepository klineDataRepository,
            IUnitOfWork unitOfWork,
            ILogger<MarketDataApplicationService> logger)
        {
            _tradingPairRepository = tradingPairRepository;
            _klineDataRepository = klineDataRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <summary>
        /// 更新交易对价格用例
        /// </summary>
        public async Task<bool> UpdateTradingPairPriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var success = await _tradingPairRepository.UpdatePriceAsync(symbol, price, change24h, volume24h, high24h, low24h);
                if (success)
                {
                    await _unitOfWork.CommitTransactionAsync(transaction);
                    _logger.LogInformation("价格更新成功: {Symbol}, Price={Price}, Change24h={Change24h}%", 
                        symbol, price, change24h * 100);
                }
                else
                {
                    await _unitOfWork.RollbackTransactionAsync(transaction);
                    _logger.LogWarning("价格更新失败: {Symbol}", symbol);
                }
                return success;
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(transaction);
                _logger.LogError(ex, "价格更新异常: {Symbol}", symbol);
                return false;
            }
        }

        /// <summary>
        /// 保存K线数据用例
        /// </summary>
        public async Task<bool> SaveKLineDataAsync(KLineData klineData)
        {
            var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                await _klineDataRepository.AddAsync(klineData);
                await _unitOfWork.CommitTransactionAsync(transaction);
                
                _logger.LogDebug("K线数据保存成功: TradingPairId={TradingPairId}, TimeFrame={TimeFrame}, OpenTime={OpenTime}", 
                    klineData.TradingPairId, klineData.TimeFrame, klineData.OpenTime);
                return true;
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(transaction);
                _logger.LogError(ex, "K线数据保存失败: TradingPairId={TradingPairId}", klineData.TradingPairId);
                return false;
            }
        }

        /// <summary>
        /// 批量保存K线数据用例
        /// </summary>
        public async Task<bool> SaveKLineDataBatchAsync(IEnumerable<KLineData> klineDataArray)
        {
            var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                await _klineDataRepository.SaveKLineDataBatchAsync(klineDataArray);
                await _unitOfWork.CommitTransactionAsync(transaction);
                
                _logger.LogInformation("批量K线数据保存成功: {Count} 条数据", klineDataArray.Count());
                return true;
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(transaction);
                _logger.LogError(ex, "批量K线数据保存失败");
                return false;
            }
        }

        /// <summary>
        /// 获取K线数据用例
        /// </summary>
        public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
        {
            try
            {
                // 获取交易对ID
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (tradingPair == null)
                {
                    _logger.LogWarning("交易对不存在: {Symbol}", symbol);
                    return Enumerable.Empty<KLineData>();
                }

                return await _klineDataRepository.GetKLineDataByTradingPairIdAsync(tradingPair.Id, interval, limit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取K线数据失败: {Symbol}, {Interval}", symbol, interval);
                return Enumerable.Empty<KLineData>();
            }
        }

        /// <summary>
        /// 获取最新K线数据用例
        /// </summary>
        public async Task<KLineData?> GetLatestKLineDataAsync(string symbol, string interval)
        {
            try
            {
                // 获取交易对ID
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (tradingPair == null)
                {
                    _logger.LogWarning("交易对不存在: {Symbol}", symbol);
                    return null;
                }

                return await _klineDataRepository.GetLatestKLineDataAsync(tradingPair.Id, interval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最新K线数据失败: {Symbol}, {Interval}", symbol, interval);
                return null;
            }
        }

        /// <summary>
        /// 获取交易对信息用例
        /// </summary>
        public async Task<TradingPair?> GetTradingPairAsync(string symbol)
        {
            return await _tradingPairRepository.GetBySymbolAsync(symbol);
        }

        /// <summary>
        /// 获取活跃交易对列表用例
        /// </summary>
        public async Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync()
        {
            return await _tradingPairRepository.GetActiveTradingPairsAsync();
        }
    }
}
