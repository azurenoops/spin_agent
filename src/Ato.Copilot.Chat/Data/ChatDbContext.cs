using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ato.Copilot.Chat.Models;

namespace Ato.Copilot.Chat.Data;

/// <summary>
/// EF Core database context for the Chat application.
/// Supports dual-provider registration (SQLite for development, SQL Server for production).
/// Uses JSON ValueConverters for cross-provider compatibility.
/// </summary>
public class ChatDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatDbContext"/> class.
    /// </summary>
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }

    // ─── DbSets ──────────────────────────────────────────────────────

    /// <summary>Chat conversations.</summary>
    public DbSet<Conversation> Conversations => Set<Conversation>();

    /// <summary>Chat messages within conversations.</summary>
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    /// <summary>Contextual metadata for conversations.</summary>
    public DbSet<ConversationContext> ConversationContexts => Set<ConversationContext>();

    /// <summary>File attachments for messages.</summary>
    public DbSet<MessageAttachment> Attachments => Set<MessageAttachment>();

    // ─── #1357 Collaboration DbSets ──────────────────────────────────

    /// <summary>Named collaborators granted access to a document. (#1357 Sub-task 1)</summary>
    public DbSet<DocumentCollaborator> DocumentCollaborators => Set<DocumentCollaborator>();

    /// <summary>Per-document link-sharing settings. (#1357 Sub-task 1)</summary>
    public DbSet<DocumentLinkSharing> DocumentLinkSharing => Set<DocumentLinkSharing>();

    /// <summary>Inline comment threads anchored to text selections. (#1357 Sub-task 4)</summary>
    public DbSet<DocumentCommentThread> DocumentCommentThreads => Set<DocumentCommentThread>();

    /// <summary>Replies within comment threads. (#1357 Sub-task 4)</summary>
    public DbSet<CommentReply> CommentReplies => Set<CommentReply>();

    /// <summary>
    /// Configures entity relationships, constraints, indexes, and JSON value converters
    /// per data-model.md specification.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ─── Value Converters (cross-provider: SQLite + SQL Server) ───

        var stringListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
        );

        var dictConverter = new ValueConverter<Dictionary<string, object>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>()
        );

        var toolResultConverter = new ValueConverter<ToolExecutionResult?, string?>(
            v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => string.IsNullOrEmpty(v) ? null : JsonSerializer.Deserialize<ToolExecutionResult>(v, (JsonSerializerOptions?)null)
        );

        // ─── Conversation ────────────────────────────────────────────

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(450);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.UserId).HasMaxLength(100);
            entity.Property(e => e.Metadata).HasConversion(dictConverter);

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.UpdatedAt);

            entity.HasMany(e => e.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Context)
                .WithOne(c => c.Conversation)
                .HasForeignKey<ConversationContext>(c => c.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── ChatMessage ─────────────────────────────────────────────

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(450);
            entity.Property(e => e.ConversationId).HasMaxLength(450);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Role).HasConversion<string>();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.Metadata).HasConversion(dictConverter);
            entity.Property(e => e.ParentMessageId).HasMaxLength(450);
            entity.Property(e => e.Tools).HasConversion(stringListConverter);
            entity.Property(e => e.ToolResult).HasConversion(toolResultConverter);

            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Role);

            entity.HasMany(e => e.Attachments)
                .WithOne(a => a.Message)
                .HasForeignKey(a => a.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── ConversationContext ─────────────────────────────────────

        modelBuilder.Entity<ConversationContext>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(450);
            entity.Property(e => e.ConversationId).HasMaxLength(450);
            entity.Property(e => e.Type).HasMaxLength(50);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Data).HasConversion(dictConverter);
            entity.Property(e => e.Tags).HasConversion(stringListConverter);

            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.LastAccessedAt);
        });

        // ─── MessageAttachment ───────────────────────────────────────

        modelBuilder.Entity<MessageAttachment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(450);
            entity.Property(e => e.MessageId).HasMaxLength(450);
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.StoragePath).HasMaxLength(500);
            entity.Property(e => e.Type).HasConversion<string>();
            entity.Property(e => e.Metadata).HasConversion(dictConverter);

            entity.HasIndex(e => e.MessageId);
            entity.HasIndex(e => e.UploadedAt);
            entity.HasIndex(e => e.Type);
        });

        // ─── #1357: DocumentCollaborator ─────────────────────────────

        modelBuilder.Entity<DocumentCollaborator>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(450);
            entity.Property(e => e.DocumentId).HasMaxLength(450);
            entity.Property(e => e.Email).HasMaxLength(320);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Permission).HasConversion<string>();
            entity.Property(e => e.InvitedByUserId).HasMaxLength(450);

            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => new { e.DocumentId, e.Email }).IsUnique();
        });

        // ─── #1357: DocumentLinkSharing ──────────────────────────────

        modelBuilder.Entity<DocumentLinkSharing>(entity =>
        {
            entity.HasKey(e => e.DocumentId);
            entity.Property(e => e.DocumentId).HasMaxLength(450);
            entity.Property(e => e.LinkPermission).HasConversion<string?>();
        });

        // ─── #1357: DocumentCommentThread ────────────────────────────

        modelBuilder.Entity<DocumentCommentThread>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(450);
            entity.Property(e => e.DocumentId).HasMaxLength(450);
            entity.Property(e => e.AnchorBlockId).HasMaxLength(450);
            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.ResolvedByUserId).HasMaxLength(450);

            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => new { e.DocumentId, e.CreatedAt });

            entity.HasMany(e => e.Replies)
                .WithOne(r => r.Thread)
                .HasForeignKey(r => r.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── #1357: CommentReply ──────────────────────────────────────

        modelBuilder.Entity<CommentReply>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(450);
            entity.Property(e => e.ThreadId).HasMaxLength(450);
            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.Property(e => e.DisplayName).HasMaxLength(200);

            entity.HasIndex(e => e.ThreadId);
        });
    }
}
