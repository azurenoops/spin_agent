# HTTP API Contract — 074: Policy + Technical Narrative Split

All endpoints follow the existing Security Posture Intelligence Navigator envelope pattern:

```json
{
  "status": "success" | "error",
  "data": <T>,
  "metadata": { "executionTimeMs": number, "timestamp": "ISO-8601", "tool": null },
  "error": { "errorCode": string, "message": string, "suggestion": string } | null
}
```

Authentication: Bearer JWT (MSAL). All endpoints require an authenticated caller.
Role constraints are noted per endpoint.

## Narrative Library continuation (#1001)

The library endpoints use their existing direct JSON DTOs (not the dual-endpoint
envelope illustrated below). Failures return `{ "errorCode": "...", "error": "..." }`.

- `GET /api/systems/{systemId}/narrative-library/access` adds `canGenerate`.
  `canAuthor` permits reference editing; it is **not** permission to generate a
  narrative. Workspace generation uses `CanAuthorNarratives` from the shared
  system access service. Review uses `CanReviewNarratives` (ISSM) plus a distinct
  authenticated Person from the author.
- `PATCH /api/systems/{systemId}/narrative-library/{id}` accepts
  `{ expectedRevision, scope, scopeId, passages }`. Each passage has nullable
  `controlId`, nullable `narrativeType`, and `content`. Draft mappings may remain
  incomplete. Publication still requires complete, known control/type mappings,
  reviewer acknowledgement, and the latest revision. Both original and new
  scope authority are checked. Published revisions cannot be edited.
- Proposal responses add `changeSourceKind`, `changeSourceId`, and
  `generationErrorCode`. States include `PendingGeneration`, `GenerationFailed`,
  `Superseded`, `Draft`, `Approved`, and `NeedsRevision`. Only a current `Draft`
  can be accepted. Pending/failed/superseded work must not render an empty
  proposal as a successful generated narrative.
- Upload/revision/proposal races return `409 CONCURRENCY_CONFLICT`; model
  unavailable/invalid responses retain their `503`/`502` behavior.
- `GET /api/systems/{systemId}/narrative-library/proposals/{id}/impact-receipts`
  accepts `page` (default 1) and `pageSize` (default 50, maximum 100). It returns
  `{ items, totalCount, page, pageSize }`, authorized against both the system and
  proposal owner. Each item includes `id`, opaque `impactId`, UTC `recordedAt`,
  nullable `sourceKind`/`sourceId`/`sourceActor`, and typed `sourceContext`.
  Unrecorded historical fields remain null.

UI provenance labels must distinguish **proposal creation trigger**
(`provenance.changeOrigin`) from **source delivery history** (impact receipts).
Several offline events can share one current-state proposal. Do not label the
creation origin as the latest provider change, or receipt recording time as the
original provider event time. Show the captured proposal state and immutable
source contexts without rewriting either history.

Internal integration (not a public HTTP endpoint):

`INarrativeChangeImpactService.QueueAsync(NarrativeChangeImpactRequest, ct)` takes
the exact tenant, real system ID, affected control IDs, narrative types, source
kind/ID and authenticated event actor, plus optional deterministic `ImpactId`
(at most 128 characters) for durable outbox delivery. It creates semantic-state-deduplicated work
without invoking the model. Dispatch returned IDs after the source transaction
commits through `GenerateQueuedAsync(id, ct)` in the same affected tenant.
Generation failure is persisted and rethrown; callers must log and retry it.
The service never accepts proposals on behalf of the dispatcher.
Safe worker failure codes distinguish `AI_NOT_AVAILABLE`,
`GENERATION_INPUT_INVALID`, `GENERATION_TIMEOUT`, `GENERATION_CANCELLED`, and
`GENERATION_FAILED`. Cooperative caller cancellation leaves durable pending work;
it does not clear that work or create an empty successful draft. Source delivery
acknowledgement follows durable queuing, not model completion.

The queue joins an existing transaction only when the producer shares the exact
scoped context and customer tenant. Provider fan-out must first persist a
producer-owned outbox in the provider mutation transaction. A tenant-bound
dispatcher then supplies `ImpactId` and calls the queue. Immutable
`NarrativeImpactReceipts` record each tenant/system/control/type delivery,
including unchanged results, so replay cannot reopen rejected/reviewed work.
Concurrent delivery-key collisions fail explicitly and can be retried safely.
Detailed baseline/subscription/provider-revision event metadata remains owned
by the #957 outbox. The finalized optional `SourceContext` parameter accepts
`NarrativeChangeSourceContext(SourceRevision, Cause, BaselineId, SubscriptionId,
CspProfileId?, CspInheritedComponentId?, CspCapabilityId?,
PreviousInheritanceType?, CurrentInheritanceType?)`.
Causes are `ProviderChanged`, `SubscriptionRemoved`, and `ResponsibilityChanged`.
The producer supplies real identifiers and previously captured values; none grant
allocation authority. Revision is bounded to 128 characters, baseline and
subscription IDs to 36. Nullable allocation values remain unknown.

The context is copied to immutable receipt metadata and a newly queued proposal's
`provenance.changeOrigin`. It survives link removal and is excluded from semantic
freshness hashing. Reusing an ImpactId with different source context returns a
concurrency conflict. Organization capability sources cannot claim CSP publication
identifiers.

Parent integration must register the interface to the existing scoped
`NarrativeProposalService`, retain the `NarrativeLibrarySchemaAdditions` startup
call, and wire committed source events plus a durable dispatcher. The interface
alone does not establish automatic delivery.

### Standalone libraries

| Workspace | Route root | Permitted scopes | Server authorization |
|---|---|---|---|
| Organization | `/api/narrative-library` | `Organization`, `Capability` (organization-owned SecurityCapability) | Active tenant ISSM/Administrator publication authority; fresh membership for workspace requests |
| Provider | `/api/csp/narrative-library` | `Provider`, `ProviderCapability` (CspInheritedCapability) | Authorized provider context only; organization and support impersonation contexts denied |

Both roots expose `GET /`, `GET /access`, `GET /{id}`, multipart
`POST /imports`, `PATCH /{id}` and `POST /{id}/publish`, using the same reference,
mapping and publication shapes as the system library. Organization imports have
no system origin. Provider imports use separate provider-owned storage and the
real profile/capability IDs returned by `/access`.

Organization access returns `{ tenantId, canPublishShared, capabilities }`.
Provider access returns `{ cspProfileId, displayName, canPublish, capabilities }`.
These are not system-access DTOs and must not be forced into a `systemName` or
fabricated system context in the UI.

`GET /api/systems/{systemId}/narrative-library/provider-references?controlId=AC-2`
returns published provider passages applicable to an authorized system/control.
An active subscription, mapped capability and published provider component are
all required. Select the latest published reference revision **before** filtering
passages; a newer revision removing a control cannot reactivate older text.
Grounding uses this same applicability path. Provider reference text remains
unverified input, never implementation evidence.

Publication atomically inserts `NarrativeReferencePublications` (tenant-owned)
or `ProviderNarrativeReferencePublications` (provider-owned). These source outboxes
contain immutable revision payloads and the union of old/new control-half mappings.
They make no model calls and contain no provider-side customer enumeration.
Parent dispatch must group targets by narrative type (not create a Cartesian
product), use realm-prefixed event IDs as `ImpactId`, and acknowledge source
delivery only after durable target queuing. An undelivered event remains Pending.

Required host wiring (model and service registration have now been observed in
the parent's context and agent service extensions; standalone route mapping
remains to be added):

```csharp
// AtoCopilotContext: model registration; no query-filter bypass.
public DbSet<ProviderNarrativeReference> ProviderNarrativeReferences
    => Set<ProviderNarrativeReference>();

// Existing host DI and endpoint registration.
builder.Services.AddScoped<ProviderNarrativeLibraryService>();
app.MapScopedNarrativeLibraryEndpoints();
```

Publication outbox models are discovered through their reference navigations.
The schema-additions module creates them and upgrades nullable organization
origins. Tests register the provider entity in a derived context; production
context/model registration and the provider/impact-service registrations were
subsequently verified in the shared branch. Standalone host route mapping and
publication dispatch remain parent-owned; dispatch has not been verified here.
SQL Server/RLS and interactive acceptance remain unverified release gates.

---

## 1. `GET /api/systems/{systemId}/controls/{controlId}/narrative`

Returns both narrative halves plus evidence split by narrative type.

### Authorization
Any authenticated tenant user. No role restriction.

### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `systemId` | string | Yes | System GUID, name, or acronym |
| `controlId` | string | Yes | NIST 800-53 control ID (e.g., `AC-2`, `AC-2(1)`) |

### Response `200 OK`

```json
{
  "status": "success",
  "data": {
    "systemId": "aaaaaaaa-0000-0000-0000-000000000001",
    "controlId": "AC-2",
    "policyNarrative": "The Account Management Policy (AMP-001) governs lifecycle for all users. Reviews occur annually.",
    "technicalNarrative": "Azure AD Conditional Access policy 'Require MFA' enforces account management controls.",
    "legacyNarrative": "We have an account management policy and Azure AD enforces lifecycle workflows.",
    "migratedFromLegacy": true,
    "policyEvidence": [
      {
        "id": "bbbbbbbb-0000-0000-0000-000000000001",
        "fileName": "Account_Management_Policy_v3.0.pdf",
        "contentType": "application/pdf",
        "fileSizeBytes": 204800,
        "narrativeType": 0,
        "autoTagRationale": "Filename matches PolicyDocumentRegex",
        "manuallyTaggedBy": null,
        "uploadedAt": "2026-03-15T10:00:00Z"
      }
    ],
    "technicalEvidence": [
      {
        "id": "cccccccc-0000-0000-0000-000000000001",
        "fileName": "azure_policy_compliance_ac2.json",
        "contentType": "application/json",
        "fileSizeBytes": 8192,
        "narrativeType": 1,
        "autoTagRationale": "Source=AzurePolicy",
        "manuallyTaggedBy": null,
        "uploadedAt": "2026-05-01T08:00:00Z"
      }
    ],
    "unclassifiedEvidence": [],
    "isPolicyStale": false,
    "isTechnicalStale": false,
    "policyStaleReason": null,
    "technicalStaleReason": null
  },
  "metadata": { "executionTimeMs": 18, "timestamp": "2026-06-11T00:00:00Z", "tool": null }
}
```

### Response `404 Not Found`

```json
{
  "status": "error",
  "data": null,
  "metadata": { "executionTimeMs": 5, "timestamp": "...", "tool": null },
  "error": {
    "errorCode": "NOT_FOUND",
    "message": "No ControlImplementation found for system 'aaaa...' control 'AC-2'.",
    "suggestion": "Verify the systemId and controlId are correct."
  }
}
```

---

## 2. `PATCH /api/systems/{systemId}/controls/{controlId}/narrative`

Partially updates one or both narrative halves. Omitted fields are left unchanged.

### Authorization

| Caller Role | `policyNarrative` | `technicalNarrative` |
|-------------|-------------------|----------------------|
| `Analyst` (ISSO/ISSM) | ✅ allowed | ✅ allowed |
| `SecurityLead` (ISSM) | ✅ allowed | ✅ allowed |
| `PlatformEngineer` | ❌ 403 | ✅ allowed |
| `Sca` | ❌ 403 | ✅ allowed |
| `AuditReader` | ❌ 403 | ❌ 403 |

### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `systemId` | string | Yes | System GUID, name, or acronym |
| `controlId` | string | Yes | NIST 800-53 control ID |

### Request Body

```json
{
  "policyNarrative": "The Account Management Policy (AMP-001) governs...",
  "technicalNarrative": "Azure AD Conditional Access enforces..."
}
```

Both fields are optional. Send only the field(s) you want to update.

### Response `200 OK`

Returns the same `DualNarrativeResponse` shape as GET (section 1 above), reflecting
the updated values.

### Response `403 Forbidden`

```json
{
  "status": "error",
  "data": null,
  "metadata": { "executionTimeMs": 3, "timestamp": "...", "tool": null },
  "error": {
    "errorCode": "FORBIDDEN",
    "message": "Role 'PlatformEngineer' is not permitted to write policyNarrative.",
    "suggestion": "Ask your ISSM (Analyst role) to author the Policy narrative."
  }
}
```

### Response `400 Bad Request`

```json
{
  "status": "error",
  "data": null,
  "metadata": { "executionTimeMs": 2, "timestamp": "...", "tool": null },
  "error": {
    "errorCode": "VALIDATION_ERROR",
    "message": "policyNarrative exceeds maximum length of 8000 characters.",
    "suggestion": "Shorten the narrative to 8000 characters or fewer."
  }
}
```

---

## 3. `PATCH /api/evidence/{artifactId}/classify`

Re-classifies an existing evidence artifact's narrative type.

### Authorization
Any authenticated tenant user with access to the system that owns the artifact.
Manual re-tag always allowed (overrides auto-tag). Sets `manuallyTaggedBy = caller OID`,
clears `autoTagRationale`.

### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `artifactId` | string | Yes | Evidence artifact GUID |

### Request Body

```json
{
  "narrativeType": 0,
  "rationale": "This is the signed Account Management Policy PDF."
}
```

| Field | Type | Required | Values |
|-------|------|----------|--------|
| `narrativeType` | integer | Yes | `0`=Policy, `1`=Technical, `2`=Combined, `3`=Unclassified |
| `rationale` | string | No | Optional human-readable note stored in `AutoTagRationale` (cleared on next classifier run) |

### Response `200 OK`

```json
{
  "status": "success",
  "data": {
    "id": "cccccccc-0000-0000-0000-000000000001",
    "fileName": "azure_policy_compliance_ac2.json",
    "narrativeType": 0,
    "autoTagRationale": "This is the signed Account Management Policy PDF.",
    "manuallyTaggedBy": "john.doe@contoso.com"
  },
  "metadata": { "executionTimeMs": 6, "timestamp": "...", "tool": null }
}
```

### Response `404 Not Found`
Returns `NOT_FOUND` error if the artifact does not exist in the caller's tenant.

---

## 4. Error Codes

| `errorCode` | HTTP Status | Description |
|-------------|-------------|-------------|
| `NOT_FOUND` | 404 | `ControlImplementation` or `EvidenceArtifact` does not exist in tenant |
| `FORBIDDEN` | 403 | Caller role not permitted to write the requested field |
| `VALIDATION_ERROR` | 400 | Input fails length or enum validation |
| `INTERNAL_ERROR` | 500 | Unexpected server error |
