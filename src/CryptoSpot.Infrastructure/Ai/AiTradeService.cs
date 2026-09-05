using System.Diagnostics;
using System.Text.Json;
using CryptoSpot.Application.Abstractions.Services.Ai;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderSide = CryptoSpot.Domain.Entities.OrderSide;
using OrderType = CryptoSpot.Domain.Entities.OrderType;

namespace CryptoSpot.Infrastructure.Ai;

public sealed class AiTradeService : IAiTradeService
{
    private const string TradePrompt = """
        你是 CryptoSpot 的交易操作编排助手。仅通过提供的工具执行用户的交易意图，绝不虚构执行结果。
        下单使用 place_order，参数为 symbol、side（buy/sell）、type（market/limit）、quantity 和限价单的 price。
        撤单使用 cancel_order，参数为 orderId。批量撤单使用 cancel_all_orders，参数可包含 symbol。
        写操作会由平台进行风控、审批和审计；用户尚未明确要求的操作不要执行。完成后用中文简洁说明结果。
        """;
    private const string PlaceOrderSchema = """{"type":"object","properties":{"symbol":{"type":"string"},"side":{"type":"string","enum":["buy","sell"]},"type":{"type":"string","enum":["market","limit"]},"quantity":{"type":"number","exclusiveMinimum":0},"price":{"type":["number","null"],"exclusiveMinimum":0}},"required":["symbol","side","type","quantity"],"additionalProperties":false}""";
    private const string CancelOrderSchema = """{"type":"object","properties":{"orderId":{"type":"integer","minimum":1}},"required":["orderId"],"additionalProperties":false}""";
    private const string CancelAllSchema = """{"type":"object","properties":{"symbol":{"type":"string"}},"additionalProperties":false}""";

    private readonly IAiModel _model;
    private readonly ITradingService _trading;
    private readonly IAiToolExecutor _readOnlyTools;
    private readonly IAiApprovalService _approvals;
    private readonly IAiAuditService _audit;
    private readonly ApplicationDbContext _dbContext;
    private readonly AiGuardrailOptions _guardrails;
    private readonly IOptionsMonitor<AiOptions> _options;

    public AiTradeService(
        IAiModel model,
        ITradingService trading,
        IAiToolExecutor readOnlyTools,
        IAiApprovalService approvals,
        IAiAuditService audit,
        ApplicationDbContext dbContext,
        IOptions<AiGuardrailOptions> guardrails,
        IOptionsMonitor<AiOptions> options)
    {
        _model = model;
        _trading = trading;
        _readOnlyTools = readOnlyTools;
        _approvals = approvals;
        _audit = audit;
        _dbContext = dbContext;
        _guardrails = guardrails.Value;
        _options = options;
    }

    public async Task<AiTradeResult> PlanAndExecuteAsync(
        long userId,
        string instruction,
        string defaultSymbol,
        CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.Enabled)
            throw new InvalidOperationException("AI integration is disabled by configuration.");
        if (string.IsNullOrWhiteSpace(instruction) || string.IsNullOrWhiteSpace(defaultSymbol))
            throw new ArgumentException("交易指令和默认交易对不能为空。");

        var normalizedSymbol = defaultSymbol.Trim().ToUpperInvariant();
        var messages = new List<AiMessage>
        {
            new("system", TradePrompt),
            new("user", $"默认交易对：{normalizedSymbol}\n用户指令：{instruction}")
        };
        var approvals = new List<long>();
        var executedActions = new List<AiTradeAction>();
        var response = new AiChatResponse(string.Empty);

        for (var round = 0; round < 5; round++)
        {
            response = await _model.ChatAsync(new AiChatRequest
            {
                Messages = messages,
                Tools = ToolDefinitions,
                Temperature = 0,
                MaxTokens = 512
            }, cancellationToken);
            if (response.ToolCalls is not { Count: > 0 })
                return BuildResult(response.Content, approvals, executedActions);

            messages.Add(new AiMessage("assistant", response.Content, ToolCalls: response.ToolCalls));
            foreach (var toolCall in response.ToolCalls)
            {
                var outcome = await ExecuteToolAsync(userId, toolCall, cancellationToken);
                if (outcome.ApprovalId.HasValue)
                    approvals.Add(outcome.ApprovalId.Value);
                if (outcome.Action is not null)
                    executedActions.Add(outcome.Action);
                messages.Add(new AiMessage("tool", outcome.ToolResult, toolCall.Id));
            }
        }

        return BuildResult(
            "已完成可执行步骤；因工具调用轮次已达到上限，未继续执行其他操作。",
            approvals,
            executedActions);
    }

    public async Task<AiTradeResult> ExecuteApprovedAsync(long userId, long approvalId, CancellationToken cancellationToken = default)
    {
        var approval = await _dbContext.AiApprovals.SingleOrDefaultAsync(value =>
            value.Id == approvalId && value.UserId == userId && !value.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("AI approval was not found.");
        if (approval.Status != "Approved")
            throw new InvalidOperationException("AI operation must be approved before execution.");

        ToolOutcome outcome;
        switch (approval.ActionType)
        {
            case "place_order":
            {
                var plan = ParseOrder(approval.PayloadJson);
                var evaluation = await EvaluateOrderAsync(userId, plan, cancellationToken);
                outcome = evaluation.DenyReason is not null
                    ? await DenyAsync(userId, "place_order", approval.PayloadJson, evaluation.DenyReason, cancellationToken)
                    : await SubmitOrderAsync(userId, plan, evaluation.Notional, cancellationToken);
                break;
            }
            case "cancel_all_orders":
                outcome = await CancelAllOrdersAsync(userId, ParseCancelAll(approval.PayloadJson), cancellationToken);
                break;
            case "cancel_order":
                outcome = await CancelOrderAsync(userId, ParseOrderId(approval.PayloadJson), cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unsupported approved AI action '{approval.ActionType}'.");
        }

        approval.Status = outcome.Status == "success" ? "Executed" : "Failed";
        approval.Touch();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new AiTradeResult(
            outcome.Action?.Summary ?? outcome.ToolResult,
            outcome.Status == "success" ? "executed" : outcome.Status,
            approvalId,
            outcome.Action?.OrderId,
            new[] { approvalId },
            outcome.Action is null ? Array.Empty<AiTradeAction>() : new[] { outcome.Action });
    }

    private IReadOnlyList<AiToolDefinition> ToolDefinitions =>
        _readOnlyTools.Definitions.Concat(
        [
            new AiToolDefinition("place_order", "提交一笔现货限价单或市价单。高风险订单需审批。", PlaceOrderSchema),
            new AiToolDefinition("cancel_order", "取消当前用户的一笔订单。", CancelOrderSchema),
            new AiToolDefinition("cancel_all_orders", "取消当前用户的全部未完成订单，或指定交易对的未完成订单。需审批。", CancelAllSchema)
        ]).ToArray();

    private async Task<ToolOutcome> ExecuteToolAsync(long userId, AiToolCall toolCall, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (_readOnlyTools.Definitions.Any(value => value.Name == toolCall.Name))
            {
                var data = await _readOnlyTools.ExecuteAsync(userId, toolCall, cancellationToken);
                await WriteAuditAsync(userId, toolCall.Name, toolCall.ArgumentsJson, data, "success", stopwatch.ElapsedMilliseconds, cancellationToken);
                return new ToolOutcome("success", data);
            }

            return toolCall.Name switch
            {
                "place_order" => await PlanOrSubmitOrderAsync(userId, toolCall.ArgumentsJson, cancellationToken),
                "cancel_order" => await CancelOrderAsync(userId, ParseOrderId(toolCall.ArgumentsJson), cancellationToken),
                "cancel_all_orders" => await CreateCancellationApprovalAsync(userId, ParseCancelAll(toolCall.ArgumentsJson), cancellationToken),
                _ => throw new InvalidOperationException($"AI attempted to use unsupported tool '{toolCall.Name}'.")
            };
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            var result = JsonSerializer.Serialize(new { error = exception.Message });
            await WriteAuditAsync(userId, toolCall.Name, toolCall.ArgumentsJson, result, "failed", stopwatch.ElapsedMilliseconds, cancellationToken);
            return new ToolOutcome("failed", result);
        }
    }

    private async Task<ToolOutcome> PlanOrSubmitOrderAsync(long userId, string argumentsJson, CancellationToken cancellationToken)
    {
        var plan = ParseOrder(argumentsJson);
        var evaluation = await EvaluateOrderAsync(userId, plan, cancellationToken);
        if (evaluation.DenyReason is not null)
            return await DenyAsync(userId, "place_order", argumentsJson, evaluation.DenyReason, cancellationToken);
        if (evaluation.RequiresApproval)
        {
            var approval = await _approvals.CreateAsync(userId, "place_order", JsonSerializer.Serialize(plan), cancellationToken);
            var result = JsonSerializer.Serialize(new { status = "pending_approval", approvalId = approval.Id, notional = evaluation.Notional });
            await WriteAuditAsync(userId, "place_order", argumentsJson, result, "pending_approval", 0, cancellationToken);
            return new ToolOutcome("pending_approval", result, approval.Id);
        }
        return await SubmitOrderAsync(userId, plan, evaluation.Notional, cancellationToken);
    }

    private async Task<ToolOutcome> SubmitOrderAsync(long userId, AiOrderPlan plan, decimal notional, CancellationToken cancellationToken)
    {
        var response = await _trading.SubmitOrderAsync(userId, ToRequest(plan));
        if (!response.Success)
        {
            var error = response.Error ?? "订单提交失败。";
            return await DenyAsync(userId, "place_order", JsonSerializer.Serialize(plan), error, cancellationToken);
        }

        var action = new AiTradeAction("place_order", $"已提交 {plan.Symbol} {plan.Side} 订单。", response.Data?.Id);
        var result = JsonSerializer.Serialize(new { status = "success", orderId = response.Data?.Id, notional });
        await WriteAuditAsync(userId, "place_order", JsonSerializer.Serialize(plan), result, "success", 0, cancellationToken);
        return new ToolOutcome("success", result, Action: action);
    }

    private async Task<ToolOutcome> CancelOrderAsync(long userId, long orderId, CancellationToken cancellationToken)
    {
        var order = await _trading.GetOrderAsync(userId, orderId);
        if (!order.Success || order.Data is null)
            return await DenyAsync(userId, "cancel_order", JsonSerializer.Serialize(new { orderId }), "订单不存在或不属于当前用户。", cancellationToken);

        var response = await _trading.CancelOrderAsync(userId, orderId);
        if (!response.Success)
            return await DenyAsync(userId, "cancel_order", JsonSerializer.Serialize(new { orderId }), response.Error ?? "订单取消失败。", cancellationToken);

        var action = new AiTradeAction("cancel_order", $"已取消订单 {orderId}。", orderId);
        var result = JsonSerializer.Serialize(new { status = "success", orderId });
        await WriteAuditAsync(userId, "cancel_order", JsonSerializer.Serialize(new { orderId }), result, "success", 0, cancellationToken);
        return new ToolOutcome("success", result, Action: action);
    }

    private async Task<ToolOutcome> CreateCancellationApprovalAsync(long userId, AiCancelAllPlan plan, CancellationToken cancellationToken)
    {
        var approval = await _approvals.CreateAsync(userId, "cancel_all_orders", JsonSerializer.Serialize(plan), cancellationToken);
        var arguments = JsonSerializer.Serialize(plan);
        var result = JsonSerializer.Serialize(new { status = "pending_approval", approvalId = approval.Id, symbol = plan.Symbol });
        await WriteAuditAsync(userId, "cancel_all_orders", arguments, result, "pending_approval", 0, cancellationToken);
        return new ToolOutcome("pending_approval", result, approval.Id);
    }

    private async Task<ToolOutcome> CancelAllOrdersAsync(long userId, AiCancelAllPlan plan, CancellationToken cancellationToken)
    {
        var response = await _trading.CancelAllOrdersAsync(userId, new BatchCancelOrdersRequestDto { Symbol = plan.Symbol });
        if (!response.Success || response.Data is null)
            return await DenyAsync(userId, "cancel_all_orders", JsonSerializer.Serialize(plan), response.Error ?? "批量撤单失败。", cancellationToken);

        var action = new AiTradeAction(
            "cancel_all_orders",
            $"已取消 {response.Data.SuccessCount} 笔{(string.IsNullOrWhiteSpace(plan.Symbol) ? string.Empty : $" {plan.Symbol}")}订单。");
        var result = JsonSerializer.Serialize(new { status = "success", response.Data.SuccessCount, response.Data.FailedCount, response.Data.CancelledOrderIds });
        await WriteAuditAsync(userId, "cancel_all_orders", JsonSerializer.Serialize(plan), result, "success", 0, cancellationToken);
        return new ToolOutcome("success", result, Action: action);
    }

    private async Task<ToolOutcome> DenyAsync(long userId, string toolName, string argumentsJson, string reason, CancellationToken cancellationToken)
    {
        var result = JsonSerializer.Serialize(new { error = reason });
        await WriteAuditAsync(userId, toolName, argumentsJson, result, "denied", 0, cancellationToken);
        return new ToolOutcome("denied", result);
    }

    private async Task<OrderEvaluation> EvaluateOrderAsync(long userId, AiOrderPlan order, CancellationToken cancellationToken)
    {
        if (order.Quantity <= 0 || string.IsNullOrWhiteSpace(order.Symbol))
            return new OrderEvaluation("AI 未能生成有效订单，请明确说明交易对、方向和数量。", false, 0);
        if (order.Type == "limit" && (!order.Price.HasValue || order.Price <= 0))
            return new OrderEvaluation("限价单必须包含有效价格。", false, 0);

        var pair = await _trading.GetTradingPairAsync(order.Symbol);
        if (!pair.Success || pair.Data is null || pair.Data.Price <= 0)
            return new OrderEvaluation("无法获取交易对的当前价格，订单未提交。", false, 0);
        var notional = order.Quantity * (order.Price ?? pair.Data.Price);
        if (order.Type == "limit" && Math.Abs(order.Price!.Value - pair.Data.Price) / pair.Data.Price * 100 > _guardrails.MaxPriceDeviationPercent)
            return new OrderEvaluation("限价偏离当前市场价格超过允许范围，订单未提交。", false, notional);

        var minuteAgo = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        var recentOrders = await _dbContext.AiAuditLogs.CountAsync(value =>
            value.UserId == userId && value.ToolName == "place_order" && value.Status == "success" && value.CreatedAt >= minuteAgo,
            cancellationToken);
        if (recentOrders >= _guardrails.MaxOrdersPerMinute)
            return new OrderEvaluation("已超过 AI 下单频率限制，请稍后再试。", false, notional);

        var openOrders = await _trading.GetOpenOrdersAsync(userId, order.Symbol);
        if (openOrders.Success && openOrders.Data is not null && order.Type == "limit" &&
            openOrders.Data.Any(value => value.Side != ToSide(order.Side) && value.Price.HasValue &&
                ((order.Side == "buy" && value.Price <= order.Price) || (order.Side == "sell" && value.Price >= order.Price))))
            return new OrderEvaluation("该订单可能与您的反向挂单自成交，订单未提交。", false, notional);

        var startOfDay = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var today = await _dbContext.AiAuditLogs.AsNoTracking()
            .Where(value => value.UserId == userId && value.ToolName == "place_order" && value.Status == "success" && value.CreatedAt >= startOfDay)
            .Select(value => value.ResultJson).ToListAsync(cancellationToken);
        var dailyNotional = today.Sum(GetAuditedNotional);
        var requiresApproval = (order.Type == "market" && notional > _guardrails.MarketOrderApprovalThreshold)
            || (order.Type == "limit" && notional > _guardrails.LimitOrderApprovalThreshold)
            || dailyNotional + notional > _guardrails.DailyTradeApprovalThreshold;
        return new OrderEvaluation(null, requiresApproval, notional);
    }

    private static decimal GetAuditedNotional(string resultJson)
    {
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            return document.RootElement.TryGetProperty("notional", out var value) && value.TryGetDecimal(out var notional)
                ? notional
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static AiOrderPlan ParseOrder(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        EnsureOnlyProperties(root, "symbol", "side", "type", "quantity", "price");
        var symbol = GetRequiredString(root, "symbol").Trim().ToUpperInvariant();
        var side = GetRequiredString(root, "side").ToLowerInvariant();
        var type = GetRequiredString(root, "type").ToLowerInvariant();
        var quantity = GetRequiredDecimal(root, "quantity");
        decimal? price = null;
        if (TryGetProperty(root, "price", out var priceElement) && priceElement.ValueKind != JsonValueKind.Null)
            price = priceElement.TryGetDecimal(out var value) ? value : throw new ArgumentException("price must be a number.");
        if (side is not ("buy" or "sell") || type is not ("market" or "limit"))
            throw new ArgumentException("side and type are not supported.");
        return new AiOrderPlan(symbol, side, type, quantity, price);
    }

    private static long ParseOrderId(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        EnsureOnlyProperties(root, "orderId");
        if (!TryGetProperty(root, "orderId", out var value) || !value.TryGetInt64(out var orderId) || orderId <= 0)
            throw new ArgumentException("orderId must be a positive integer.");
        return orderId;
    }

    private static AiCancelAllPlan ParseCancelAll(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        EnsureOnlyProperties(root, "symbol");
        var symbol = TryGetProperty(root, "symbol", out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()?.Trim().ToUpperInvariant()
            : null;
        return new AiCancelAllPlan(string.IsNullOrWhiteSpace(symbol) ? null : symbol);
    }

    private static string GetRequiredString(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new ArgumentException($"{name} is required.");

    private static decimal GetRequiredDecimal(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value) && value.TryGetDecimal(out var result) && result > 0
            ? result
            : throw new ArgumentException($"{name} must be a positive number.");

    private static void EnsureOnlyProperties(JsonElement root, params string[] allowed)
    {
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(value => !allowed.Contains(value.Name, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("工具参数不符合允许的格式。");
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static CreateOrderRequestDto ToRequest(AiOrderPlan order) => new()
    {
        Symbol = order.Symbol,
        Side = ToSide(order.Side),
        Type = order.Type == "market" ? OrderType.Market : OrderType.Limit,
        Quantity = order.Quantity,
        Price = order.Price,
        ClientOrderId = $"AI_{Guid.NewGuid():N}"
    };

    private async Task WriteAuditAsync(long userId, string toolName, string arguments, string result, string status, long elapsedMs, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new AiAuditEntry(userId, null, "ai.tool.execute", _model.ModelId, toolName, arguments, result, status, elapsedMs), cancellationToken);

    private static AiTradeResult BuildResult(string summary, IReadOnlyList<long> approvals, IReadOnlyList<AiTradeAction> actions)
    {
        var fallback = actions.Count > 0
            ? string.Join(" ", actions.Select(value => value.Summary))
            : approvals.Count > 0
                ? "操作等待用户确认。"
                : "未执行交易操作。";
        return new AiTradeResult(
            string.IsNullOrWhiteSpace(summary) ? fallback : summary,
            approvals.Count > 0 ? "pending_approval" : actions.Count > 0 ? "executed" : "completed",
            approvals.FirstOrDefault() == 0 ? null : approvals.FirstOrDefault(),
            actions.FirstOrDefault(value => value.OrderId.HasValue)?.OrderId,
            approvals,
            actions);
    }

    private static OrderSide ToSide(string side) => side == "buy" ? OrderSide.Buy : OrderSide.Sell;

    private sealed record AiOrderPlan(string Symbol, string Side, string Type, decimal Quantity, decimal? Price);
    private sealed record AiCancelAllPlan(string? Symbol);
    private sealed record OrderEvaluation(string? DenyReason, bool RequiresApproval, decimal Notional);
    private sealed record ToolOutcome(string Status, string ToolResult, long? ApprovalId = null, AiTradeAction? Action = null);
}
