using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;

namespace CryptoSpot.Application.Features.Trading.GetAssets
{
    /// <summary>
    /// 获取资产查询
    /// </summary>
    public record GetAssetsQuery() : ICommand<Result<List<AssetResponse>>>;

    /// <summary>
    /// 资产响应
    /// </summary>
    public record AssetResponse(
        long Id,
        string Currency,
        decimal Available,
        decimal Frozen,
        decimal Total
    );
}
