using CryptoSpot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoSpot.Persistence.Data.Configurations;

public sealed class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> entity)
    {
        entity.HasKey(value => value.Id);
        entity.HasIndex(value => new { value.UserId, value.UpdatedAt });
        entity.HasMany(value => value.Messages).WithOne(value => value.Conversation)
            .HasForeignKey(value => value.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public void Configure(EntityTypeBuilder<AiMessage> entity)
    {
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Role).IsRequired().HasMaxLength(20);
        entity.Property(value => value.Content).IsRequired().HasColumnType("longtext");
        entity.HasIndex(value => new { value.ConversationId, value.CreatedAt });
    }
}

public sealed class AiAuditLogConfiguration : IEntityTypeConfiguration<AiAuditLog>
{
    public void Configure(EntityTypeBuilder<AiAuditLog> entity)
    {
        entity.HasKey(value => value.Id);
        entity.Property(value => value.ResultJson).IsRequired().HasColumnType("longtext");
        entity.Property(value => value.ToolArgumentsJson).HasColumnType("longtext");
        entity.HasIndex(value => new { value.UserId, value.CreatedAt });
    }
}

public sealed class AiApprovalConfiguration : IEntityTypeConfiguration<AiApproval>
{
    public void Configure(EntityTypeBuilder<AiApproval> entity)
    {
        entity.HasKey(value => value.Id);
        entity.Property(value => value.PayloadJson).IsRequired().HasColumnType("longtext");
        entity.HasIndex(value => new { value.UserId, value.Status, value.ExpiresAt });
    }
}
