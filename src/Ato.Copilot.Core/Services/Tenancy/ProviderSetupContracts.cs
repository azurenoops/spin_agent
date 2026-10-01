using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;

namespace Ato.Copilot.Core.Services.Tenancy;

public sealed record ProviderSetupActor(string DirectoryTenantId, string ObjectId, string DisplayName);
public sealed record ProviderServiceDescription(string EnvironmentKind, string? EnvironmentLabel = null,
    string? ServiceModel = null, string? ManagedBy = null, string? IntendedUse = null,
    string? DeclaredImpactLevel = null, string? DeclaredImpactText = null);
public sealed record SaveProviderSetupDraft(long ExpectedRevision, JsonElement Draft);
public sealed record CommitProviderSetup(long ExpectedRevision, string Section, long ExpectedProfileRevision,
    long? ExpectedOfferingRevision = null);
public sealed record CompleteProviderSetup(long ExpectedRevision, long ExpectedProfileRevision, bool Confirmed,
    IReadOnlyList<Guid> AcknowledgedUnresolvedIntentIds);
public sealed record ReconcileProviderSetup(string Operation, string RequestKey);
public sealed record ProviderUploadFile(int Ordinal, string FileName, string MediaType, long ByteLength, string Sha256);
public sealed record ProviderContentDeclaration(string Classification, IReadOnlyList<string> Markings, bool ContainsOnlySyntheticData);
public sealed record ProviderUploadIntentInput(Guid IntentId, int SchemaVersion, string PackageName, string EntryPoint,
    string AssociationMode, Guid? OfferingHintId, PackageOfferingContext? Context,
    IReadOnlyList<ProviderUploadFile> Files, string HandlingPolicyVersion, ProviderContentDeclaration DeclaredContent);
public sealed record PrepareProviderUpload(long ExpectedSetupRevision, ProviderUploadIntentInput Intent);
public sealed record ReconcileProviderReceipt(string RequestKey, string IntentHash);
public sealed record ProviderSetupCommitOutcome(Guid CommandId, Guid ProviderId, Guid DraftId, string Operation,
    DateTimeOffset CommittedAt, long CommittedDraftRevision, long CommittedProfileRevision,
    Guid? CommittedOfferingId = null, long? CommittedOfferingRevision = null);
public sealed record ProviderSetupCommandResult(string Outcome, bool Replayed,
    ProviderSetupCommitOutcome? CommittedOutcome, JsonElement? HistoricalCommitSnapshot, object Current);
