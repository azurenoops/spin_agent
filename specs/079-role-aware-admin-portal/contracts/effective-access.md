# Effective Access HTTP Contract

## `GET /api/auth/effective-access`

Authenticated and non-cacheable.

```json
{
  "status": "success",
  "data": {
    "version": "1",
    "generatedAt": "2026-10-01T14:00:00Z",
    "subject": {
      "objectId": "00000000-0000-0000-0000-000000000000",
      "displayName": "Jordan Diaz",
      "tenantId": "00000000-0000-0000-0000-000000000001",
      "isCspAdmin": false
    },
    "defaultDestinationId": "admin:organization:<tenant-id>",
    "destinations": [
      {
        "id": "admin:organization:<tenant-id>",
        "workspace": "Administration",
        "scopeKind": "Organization",
        "scopeId": "<tenant-id>",
        "displayName": "Acme Federal",
        "actions": [
          "organization.profile.view",
          "organization.profile.edit"
        ],
        "badges": [
          {
            "label": "Administrator",
            "source": "OrganizationRoleAssignment"
          }
        ],
        "availability": "Available"
      }
    ]
  },
  "metadata": {
    "executionTimeMs": 12,
    "timestamp": "2026-10-01T14:00:00Z"
  }
}
```

The client may send a destination id only as a selection preference. Every
resource endpoint independently verifies subject, action, and resource scope.
