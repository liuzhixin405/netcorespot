using CryptoSpot.Application.Abstractions.Services.Risk;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CryptoSpot.API.Controllers;

[ApiController]
[Route("api/risk")]
[Authorize]
public sealed class RiskController : ControllerBase
{
    private readonly IRiskEventService _riskEvents;
    private readonly ICurrentUserService _currentUser;

    public RiskController(IRiskEventService riskEvents, ICurrentUserService currentUser)
    {
        _riskEvents = riskEvents;
        _currentUser = currentUser;
    }

    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] RiskEventStatus? status, CancellationToken cancellationToken) =>
        Ok(new { success = true, data = await _riskEvents.ListAsync(_currentUser.UserId, status, cancellationToken) });

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken) =>
        Ok(new { success = true, data = await _riskEvents.GetDashboardAsync(_currentUser.UserId, cancellationToken) });

    [HttpPost("events/{eventId:long}/handle")]
    public async Task<IActionResult> Handle(long eventId, [FromBody] RiskEventHandleRequest request, CancellationToken cancellationToken)
    {
        var result = await _riskEvents.HandleAsync(_currentUser.UserId, eventId, request.Status, cancellationToken);
        return Ok(new { success = true, data = result });
    }
}

public sealed record RiskEventHandleRequest(RiskEventStatus Status);
