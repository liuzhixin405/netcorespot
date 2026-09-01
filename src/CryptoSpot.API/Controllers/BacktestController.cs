using CryptoSpot.Application.Abstractions.Services.Analytics;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.DTOs.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CryptoSpot.API.Controllers;

[ApiController]
[Route("api/backtest")]
[Authorize]
public sealed class BacktestController : ControllerBase
{
    private readonly IBacktestService _backtests;
    private readonly ICurrentUserService _currentUser;

    public BacktestController(IBacktestService backtests, ICurrentUserService currentUser)
    {
        _backtests = backtests;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Run([FromBody] BacktestRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _backtests.RunAsync(_currentUser.UserId, request, cancellationToken);
        return Ok(new { success = true, data = result });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var result = await _backtests.GetAsync(_currentUser.UserId, id, cancellationToken);
        return result is null ? NotFound(new { error = "backtest result was not found." }) : Ok(new { success = true, data = result });
    }

    [HttpGet("strategies")]
    [AllowAnonymous]
    public IActionResult Strategies() => Ok(new { success = true, data = _backtests.GetStrategies() });
}
