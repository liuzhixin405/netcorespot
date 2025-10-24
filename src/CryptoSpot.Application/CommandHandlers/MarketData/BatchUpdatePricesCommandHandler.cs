using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.DomainCommands.MarketData;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CryptoSpot.Application.CommandHandlers.MarketData
{
    /// <summary>
    /// 批量价格更新命令处理器（高频场景）
    /// </summary>
    public class BatchUpdatePricesCommandHandler : ICommandHandler<BatchUpdatePricesCommand, BatchUpdatePricesResult>
    {
        private readonly IPriceDataService _priceDataService;
        private readonly ILogger<BatchUpdatePricesCommandHandler> _logger;

        public BatchUpdatePricesCommandHandler(
            IPriceDataService priceDataService,
            ILogger<BatchUpdatePricesCommandHandler> logger)
        {
            _priceDataService = priceDataService;
            _logger = logger;
        }

        public async Task<BatchUpdatePricesResult> HandleAsync(BatchUpdatePricesCommand command, CancellationToken ct = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var successCount = 0;
            var failedSymbols = new List<string>();

            try
            {
                // 去重：每个 symbol 只保留最新的一条
                var latestUpdates = command.PriceUpdates
                    .GroupBy(x => x.Symbol)
                    .Select(g => g.Last())
                    .ToList();

                _logger.LogDebug("批量价格更新: {Total} 个请求 -> {Unique} 个唯一交易对",
                    command.PriceUpdates.Count, latestUpdates.Count);

                // 并发批量更新（使用 Task.WhenAll 提高性能）
                var tasks = latestUpdates.Select(async update =>
                {
                    try
                    {
                        await _priceDataService.UpdateTradingPairPriceAsync(
                            update.Symbol,
                            update.Price,
                            update.Change24h,
                            update.Volume24h,
                            update.High24h,
                            update.Low24h);

                        Interlocked.Increment(ref successCount);
                        return (Success: true, Symbol: update.Symbol);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "更新 {Symbol} 价格失败", update.Symbol);
                        return (Success: false, Symbol: update.Symbol);
                    }
                });

                var results = await Task.WhenAll(tasks);
                failedSymbols = results.Where(r => !r.Success).Select(r => r.Symbol).ToList();

                stopwatch.Stop();

                return new BatchUpdatePricesResult
                {
                    Success = failedSymbols.Count == 0,
                    TotalCount = latestUpdates.Count,
                    SuccessCount = successCount,
                    FailedCount = failedSymbols.Count,
                    FailedSymbols = failedSymbols
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量价格更新命令处理失败");
                stopwatch.Stop();

                return new BatchUpdatePricesResult
                {
                    Success = false,
                    TotalCount = command.PriceUpdates.Count,
                    SuccessCount = successCount,
                    FailedCount = command.PriceUpdates.Count - successCount,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
