namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Tracks dependencies between source onboarding artifacts (templates, imports, narrative
/// seeds) and dependent compliance entities (registered systems, exports, control
/// implementations) so that replacing a source can cascade-flag dependents as stale
/// (FR-094, research §R6).
/// </summary>
public class WizardArtifactDependency
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Discriminator for the source artifact's table.</summary>
    public ArtifactSourceKind SourceArtifactType { get; set; }

    /// <summary>Source artifact id (resolved against the table indicated by <see cref="SourceArtifactType"/>).</summary>
    public Guid SourceArtifactId { get; set; }

    /// <summary>
    /// Content checksum (or template <c>Version</c>) at the time of derivation. Used to
    /// determine whether the dependent is still in sync.
    /// </summary>
    public string SourceVersionTag { get; set; } = string.Empty;

    /// <summary>Discriminator for the dependent entity's table.</summary>
    public ArtifactDependentKind DependentType { get; set; }

    /// <summary>Dependent entity id.</summary>
    public Guid DependentId { get; set; }

    /// <summary>UTC timestamp when the dependent was first derived from the source.</summary>
    public DateTimeOffset DerivedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set to <c>true</c> when the source is replaced (FR-094 cascade).</summary>
    public bool IsStale { get; set; }

    /// <summary>UTC timestamp when <see cref="IsStale"/> flipped to <c>true</c>.</summary>
    public DateTimeOffset? StaleSince { get; set; }

    /// <summary>Plain-language summary of why the dependent is stale (Constitution VII).</summary>
    public string? StaleReason { get; set; }

    /// <summary>FK → most recent re-run <see cref="WizardJobStatus.Id"/>.</summary>
    public Guid? LastReRunJobId { get; set; }
}

/// <summary>Source artifact tables that participate in the cascade.</summary>
public enum ArtifactSourceKind
{
    Template,
    EmassImportSession,
    SspPdfImportSession,
    NarrativeSeedDocument,
}

/// <summary>Dependent entity tables that can be flagged stale.</summary>
public enum ArtifactDependentKind
{
    RegisteredSystem,
    ControlImplementation,
    PoamItem,
    SspExport,
    SarExport,
    SapExport,
    CrmExport,
    HwSwExport,
    NarrativeSuggestion,
}
