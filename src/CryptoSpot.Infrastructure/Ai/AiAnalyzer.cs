using System.Text.Json;
using CryptoSpot.Application.Abstractions.Services.Ai;
using CryptoSpot.Application.Abstractions.Services.Trading;
using Microsoft.Extensions.Options;

namespace CryptoSpot.Infrastructure.Ai;

public sealed class AiAnalyzer : IAiAnalyzer
{
    private const string SystemPrompt = """
        你是 CryptoSpot 交易平台的 AI 行情分析师。只允许依据下方提供的真实平台数据作答，禁止编造价格、持仓或指标。
        回答依次覆盖：当前价格与关键数据、技术面、用户资产和挂单（如有）、观察建议、风险提示。
        回答不构成投资建议。数据不足时必须明确说明。
        """;

    private readonly IAiModel _model;
    private readonly ITradingService _tradingService;
    private readonly IAiToolExecutor _tools;
    private readonly IAiAuditService _audit;
    private readonly IOptionsMonitor<AiOptions> _options;

    public AiAnalyzer(IAiModel model, ITradingService tradingService, IAiToolExecutor tools, IAiAuditService audit, IOptionsMonitor<AiOptions> options)
    {
        _model = model;
        _tradingService = tradingService;
        _tools = tools;
        _audit = audit;
        _options = options;
    }

    public async Task<AiAnalysisResult> AnalyzeAsync(long userId, string question, string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question is required.", nameof(question));
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        var options = _options.CurrentValue;
        if (!options.Enabled)
            throw new InvalidOperationException("AI integration is disabled by configuration.");

        symbol = symbol.Trim().ToUpperInvariant();
        var citations = new List<string>();
        var messages = new List<AiMessage>
        {
            new("system", SystemPrompt),
            new("user", $"用户问题：{question}\n默认交易对：{symbol}")
        };
        AiChatResponse response = new(string.Empty);
        for (var round = 0; round < 5; round++)
        {
            response = await _model.ChatAsync(new AiChatRequest
            {
                Messages = messages,
                Tools = _tools.Definitions,
                Temperature = options.Temperature,
                MaxTokens = options.MaxTokens
            }, cancellationToken);
            if (response.ToolCalls is not { Count: > 0 })
                return new AiAnalysisResult(response.Content, citations);

            messages.Add(new AiMessage("assistant", response.Content, ToolCalls: response.ToolCalls));
            foreach (var toolCall in response.ToolCalls)
            {
                var result = await _tools.ExecuteAsync(userId, toolCall, cancellationToken);
                citations.Add(toolCall.Name);
                await _audit.WriteAsync(new AiAuditEntry(userId, null, "ai.tool.call", _model.ModelId,
                    toolCall.Name, toolCall.ArgumentsJson, result, "success", 0), cancellationToken);
                messages.Add(new AiMessage("tool", result, toolCall.Id));
            }
        }
        throw new InvalidOperationException("AI analysis exceeded the maximum of five tool-call rounds.");
    }
}
