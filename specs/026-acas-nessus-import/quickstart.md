# Quickstart: ACAS/Nessus Scan Import

**Feature**: 026-acas-nessus-import | **Branch**: `026-acas-nessus-import`

---

## Prerequisites

- .NET 8 SDK
- Running Cosmos DB emulator or Azure Cosmos DB connection
- Existing registered system in Security Posture Intelligence Navigator (for import testing)
- Sample .nessus file (see Test Fixtures below)

## Build & Run

```bash
# From repo root
cd src/Ato.Copilot.Mcp
dotnet build
dotnet run
```

## Key Files to Implement

| File | Purpose |
|------|---------|
| `src/Ato.Copilot.Core/Models/Compliance/NessusModels.cs` | DTOs: ParsedNessusFile, NessusReportHost, NessusPluginResult, mapping DTOs |
| `src/Ato.Copilot.Core/Models/Compliance/ScanImportModels.cs` | Extend: ScanImportType.NessusXml, Nessus fields on ScanImportFinding/Record |
| `src/Ato.Copilot.Core/Interfaces/Compliance/IScanImportService.cs` | Extend: ImportNessusAsync method |
| `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/NessusParser.cs` | INessusParser + NessusParser: XDocument XML parsing |
| `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/NessusControlMapper.cs` | INessusControlMapper: STIG-ID xref + plugin-family heuristic |
| `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/PluginFamilyMappings.cs` | Static mapping table loader (reads embedded JSON) |
| `src/Ato.Copilot.Agents/Compliance/Resources/plugin-family-mappings.json` | 35-entry curated plugin-family → NIST control table |
| `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/ScanImportService.cs` | Extend: ImportNessusAsync orchestration |
| `src/Ato.Copilot.Agents/Compliance/Tools/ImportNessusTool.cs` | MCP tool: compliance_import_nessus |
| `src/Ato.Copilot.Agents/Compliance/Tools/ListNessusImportsTool.cs` | MCP tool: compliance_list_nessus_imports |
| `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` | Extend: DI registration for parser, mapper, tools |

## Implementation Order

1. **Models first**: `NessusModels.cs` DTOs → extend `ScanImportModels.cs` (enum + fields) → extend `IScanImportService.cs`
2. **Parser**: `NessusParser.cs` — implement `INessusParser.Parse(byte[])` using XDocument
3. **Mapping**: `plugin-family-mappings.json` → `PluginFamilyMappings.cs` → `NessusControlMapper.cs`
4. **Service**: `ScanImportService.ImportNessusAsync` — follow the 6-step CKL/XCCDF pattern
5. **Tools**: `ImportNessusTool.cs`, `ListNessusImportsTool.cs` — extend `BaseTool`
6. **DI**: Register all new types in `ServiceCollectionExtensions.cs`

## Existing Patterns to Follow

### Parser Pattern (CklParser)

```csharp
public interface INessusParser
{
    ParsedNessusFile Parse(byte[] fileContent);
}

public class NessusParser : INessusParser
{
    public ParsedNessusFile Parse(byte[] fileContent)
    {
        using var stream = new MemoryStream(fileContent);
        var doc = XDocument.Load(stream);
        var report = doc.Root?.Element("Report");
        // ... extract hosts and plugin results
    }
}
```

### BaseTool Pattern

```csharp
public class ImportNessusTool : BaseTool
{
    public override string Name => "compliance_import_nessus";
    public override string Description => "Import an ACAS/Nessus .nessus scan file";
    // Parameters, RequiredPimTier, AgentName, ExecuteAsync...
}
```

### DI Registration Pattern

```csharp
// In ServiceCollectionExtensions.cs, add to the scan import section:
services.AddSingleton<INessusParser, NessusParser>();
services.AddSingleton<INessusControlMapper, NessusControlMapper>();
services.RegisterTool<ImportNessusTool>();
services.RegisterTool<ListNessusImportsTool>();
```

## Running Tests

```bash
# Unit tests only
dotnet test tests/Ato.Copilot.Tests.Unit --filter "FullyQualifiedName~Nessus"

# Integration tests (requires Cosmos DB)
dotnet test tests/Ato.Copilot.Tests.Integration --filter "FullyQualifiedName~Nessus"

# All tests
dotnet test
```

## Test Fixtures

Create sample .nessus files in `tests/Ato.Copilot.Tests.Unit/Resources/`:

| File | Content |
|------|---------|
| `sample-single-host.nessus` | 1 host, 5 plugins (1 per severity 0-4), with CVEs and STIG-ID xrefs |
| `sample-multi-host.nessus` | 3 hosts, mixed severities, different plugin families |
| `sample-large.nessus` | 500+ plugins for performance testing |
| `sample-malformed.nessus` | Invalid XML for error handling tests |

## Key Design Decisions

- **Control mapping**: Layered strategy — STIG-ID xref (high confidence) → plugin-family heuristic (medium confidence)
- **Finding identity**: Plugin ID + Hostname + Port composite key for deduplication
- **Informational findings**: Severity 0 excluded from findings; summary counts only
- **POA&M threshold**: Critical + High + Medium (severity ≥ 2) create POA&M weaknesses with source "ACAS"
- **File size limit**: 5MB (consistent with CKL/XCCDF imports)

## Related Specs

- [spec.md](spec.md) — Feature specification
- [research.md](research.md) — .nessus format and mapping strategy research
- [data-model.md](data-model.md) — Entity extensions and DTOs
- [contracts/mcp-tools.md](contracts/mcp-tools.md) — MCP tool contracts
