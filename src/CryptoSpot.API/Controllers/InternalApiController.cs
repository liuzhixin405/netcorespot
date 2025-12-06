using Microsoft.AspNetCore.Mvc;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.Trading;

namespace CryptoSpot.API.Controllers;

/// <summary>
/// 内部 API 控制器，用于撮合服务与 API 服务之间的通信
/// </summary>
[ApiController]
[Route("api/internal")]
public class InternalApiController : ControllerBase
{
    private readonly ITradingPairService _tradingPairService;
    private readonly IAssetRepository _assetRepository;
    private readonly ILogger<InternalApiController> _logger;

    public InternalApiController(
        ITradingPairService tradingPairService,
        IAssetRepository assetRepository,
        ILogger<InternalApiController> logger)
    {
        _tradingPairService = tradingPairService;
        _assetRepository = assetRepository;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有活跃的交易对（撮合引擎启动时调用）
    /// </summary>
    [HttpGet("trading-pairs")]
    public async Task<IActionResult> GetActiveTradingPairs()
    {
        try
        {
            var response = await _tradingPairService.GetActiveTradingPairsAsync();
            
            if (!response.Success || response.Data == null)
            {
                return BadRequest(new
                {
                    Success = false,
                    Message = response.Message ?? "获取活跃交易对失败"
                });
            }

            var result = response.Data.Select(tp => new
            {
                Symbol = tp.Symbol,
                BaseAsset = tp.BaseAsset,
                QuoteAsset = tp.QuoteAsset,
                MinQuantity = tp.MinQuantity,
                MaxQuantity = tp.MaxQuantity,
                PricePrecision = tp.PricePrecision,
                QuantityPrecision = tp.QuantityPrecision
            });

            return Ok(new
            {
                Success = true,
                Data = result,
                Message = "获取活跃交易对成功"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取活跃交易对失败");
            return StatusCode(500, new
            {
                Success = false,
                Message = "获取活跃交易对失败",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// 获取所有用户资产（撮合引擎启动时调用）
    /// </summary>
    [HttpGet("assets")]
    public async Task<IActionResult> GetAllAssets()
    {
        try
        {
            var assets = await _assetRepository.GetAllAsync();
            
            var result = assets.Select(a => new
            {
                UserId = a.UserId,
                Asset = a.Symbol,
                Available = a.Available,
                Frozen = a.Frozen
            });

            return Ok(new
            {
                Success = true,
                Data = result,
                Message = $"获取所有资产成功，共 {assets.Count()} 条"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取所有资产失败");
            return StatusCode(500, new
            {
                Success = false,
                Message = "获取所有资产失败",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// 批量更新用户资产（撮合引擎关闭时调用）
    /// </summary>
    [HttpPost("assets/batch-update")]
    public async Task<IActionResult> BatchUpdateAssets([FromBody] List<AssetUpdateRequest> updates)
    {
        try
        {
            if (updates == null || updates.Count == 0)
            {
                return BadRequest(new
                {
                    Success = false,
                    Message = "更新列表为空"
                });
            }

            int successCount = 0;
            int failCount = 0;

            foreach (var update in updates)
            {
                try
                {
                    var asset = await _assetRepository.GetUserAssetAsync((int)update.UserId, update.Asset);
                    if (asset != null)
                    {
                        asset.Available = update.Available;
                        asset.Frozen = update.Frozen;
                        asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        
                        await _assetRepository.UpdateAsync(asset);
                        successCount++;
                    }
                    else
                    {
                        _logger.LogWarning("资产不存在: UserId={UserId}, Asset={Asset}", update.UserId, update.Asset);
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "更新资产失败: UserId={UserId}, Asset={Asset}", update.UserId, update.Asset);
                    failCount++;
                }
            }

            return Ok(new
            {
                Success = true,
                Message = $"批量更新完成: 成功 {successCount} 条, 失败 {failCount} 条",
                Data = new
                {
                    TotalCount = updates.Count,
                    SuccessCount = successCount,
                    FailCount = failCount
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量更新资产失败");
            return StatusCode(500, new
            {
                Success = false,
                Message = "批量更新资产失败",
                Error = ex.Message
            });
        }
    }
}

/// <summary>
/// 资产更新请求
/// </summary>
public class AssetUpdateRequest
{
    public long UserId { get; set; }
    public string Asset { get; set; } = string.Empty;
    public decimal Available { get; set; }
    public decimal Frozen { get; set; }
}
