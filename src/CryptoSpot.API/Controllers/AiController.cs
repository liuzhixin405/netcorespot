using CryptoSpot.Application.Abstractions.Services.Ai;
using CryptoSpot.Application.Common.Interfaces;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CryptoSpot.API.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public sealed class AiController : ControllerBase
{
    private readonly IAiAnalyzer _analyzer;
    private readonly ICurrentUserService _currentUser;
    private readonly IAiConversationService _conversations;
    private readonly IAiAuditService _audit;
    private readonly IAiApprovalService _approvals;
    private readonly IAiTradeService _tradeService;
    private readonly IAiExpansionService _expansionService;
    private readonly IAiModel _model;

    public AiController(
        IAiAnalyzer analyzer,
        ICurrentUserService currentUser,
        IAiConversationService conversations,
        IAiAuditService audit,
        IAiApprovalService approvals,
        IAiTradeService tradeService,
        IAiExpansionService expansionService,
        IAiModel model)
    {
        _analyzer = analyzer;
        _currentUser = currentUser;
        _conversations = conversations;
        _audit = audit;
        _approvals = approvals;
        _tradeService = tradeService;
        _expansionService = expansionService;
        _model = model;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] AiAnalyzeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest(new { error = "question and symbol are required." });

        var conversation = await _conversations.GetOrCreateAsync(
            _currentUser.UserId, request.ConversationId, request.Question, cancellationToken);
        await _conversations.AddMessageAsync(conversation.Id, "user", request.Question, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var result = await _analyzer.AnalyzeAsync(_currentUser.UserId, request.Question, request.Symbol, cancellationToken);
        await _conversations.AddMessageAsync(conversation.Id, "assistant", result.Analysis, cancellationToken);
        var auditId = await _audit.WriteAsync(new AiAuditEntry(
            _currentUser.UserId, conversation.Id, "ai.analyze", null, "market_snapshot", "{}",
            JsonSerializer.Serialize(result), "success", stopwatch.ElapsedMilliseconds), cancellationToken);
        return Ok(new { success = true, data = new { result.Analysis, result.Citations, conversationId = conversation.Id, auditId } });
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(CancellationToken cancellationToken) =>
        Ok(new { success = true, data = await _conversations.ListAsync(_currentUser.UserId, cancellationToken) });

    [HttpGet("conversations/{conversationId:long}")]
    public async Task<IActionResult> Messages(long conversationId, CancellationToken cancellationToken) =>
        Ok(new { success = true, data = await _conversations.GetMessagesAsync(_currentUser.UserId, conversationId, cancellationToken) });

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(CancellationToken cancellationToken) =>
        Ok(new { success = true, data = await _audit.ListAsync(_currentUser.UserId, cancellationToken) });

    [HttpPost("trade")]
    public async Task<IActionResult> Trade([FromBody] AiTradeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Instruction) || string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest(new { error = "instruction and symbol are required." });

        var stopwatch = Stopwatch.StartNew();
        var result = await _tradeService.PlanAndExecuteAsync(
            _currentUser.UserId, request.Instruction, request.Symbol.Trim().ToUpperInvariant(), cancellationToken);
        await _audit.WriteAsync(new AiAuditEntry(_currentUser.UserId, null, "ai.trade.plan", null,
            "place_order", request.Instruction, JsonSerializer.Serialize(result), result.Status, stopwatch.ElapsedMilliseconds),
            cancellationToken);
        return Ok(new { success = true, data = result });
    }

    [HttpPost("chat")]
    public async Task Chat([FromBody] AiChatRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = "message is required." }, cancellationToken);
            return;
        }

        var conversation = await _conversations.GetOrCreateAsync(
            _currentUser.UserId, request.ConversationId, request.Message, cancellationToken);
        await _conversations.AddMessageAsync(conversation.Id, "user", request.Message, cancellationToken);
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        var fullResponse = new System.Text.StringBuilder();
        await foreach (var chunk in _model.ChatStreamAsync(new CryptoSpot.Application.Abstractions.Services.Ai.AiChatRequest
        {
            Messages = new[]
            {
                new CryptoSpot.Application.Abstractions.Services.Ai.AiMessage("system",
                    "You are the CryptoSpot AI assistant. Do not make up market data or execute trades. Reply in Chinese."),
                new CryptoSpot.Application.Abstractions.Services.Ai.AiMessage("user", request.Message)
            }
        }, cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.ContentDelta))
            {
                fullResponse.Append(chunk.ContentDelta);
                await Response.WriteAsync($"event: message\ndata: {JsonSerializer.Serialize(chunk)}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        await _conversations.AddMessageAsync(conversation.Id, "assistant", fullResponse.ToString(), cancellationToken);
        await Response.WriteAsync($"event: done\ndata: {JsonSerializer.Serialize(new { conversationId = conversation.Id })}\n\n", cancellationToken);
    }

    [HttpPost("approvals/{approvalId:long}/approve")]
    public Task<IActionResult> Approve(long approvalId, CancellationToken cancellationToken) =>
        DecideApproval(approvalId, true, cancellationToken);

    [HttpPost("approvals/{approvalId:long}/reject")]
    public Task<IActionResult> Reject(long approvalId, CancellationToken cancellationToken) =>
        DecideApproval(approvalId, false, cancellationToken);

    private async Task<IActionResult> DecideApproval(long approvalId, bool approved, CancellationToken cancellationToken)
    {
        var approval = await _approvals.DecideAsync(_currentUser.UserId, approvalId, approved, cancellationToken);
        object result = new AiTradeResult(
            approved ? "交易确认已收到，正在提交订单。" : "交易计划已取消，未提交任何订单。",
            approval.Status.ToLowerInvariant(),
            approval.Id);
        if (approved && approval.Status == "Approved")
            result = await _tradeService.ExecuteApprovedAsync(_currentUser.UserId, approvalId, cancellationToken);
        await _audit.WriteAsync(new AiAuditEntry(
            _currentUser.UserId, null, approved ? "ai.approval.approved" : "ai.approval.rejected", null,
            approval.ActionType, approval.PayloadJson, JsonSerializer.Serialize(result), approval.Status.ToLowerInvariant(), 0),
            cancellationToken);
        return Ok(new { success = true, data = result });
    }

    [HttpPost("assistant")]
    public async Task<IActionResult> DocumentationAssistant([FromBody] DocumentationAssistantRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "question is required." });
        var result = await _expansionService.AnswerDocumentationAsync(_currentUser.UserId, request.Question, cancellationToken);
        return Ok(new { success = true, data = result });
    }

    [HttpPost("news/sentiment")]
    public async Task<IActionResult> NewsSentiment([FromBody] NewsSentimentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Headline))
            return BadRequest(new { error = "headline is required." });
        var result = await _expansionService.AnalyzeNewsSentimentAsync(_currentUser.UserId,
            new CryptoSpot.Application.Abstractions.Services.Ai.NewsSentimentRequest(request.Headline, request.Content, request.Symbol),
            cancellationToken);
        return Ok(new { success = true, data = result });
    }

    [HttpPost("portfolio/report")]
    public async Task<IActionResult> PortfolioReport(CancellationToken cancellationToken)
    {
        var result = await _expansionService.CreatePortfolioReportAsync(_currentUser.UserId, cancellationToken);
        return Ok(new { success = true, data = result });
    }
}

public sealed record AiAnalyzeRequest(string Question, string Symbol, long? ConversationId = null);
public sealed record AiTradeRequest(string Instruction, string Symbol);
public sealed record AiChatRequestDto(string Message, long? ConversationId = null);
public sealed record DocumentationAssistantRequest(string Question);
public sealed record NewsSentimentRequest(string Headline, string? Content = null, string? Symbol = null);
