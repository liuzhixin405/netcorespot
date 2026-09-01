using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Domain.Entities;

[Table("AiConversations")]
public sealed class AiConversation : BaseEntity
{
    public long UserId { get; set; }
    [MaxLength(200)]
    public string? Title { get; set; }
    public ICollection<AiMessage> Messages { get; set; } = new List<AiMessage>();
}

[Table("AiMessages")]
public sealed class AiMessage : BaseEntity
{
    public long ConversationId { get; set; }
    [MaxLength(20)]
    public required string Role { get; set; }
    public required string Content { get; set; }
    public AiConversation Conversation { get; set; } = null!;
}

[Table("AiAuditLogs")]
public sealed class AiAuditLog : BaseEntity
{
    public long UserId { get; set; }
    public long? ConversationId { get; set; }
    [MaxLength(50)]
    public required string Action { get; set; }
    [MaxLength(100)]
    public string? ModelId { get; set; }
    [MaxLength(100)]
    public string ToolName { get; set; } = "none";
    public string? ToolArgumentsJson { get; set; }
    public required string ResultJson { get; set; }
    [MaxLength(20)]
    public required string Status { get; set; }
    public long ElapsedMs { get; set; }
}

[Table("AiApprovals")]
public sealed class AiApproval : BaseEntity
{
    public long UserId { get; set; }
    public long? AuditLogId { get; set; }
    [MaxLength(100)]
    public required string ActionType { get; set; }
    public required string PayloadJson { get; set; }
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";
    public long ExpiresAt { get; set; }
    public long? DecidedAt { get; set; }
}
