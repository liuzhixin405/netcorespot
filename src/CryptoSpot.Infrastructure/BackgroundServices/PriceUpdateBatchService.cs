using System.Globalization;
using System.Threading.Channels;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Infrastructure.MarketData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.BackgroundServices;

/// <summary>
/// 批量处理价格更新：有界队列、单批数据库更新、一次批量 SignalR 推送。
/// </summary>
public class PriceUpdateBatchService : BackgroundService
{
    private readonly ILogger<PriceUpdateBatchService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRealTimeDataPushService _pushService;
    private readonly MarketDataMetrics _metrics;
    private readonly MarketDataFeedOptions _options;
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
        IServiceScopeFactory scopeFactory,
        IRealTimeDataPushService pushService,
        MarketDataMetrics metrics,
        MarketDataFeedOptions options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _pushService = pushService;
        _metrics = metrics;
        _options = options;

        // [联网-官方: https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.boundedchanneloptions]
        _channel = Channel.CreateBounded<PriceUpdateRequest>(new BoundedChannelOptions(options.PriceQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    /// <summary>
    /// 异步提交价格更新请求（非阻塞；队列满时丢弃最旧数据）。
    /// </summary>
    public bool TryEnqueue(string symbol, decimal price, decimal change, decimal volume, decimal high, decimal low)
    {
        if (!_channel.Writer.TryWrite(new PriceUpdateRequest(symbol, price, change, volume, high, low)))
        {
            _metrics.QueueDropped();
            return false;
        }

        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PriceUpdateBatchService 已启动");

        var buffer = new List<PriceUpdateRequest>(100);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                buffer.Clear();

                await _channel.Reader.WaitToReadAsync(stoppingToken);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
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
                            await _channel.Reader.WaitToReadAsync(timeoutCts.Token);
                        }
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    // 超时，处理当前批次
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
                _logger.LogError(ex, "PriceUpdateBatchService 批处理异常");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("PriceUpdateBatchService 已停止");
    }

    private async Task ProcessBatchAsync(List<PriceUpdateRequest> batch, CancellationToken ct)
    {
        try
        {
            var latestUpdates = batch
                .GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();

            _logger.LogDebug("批处理价格更新: {Count} 个请求 -> {Unique} 个唯一交易对",
                batch.Count, latestUpdates.Count);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CryptoSpot.Persistence.Data.ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (latestUpdates.Count > 0)
            {
                var priceCases = string.Join(" ", latestUpdates.Select(u =>
                    $"WHEN {QuoteSqlString(u.Symbol)} THEN {FormatDecimal(u.Price)}"));
                var changeCases = string.Join(" ", latestUpdates.Select(u =>
                    $"WHEN {QuoteSqlString(u.Symbol)} THEN {FormatDecimal(u.Change24h)}"));
                var volumeCases = string.Join(" ", latestUpdates.Select(u =>
                    $"WHEN {QuoteSqlString(u.Symbol)} THEN {FormatDecimal(u.Volume24h)}"));
                var highCases = string.Join(" ", latestUpdates.Select(u =>
                    $"WHEN {QuoteSqlString(u.Symbol)} THEN {FormatDecimal(u.High24h)}"));
                var lowCases = string.Join(" ", latestUpdates.Select(u =>
                    $"WHEN {QuoteSqlString(u.Symbol)} THEN {FormatDecimal(u.Low24h)}"));
                var symbols = string.Join(", ", latestUpdates.Select(u => QuoteSqlString(u.Symbol)));

                var batchSql = $"""
                    UPDATE TradingPairs SET
                        Price = CASE Symbol {priceCases} ELSE Price END,
                        Change24h = CASE Symbol {changeCases} ELSE Change24h END,
                        Volume24h = CASE Symbol {volumeCases} ELSE Volume24h END,
                        High24h = CASE Symbol {highCases} ELSE High24h END,
                        Low24h = CASE Symbol {lowCases} ELSE Low24h END,
                        UpdatedAt = {now},
                        LastUpdated = {now}
                    WHERE IsDeleted = 0 AND Symbol IN ({symbols})
                """;

                await db.Database.ExecuteSqlRawAsync(batchSql, ct);
            }

            // 同一批里为每个 symbol 同时推两类事件，避免逐事件建 scope 和重复排队。
            var pushTasks = latestUpdates.Select(async u =>
            {
                var priceData = new
                {
                    symbol = u.Symbol,
                    price = u.Price,
                    change24h = u.Change24h,
                    volume24h = u.Volume24h,
                    high24h = u.High24h,
                    low24h = u.Low24h,
                    timestamp = now
                };

                await _pushService.PushPriceDataAsync(u.Symbol, priceData);
                await _pushService.PushLastTradeAndMidPriceAsync(
                    u.Symbol, u.Price, null, null, null, null, now);
                _metrics.TickerPushed();
            });

            await Task.WhenAll(pushTasks);

            _logger.LogDebug("批处理完成: {Count} 个交易对已更新", latestUpdates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批处理执行失败，批次大小: {Count}", batch.Count);
        }
    }

    private static string FormatDecimal(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static string QuoteSqlString(string value)
        => $"'{value.Replace("'", "''")}'";

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.Complete();
        _logger.LogInformation("等待剩余价格更新请求完成...");
        await base.StopAsync(cancellationToken);
    }
}
