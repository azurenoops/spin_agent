# Quickstart — Feature 075: CSP AWS/GCP Integration

## Prerequisites

- AWS SDK: `dotnet add package AWSSDK.SecurityHub` + `AWSSDK.ConfigService` + `AWSSDK.SecurityToken`
- GCP SDK: `dotnet add package Google.Cloud.SecurityCenter.V1`
- Feature flags enabled in `appsettings.Development.json`:
  ```json
  "FeatureFlags": {
    "AwsScanning": true,
    "GcpScanning": true
  }
  ```

## Local Dev Setup

### AWS (using test account)
1. Create a test IAM role in your AWS account with `SecurityHub:GetFindings` + `config:DescribeComplianceByConfigRule` permissions
2. Set env var: `AWS_TEST_ROLE_ARN=arn:aws:iam::123456789:role/SpinTestRole`
3. Set env var: `AWS_TEST_EXTERNAL_ID=spin-dev-external-id`

### GCP (using test project)
1. Create a service account with `securitycenter.findings.list` permission in your test GCP project
2. Download the JSON key
3. Set env var: `GCP_TEST_SA_JSON=/path/to/sa-key.json`

## Running Unit Tests

```bash
dotnet test tests/Ato.Copilot.Tests.Unit \
  --filter "FullyQualifiedName~AwsCloudScanProvider|GcpCloudScanProvider|AwsNistControlMapper"
```

## Running Integration Tests

```bash
# Requires AWS + GCP env vars above
dotnet test tests/Ato.Copilot.Tests.Integration \
  --filter "FullyQualifiedName~CspIntegration"
```

## Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `AccessDeniedException: AssumeRole` | IAM role trust policy missing SPIN principal | Update trust policy |
| `PERMISSION_DENIED: request had insufficient authentication` | GCP SA missing securitycenter permissions | Add `roles/securitycenter.findingsViewer` |
| `AwsScanning feature is not enabled` | Feature flag off | Set `FeatureFlags:AwsScanning=true` in appsettings |
