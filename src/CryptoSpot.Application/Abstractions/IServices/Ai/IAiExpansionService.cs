namespace CryptoSpot.Application.Abstractions.Services.Ai;

public interface IAiExpansionService
{
    Task<DocumentAssistantResult> AnswerDocumentationAsync(long userId, string question, CancellationToken cancellationToken = default);
    Task<NewsSentimentResult> AnalyzeNewsSentimentAsync(long userId, NewsSentimentRequest request, CancellationToken cancellationToken = default);
    Task<PortfolioReportResult> CreatePortfolioReportAsync(long userId, CancellationToken cancellationToken = default);
}

public sealed record DocumentAssistantResult(string Answer, IReadOnlyList<string> Citations);

public sealed record NewsSentimentRequest(string Headline, string? Content = null, string? Symbol = null);

public sealed record NewsSentimentResult(string Sentiment, decimal Score, string Summary, string? Symbol);

public sealed record PortfolioReportResult(string Report, decimal TotalValue, IReadOnlyList<string> Citations);
