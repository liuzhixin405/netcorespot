using System;
using System.Threading.Tasks;
using CryptoSpot.Redis;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.MatchEngine.Core
{
    /// <summary>
    /// 复用现有 InMemoryMatchEngineService 的 Lua 结算逻辑，搬运为独立服务。
    /// </summary>
    public class LuaSettlementService : ISettlementService
    {
        private readonly IRedisCache _redis;
        private readonly ILogger<LuaSettlementService> _logger;
        private const long PRECISION = 100_000_000;

        public LuaSettlementService(IRedisCache redis, ILogger<LuaSettlementService> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public Task<SettlementResult> SettleAsync(SettlementContext ctx)
        {
            var (baseCurrency, quoteCurrency) = ParseSymbol(ctx.Symbol);
            var baseAmount = ctx.Quantity;
            var quoteAmount = ctx.Quantity * ctx.Price;

            var buyOrder = ctx.Taker.Side == CryptoSpot.Domain.Entities.OrderSide.Buy ? ctx.Taker : ctx.Maker.Side == CryptoSpot.Domain.Entities.OrderSide.Buy ? ctx.Maker : null;
            var sellOrder = ctx.Taker.Side == CryptoSpot.Domain.Entities.OrderSide.Sell ? ctx.Taker : ctx.Maker.Side == CryptoSpot.Domain.Entities.OrderSide.Sell ? ctx.Maker : null;
            if (buyOrder == null || sellOrder == null)
                return Task.FromResult(new SettlementResult(false, "角色识别失败"));

            var buyUserId = buyOrder.UserId ?? 0;
            var sellUserId = sellOrder.UserId ?? 0;

            var buyQuoteKey = $"asset:{{{ctx.Symbol}}}:{buyUserId}:{quoteCurrency}";
            var buyBaseKey = $"asset:{{{ctx.Symbol}}}:{buyUserId}:{baseCurrency}";
            var sellBaseKey = $"asset:{{{ctx.Symbol}}}:{sellUserId}:{baseCurrency}";
            var sellQuoteKey = $"asset:{{{ctx.Symbol}}}:{sellUserId}:{quoteCurrency}";

            var quoteAmountLong = (long)(quoteAmount * PRECISION);
            var baseAmountLong = (long)(baseAmount * PRECISION);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var script = @"\n            local buyQuoteFrozen = tonumber(redis.call('HGET', KEYS[1], 'frozenBalance') or 0)\n            if buyQuoteFrozen < tonumber(ARGV[1]) then\n                return 0\n            end\n            local sellBaseFrozen = tonumber(redis.call('HGET', KEYS[3], 'frozenBalance') or 0)\n            if sellBaseFrozen < tonumber(ARGV[2]) then\n                return 0\n            end\n            redis.call('HINCRBY', KEYS[1], 'frozenBalance', -ARGV[1])\n            redis.call('HSET', KEYS[1], 'updatedAt', ARGV[3])\n            redis.call('HINCRBY', KEYS[2], 'availableBalance', ARGV[2])\n            redis.call('HSET', KEYS[2], 'updatedAt', ARGV[3])\n            redis.call('HINCRBY', KEYS[3], 'frozenBalance', -ARGV[2])\n            redis.call('HSET', KEYS[3], 'updatedAt', ARGV[3])\n            redis.call('HINCRBY', KEYS[4], 'availableBalance', ARGV[1])\n            redis.call('HSET', KEYS[4], 'updatedAt', ARGV[3])\n            return 1\n            ";

            try
            {
                var res = _redis.Execute("EVAL", script, 4, buyQuoteKey, buyBaseKey, sellBaseKey, sellQuoteKey, quoteAmountLong.ToString(), baseAmountLong.ToString(), timestamp.ToString());
                return Task.FromResult(res?.ToString() == "1" ? new SettlementResult(true, null) : new SettlementResult(false, "Lua返回0"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lua 结算失败");
                return Task.FromResult(new SettlementResult(false, ex.Message));
            }
        }

        private (string baseCurrency, string quoteCurrency) ParseSymbol(string symbol)
        {
            var quote = "USDT"; // TODO: 未来通过 TradingPair 配置
            return (symbol.Replace(quote, string.Empty), quote);
        }
    }
}
