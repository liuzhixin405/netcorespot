using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.Abstractions.Repositories;

namespace CryptoSpot.Application.Features.Trading.GetAssets
{
    /// <summary>
    /// 获取资产查询处理器
    /// </summary>
    public class GetAssetsQueryHandler : ICommandHandler<GetAssetsQuery, Result<List<AssetResponse>>>
    {
        private readonly ICurrentUserService _currentUser;
        private readonly IAssetRepository _assetRepository;

        public GetAssetsQueryHandler(
            ICurrentUserService currentUser,
            IAssetRepository assetRepository)
        {
            _currentUser = currentUser;
            _assetRepository = assetRepository;
        }

        public async Task<Result<List<AssetResponse>>> HandleAsync(GetAssetsQuery query, CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated)
                return Result<List<AssetResponse>>.Failure("User is not authenticated");

            var assets = await _assetRepository.GetAssetsByUserIdAsync((int)_currentUser.UserId);

            var response = assets.Select(a => new AssetResponse(
                a.Id,
                a.Symbol,
                a.Available,
                a.Frozen,
                a.Available + a.Frozen
            )).ToList();

            return Result<List<AssetResponse>>.Success(response);
        }
    }
}
