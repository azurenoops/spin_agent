# Research: Evidence Repository

**Feature**: 038-evidence-repository | **Date**: 2026-03-18

## R1: File Storage Abstraction Pattern

**Decision**: Use an `IFileStorageProvider` interface with two implementations: `LocalFileStorageProvider` (default) and `AzureBlobStorageProvider` (optional).

**Rationale**: The spec requires user-selectable storage via dashboard settings. An abstracted interface allows adding providers without modifying consuming code. The project already uses DI extensively (70+ registered services).

**Alternatives considered**:
- **Direct Azure Blob Storage only**: Rejected — requires Azure Storage account for local development and testing.
- **EF Core binary column**: Rejected — storing files in SQL Server VARBINARY(MAX) is inefficient for files up to 25 MB and doesn't scale.
- **ASP.NET `IFileProvider`**: Rejected — read-only abstraction, doesn't support writing/deleting.

**Interface contract**:
```csharp
public interface IFileStorageProvider
{
    Task<string> SaveAsync(string path, Stream content, string contentType, CancellationToken ct);
    Task<Stream> GetAsync(string path, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
    Task<bool> ExistsAsync(string path, CancellationToken ct);
}
```

**Storage path convention**: `evidence/{systemId}/{artifactId}/{filename}` — partitioned by system for easy per-system cleanup.

---

## R2: Coexistence with Existing EvidenceStorageService

**Decision**: New `IEvidenceArtifactService` + `EvidenceArtifact` entity coexists alongside existing `IEvidenceStorageService` + `ComplianceEvidence` entity. No merge.

**Rationale**:
- `EvidenceStorageService` is automated — collects JSON text from Azure Policy/Defender, stores inline in `ComplianceEvidence.Content` (string field).
- `EvidenceArtifact` is user-uploaded — stores binary files via `IFileStorageProvider`, metadata in DB.
- Different lifecycles: automated evidence is collected during assessments; user evidence is uploaded ad-hoc.
- Merging would require migrating existing data and breaking the automated collection flow.

**Unified view strategy**: The Evidence Repository page queries both `db.Evidence` (ComplianceEvidence) and `db.EvidenceArtifacts` concurrently, merges results into a common DTO with a `Source` discriminator ("Automated" vs "Manual").

**Reuse**: `EvidenceStorageService.ComputeHash()` (static SHA-256) is reused for uploaded file integrity hashing.

---

## R3: File Upload Pattern (Precedent)

**Decision**: Follow the existing `POST /templates` multipart/form-data pattern from Feature 037 (SSP Export).

**Rationale**: An established, tested pattern exists in `DashboardEndpoints.cs` (line ~3814):
- `request.HasFormContentType` validation
- `request.ReadFormAsync(ct)` for parsing
- `form.Files.GetFile("file")` for file access
- `.DisableAntiforgery()` on upload endpoints
- Stream-based processing via `file.OpenReadStream()`

Also used in `ChatControllers.cs` for message attachments.

**Validation additions for evidence**:
1. Check file extension against allowlist
2. Check `file.ContentType` against content-type allowlist
3. Reject zero-byte files (`file.Length == 0`)
4. Reject files exceeding 25 MB (`file.Length > 25 * 1024 * 1024`)

---

## R4: Dashboard Settings Persistence

**Decision**: Extend existing `DashboardSettings` interface and `useSettings` hook with evidence storage fields. Settings persist in browser `localStorage` via `useLocalStorage` hook.

**Rationale**: The dashboard already has a mature settings system:
- `useSettings.ts`: React Context + `useLocalStorage` hook with `DashboardSettings` interface containing 30+ fields across 8 sections.
- `SettingsPanel.tsx`: Full settings UI with sections for Profile, Notifications, Dashboard, Chat, Export, Compliance, Integrations, Admin.
- All settings are client-side (no backend persistence needed for dashboard preferences).

**New settings fields**:
```typescript
// Evidence Storage
evidenceStorageProvider: 'local' | 'azure-blob';
azureBlobConnectionString: string;
azureBlobContainerName: string;
evidenceRetentionDays: number;  // default: 365
```

**Backend pass-through**: The storage provider selection and Azure credentials are passed to the backend via API headers or a dedicated configuration endpoint, since file storage is server-side.

---

## R5: Navigation Item Placement

**Decision**: Add `{ path: 'evidence', label: 'Evidence', d: '...' }` to the `navItems` array in `SystemLayout.tsx`, positioned after the "Remediation" entry.

**Rationale**: The spec requires "Evidence" nav item after Remediation. The `navItems` array in `SystemLayout.tsx` (line ~25) defines all system sidebar items in display order. Current order: Overview, Capability Coverage, Boundaries, Legal & Regulatory, Narratives, Assessments, Remediation, Gap Analysis, Deviations, Documents, Roadmap.

**Badge implementation**: The "Evidence" nav item will include a count badge. This follows the same pattern as notification badges already used in the dashboard. The count is fetched from a lightweight `GET /systems/{id}/evidence/count` endpoint.

---

## R6: Database Schema Strategy

**Decision**: Add `EvidenceArtifact` and `EvidenceVersion` entities to AtoCopilotContext with new DbSet properties. Use the existing EF Core migration approach.

**Rationale**: The project uses a mixed strategy — EF Core Migrations for SQL Server (production), `EnsureCreatedAsync` for development. The `AtoCopilotContext` already has 70+ DbSet properties. New entities follow the same pattern:
```csharp
public DbSet<EvidenceArtifact> EvidenceArtifacts => Set<EvidenceArtifact>();
public DbSet<EvidenceVersion> EvidenceVersions => Set<EvidenceVersion>();
```

The `OnModelCreating` method will configure indexes for efficient querying:
- `EvidenceArtifact`: Composite index on `(RegisteredSystemId, ControlImplementationId)`
- `EvidenceArtifact`: Index on `SecurityCapabilityId` (for capability-level evidence)
- `EvidenceVersion`: Index on `EvidenceArtifactId` (for version history lookup)

---

## R7: Inline Detail Panel Pattern

**Decision**: Use a slide-over panel (right-side drawer) for evidence detail, consistent with the existing `DeviationDetailDrawer.tsx` pattern.

**Rationale**: The dashboard already has `DeviationDetailDrawer.tsx` which implements a slide-over detail panel. The evidence detail panel follows the same UX pattern: clicking a row opens a right-side panel showing full metadata, file preview (for images/PDFs via `<img>` / `<iframe>`), and download button. User stays on the Evidence Repository page.

---

## R8: ControlImplementation and SecurityCapability FK Design

**Decision**: `EvidenceArtifact` has nullable FKs to both `ControlImplementation` and `SecurityCapability`. Exactly one must be set (enforced via application logic, not DB constraint).

**Rationale**:
- `ControlImplementation` has: `Id` (GUID), `RegisteredSystemId`, `ControlId`, `Narrative`, `SecurityCapabilityId`, plus governance fields.
- `SecurityCapability` has: `Id` (GUID), `Name`, `Provider`, `Category`, `Description`, `ImplementationStatus`, `Owner`, plus `ControlMappings` navigation.
- Evidence can be attached to either, but not both simultaneously. A check constraint would be fragile; application-level validation is simpler and already the project pattern.

**Capability-level evidence propagation**: When viewing a control narrative, query: (1) direct evidence on that `ControlImplementationId`, plus (2) evidence on any `SecurityCapabilityId` linked to that control via `CapabilityControlMapping`. The second query adds an "Inherited from [Capability Name]" label.
