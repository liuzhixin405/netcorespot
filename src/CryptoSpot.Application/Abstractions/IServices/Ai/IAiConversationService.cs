namespace CryptoSpot.Application.Abstractions.Services.Ai;

public interface IAiConversationService
{
    Task<AiConversationDto> GetOrCreateAsync(long userId, long? conversationId, string firstMessage, CancellationToken cancellationToken = default);
    Task AddMessageAsync(long conversationId, string role, string content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiConversationDto>> ListAsync(long userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiMessageDto>> GetMessagesAsync(long userId, long conversationId, CancellationToken cancellationToken = default);
}

public sealed record AiConversationDto(long Id, string? Title, DateTime UpdatedAt);
public sealed record AiMessageDto(long Id, string Role, string Content, DateTime CreatedAt);
