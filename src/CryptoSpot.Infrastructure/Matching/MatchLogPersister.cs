using CryptoSpot.Domain.Matching;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Persistence.Data;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Matching;

/// <summary>
/// 撮合日志持久化处理器
/// 将内存撮合结果写入数据库
/// </summary>
public class MatchLogPersister
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILogger<MatchLogPersister> _logger;

    public MatchLogPersister(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<MatchLogPersister> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// 处理撮合日志并持久化
    /// </summary>
    public async Task ProcessLogAsync(LogBase log)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            switch (log)
            {
                case MatchLog matchLog:
                    await HandleMatchLogAsync(context, matchLog);
                    break;

                case DoneLog doneLog:
                    await HandleDoneLogAsync(context, doneLog);
                    break;

                case OpenLog openLog:
                    await HandleOpenLogAsync(context, openLog);
                    break;
            }

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist log {LogType}", log.GetType().Name);
        }
    }

    private async Task HandleMatchLogAsync(ApplicationDbContext context, MatchLog matchLog)
    {
        // 1. 创建成交记录
        var trade = new Trade
        {
            TradingPairId = await GetTradingPairIdAsync(context, matchLog.Symbol),
            BuyOrderId = matchLog.Taker.Side == Side.Buy ? matchLog.Taker.OrderId : matchLog.Maker.OrderId,
            SellOrderId = matchLog.Taker.Side == Side.Sell ? matchLog.Taker.OrderId : matchLog.Maker.OrderId,
            BuyerId = matchLog.Taker.Side == Side.Buy ? matchLog.Taker.UserId : matchLog.Maker.UserId,
            SellerId = matchLog.Taker.Side == Side.Sell ? matchLog.Taker.UserId : matchLog.Maker.UserId,
            Price = matchLog.Price,
            Quantity = matchLog.Size,
            ExecutedAt = new DateTimeOffset(matchLog.Timestamp).ToUnixTimeMilliseconds(),
            TradeId = $"{matchLog.TradeSeq}"
        };
        context.Trades.Add(trade);

        // 2. 更新订单状态
        await UpdateOrderStatusAsync(context, matchLog.Taker.OrderId, matchLog.Size);
        await UpdateOrderStatusAsync(context, matchLog.Maker.OrderId, matchLog.Size);

        // 3. 更新用户资产
        await UpdateUserAssetsAsync(context, matchLog);

        _logger.LogDebug("Persisted match: Trade={TradeSeq}", matchLog.TradeSeq);
    }

    private async Task HandleDoneLogAsync(ApplicationDbContext context, DoneLog doneLog)
    {
        var order = await context.Orders.FindAsync(doneLog.Order.OrderId);
        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found for done log", doneLog.Order.OrderId);
            return;
        }

        order.Status = doneLog.Reason == DoneReason.Filled 
            ? Domain.Entities.OrderStatus.Filled 
            : Domain.Entities.OrderStatus.Cancelled;
        
        order.UpdatedAt = new DateTimeOffset(doneLog.Timestamp).ToUnixTimeMilliseconds();

        _logger.LogDebug("Updated order {OrderId} status to {Status}", 
            order.Id, order.Status);
    }

    private async Task HandleOpenLogAsync(ApplicationDbContext context, OpenLog openLog)
    {
        var order = await context.Orders.FindAsync(openLog.Order.OrderId);
        if (order != null)
        {
            order.Status = Domain.Entities.OrderStatus.Active;
            order.UpdatedAt = new DateTimeOffset(openLog.Timestamp).ToUnixTimeMilliseconds();
            
            _logger.LogDebug("Order {OrderId} opened on book", order.Id);
        }
    }

    private async Task UpdateOrderStatusAsync(ApplicationDbContext context, long orderId, decimal filledQuantity)
    {
        var order = await context.Orders.FindAsync(orderId);
        if (order == null) return;

        order.FilledQuantity += filledQuantity;
        
        if (order.FilledQuantity >= order.Quantity)
        {
            order.Status = Domain.Entities.OrderStatus.Filled;
        }
        else
        {
            order.Status = Domain.Entities.OrderStatus.PartiallyFilled;
        }

        order.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private async Task UpdateUserAssetsAsync(ApplicationDbContext context, MatchLog matchLog)
    {
        var tradingPairId = await GetTradingPairIdAsync(context, matchLog.Symbol);
        var tradingPair = await context.TradingPairs
            .FirstOrDefaultAsync(tp => tp.Id == tradingPairId);

        if (tradingPair == null) return;

        var tradeAmount = matchLog.Price * matchLog.Size;

        var quoteSymbol = tradingPair.QuoteAsset;
        var baseSymbol = tradingPair.BaseAsset;

        // 更新 Taker 资产
        if (matchLog.Taker.Side == Side.Buy)
        {
            // 买入：减少 Quote 资产冻结，增加 Base 资产可用
            await UpdateAssetAsync(context, matchLog.Taker.UserId, quoteSymbol, 
                0, -tradeAmount, "frozen");
            await UpdateAssetAsync(context, matchLog.Taker.UserId, baseSymbol, 
                matchLog.Size, 0, "available");
        }
        else
        {
            // 卖出：减少 Base 资产冻结，增加 Quote 资产可用
            await UpdateAssetAsync(context, matchLog.Taker.UserId, baseSymbol, 
                0, -matchLog.Size, "frozen");
            await UpdateAssetAsync(context, matchLog.Taker.UserId, quoteSymbol, 
                tradeAmount, 0, "available");
        }

        // 更新 Maker 资产（相反操作）
        if (matchLog.Maker.Side == Side.Buy)
        {
            await UpdateAssetAsync(context, matchLog.Maker.UserId, quoteSymbol, 
                0, -tradeAmount, "frozen");
            await UpdateAssetAsync(context, matchLog.Maker.UserId, baseSymbol, 
                matchLog.Size, 0, "available");
        }
        else
        {
            await UpdateAssetAsync(context, matchLog.Maker.UserId, baseSymbol, 
                0, -matchLog.Size, "frozen");
            await UpdateAssetAsync(context, matchLog.Maker.UserId, quoteSymbol, 
                tradeAmount, 0, "available");
        }
    }

    private async Task UpdateAssetAsync(
        ApplicationDbContext context, 
        long userId, 
        string assetSymbol, 
        decimal availableDelta, 
        decimal frozenDelta,
        string type)
    {
        var asset = await context.Assets
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Symbol == assetSymbol);

        if (asset == null)
        {
            _logger.LogWarning("Asset not found: User={UserId}, Symbol={Symbol}", 
                userId, assetSymbol);
            return;
        }

        asset.Available += availableDelta;
        asset.Frozen += frozenDelta;
        asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private async Task<long> GetTradingPairIdAsync(ApplicationDbContext context, string symbol)
    {
        var pair = await context.TradingPairs
            .FirstOrDefaultAsync(tp => tp.Symbol == symbol);
        
        return pair?.Id ?? throw new InvalidOperationException($"Trading pair {symbol} not found");
    }
}
