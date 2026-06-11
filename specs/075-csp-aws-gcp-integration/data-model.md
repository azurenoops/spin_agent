# Data Model — Feature 075: CSP AWS/GCP Integration

## New Enums

```csharp
// src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs
public enum CloudProvider
{
    Azure = 0,
    AwsCommercial = 1,
    AwsGovCloud = 2,
    Gcp = 3
}
```

## Modified Entities

### RegisteredSystem (extension — additive)
```csharp
// Add nullable CloudProvider — null means Azure (backwards compat)
public CloudProvider? CloudProvider { get; set; }

// Existing AzureEnvironmentProfile nullable — preserved
public AzureEnvironmentProfile? AzureProfile { get; set; }

// New: credential reference (Key Vault secret ID, not the credential itself)
public string? CloudCredentialSecretId { get; set; }
```

## New Entities

### AwsCredential
```csharp
public class AwsCredential
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid TenantId { get; set; }
    public string RegisteredSystemId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string IamRoleArn { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1"; // or us-gov-east-1
    public CloudProvider Provider { get; set; } = CloudProvider.AwsCommercial;
    // Key Vault secret ID — actual credentials never stored in DB
    public string KeyVaultSecretId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    
    public RegisteredSystem RegisteredSystem { get; set; } = null!;
}
```

### GcpCredential
```csharp
public class GcpCredential
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid TenantId { get; set; }
    public string RegisteredSystemId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ServiceAccountEmail { get; set; } = string.Empty;
    // Key Vault secret ID for service account JSON key
    public string KeyVaultSecretId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    
    public RegisteredSystem RegisteredSystem { get; set; } = null!;
}
```

## New Interface

```csharp
// src/Ato.Copilot.Core/Interfaces/Compliance/ICloudScanProvider.cs
public interface ICloudScanProvider
{
    CloudProvider Provider { get; }
    
    Task<IReadOnlyList<ComplianceFinding>> ScanAsync(
        CloudScanRequest request, CancellationToken ct);
    
    Task<ConnectionTestResult> TestConnectionAsync(
        string credentialSecretId, CancellationToken ct);
}

public record CloudScanRequest(
    string RegisteredSystemId,
    string CredentialSecretId,
    string? ControlFamily,
    CloudProvider Provider);

public record ConnectionTestResult(
    bool Success,
    string? ErrorMessage,
    string? AccountId);
```

## EF Core Migration Notes

- `RegisteredSystem`: additive columns `CloudProvider` (nullable int) and `CloudCredentialSecretId` (nullable string)
- New tables: `AwsCredentials`, `GcpCredentials` (both with TenantId for RLS)
- Existing `AzureEnvironmentProfile` table: untouched
- Seed data: `AwsNistControlMappings` and `GcpNistControlMappings` in a JSON seed file (not DB rows — loaded at runtime for mapping lookups)
