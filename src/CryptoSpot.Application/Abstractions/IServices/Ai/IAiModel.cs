namespace CryptoSpot.Application.Abstractions.Services.Ai;

public interface IAiModel
{
    string ModelId { get; }
    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AiChatChunk> ChatStreamAsync(AiChatRequest request, CancellationToken cancellationToken = default);
}

public sealed record AiChatRequest
{
    public required IReadOnlyList<AiMessage> Messages { get; init; }
    public IReadOnlyList<AiToolDefinition>? Tools { get; init; }
    public double Temperature { get; init; } = 0.2;
    public int MaxTokens { get; init; } = 512;
}

public sealed record AiMessage(string Role, string Content, string? ToolCallId = null, IReadOnlyList<AiToolCall>? ToolCalls = null);
public sealed record AiToolDefinition(string Name, string Description, string ParametersJsonSchema);
public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);

public sealed record AiChatResponse(string Content, IReadOnlyList<AiToolCall>? ToolCalls = null, string? FinishReason = null);
public sealed record AiChatChunk(string? ContentDelta, IReadOnlyList<AiToolCall>? ToolCalls = null, string? FinishReason = null);
