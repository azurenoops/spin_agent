namespace Ato.Copilot.Core.Dtos.SystemDesign;

public sealed record DesignSource(string Type, string Id, string Version, string Provenance,
    string ReviewState, int Precedence, string ResolutionUrl)
{
    public Guid? SourceTenantId { get; init; }
}

public sealed record DesignNode
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Kind { get; init; } = "ExternalSystem";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DiagramRole { get; init; }
    public DesignSource? Source { get; init; }
    public string BoundaryDisposition { get; init; } = "Undetermined";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? BoundaryDefinitionId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? BoundaryRationale { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SecurityResponsibility { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? BoundaryRelationship { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalAuthorizationReference { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DataFlowRole { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? FunctionDescription { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DataRetention { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DisposalMethod { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? NetworkRole { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? NetworkSegment { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? NetworkAddress { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? HostingImpactLevel { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SacaZone { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SacaRole { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DeploymentScopeNodeId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DeploymentOwner { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DeploymentEvidenceReference { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DeploymentSecurityFunctions { get; init; }
    public string? Environment { get; init; }
    public string? NetworkZone { get; init; }
    public string? Provider { get; init; }
    public string ProjectionStatus { get; init; } = "Working";
    public string ReviewState { get; init; } = "Draft";
    public string SspImpact { get; init; } = "System description";
    public Dictionary<string, string?> Properties { get; init; } = [];
}

public sealed record DesignEdge
{
    public string Id { get; init; } = "";
    public string SourceNodeId { get; init; } = "";
    public string TargetNodeId { get; init; } = "";
    public string RelationshipType { get; init; } = "DataFlow";
    public string Origin { get; init; } = "UserAuthored";
    public string Direction { get; init; } = "Outbound";
    public string? Purpose { get; init; }
    public string? InformationType { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? InformationTypeId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LifecycleStage { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ProtocolStack { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? StandardsReference { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ConnectionMedium { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SecurityControlReferences { get; init; }
    public string? Classification { get; init; }
    public string? Port { get; init; }
    public string? Protocol { get; init; }
    public string? Service { get; init; }
    public string? PpsEntryId { get; init; }
    public string? Protection { get; init; }
    public string? EncryptionState { get; init; }
    public string BoundaryCrossing { get; init; } = "Unknown";
    public string? InterconnectionId { get; init; }
    public string? AgreementStatus { get; init; }
    public DesignSource? Source { get; init; }
    public string ReviewState { get; init; } = "Draft";
    public string ProjectionStatus { get; init; } = "Working";
}

public sealed record DesignGroup(string Id, string Label, string Kind, IReadOnlyList<string> NodeIds);
public sealed record DesignGap(string Id, string Severity, string Explanation, string RecordId,
    string View, string SspImpact, string Owner, string ResolutionUrl);
public sealed record DesignContribution(string Section, int RecordCount, string State, string Explanation, string ResolutionUrl);
public sealed record DesignChange(string Kind, string RecordId, string? Before, string? After);
public sealed record ScopeWordingBasis(Guid DraftId, long Revision, string SourceHash, string Origin,
    string OriginalWording, bool UserEdited, IReadOnlyList<string> SourceIds);
public sealed record ComponentScopeUse(string Source, string ComponentId, string Name, string SourceRevision,
    string Decision, string? BoundaryId, string? BoundaryName, string Usage)
{
    public ScopeWordingBasis? WordingBasis { get; init; }
}
public sealed record SaveComponentScopeRequest(long ExpectedRevision, string Source, string ComponentId,
    string SourceRevision, string Decision, string? BoundaryId, string Usage)
{
    public Guid? WordingDraftId { get; init; }
    public long? WordingDraftRevision { get; init; }
}
public sealed record DesignActions(bool CanEdit, bool CanSubmit, bool CanWithdraw, bool CanReview, bool CanReconcile,
    bool CanDeriveDraft = false);
public sealed record DesignProposal
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "Add";
    public string RecordId { get; init; } = "";
    public string SourceFingerprint { get; init; } = "";
    public string State { get; init; } = "Pending";
    public DesignNode? OriginalNode { get; init; }
    public DesignEdge? OriginalEdge { get; init; }
    public DesignNode? ResultNode { get; init; }
    public DesignEdge? ResultEdge { get; init; }
    public string? Actor { get; init; }
    public DateTimeOffset? DecidedAt { get; init; }
    public string? Reason { get; init; }
    public bool ConflictsWithHigherPrecedence { get; init; }
}

public sealed record SystemDesignGraph
{
    public Guid TenantId { get; init; }
    public string SystemId { get; init; } = "";
    public string SystemName { get; init; } = "";
    public long Revision { get; init; }
    public long? ApprovedRevision { get; init; }
    public string GovernanceStatus { get; init; } = "NotStarted";
    public string SourceFingerprint { get; init; } = "";
    public bool SourcesStale { get; init; }
    public DateTimeOffset? SynchronizedAt { get; init; }
    public string? LastEditor { get; init; }
    public string? Reviewer { get; init; }
    public string? ReviewerComments { get; init; }
    public IReadOnlyList<DesignNode> Nodes { get; init; } = [];
    public IReadOnlyList<ComponentScopeUse> ComponentScopes { get; init; } = [];
    /// <summary>Authorized canonical records not currently present in the working graph; observed Azure candidates remain proposals.</summary>
    public IReadOnlyList<DesignNode> AvailableNodes { get; init; } = [];
    public IReadOnlyList<DesignEdge> Edges { get; init; } = [];
    public IReadOnlyList<DesignGroup> Groups { get; init; } = [];
    public IReadOnlyList<DesignGap> Gaps { get; init; } = [];
    public IReadOnlyList<DesignProposal> Proposals { get; init; } = [];
    public IReadOnlyList<string> SuppressedSourceIds { get; init; } = [];
    public bool HasAssemblyBaseline { get; init; }
    public IReadOnlyList<DesignContribution> Contributions { get; init; } = [];
    public IReadOnlyList<DesignChange> BaselineChanges { get; init; } = [];
    public DesignActions Actions { get; init; } = new(false, false, false, false, false);
    public int CompletenessPercentage { get; init; }
    public string SspReadiness { get; init; } = "Missing";
    public string DiscoveryState { get; init; } = "NotChecked";
    public string MonitoringState { get; init; } = "NotChecked";
}

public sealed record SaveSystemDesignRequest(long ExpectedRevision, IReadOnlyList<DesignNode> Nodes,
    IReadOnlyList<DesignEdge> Edges, IReadOnlyList<DesignGroup> Groups, string Reason);
public sealed record DesignRevisionRequest(long ExpectedRevision, string Reason);
public sealed record DesignReviewRequest(long ExpectedRevision, string Action, string Reason);
public sealed record DesignProposalDecisionRequest(long ExpectedRevision, string Action, string Reason,
    DesignNode? Node = null, DesignEdge? Edge = null);
public sealed record DesignHistoryEntry(long Revision, string Action, string Actor, DateTimeOffset At,
    string Reason, string GovernanceStatus, string SourceFingerprint);
public sealed record DesignPosition(double X, double Y);
public sealed record DesignViewport(double X, double Y, double Zoom);
public sealed record DesignLayout
{
    public long Version { get; init; }
    public string View { get; init; } = "Context";
    public Dictionary<string, DesignPosition> Positions { get; init; } = [];
    public IReadOnlyList<string> CollapsedGroups { get; init; } = [];
    public Dictionary<string, string> EdgeRouting { get; init; } = [];
    public Dictionary<string, bool> Visibility { get; init; } = [];
    public DesignViewport Viewport { get; init; } = new(0, 0, 1);
}
public sealed record SaveDesignLayoutRequest(long ExpectedVersion, DesignLayout Layout);

/// <summary>Read-only retained approval. Export workers must call the service within a reauthorized tenant/member scope.</summary>
public sealed record ApprovedSystemDesign(SystemDesignGraph Graph, long Revision, string ApprovedBy,
    DateTimeOffset ApprovedAt, string SnapshotHash, string SourceFingerprint, bool SourcesStale);
