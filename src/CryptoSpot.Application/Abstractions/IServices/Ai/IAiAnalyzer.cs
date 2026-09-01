namespace CryptoSpot.Application.Abstractions.Services.Ai;

public interface IAiAnalyzer
{
    Task<AiAnalysisResult> AnalyzeAsync(long userId, string question, string symbol, CancellationToken cancellationToken = default);
}

public sealed record AiAnalysisResult(string Analysis, IReadOnlyList<string> Citations);
