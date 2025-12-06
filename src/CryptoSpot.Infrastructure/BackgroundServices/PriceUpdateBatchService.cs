using System.Threading.Channels;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.BackgroundServices;

/// <summary>
/// 批量处理价格更新，避免高频并发数据库操作
/// 解决 "ServerSession is not connected" 和并发冲突问题
/// </summary>
public class PriceUpdateBatchService : BackgroundService
{
    private readonly ILogger<PriceUpdateBatchService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<PriceUpdateRequest> _channel;

    private record PriceUpdateRequest(
        string Symbol,
        decimal Price,
        decimal Change24h,
        decimal Volume24h,
        decimal High24h,
        decimal Low24h);

    public PriceUpdateBatchService(
        ILogger<PriceUpdateBatchService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        
        // 无界队列，单读多写
        _channel = Channel.CreateUnbounded<PriceUpdateRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// 异步提交价格更新请求（非阻塞）
    /// </summary>
    public bool TryEnqueue(string symbol, decimal price, decimal change, decimal volume, decimal high, decimal low)
    {
        var success = _channel.Writer.TryWrite(new PriceUpdateRequest(symbol, price, change, volume, high, low));
        if (!success)
        {
            _logger.LogWarning("⚠️ 价格更新队列已满，丢弃 {Symbol} 数据", symbol);
        }
        return success;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("✅ PriceUpdateBatchService 已启动");

        var buffer = new List<PriceUpdateRequest>(100);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                buffer.Clear();

                // 等待第一个请求
                await _channel.Reader.WaitToReadAsync(stoppingToken);

                // 收集批次（最多等待 100ms 或收集到 50 个）
                var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(100);

                try
                {
                    while (buffer.Count < 50)
                    {
                        if (_channel.Reader.TryRead(out var request))
                        {
                            buffer.Add(request);
                        }
                        else
                        {
                            // 等待新数据或超时
                            await _channel.Reader.WaitToReadAsync(timeoutCts.Token);
                        }
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    // 超时，处理当前批次
                }
                finally
                {
                    timeoutCts.Dispose();
                }

                if (buffer.Count > 0)
                {
                    await ProcessBatchAsync(buffer, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ PriceUpdateBatchService 批处理异常");
                await Task.Delay(1000, stoppingToken); // 错误后延迟
            }
        }

        _logger.LogInformation("❌ PriceUpdateBatchService 已停止");
    }

    private async Task ProcessBatchAsync(List<PriceUpdateRequest> batch, CancellationToken ct)
    {
        try
        {
            // 去重：每个 symbol 只保留最新的一条
            var latestUpdates = batch
                .GroupBy(x => x.Symbol)
                .Select(g => g.Last())
                .ToList();

            _logger.LogDebug("📦 批处理价格更新: {Count} 个请求 -> {Unique} 个唯一交易对",
                batch.Count, latestUpdates.Count);

            // 创建独立的 Scope 进行数据库操作
            using var scope = _scopeFactory.CreateScope();
            var priceService = scope.ServiceProvider.GetRequiredService<IPriceDataService>();

            // 批量更新
            var successCount = 0;
            foreach (var update in latestUpdates)
            {
                try
                {
                    await priceService.UpdateTradingPairPriceAsync(
                        update.Symbol,
                        update.Price,
                        update.Change24h,
                        update.Volume24h,
                        update.High24h,
                        update.Low24h);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ 更新 {Symbol} 价格失败", update.Symbol);
                }
            }

            _logger.LogDebug("✅ 批处理完成: {Success}/{Total} 个交易对已更新", successCount, latestUpdates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 批处理执行失败，批次大小: {Count}", batch.Count);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // 完成写入，等待所有待处理数据完成
        _channel.Writer.Complete();

        _logger.LogInformation("⏳ 等待剩余价格更新请求完成...");

        await base.StopAsync(cancellationToken);
    }
}
