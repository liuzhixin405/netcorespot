using CryptoSpot.Application.Abstractions.Services.Analytics;
using CryptoSpot.Application.DTOs.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CryptoSpot.API.Controllers;

[ApiController]
[Route("api/signals")]
public sealed class SignalsController : ControllerBase
{
    private readonly ISignalService _signals;

    public SignalsController(ISignalService signals)
    {
        _signals = signals;
    }

    [HttpGet("{symbol}")]
    [AllowAnonymous]
    public async Task<IActionResult> Latest(
        string symbol,
        [FromQuery] string interval = "1h",
        [FromQuery] StrategyType strategy = StrategyType.MaCross,
        CancellationToken cancellationToken = default)
    {
        var result = await _signals.GetLatestAsync(symbol, interval, strategy, cancellationToken: cancellationToken);
        return Ok(new { success = true, data = result });
    }

    [HttpGet("{symbol}/history")]
    [AllowAnonymous]
    public async Task<IActionResult> History(
        string symbol,
        [FromQuery] string interval = "1h",
        [FromQuery] StrategyType strategy = StrategyType.MaCross,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _signals.GetHistoryAsync(symbol, interval, strategy, limit, cancellationToken: cancellationToken);
        return Ok(new { success = true, data = result });
    }
}
