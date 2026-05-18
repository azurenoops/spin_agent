# API Error Codes

> **Spec**: [`specs/048-tenant-isolation/spec.md`](../../specs/048-tenant-isolation/spec.md) ·
> **Architecture**: [`docs/architecture/tenant-isolation.md`](../architecture/tenant-isolation.md)

ATO Copilot returns a uniform error envelope across all HTTP endpoints:

```json
{
  "status": "error",
  "metadata": {
    "executionTimeMs": 12,
    "timestamp": "2026-02-21T15:32:11.118Z",
    "correlationId": "0HMV3..."
  },
  "error": {
    "code": "TENANT_NOT_PROVISIONED",
    "message": "Tenant 'd4ba4...' is not provisioned in this deployment."
  }
}
```

The `error.code` value is stable and machine-readable. Clients SHOULD branch on
`code`, never on `message` (which is human-readable and may change).

## Tenancy Error Codes (Feature 048)

| Code | HTTP | When | Action |
|---|---|---|---|
| `MISSING_TENANT_CLAIM` | 400 | Caller's bearer token has no `tid` claim and the deployment is in `MultiTenant` mode. | Re-authenticate via an Entra IDP that emits the `tid` claim. |
| `TENANT_NOT_PROVISIONED` | 403 | The `tid` claim points at an Entra tenant that has no `Tenant` row in this deployment. | Have a `CSP.Admin` provision the tenant via `POST /api/tenants` (US4) or via the onboarding wizard. |
| `TENANT_SUSPENDED` | 403 | Tenant exists and `Status = Suspended`. | Contact the CSP-Admin who suspended the tenant (the audit log records the actor + reason). |
| `TENANT_DISABLED` | 410 | Tenant exists and `Status = Disabled`. | Tenant is permanently retired. Restore from a backup or create a new tenant. |
| `TENANT_ONBOARDING_INCOMPLETE` | 503 | Tenant is `Active` but `OnboardingState != Active`. | Complete the per-tenant onboarding wizard (US4). |
| `FORBIDDEN_NOT_CSP_ADMIN` | 403 | Endpoint requires `CSP.Admin` role and the caller does not have it. Used by `/api/admin/migrate-to-multitenant`, `/api/global-baselines`, `/api/tenants`, `/api/audit/query`. | Authenticate as a `CSP.Admin`. |
| `CROSS_TENANT_REFERENCE_REJECTED` | 409 | A `SaveChanges` operation tried to insert/update a row whose foreign key points at a row owned by a different tenant. Stamped by `TenantStampingSaveChangesInterceptor`. | Inspect the request payload — the offending FK is named in `error.message`. Use the `[GlobalReference]` model only for genuinely shared entities. |
| `CSP_ONBOARDING_INCOMPLETE` | 503 | `MultiTenant` mode and no `CspProfile` row is `Active`. Returned for every tenant-scoped endpoint until the CSP wizard finishes. | Have the `CSP.Admin` complete `/onboarding/csp`. |
| `CSP_ALREADY_ONBOARDED` | 409 | `POST /api/csp/onboarding/submit` called after the `CspProfile` is already `Active`. | Use `PUT` semantics on the individual step endpoints instead. |
| `SINGLE_TENANT_MODE` | 404 | A `MultiTenant`-only endpoint (`/api/csp/*`, `/api/tenants/*` impersonation paths, `/api/admin/migrate-to-multitenant`) is called when `Deployment:Mode = SingleTenant`. | Switch to `MultiTenant` mode (see migration runbook) or remove the cross-tenant call. |

## Migration Error Codes

| Code | HTTP | When |
|---|---|---|
| `INVALID_REQUEST` | 400 | The migration request body is malformed (missing `defaultTenantId`, malformed CSV row, etc.). |
| `MIGRATION_FAILED` | 500 | The `MultiTenantMigrationService.ExecuteAsync` returned a non-empty `error` field. The full report (per-table row counts, RLS install status) is in the response body. |

## Generic Error Codes

| Code | HTTP | When |
|---|---|---|
| `INVALID_REQUEST` | 400 | Body validation failed. `error.details` contains the field-level errors. |
| `NOT_FOUND` | 404 | The addressed resource does not exist (or the caller lacks visibility into it under the active tenant filter). |
| `INTERNAL_ERROR` | 500 | Unhandled exception. The `correlationId` MUST be quoted when filing a bug. |

## Producing Error Responses (Server-Side)

All endpoints use the same helpers, defined per-endpoint group, e.g.
[`AdminMigrationEndpoints.Error`](../../src/Ato.Copilot.Mcp/Endpoints/AdminMigrationEndpoints.cs). The pattern is:

```csharp
return Results.Json(new
{
    status = "error",
    metadata = new { executionTimeMs = sw.ElapsedMilliseconds, timestamp = DateTimeOffset.UtcNow, correlationId },
    error = new { code = "TENANT_NOT_PROVISIONED", message = "..." }
}, statusCode: 403);
```

When adding a new code, **also add it to this table** and to the
`tests/Ato.Copilot.Tests.Integration/Tenancy/...ContractTests.cs` envelope-shape
assertions. CI will fail otherwise.
