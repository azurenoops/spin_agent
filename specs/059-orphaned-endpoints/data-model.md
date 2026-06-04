# Data Model — 059 Orphaned Endpoints

## No New Database Entities

This epic introduces **no new backend entities, migrations, or schema changes**.
All persistence objects already exist from Feature 048.

## Existing entities referenced

### `AuditLogEntry` (table: `AuditLogs`)
Defined in `Ato.Copilot.Core.Models.Compliance.AuditLogEntry`.

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK |
| `TenantId` | `Guid` | FK → Tenant |
| `ActorTenantId` | `Guid` | Tenant the actor belongs to (differs from TenantId during impersonation) |
| `UserId` | `string` | Actor OID |
| `Action` | `string` | e.g. `"Capability.Create"`, `"Capability.Move"` |
| `EntityType` | `string` | e.g. `"CspInheritedCapability"` |
| `EntityId` | `string` | |
| `Timestamp` | `DateTime` | UTC; indexed via `IX_AuditLogs_TenantId_Timestamp` |
| `MetadataJson` | `string?` | Arbitrary JSON blob |

### `NotificationPreference` (existing service model)
Managed by `NotificationEndpoints` / `PUT /api/notifications/preferences`.
Shape returned by `GET /api/notifications/preferences`:
```json
{
  "preferences": {
    "capabilityReview": true,
    "systemAlerts": true,
    "weeklyDigest": false
  }
}
```

### `DeploymentOptions` (config, not DB)
`Ato.Copilot.Mcp.Configuration.DeploymentOptions`:
- `Mode`: `SingleTenant | MultiTenant`
- `DefaultTenantId`: `Guid?` (SingleTenant only)

## Frontend-only state

All UI state for filters, pagination cursor, and session-level "migration
already executed" flag is stored in URL query params and `sessionStorage`
respectively — no new backend persistence.
