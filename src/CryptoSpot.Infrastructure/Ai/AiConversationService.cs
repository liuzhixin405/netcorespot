using CryptoSpot.Application.Abstractions.Services.Ai;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Ai;

public sealed class AiConversationService : IAiConversationService
{
    private readonly ApplicationDbContext _dbContext;

    public AiConversationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AiConversationDto> GetOrCreateAsync(long userId, long? conversationId, string firstMessage, CancellationToken cancellationToken = default)
    {
        AiConversation conversation;
        if (conversationId.HasValue)
        {
            conversation = await _dbContext.AiConversations.SingleOrDefaultAsync(value =>
                value.Id == conversationId.Value && value.UserId == userId && !value.IsDeleted, cancellationToken)
                ?? throw new InvalidOperationException("AI conversation was not found.");
        }
        else
        {
            conversation = new AiConversation { UserId = userId, Title = firstMessage.Trim()[..Math.Min(firstMessage.Trim().Length, 200)] };
            _dbContext.AiConversations.Add(conversation);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AiConversationDto(conversation.Id, conversation.Title, conversation.UpdatedDateTime);
    }

    public async Task AddMessageAsync(long conversationId, string role, string content, CancellationToken cancellationToken = default)
    {
        if (role is not ("user" or "assistant" or "tool" or "system"))
            throw new ArgumentOutOfRangeException(nameof(role), "Unsupported AI message role.");

        _dbContext.AiMessages.Add(new CryptoSpot.Domain.Entities.AiMessage
        {
            ConversationId = conversationId,
            Role = role,
            Content = content
        });
        var conversation = await _dbContext.AiConversations.FindAsync(new object?[] { conversationId }, cancellationToken)
            ?? throw new InvalidOperationException("AI conversation was not found.");
        conversation.Touch();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiConversationDto>> ListAsync(long userId, CancellationToken cancellationToken = default) =>
        await _dbContext.AiConversations.AsNoTracking().Where(value => value.UserId == userId && !value.IsDeleted)
            .OrderByDescending(value => value.UpdatedAt)
            .Select(value => new AiConversationDto(value.Id, value.Title, value.UpdatedDateTime))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AiMessageDto>> GetMessagesAsync(long userId, long conversationId, CancellationToken cancellationToken = default) =>
        await _dbContext.AiMessages.AsNoTracking()
            .Where(value => value.ConversationId == conversationId && value.Conversation.UserId == userId && !value.IsDeleted)
            .OrderBy(value => value.CreatedAt)
            .Select(value => new AiMessageDto(value.Id, value.Role, value.Content, value.CreatedDateTime))
            .ToListAsync(cancellationToken);
}
