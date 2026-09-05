using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CryptoSpot.Application.Abstractions.Services.Ai;
using CryptoSpot.Application.Abstractions.Services.Trading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CryptoSpot.Infrastructure.Ai;

public sealed class AiExpansionService : IAiExpansionService
{
    private static readonly Regex WordPattern = new(@"[A-Za-z0-9_]{2,}", RegexOptions.Compiled);
    private readonly IAiModel _model;
    private readonly ITradingService _tradingService;
    private readonly IAiAuditService _audit;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly Lazy<IReadOnlyList<DocumentChunk>> _documents;

    public AiExpansionService(
        IAiModel model,
        ITradingService tradingService,
        IAiAuditService audit,
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<AiOptions> options)
    {
        _model = model;
        _tradingService = tradingService;
        _audit = audit;
        _options = options;
        _documents = new Lazy<IReadOnlyList<DocumentChunk>>(() => LoadDocuments(hostEnvironment.ContentRootPath));
    }

    public async Task<DocumentAssistantResult> AnswerDocumentationAsync(long userId, string question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("问题不能为空。", nameof(question));

        var matches = FindRelevantChunks(question).Take(4).ToList();
        if (matches.Count == 0)
        {
            const string noMatch = "未在平台文档中找到与该问题直接相关的内容。请换一种描述，或查看项目文档目录。";
            await AuditAsync(userId, "ai.docs.answer", "{}", JsonSerializer.Serialize(new { answer = noMatch }), "success", cancellationToken);
            return new DocumentAssistantResult(noMatch, Array.Empty<string>());
        }

        var citations = matches.Select(value => value.Path).Distinct(StringComparer.Ordinal).ToArray();
        var context = string.Join("\n\n", matches.Select(value => $"来源：{value.Path}\n{value.Content}"));
        var fallback = BuildExtractiveAnswer(matches);
        var answer = fallback;
        var status = "fallback";
        if (_options.CurrentValue.Enabled)
        {
            try
            {
                var response = await _model.ChatAsync(new AiChatRequest
                {
                    Messages =
                    [
                        new AiMessage("system", "你是 CryptoSpot 平台文档助手。仅基于给出的文档片段回答，不知道就明确说未找到。不得编造产品行为、价格或规则。用中文作答，并在陈述后引用提供的来源路径。"),
                        new AiMessage("user", $"问题：{question}\n\n文档片段：\n{context}")
                    ],
                    Temperature = 0,
                    MaxTokens = 700
                }, cancellationToken);
                if (!string.IsNullOrWhiteSpace(response.Content))
                {
                    answer = response.Content;
                    status = "success";
                }
            }
            catch (HttpRequestException)
            {
                status = "fallback";
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                status = "fallback";
            }
        }
        await AuditAsync(userId, "ai.docs.answer", JsonSerializer.Serialize(new { question, citations }),
            JsonSerializer.Serialize(new { answer, citations }), status, cancellationToken);
        return new DocumentAssistantResult(answer, citations);
    }

    public async Task<NewsSentimentResult> AnalyzeNewsSentimentAsync(
        long userId,
        NewsSentimentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Headline))
            throw new ArgumentException("新闻标题不能为空。", nameof(request));

        var text = $"{request.Headline}\n{request.Content}".Trim();
        var result = AnalyzeLexically(text, request.Symbol);
        var status = "fallback";
        if (_options.CurrentValue.Enabled)
        {
            try
            {
                var response = await _model.ChatAsync(new AiChatRequest
                {
                    Messages =
                    [
                        new AiMessage("system", "分析新闻对加密资产的短期情绪。仅返回 JSON：{\"sentiment\":\"positive|negative|neutral\",\"score\":-1到1之间的数值,\"summary\":\"中文简述\"}。不提供投资建议。"),
                        new AiMessage("user", $"交易对：{request.Symbol ?? "未指定"}\n新闻：{text}")
                    ],
                    Temperature = 0,
                    MaxTokens = 512
                }, cancellationToken);
                result = ParseSentiment(response.Content, request.Symbol);
                status = "success";
            }
            catch (HttpRequestException)
            {
                status = "fallback";
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                status = "fallback";
            }
        }
        await AuditAsync(userId, "ai.news.sentiment", JsonSerializer.Serialize(request), JsonSerializer.Serialize(result), status, cancellationToken);
        return result;
    }

    public async Task<PortfolioReportResult> CreatePortfolioReportAsync(long userId, CancellationToken cancellationToken = default)
    {
        var assetsResponse = await _tradingService.GetUserAssetsAsync(userId);
        var summaryResponse = await _tradingService.GetUserAssetSummaryAsync(userId);
        var tradesResponse = await _tradingService.GetUserTradesAsync(userId);
        var assets = assetsResponse.Success && assetsResponse.Data is not null ? assetsResponse.Data.Where(value => value.Total > 0).ToList() : [];
        var trades = tradesResponse.Success && tradesResponse.Data is not null ? tradesResponse.Data.OrderByDescending(value => value.ExecutedAt).Take(10).ToList() : [];
        var totalValue = summaryResponse.Success && summaryResponse.Data is not null ? summaryResponse.Data.TotalValue : assets.Sum(value => value.UsdtValue ?? value.Total);
        var context = JsonSerializer.Serialize(new
        {
            totalValue,
            assets = assets.Select(value => new { value.Symbol, value.Available, value.Frozen, value.Total, value.UsdtValue }),
            recentTrades = trades.Select(value => new { value.Symbol, value.Side, value.Price, value.Quantity, value.ExecutedAt })
        });
        var fallback = BuildPortfolioFallback(assets, trades.Count, totalValue);
        var report = fallback;
        var status = "fallback";
        if (_options.CurrentValue.Enabled)
        {
            try
            {
                var response = await _model.ChatAsync(new AiChatRequest
                {
                    Messages =
                    [
                        new AiMessage("system", "你是 CryptoSpot 持仓报告助手。仅基于提供的用户资产和近期成交生成中文报告，覆盖资产概览、集中度观察和风险提示。不得推测成本、收益或未来价格；报告不构成投资建议。"),
                        new AiMessage("user", $"用户数据：{context}")
                    ],
                    Temperature = 0,
                    MaxTokens = 700
                }, cancellationToken);
                if (!string.IsNullOrWhiteSpace(response.Content))
                {
                    report = response.Content;
                    status = "success";
                }
            }
            catch (HttpRequestException)
            {
                status = "fallback";
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                status = "fallback";
            }
        }
        var citations = new[] { "trading.assets", "trading.recent_trades" };
        var result = new PortfolioReportResult(report, totalValue, citations);
        await AuditAsync(userId, "ai.portfolio.report", "{}", JsonSerializer.Serialize(result), status, cancellationToken);
        return result;
    }

    private IEnumerable<DocumentChunk> FindRelevantChunks(string question)
    {
        var queryTokens = Tokenize(question);
        return _documents.Value
            .Select(value => new { Chunk = value, Score = value.Tokens.Intersect(queryTokens).Count() + (value.Content.Contains(question, StringComparison.OrdinalIgnoreCase) ? 10 : 0) })
            .Where(value => value.Score > 0)
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Chunk.Path, StringComparer.Ordinal)
            .Select(value => value.Chunk);
    }

    private static IReadOnlyList<DocumentChunk> LoadDocuments(string contentRoot)
    {
        var docsDirectory = FindDocsDirectory(contentRoot);
        if (docsDirectory is null)
            return Array.Empty<DocumentChunk>();

        var chunks = new List<DocumentChunk>();
        foreach (var file in Directory.EnumerateFiles(docsDirectory, "*.md", SearchOption.AllDirectories).OrderBy(value => value, StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(docsDirectory, file).Replace('\\', '/');
            var text = File.ReadAllText(file);
            foreach (var chunk in SplitIntoChunks(text))
                chunks.Add(new DocumentChunk(relativePath, chunk, Tokenize(chunk)));
        }
        return chunks;
    }

    private static string? FindDocsDirectory(string contentRoot)
    {
        var candidates = new[]
        {
            Path.Combine(contentRoot, "docs"),
            Path.Combine(Directory.GetCurrentDirectory(), "docs")
        };
        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        var directory = new DirectoryInfo(contentRoot);
        for (var depth = 0; directory is not null && depth < 4; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "docs");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static IEnumerable<string> SplitIntoChunks(string text)
    {
        const int maxLength = 1400;
        var current = new StringBuilder();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.Length > 0 && current.Length + paragraph.Length + 2 > maxLength)
            {
                yield return current.ToString();
                current.Clear();
            }
            if (paragraph.Length > maxLength)
            {
                for (var offset = 0; offset < paragraph.Length; offset += maxLength)
                    yield return paragraph.Substring(offset, Math.Min(maxLength, paragraph.Length - offset));
            }
            else
            {
                if (current.Length > 0)
                    current.AppendLine().AppendLine();
                current.Append(paragraph);
            }
        }
        if (current.Length > 0)
            yield return current.ToString();
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in WordPattern.Matches(text))
            tokens.Add(match.Value);
        foreach (var character in text)
        {
            if (character >= '\u4e00' && character <= '\u9fff')
                tokens.Add(character.ToString());
        }
        return tokens;
    }

    private static string BuildExtractiveAnswer(IReadOnlyList<DocumentChunk> chunks) =>
        "根据平台文档：\n" + string.Join("\n\n", chunks.Take(2).Select(value => $"{value.Content[..Math.Min(500, value.Content.Length)]}\n来源：{value.Path}"));

    private static NewsSentimentResult ParseSentiment(string content, string? symbol)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var sentiment = root.TryGetProperty("sentiment", out var sentimentElement)
            ? sentimentElement.GetString()?.ToLowerInvariant()
            : null;
        var score = root.TryGetProperty("score", out var scoreElement) && scoreElement.TryGetDecimal(out var parsedScore)
            ? Math.Clamp(parsedScore, -1m, 1m)
            : throw new JsonException("score is missing.");
        var summary = root.TryGetProperty("summary", out var summaryElement) ? summaryElement.GetString() : null;
        if (sentiment is not ("positive" or "negative" or "neutral") || string.IsNullOrWhiteSpace(summary))
            throw new JsonException("sentiment response is invalid.");
        return new NewsSentimentResult(sentiment, score, summary, NormalizeSymbol(symbol));
    }

    private static NewsSentimentResult AnalyzeLexically(string text, string? symbol)
    {
        var positive = new[] { "上涨", "利好", "增长", "获批", "突破", "bullish", "surge", "gain", "approval", "adoption" };
        var negative = new[] { "下跌", "利空", "暴跌", "风险", "黑客", "处罚", "bearish", "drop", "hack", "ban", "lawsuit" };
        var positiveCount = positive.Count(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
        var negativeCount = negative.Count(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
        var score = Math.Clamp((decimal)(positiveCount - negativeCount) / Math.Max(positiveCount + negativeCount, 1), -1m, 1m);
        var sentiment = score switch { > 0 => "positive", < 0 => "negative", _ => "neutral" };
        return new NewsSentimentResult(sentiment, score, $"基于关键词的确定性情绪判断为 {sentiment}；模型服务不可用时请谨慎解读。", NormalizeSymbol(symbol));
    }

    private static string BuildPortfolioFallback<TAsset>(IReadOnlyList<TAsset> assets, int tradeCount, decimal totalValue)
        where TAsset : CryptoSpot.Application.DTOs.Users.AssetDto =>
        $"资产概览：当前记录的总资产值为 {totalValue:F2}。持有 {assets.Count} 种非零资产，近期共有 {tradeCount} 笔成交记录可供参考。请注意，资产估值和成本数据可能不完整；避免过度集中并根据自身风险承受能力管理仓位。本报告不构成投资建议。";

    private async Task AuditAsync(long userId, string action, string arguments, string result, string status, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new AiAuditEntry(userId, null, action, _model.ModelId, "none", arguments, result, status, 0), cancellationToken);

    private static string? NormalizeSymbol(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

    private sealed record DocumentChunk(string Path, string Content, HashSet<string> Tokens);
}
