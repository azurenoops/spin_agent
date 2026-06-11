# HTTP API Contract — Feature 075: CSP AWS/GCP Integration

## Endpoints

### POST /api/systems/{id}/cloud-credentials/aws
Register AWS credentials for a system.

**Authorization**: ISSM role required
**Request:**
```json
{
  "accountId": "123456789012",
  "iamRoleArn": "arn:aws:iam::123456789012:role/SpinReader",
  "externalId": "spin-tenant-uuid",
  "region": "us-east-1",
  "provider": "AwsCommercial"
}
```
**Response 201:**
```json
{
  "credentialId": "uuid",
  "provider": "AwsCommercial",
  "accountId": "123456789012",
  "createdAt": "2026-06-11T00:00:00Z"
}
```
**Errors:** 400 (validation), 409 (credential already exists for system)

---

### POST /api/systems/{id}/cloud-credentials/gcp
Register GCP credentials for a system.

**Authorization**: ISSM role required
**Request:**
```json
{
  "projectId": "my-dod-project",
  "serviceAccountEmail": "spin-reader@my-dod-project.iam.gserviceaccount.com",
  "serviceAccountKeyJson": "{ ... }"
}
```
**Response 201:**
```json
{
  "credentialId": "uuid",
  "provider": "Gcp",
  "projectId": "my-dod-project",
  "createdAt": "2026-06-11T00:00:00Z"
}
```
**Notes:** `serviceAccountKeyJson` is stored in Key Vault — never returned after creation.

---

### POST /api/systems/{id}/cloud-credentials/test
Test connectivity for registered credentials.

**Authorization**: ISSM role required
**Response 200:**
```json
{
  "success": true,
  "provider": "AwsCommercial",
  "accountId": "123456789012",
  "message": "Connection successful"
}
```

---

### GET /api/systems/{id}/compliance/findings?cloudProvider=AwsCommercial
Returns `ComplianceFinding` records for a specific CSP.
Existing endpoint extended with optional `cloudProvider` filter parameter.

---

## Extended compliance_assess Tool Parameters

```json
{
  "subscription_id": "string (Azure only — omit for AWS/GCP)",
  "cloud_provider": "Azure | AwsCommercial | AwsGovCloud | Gcp (optional, default: Azure)",
  "system_id": "string (required for AWS/GCP — identifies RegisteredSystem)",
  "scan_type": "quick | policy | full"
}
```
