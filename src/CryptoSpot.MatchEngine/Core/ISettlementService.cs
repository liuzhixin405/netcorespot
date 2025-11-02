using System.Threading.Tasks;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.MatchEngine.Core
{
    public record SettlementContext(Order Maker, Order Taker, decimal Price, decimal Quantity, string Symbol);
    public record SettlementResult(bool Success, string? Error);

    /// <summary>
    /// 资产原子结算抽象，便于替换 Lua / 本地撮合 / 其它后端。
    /// </summary>
    public interface ISettlementService
    {
        Task<SettlementResult> SettleAsync(SettlementContext ctx);
    }
}
