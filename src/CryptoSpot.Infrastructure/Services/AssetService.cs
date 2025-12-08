using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 资产服务实现 - 纯数据库实现
    /// </summary>
    public class AssetService : IAssetService
    {
        private readonly IAssetRepository _assetRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDtoMappingService _mappingService;
        private readonly ILogger<AssetService> _logger;

        public AssetService(
            IAssetRepository assetRepository,
            IUnitOfWork unitOfWork,
            IDtoMappingService mappingService,
            ILogger<AssetService> logger)
        {
            _assetRepository = assetRepository;
            _unitOfWork = unitOfWork;
            _mappingService = mappingService;
            _logger = logger;
        }

        public Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(long userId)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var assets = await _assetRepository.GetAssetsByUserIdAsync((int)userId);
                return assets.Select(_mappingService.MapToDto);
            }, _logger, "获取资产失败");
        }

        public Task<ApiResponseDto<AssetDto?>> GetUserAssetAsync(long userId, string symbol)
        {
            return ServiceHelper.ExecuteAsync<AssetDto?>(async () =>
            {
                var asset = await _assetRepository.GetAssetByUserIdAndSymbolAsync((int)userId, symbol) 
                    ?? throw new InvalidOperationException("资产不存在");
                return _mappingService.MapToDto(asset);
            }, _logger, "获取资产失败");
        }

        public Task<ApiResponseDto<bool>> AddAssetAsync(long userId, AssetOperationRequestDto request)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var success = await _assetRepository.UpdateBalanceAsync((int)userId, request.Symbol, request.Amount);
                if (success) await _unitOfWork.SaveChangesAsync();
                return success ? true : throw new InvalidOperationException("资产增加失败");
            }, _logger, "资产增加失败");
        }

        public Task<ApiResponseDto<bool>> DeductAssetAsync(long userId, AssetOperationRequestDto request)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var success = await _assetRepository.UpdateBalanceAsync((int)userId, request.Symbol, -request.Amount);
                if (success) await _unitOfWork.SaveChangesAsync();
                return success ? true : throw new InvalidOperationException("资产扣减失败");
            }, _logger, "资产扣减失败");
        }

        public Task<ApiResponseDto<bool>> FreezeAssetAsync(long userId, AssetOperationRequestDto request)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var success = await _assetRepository.FreezeAssetAsync((int)userId, request.Symbol, request.Amount);
                if (success) await _unitOfWork.SaveChangesAsync();
                return success ? true : throw new InvalidOperationException("资产冻结失败");
            }, _logger, "资产冻结失败");
        }

        public Task<ApiResponseDto<bool>> UnfreezeAssetAsync(long userId, AssetOperationRequestDto request)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var success = await _assetRepository.UnfreezeAssetAsync((int)userId, request.Symbol, request.Amount);
                if (success) await _unitOfWork.SaveChangesAsync();
                return success ? true : throw new InvalidOperationException("资产解冻失败");
            }, _logger, "资产解冻失败");
        }

        public Task<ApiResponseDto<bool>> ConsumeFrozenAssetAsync(long userId, AssetOperationRequestDto request)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var unfreezeSuccess = await _assetRepository.UnfreezeAssetAsync((int)userId, request.Symbol, request.Amount);
                if (!unfreezeSuccess) throw new InvalidOperationException("解冻资产失败");

                var deductSuccess = await _assetRepository.UpdateBalanceAsync((int)userId, request.Symbol, -request.Amount);
                if (!deductSuccess) throw new InvalidOperationException("扣减资产失败");

                await _unitOfWork.SaveChangesAsync();
                return true;
            }, _logger, "消耗冻结资产失败");
        }

        public Task<ApiResponseDto<bool>> TransferAssetAsync(long fromUserId, AssetTransferRequestDto request)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var deductSuccess = await _assetRepository.UpdateBalanceAsync((int)fromUserId, request.Symbol, -request.Amount);
                if (!deductSuccess) throw new InvalidOperationException("扣减发送方资产失败");

                var addSuccess = await _assetRepository.UpdateBalanceAsync((int)request.ToUserId, request.Symbol, request.Amount);
                if (!addSuccess) throw new InvalidOperationException("增加接收方资产失败");

                await _unitOfWork.SaveChangesAsync();
                return true;
            }, _logger, "资产转账失败");
        }

        public Task<ApiResponseDto<bool>> InitializeUserAssetsAsync(long userId, Dictionary<string, decimal> initialBalances)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                foreach (var (symbol, balance) in initialBalances)
                {
                    var asset = new Asset
                    {
                        UserId = (int)userId,
                        Symbol = symbol,
                        Available = balance,
                        Frozen = 0,
                        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    await _assetRepository.AddAsync(asset);
                }
                await _unitOfWork.SaveChangesAsync();
                return true;
            }, _logger, "资产初始化失败");
        }
    }
}
