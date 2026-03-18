# Data Model: Org-Wide Risk Solutions & Context-Aware Narrative Generation

**Feature**: 036-risk-solutions | **Date**: 2026-03-17

## Entity Changes Overview

| Entity | Action | Summary |
|--------|--------|---------|
| SystemComponent | MODIFY | `RegisteredSystemId` becomes nullable; direct boundary FK retained for backward compat |
| ComponentSystemAssignment | NEW | Join entity linking org-wide components to systems with boundary scope |
| BoundaryMappingContext | MODIFY | Add optional `Components` collection for narrative enrichment |
| ComponentContext | NEW | Record carrying component metadata for narrative templates |
| NarrativeTemplateService | MODIFY | Constructor accepts optional `IChatClient?` and `AzureAiOptions?` for AI-assisted generation; new `GenerateNarrativeWithAiAsync()` method; new prompt file |
| All others | UNCHANGED | SecurityCapability, CapabilityControlMapping, ControlImplementation, NarrativeVersion, AuthorizationBoundaryDefinition, ComponentCapabilityLink |

---

## Modified Entities

### SystemComponent (MODIFIED)

**File**: `src/Ato.Copilot.Core/Models/Compliance/SystemComponent.cs`

**Changes**:
- `RegisteredSystemId`: `[Required]` removed, type stays `string?` (nullable)
- `RegisteredSystem` navigation: stays but becomes nullable
- New navigation: `SystemAssignments` collection for org-wide assignment tracking

```csharp
public class SystemComponent
{
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // CHANGED: Was [Required]. Now nullable for org-wide components.
    [MaxLength(36)]
    public string? RegisteredSystemId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public ComponentType ComponentType { get; set; }

    [MaxLength(100)]
    public string? SubType { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(200)]
    public string? Owner { get; set; }

    [Required]
    public ComponentStatus Status { get; set; } = ComponentStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    // CHANGED: Now nullable (org-wide components have no direct system link)
    public RegisteredSystem? RegisteredSystem { get; set; }

    public ICollection<ComponentCapabilityLink> CapabilityLinks { get; set; } = new List<ComponentCapabilityLink>();

    // NEW: Org-wide system assignments
    public ICollection<ComponentSystemAssignment> SystemAssignments { get; set; } = new List<ComponentSystemAssignment>();

    // ─── Feature 033: Boundary-Scoped Model ──────────────────────────────────

    [MaxLength(36)]
    public string? AuthorizationBoundaryDefinitionId { get; set; }

    public AuthorizationBoundaryDefinition? AuthorizationBoundaryDefinition { get; set; }
}
```

**EF Core Config Changes**:

```csharp
modelBuilder.Entity<SystemComponent>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasMaxLength(36);
    // CHANGED: No longer .IsRequired()
    entity.Property(e => e.RegisteredSystemId).HasMaxLength(36);
    entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
    entity.Property(e => e.ComponentType).HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.SubType).HasMaxLength(100);
    entity.Property(e => e.Description).HasMaxLength(2000);
    entity.Property(e => e.Owner).HasMaxLength(200);
    entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

    // CHANGED: Index updated — RegisteredSystemId can be null now
    entity.HasIndex(e => new { e.RegisteredSystemId, e.ComponentType })
        .HasDatabaseName("IX_SystemComponent_System_Type");
    entity.HasIndex(e => e.Status).HasDatabaseName("IX_SystemComponent_Status");

    // CHANGED: .OnDelete(SetNull) instead of Cascade; relationship optional
    entity.HasOne(e => e.RegisteredSystem)
        .WithMany()
        .HasForeignKey(e => e.RegisteredSystemId)
        .OnDelete(DeleteBehavior.SetNull);
});
```

**Migration SQL** (added to `EnsureSchemaAdditionsAsync`):

```sql
-- Make RegisteredSystemId nullable (org-wide components)
ALTER TABLE SystemComponents ALTER COLUMN RegisteredSystemId NVARCHAR(36) NULL;
```

---

### BoundaryMappingContext (MODIFIED)

**File**: `src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs`

**Changes**: Add optional `Components` parameter for narrative enrichment.

```csharp
/// <summary>
/// Context for a single capability-to-control mapping within a boundary (or org-wide).
/// </summary>
public record BoundaryMappingContext(
    string CapabilityName,
    string Provider,
    string Description,
    string? BoundaryName,
    IReadOnlyList<ComponentContext>? Components = null);
```

---

## New Entities

### ComponentSystemAssignment (NEW)

**File**: `src/Ato.Copilot.Core/Models/Compliance/ComponentSystemAssignment.cs`

**Purpose**: Links an org-wide `SystemComponent` to a specific `RegisteredSystem` with an explicit boundary scope. Enables one component to serve multiple systems within specified boundaries.

```csharp
using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Links an org-wide <see cref="SystemComponent"/> to a <see cref="RegisteredSystem"/>
/// with an explicit boundary scope.
/// </summary>
public class ComponentSystemAssignment
{
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the org-wide component.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemComponentId { get; set; } = string.Empty;

    /// <summary>FK to the assigned system.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>FK to the boundary within the system (nullable — null means system-wide / Primary).</summary>
    [MaxLength(36)]
    public string? AuthorizationBoundaryDefinitionId { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who created the assignment.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    // ─── Navigation ──────────────────────────────────────────────────────────

    public SystemComponent SystemComponent { get; set; } = null!;

    public RegisteredSystem RegisteredSystem { get; set; } = null!;

    public AuthorizationBoundaryDefinition? AuthorizationBoundaryDefinition { get; set; }
}
```

**EF Core Config**:

```csharp
modelBuilder.Entity<ComponentSystemAssignment>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasMaxLength(36);
    entity.Property(e => e.SystemComponentId).HasMaxLength(36).IsRequired();
    entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
    entity.Property(e => e.AuthorizationBoundaryDefinitionId).HasMaxLength(36);
    entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

    // Unique: one component can only be assigned to a system+boundary once
    entity.HasIndex(e => new { e.SystemComponentId, e.RegisteredSystemId, e.AuthorizationBoundaryDefinitionId })
        .IsUnique()
        .HasDatabaseName("IX_ComponentSystemAssignment_Unique");

    entity.HasIndex(e => e.RegisteredSystemId)
        .HasDatabaseName("IX_ComponentSystemAssignment_System");

    entity.HasOne(e => e.SystemComponent)
        .WithMany(c => c.SystemAssignments)
        .HasForeignKey(e => e.SystemComponentId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.RegisteredSystem)
        .WithMany()
        .HasForeignKey(e => e.RegisteredSystemId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.AuthorizationBoundaryDefinition)
        .WithMany()
        .HasForeignKey(e => e.AuthorizationBoundaryDefinitionId)
        .OnDelete(DeleteBehavior.Restrict);
});
```

---

### ComponentContext (NEW)

**File**: `src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs`

**Purpose**: Lightweight record carrying component metadata for narrative template generation.

```csharp
/// <summary>
/// Component metadata for narrative template enrichment.
/// </summary>
public record ComponentContext(
    string Name,
    string ComponentType,
    string? Owner);
```

---

## NarrativeTemplateService Constructor Change (MODIFIED)

**File**: `src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs`

**Current**: Stateless class with no constructor dependencies.

**New**: Accepts optional AI dependencies via constructor injection:

```csharp
public class NarrativeTemplateService
{
    private readonly IChatClient? _chatClient;
    private readonly AzureAiOptions? _aiOptions;
    private readonly ILogger<NarrativeTemplateService>? _logger;
    private readonly string? _systemPrompt;

    /// <summary>Default constructor — deterministic-only mode.</summary>
    public NarrativeTemplateService() { }

    /// <summary>AI-enabled constructor — uses IChatClient when available.</summary>
    public NarrativeTemplateService(
        IChatClient? chatClient,
        AzureAiOptions? aiOptions,
        ILogger<NarrativeTemplateService>? logger)
    {
        _chatClient = chatClient;
        _aiOptions = aiOptions;
        _logger = logger;
        _systemPrompt = LoadPromptResource("NarrativeGeneration.prompt.txt");
    }

    // Existing deterministic methods unchanged...

    /// <summary>
    /// AI-assisted narrative generation. Returns null if AI is disabled or fails.
    /// </summary>
    public async Task<string?> GenerateNarrativeWithAiAsync(
        string capabilityName,
        string provider,
        string description,
        string controlId,
        string controlTitle,
        IReadOnlyList<ComponentContext>? components,
        string? boundaryName,
        CancellationToken cancellationToken = default);
}
```

**Prompt file**: `src/Ato.Copilot.Core/Prompts/NarrativeGeneration.prompt.txt` (NEW)

**DI Registration**: Register as singleton in `Program.cs` (deterministic is thread-safe; AI client is injected).

---

## Relationships Diagram

```text
┌─────────────────────────┐
│   SecurityCapability    │
│   (org-wide)            │
└──────┬──────────┬───────┘
       │          │
    1:N│       M:N│ ComponentCapabilityLink
       │          │
       ▼          ▼
┌──────────────┐  ┌──────────────────┐
│ Capability   │  │ SystemComponent  │
│ ControlMap   │  │ (org-wide)       │
│              │  │ RegisteredSysId? │
└──────┬───────┘  └──────┬───────────┘
       │                 │
    1:N│              1:N│ ComponentSystemAssignment
       │                 │
       ▼                 ▼
┌──────────────┐  ┌─────────────────────────────┐
│ Control      │  │ ComponentSystemAssignment    │
│ Implementa-  │  │ ├── SystemComponentId (FK)   │
│ tion         │  │ ├── RegisteredSystemId (FK)  │
│ (per-system) │  │ └── BoundaryDefId? (FK)      │
└──────┬───────┘  └──────────┬──────────────────┘
       │                     │
    1:N│                     │ FK
       │                     ▼
       ▼           ┌─────────────────────────────┐
┌──────────────┐   │ AuthorizationBoundary        │
│ Narrative    │   │ Definition                   │
│ Version      │   │ (system-scoped)              │
└──────────────┘   └─────────────────────────────┘
```

## Narrative Generation Data Flow

When generating enriched narratives, the system queries:

```text
1. CapabilityControlMapping (capability → control, with role & boundary scope)
2. ComponentCapabilityLink (capability → components)
3. ComponentSystemAssignment (component → system + boundary)
4. AuthorizationBoundaryDefinition (boundary name/type)

Assembly:
  For each capability mapped to a control:
    → Get all linked components via ComponentCapabilityLink
    → For each component, get boundary context via ComponentSystemAssignment
    → Build ComponentContext records (Name, Type, Owner)
    → Pass as BoundaryMappingContext.Components to template engine
```

## Startup Migration Logic

Added to `EnsureSchemaAdditionsAsync` in `Program.cs`:

```text
1. ALTER TABLE SystemComponents ALTER COLUMN RegisteredSystemId NVARCHAR(36) NULL
2. CREATE TABLE ComponentSystemAssignments (if not exists)
3. For each SystemComponent WHERE RegisteredSystemId IS NOT NULL:
   a. INSERT INTO ComponentSystemAssignments (ComponentId, SystemId, BoundaryDefId)
      VALUES (component.Id, component.RegisteredSystemId, component.AuthorizationBoundaryDefinitionId)
   b. SET component.RegisteredSystemId = NULL
   c. SET component.AuthorizationBoundaryDefinitionId = NULL
```

## Validation Rules

| Entity | Rule | Source |
|--------|------|--------|
| ComponentSystemAssignment | Unique (ComponentId, SystemId, BoundaryDefId) | FR-005 |
| ComponentSystemAssignment | SystemId must reference a valid RegisteredSystem | FK constraint |
| ComponentSystemAssignment | BoundaryDefId must reference a boundary belonging to the same SystemId | Application-level validation |
| SystemComponent | Name required, max 200 chars | Existing |
| SystemComponent | ComponentType required (Person/Place/Thing) | Existing |
| BoundaryMappingContext.Components | Null = no enrichment (fallback to current template) | FR-002 graceful degradation |
