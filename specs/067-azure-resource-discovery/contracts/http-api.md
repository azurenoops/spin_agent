# HTTP API Contracts — Spec 067: Azure Resource Discovery

**Epic:** #215 | **Owner:** Oracle  
**Note:** All endpoints are pre-existing. No new endpoints are introduced by this spec. This document describes the complete contract for frontend consumption.

---

## Azure Subscription Registration Endpoints

**Base URL:** `/api/onboarding/azure/subscriptions`  
**Auth:** `OnboardingAdministratorRequirement` (Admin role required)

---

### GET /api/onboarding/azure/subscriptions/registrations

List all registered Azure subscriptions for the current tenant.

**Request:** No body.

**Response 200 OK**
```json
{
  "ok": true,
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "subscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "displayName": "ACME DoD Production",
      "environment": "AzureCloud",
      "status": "Selected",
      "lastSeenVisibleAt": "2026-06-01T12:00:00Z"
    }
  ]
}
```

**Status values:** `"Selected"` (active, visible via ARM) | `"Unavailable"` (no longer visible via ARM)  
**Environment values:** `"AzureCloud"` | `"AzureUSGovernment"`

**Response 401:** Caller not authenticated  
**Response 403:** Caller lacks Admin role

---

### PUT /api/onboarding/azure/subscriptions/registrations

Upsert subscription registrations. Existing registrations are preserved; this adds new ones.

**Request Body**
```json
{
  "subscriptionIds": [
    "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy"
  ]
}
```

**Response 200 OK** — same shape as GET (returns full updated list)  
**Response 400** — validation error (empty list, invalid GUID format)  
**Response 401 / 403** — auth failure

---

### DELETE /api/onboarding/azure/subscriptions/registrations/{id}

Remove a specific subscription registration.

**Path param:** `id` — the registration's UUID (not the Azure subscription GUID)

**Response 204 No Content** — removed  
**Response 404** — not found or cross-tenant isolation (returns 404, not 403, to prevent enumeration)  
**Response 401 / 403** — auth failure

---

## Azure Resource Discovery Endpoints

**Base URL:** `/api/dashboard` (standard dashboard group)  
**Auth:** Authenticated user; role gating is application-layer

---

### GET /api/dashboard/systems/{systemId}/azure-discovery

Get discovered Azure resources for a system's registered subscription.

**Path param:** `systemId` — system UUID  
**Query params:** `resourceGroup?`, `resourceType?`, `search?`, `cursor?`

**Response 200 OK**
```json
{
  "resources": [
    {
      "resourceId": "/subscriptions/xxx/resourceGroups/rg1/providers/Microsoft.Compute/virtualMachines/vm1",
      "name": "vm1",
      "resourceType": "Microsoft.Compute/virtualMachines",
      "subscriptionId": "xxx",
      "resourceGroup": "rg1",
      "location": "usgovvirginia",
      "alreadyImported": false
    }
  ],
  "cursor": null,
  "totalCount": 12
}
```

**Response 400** `NO_SUBSCRIPTION` — system has no Azure subscription configured:
```json
{
  "error": "System has no Azure subscription configured",
  "errorCode": "NO_SUBSCRIPTION",
  "suggestion": "Register a system with a valid Azure subscription ID"
}
```

**Response 502** `AZURE_AUTH_FAILED` — Azure credentials not configured:
```json
{
  "error": "Azure credentials not configured. Run 'az login'...",
  "errorCode": "AZURE_AUTH_FAILED",
  "suggestion": "Run 'az login' on the Docker host..."
}
```

**Response 403** `AZURE_RBAC_DENIED` — insufficient subscription permissions:
```json
{
  "error": "Insufficient RBAC permissions. Reader role required.",
  "errorCode": "AZURE_RBAC_DENIED",
  "suggestion": "Assign the Reader role to the service principal on the subscription"
}
```

---

### POST /api/dashboard/systems/{systemId}/azure-discovery/apply

Apply discovered Azure resources to the system's authorization boundary.

**Request:** No body required.

**Response 200 OK**
```json
{
  "applied": 8,
  "skipped": 2,
  "boundaryDefinitionsCreated": 3
}
```

**Response 400 NO_SUBSCRIPTION** — same as GET above  
**Response 404** — system not found

---

### POST /api/dashboard/systems/{systemId}/components/discover-azure

Trigger Azure resource discovery for a specific system + subscription.

**Request Body**
```json
{
  "subscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

**Response 200 OK** — same `DiscoveryResponse` shape as `azure-discovery` GET above

**Error responses:** Same `AZURE_AUTH_FAILED`, `AZURE_RBAC_DENIED` patterns.

---

### POST /api/dashboard/systems/{systemId}/components/import-azure

Import selected discovered resources as system components.

**Request Body**
```json
{
  "subscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "resourceIds": [
    "/subscriptions/xxx/resourceGroups/rg1/providers/Microsoft.Compute/virtualMachines/vm1"
  ]
}
```

**Response 200 OK**
```json
{
  "imported": 5,
  "skipped": 1,
  "componentIds": ["uuid1", "uuid2", "..."]
}
```

---

### POST /api/dashboard/components/discover-entra

Discover Entra ID users and groups as candidate identity components.

**Note:** Only available when `EntraIdDiscoveryEnabled = true` in server config.

**Request:** No body.

**Response 200 OK**
```json
{
  "items": [
    {
      "entraObjectId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "displayName": "Jane Doe",
      "email": "jane.doe@agency.gov",
      "kind": "User",
      "department": "IT Security",
      "jobTitle": "ISSO",
      "alreadyImported": false
    }
  ],
  "partialFailure": false,
  "failureMessage": null
}
```

**Response 403 FEATURE_DISABLED** — Entra discovery not enabled:
```json
{
  "error": "Entra ID discovery is disabled",
  "errorCode": "FEATURE_DISABLED"
}
```

---

### POST /api/dashboard/components/import-entra

Import selected Entra identities as system components.

**Request Body**
```json
{
  "people": [
    { "entraObjectId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" }
  ]
}
```

**Response 200 OK**
```json
{
  "imported": 3,
  "skipped": 1
}
```

**Response 400** — `people` array is null or empty  
**Response 403 FEATURE_DISABLED** — Entra discovery not enabled

---

## Standard Error Envelope

All error responses follow the pattern:
```json
{
  "code": "ERROR_CODE",
  "message": "Human-readable description",
  "traceId": "..."
}
```

Or the flatter form used by some endpoints:
```json
{
  "error": "Human-readable description",
  "errorCode": "ERROR_CODE",
  "suggestion": "..."
}
```

Frontend should check `errorCode` (not HTTP status alone) to determine user-facing messaging.
