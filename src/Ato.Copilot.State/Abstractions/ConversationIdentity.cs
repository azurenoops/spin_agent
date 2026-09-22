using System.Security.Cryptography;
using System.Text.Json;

namespace Ato.Copilot.State.Abstractions;

/// <summary>
/// Server-validated owner of conversation history, workflow state and AI-thread references.
/// External conversation IDs are labels within this namespace, never bearer credentials.
/// </summary>
public sealed record ConversationIdentity(
    Guid DirectoryId, Guid ObjectId, string Kind, Guid? TenantId, string Mode,
    Guid? PersonId, string? SystemId = null)
{
    public string ActorId => $"{DirectoryId:D}/{ObjectId:D}";

    public string StorageKey(string conversationId) =>
        "workspace:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { DirectoryId, ObjectId, Kind, TenantId, Mode, PersonId, SystemId, ConversationId = conversationId })));
}

/// <summary>
/// Trusted ambient identity. HTTP implementations must fail closed without a validated
/// workspace; null is reserved for local, non-HTTP callers.
/// </summary>
public interface IConversationIdentityAccessor
{
    ConversationIdentity? Current { get; }
}
