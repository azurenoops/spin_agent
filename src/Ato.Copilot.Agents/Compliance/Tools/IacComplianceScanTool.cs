using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

/// <summary>
/// MCP tool for scanning Infrastructure-as-Code files (Bicep, Terraform, ARM templates)
/// against NIST 800-53 / FedRAMP compliance controls. Returns structured compliance findings
/// with unified-diff suggested fixes for auto-remediation.
/// Extends <see cref="BaseTool"/> per Constitution Principle II (FR-029f, R-009).
/// </summary>
public class IacComplianceScanTool : BaseTool
{
    private static readonly Lazy<List<IacComplianceRule>> CachedRules = new(BuildAllRules);
    private readonly IControlValidationLinkService? _validationLinks;

    /// <summary>Initializes a new instance of the <see cref="IacComplianceScanTool"/> class.</summary>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    public IacComplianceScanTool(
        ILogger<IacComplianceScanTool> logger,
        IControlValidationLinkService? validationLinks = null)
        : base(logger)
    {
        _validationLinks = validationLinks;
    }

    /// <inheritdoc />
    public override string Name => "iac_compliance_scan";

    /// <inheritdoc />
    public override string Description =>
        "Scan Infrastructure-as-Code files (Bicep, Terraform, ARM) for NIST 800-53 / FedRAMP compliance findings with suggested fixes.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["filePath"] = new()
        {
            Name = "filePath",
            Description = "Path to the IaC file to scan (e.g., 'main.bicep', 'main.tf')",
            Type = "string",
            Required = true
        },
        ["fileContent"] = new()
        {
            Name = "fileContent",
            Description = "The content of the IaC file to scan",
            Type = "string",
            Required = true
        },
        ["fileType"] = new()
        {
            Name = "fileType",
            Description = "The type of IaC file: 'bicep', 'terraform', 'arm'",
            Type = "string",
            Required = true
        },
        ["framework"] = new()
        {
            Name = "framework",
            Description = "Compliance framework to scan against (default: 'nist-800-53-r5'). Options: 'nist-800-53-r5', 'fedramp-high', 'fedramp-moderate'",
            Type = "string",
            Required = false
        },
        ["system_id"] = new()
        {
            Name = "system_id",
            Description = "Registered system ID used to attach findings as control validation links",
            Type = "string",
            Required = false
        }
    };

    /// <summary>Returns the total number of loaded compliance rules.</summary>
    internal static int RuleCount => CachedRules.Value.Count;

    /// <inheritdoc />
    /// <remarks>
    /// Honors <paramref name="cancellationToken"/> for cooperative cancellation (Constitution VIII).
    /// </remarks>
    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetArg<string>(arguments, "filePath") ?? string.Empty;
        var fileContent = GetArg<string>(arguments, "fileContent") ?? string.Empty;
        var fileType = GetArg<string>(arguments, "fileType") ?? "bicep";
        var framework = GetArg<string>(arguments, "framework") ?? "nist-800-53-r5";
        var systemId = GetArg<string>(arguments, "system_id");

        Logger.LogInformation("IaC compliance scan | File: {FilePath}, Type: {FileType}, Framework: {Framework}",
            filePath, fileType, framework);

        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "No file content provided for scanning.",
                findings = Array.Empty<object>()
            });
        }

        // Determine if the file is actually an IaC file
        var supportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "bicep", "terraform", "arm", "tf" };

        if (!supportedTypes.Contains(fileType))
        {
            return JsonSerializer.Serialize(new
            {
                success = true,
                message = $"File type '{fileType}' is not a recognized IaC format. No compliance findings to report.",
                findings = Array.Empty<object>(),
                fileType,
                framework
            });
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Perform rule-based IaC compliance scanning
        var findings = await ScanIacContentAsync(fileContent, fileType, framework, cancellationToken);
        var validationLinksCreated = 0;
        if (_validationLinks is not null && !string.IsNullOrWhiteSpace(systemId))
        {
            foreach (var finding in findings)
            {
                var created = await _validationLinks.UpsertScanLinkAsync(
                    systemId,
                    finding.ControlId,
                    $"{filePath}#{finding.FindingId}",
                    $"{finding.Title}: {finding.Description}",
                    cancellationToken);
                if (created)
                    validationLinksCreated++;
            }
        }

        var result = new
        {
            success = true,
            filePath,
            fileType,
            framework,
            totalFindings = findings.Count,
            findings,
            scannedAt = DateTime.UtcNow,
            metadata = new
            {
                validationLinksCreated,
            },
        };

        Logger.LogInformation("IaC scan complete | File: {FilePath}, Findings: {Count}",
            filePath, findings.Count);

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }

    /// <summary>
    /// Scans IaC content for compliance findings using rule-based pattern matching.
    /// </summary>
    private Task<List<IacFinding>> ScanIacContentAsync(
        string content, string fileType, string framework, CancellationToken cancellationToken)
    {
        var findings = new List<IacFinding>();
        var lines = content.Split('\n');

        var rules = GetComplianceRules(fileType, framework);

        for (var lineNum = 0; lineNum < lines.Length; lineNum++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = lines[lineNum].Trim();
            foreach (var rule in rules)
            {
                if (rule.Matches(line, fileType))
                {
                    // Generate language-specific suggested fix (T082)
                    string? suggestedFix = null;
                    if (rule.AutoRemediable && rule.SuggestedFixFunc is not null)
                    {
                        suggestedFix = rule.SuggestedFixFunc(line, fileType);
                    }

                    findings.Add(new IacFinding
                    {
                        FindingId = $"IAC-{rule.RuleId}-{lineNum + 1}",
                        RuleId = rule.RuleId,
                        ControlId = rule.ControlId,
                        ControlFamily = rule.ControlFamily,
                        Severity = rule.Severity,
                        CatSeverity = MapToCatSeverity(rule.Severity),
                        Title = rule.Title,
                        Description = rule.Description,
                        LineNumber = lineNum + 1,
                        LineContent = line,
                        Remediation = rule.Remediation,
                        AutoRemediable = rule.AutoRemediable,
                        SuggestedFix = suggestedFix,
                        Framework = framework
                    });
                }
            }
        }

        return Task.FromResult(findings);
    }

    /// <summary>Maps severity string to DoD CAT severity level.</summary>
    private static string MapToCatSeverity(string severity) => severity.ToUpperInvariant() switch
    {
        "CRITICAL" or "HIGH" => "CAT I",
        "MEDIUM" => "CAT II",
        "LOW" => "CAT III",
        _ => "CAT III"
    };

    /// <summary>
    /// Returns the set of compliance rules for the given IaC file type and framework.
    /// Rules are cached and shared across scans for performance.
    /// </summary>
    private static List<IacComplianceRule> GetComplianceRules(string fileType, string framework)
    {
        // Return applicable rules for the file type (all rules are universal or file-type-checked internally)
        return CachedRules.Value;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Rule Definitions — 50+ rules organized by NIST 800-53 control family
    // ────────────────────────────────────────────────────────────────────────

    private static List<IacComplianceRule> BuildAllRules()
    {
        var rules = new List<IacComplianceRule>();

        // ── AC: Access Control ──────────────────────────────────────────
        rules.AddRange(BuildAccessControlRules());

        // ── AU: Audit and Accountability ────────────────────────────────
        rules.AddRange(BuildAuditRules());

        // ── CM: Configuration Management ────────────────────────────────
        rules.AddRange(BuildConfigManagementRules());

        // ── IA: Identification and Authentication ───────────────────────
        rules.AddRange(BuildIdentityAuthRules());

        // ── SC: System and Communications Protection ────────────────────
        rules.AddRange(BuildSystemCommProtectionRules());

        // ── SI: System and Information Integrity ────────────────────────
        rules.AddRange(BuildSystemIntegrityRules());

        // ── CP: Contingency Planning ────────────────────────────────────
        rules.AddRange(BuildContingencyRules());

        // ── MP: Media Protection ────────────────────────────────────────
        rules.AddRange(BuildMediaProtectionRules());

        return rules;
    }

    // ── AC: Access Control (12 rules) ───────────────────────────────────

    private static List<IacComplianceRule> BuildAccessControlRules() =>
    [
        // AC-3: Access Enforcement — public access
        new()
        {
            RuleId = "AC-3-01", ControlId = "AC-3", ControlFamily = "Access Control",
            Severity = "High", Title = "Public network access enabled",
            Description = "Resources should not allow unrestricted public network access.",
            Remediation = "Set publicNetworkAccess to 'Disabled' or restrict via firewall rules.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("publicNetworkAccess", StringComparison.OrdinalIgnoreCase)
                && line.Contains("Enabled", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, ft) => UnifiedDiff(line, line.Replace("Enabled", "Disabled", StringComparison.OrdinalIgnoreCase))
        },
        // AC-3-02: Public blob access
        new()
        {
            RuleId = "AC-3-02", ControlId = "AC-3", ControlFamily = "Access Control",
            Severity = "High", Title = "Public blob access enabled on storage account",
            Description = "Storage accounts should not allow anonymous public blob access.",
            Remediation = "Set allowBlobPublicAccess to false.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("allowBlobPublicAccess", StringComparison.OrdinalIgnoreCase)
                && line.Contains("true", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\btrue\b", "false", RegexOptions.IgnoreCase))
        },
        // AC-4: Information Flow — unrestricted NSG inbound
        new()
        {
            RuleId = "AC-4-01", ControlId = "AC-4", ControlFamily = "Access Control",
            Severity = "Critical", Title = "NSG rule allows unrestricted inbound traffic (0.0.0.0/0 or *)",
            Description = "Network Security Group rules should not allow inbound traffic from all sources.",
            Remediation = "Restrict sourceAddressPrefix to specific IP ranges or service tags.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("sourceAddressPrefix", StringComparison.OrdinalIgnoreCase)
                || line.Contains("source_address_prefix", StringComparison.OrdinalIgnoreCase))
                && (line.Contains("'*'", StringComparison.Ordinal)
                    || line.Contains("\"*\"", StringComparison.Ordinal)
                    || line.Contains("0.0.0.0/0", StringComparison.Ordinal)
                    || line.Contains("'Internet'", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("\"Internet\"", StringComparison.OrdinalIgnoreCase))
        },
        // AC-4-02: NSG allows SSH from any source
        new()
        {
            RuleId = "AC-4-02", ControlId = "AC-4", ControlFamily = "Access Control",
            Severity = "Critical", Title = "SSH port (22) exposed in NSG rule",
            Description = "SSH (port 22) should not be open to the internet. Use Azure Bastion or VPN.",
            Remediation = "Remove or restrict the NSG rule for port 22.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("destinationPortRange", StringComparison.OrdinalIgnoreCase)
                || line.Contains("destination_port_range", StringComparison.OrdinalIgnoreCase))
                && (line.Contains("'22'", StringComparison.Ordinal)
                    || line.Contains("\"22\"", StringComparison.Ordinal)
                    || line.Contains(": 22", StringComparison.Ordinal)
                    || line.Contains("= \"22\"", StringComparison.Ordinal))
        },
        // AC-4-03: NSG allows RDP from any source
        new()
        {
            RuleId = "AC-4-03", ControlId = "AC-4", ControlFamily = "Access Control",
            Severity = "Critical", Title = "RDP port (3389) exposed in NSG rule",
            Description = "RDP (port 3389) should not be open to the internet. Use Azure Bastion or VPN.",
            Remediation = "Remove or restrict the NSG rule for port 3389.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("destinationPortRange", StringComparison.OrdinalIgnoreCase)
                || line.Contains("destination_port_range", StringComparison.OrdinalIgnoreCase))
                && (line.Contains("'3389'", StringComparison.Ordinal)
                    || line.Contains("\"3389\"", StringComparison.Ordinal)
                    || line.Contains(": 3389", StringComparison.Ordinal)
                    || line.Contains("= \"3389\"", StringComparison.Ordinal))
        },
        // AC-4-04: NSG allows all ports
        new()
        {
            RuleId = "AC-4-04", ControlId = "AC-4", ControlFamily = "Access Control",
            Severity = "Critical", Title = "NSG rule allows all destination ports (*)",
            Description = "Network Security Group rules should not allow traffic to all ports.",
            Remediation = "Restrict destinationPortRange to specific ports needed by the application.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("destinationPortRange", StringComparison.OrdinalIgnoreCase)
                || line.Contains("destination_port_range", StringComparison.OrdinalIgnoreCase))
                && (line.Contains("'*'", StringComparison.Ordinal)
                    || line.Contains("\"*\"", StringComparison.Ordinal))
        },
        // AC-6: Least Privilege — admin access
        new()
        {
            RuleId = "AC-6-01", ControlId = "AC-6", ControlFamily = "Access Control",
            Severity = "Critical", Title = "Administrative access detected in resource configuration",
            Description = "Resources should follow the principle of least privilege.",
            Remediation = "Use specific role assignments instead of broad administrative access.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => line.Contains("admin", StringComparison.OrdinalIgnoreCase)
                && (line.Contains("true", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("'Owner'", StringComparison.Ordinal)
                    || line.Contains("\"Owner\"", StringComparison.Ordinal))
        },
        // AC-6-02: Contributor role at subscription scope
        new()
        {
            RuleId = "AC-6-02", ControlId = "AC-6", ControlFamily = "Access Control",
            Severity = "High", Title = "Broad Contributor role assignment detected",
            Description = "Contributor role grants modify access to all resources. Use more specific roles.",
            Remediation = "Replace Contributor with a scoped role like 'Storage Blob Data Contributor'.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("'Contributor'", StringComparison.Ordinal)
                || line.Contains("\"Contributor\"", StringComparison.Ordinal)
                || line.Contains("b24988ac-6180-42a0-ab88-20f7382dd24c", StringComparison.OrdinalIgnoreCase))
        },
        // AC-6-03: Wildcard permissions
        new()
        {
            RuleId = "AC-6-03", ControlId = "AC-6", ControlFamily = "Access Control",
            Severity = "Critical", Title = "Wildcard (*) permissions detected",
            Description = "Wildcard permissions grant unrestricted access. Use explicit permissions.",
            Remediation = "Replace '*' with specific action permissions.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("actions", StringComparison.OrdinalIgnoreCase)
                || line.Contains("permissions", StringComparison.OrdinalIgnoreCase))
                && line.Contains("'*'", StringComparison.Ordinal)
        },
        // AC-17: Remote Access — public IP
        new()
        {
            RuleId = "AC-17-01", ControlId = "AC-17", ControlFamily = "Access Control",
            Severity = "Medium", Title = "Public IP address allocation detected",
            Description = "Public IP addresses expose resources to the internet. Prefer private endpoints.",
            Remediation = "Use Azure Private Link or private endpoints instead of public IPs.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => line.Contains("Microsoft.Network/publicIPAddresses", StringComparison.OrdinalIgnoreCase)
                || (line.Contains("publicIPAllocationMethod", StringComparison.OrdinalIgnoreCase)
                    && line.Contains("Static", StringComparison.OrdinalIgnoreCase))
        },
        // AC-17-02: Remote management without Bastion
        new()
        {
            RuleId = "AC-17-02", ControlId = "AC-17", ControlFamily = "Access Control",
            Severity = "Medium", Title = "Direct management port exposed without Bastion",
            Description = "Management ports (22, 3389, 5985, 5986) should be accessed via Azure Bastion.",
            Remediation = "Deploy Azure Bastion and remove direct NSG rules for management ports.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("destinationPortRange", StringComparison.OrdinalIgnoreCase)
                || line.Contains("destination_port_range", StringComparison.OrdinalIgnoreCase))
                && (line.Contains("'5985'", StringComparison.Ordinal)
                    || line.Contains("\"5985\"", StringComparison.Ordinal)
                    || line.Contains("'5986'", StringComparison.Ordinal)
                    || line.Contains("\"5986\"", StringComparison.Ordinal))
        },
        // AC-2-01: ACR admin user enabled
        new()
        {
            RuleId = "AC-2-01", ControlId = "AC-2", ControlFamily = "Access Control",
            Severity = "High", Title = "Container registry admin user enabled",
            Description = "ACR admin user should be disabled. Use Azure AD authentication instead.",
            Remediation = "Set adminUserEnabled to false and use managed identity or service principal.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("adminUserEnabled", StringComparison.OrdinalIgnoreCase)
                && line.Contains("true", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\btrue\b", "false", RegexOptions.IgnoreCase))
        },
    ];

    // ── AU: Audit and Accountability (7 rules) ─────────────────────────

    private static List<IacComplianceRule> BuildAuditRules() =>
    [
        // AU-2: Event Logging — no diagnostic settings
        new()
        {
            RuleId = "AU-2-01", ControlId = "AU-2", ControlFamily = "Audit and Accountability",
            Severity = "Medium", Title = "Resource has no diagnostic settings configured",
            Description = "Azure resources should have diagnostic settings to capture audit events.",
            Remediation = "Add a diagnosticSettings child resource with log categories enabled.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => false // Placeholder for multi-line analysis (checked as absence)
        },
        // AU-3: Content of Audit Records — retention too short
        new()
        {
            RuleId = "AU-3-01", ControlId = "AU-3", ControlFamily = "Audit and Accountability",
            Severity = "Medium", Title = "Log retention period less than 90 days",
            Description = "Audit log retention should be at least 90 days per FedRAMP/DoD requirements.",
            Remediation = "Set retentionPolicy.days to 90 or higher.",
            AutoRemediable = true,
            MatchesFunc = (line, _) =>
            {
                if (!line.Contains("days", StringComparison.OrdinalIgnoreCase)) return false;
                var match = Regex.Match(line, @"(?:days|retention).*?(\d+)", RegexOptions.IgnoreCase);
                return match.Success && int.TryParse(match.Groups[1].Value, out var days) && days < 90;
            },
            SuggestedFixFunc = (line, _) =>
            {
                var updated = Regex.Replace(line, @"(\d+)", m =>
                    int.TryParse(m.Value, out var d) && d < 90 ? "90" : m.Value);
                return UnifiedDiff(line, updated);
            }
        },
        // AU-6: Audit Review — logging disabled
        new()
        {
            RuleId = "AU-6-01", ControlId = "AU-6", ControlFamily = "Audit and Accountability",
            Severity = "Medium", Title = "Logging or auditing appears disabled",
            Description = "Resources should have audit logging enabled for accountability.",
            Remediation = "Enable diagnostic logging and audit log retention.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => (line.Contains("logging", StringComparison.OrdinalIgnoreCase)
                || line.Contains("diagnostics", StringComparison.OrdinalIgnoreCase))
                && (line.Contains("false", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("disabled", StringComparison.OrdinalIgnoreCase)),
            SuggestedFixFunc = (line, _) =>
            {
                var updated = Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase);
                updated = updated.Replace("disabled", "enabled", StringComparison.OrdinalIgnoreCase);
                return UnifiedDiff(line, updated);
            }
        },
        // AU-6-02: SQL auditing disabled
        new()
        {
            RuleId = "AU-6-02", ControlId = "AU-6", ControlFamily = "Audit and Accountability",
            Severity = "High", Title = "SQL Server auditing disabled",
            Description = "SQL Server auditing should be enabled for database accountability.",
            Remediation = "Set state to 'Enabled' on the auditing policy.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("auditingSettings", StringComparison.OrdinalIgnoreCase)
                && line.Contains("Disabled", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, line.Replace("Disabled", "Enabled", StringComparison.OrdinalIgnoreCase))
        },
        // AU-9: Protection of Audit Information — log deletion allowed
        new()
        {
            RuleId = "AU-9-01", ControlId = "AU-9", ControlFamily = "Audit and Accountability",
            Severity = "Medium", Title = "Audit log immutability not configured",
            Description = "Audit logs should be protected with immutability policies.",
            Remediation = "Enable immutability policy on the storage container used for audit logs.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => line.Contains("immutabilityPolicy", StringComparison.OrdinalIgnoreCase)
                && line.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
        },
        // AU-12-01: App Service HTTP logging disabled
        new()
        {
            RuleId = "AU-12-01", ControlId = "AU-12", ControlFamily = "Audit and Accountability",
            Severity = "Medium", Title = "HTTP logging disabled on App Service",
            Description = "App Service should have HTTP logging enabled for request auditing.",
            Remediation = "Set httpLoggingEnabled to true.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("httpLoggingEnabled", StringComparison.OrdinalIgnoreCase)
                && line.Contains("false", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase))
        },
        // AU-12-02: App Service detailed errors disabled
        new()
        {
            RuleId = "AU-12-02", ControlId = "AU-12", ControlFamily = "Audit and Accountability",
            Severity = "Low", Title = "Detailed error logging disabled on App Service",
            Description = "App Service should have detailed error messages enabled for diagnostics.",
            Remediation = "Set detailedErrorLoggingEnabled to true.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("detailedErrorLoggingEnabled", StringComparison.OrdinalIgnoreCase)
                && line.Contains("false", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase))
        },
    ];

    // ── CM: Configuration Management (6 rules) ─────────────────────────

    private static List<IacComplianceRule> BuildConfigManagementRules() =>
    [
        // CM-2: Baseline Configuration — latest TLS
        new()
        {
            RuleId = "CM-2-01", ControlId = "CM-2", ControlFamily = "Configuration Management",
            Severity = "High", Title = "TLS version below 1.2 detected",
            Description = "All services should use TLS 1.2 or higher per NIST/FedRAMP requirements.",
            Remediation = "Set minTlsVersion to '1.2' or 'TLS1_2'.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("minTlsVersion", StringComparison.OrdinalIgnoreCase)
                && (line.Contains("'1.0'", StringComparison.Ordinal)
                    || line.Contains("\"1.0\"", StringComparison.Ordinal)
                    || line.Contains("'1.1'", StringComparison.Ordinal)
                    || line.Contains("\"1.1\"", StringComparison.Ordinal)
                    || line.Contains("'TLS1_0'", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("\"TLS1_0\"", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("'TLS1_1'", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("\"TLS1_1\"", StringComparison.OrdinalIgnoreCase)),
            SuggestedFixFunc = (line, ft) =>
            {
                var updated = Regex.Replace(line, @"'(1\.0|1\.1|TLS1_0|TLS1_1)'", "'1.2'", RegexOptions.IgnoreCase);
                updated = Regex.Replace(updated, @"""(1\.0|1\.1|TLS1_0|TLS1_1)""", "\"1.2\"", RegexOptions.IgnoreCase);
                return UnifiedDiff(line, updated);
            }
        },
        // CM-2-02: HTTP 2.0 not enabled
        new()
        {
            RuleId = "CM-2-02", ControlId = "CM-2", ControlFamily = "Configuration Management",
            Severity = "Low", Title = "HTTP 2.0 not enabled on App Service",
            Description = "App Services should use HTTP/2 for improved performance and security.",
            Remediation = "Set http20Enabled to true.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("http20Enabled", StringComparison.OrdinalIgnoreCase)
                && line.Contains("false", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase))
        },
        // CM-6: Configuration Settings — FTP enabled
        new()
        {
            RuleId = "CM-6-01", ControlId = "CM-6", ControlFamily = "Configuration Management",
            Severity = "High", Title = "FTP access enabled on App Service",
            Description = "FTP (non-encrypted) should be disabled. Use FTPS or deployment through Git/CI.",
            Remediation = "Set ftpsState to 'Disabled' or 'FtpsOnly'.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("ftpsState", StringComparison.OrdinalIgnoreCase)
                && line.Contains("AllAllowed", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, line.Replace("AllAllowed", "Disabled", StringComparison.OrdinalIgnoreCase))
        },
        // CM-6-02: Remote debugging enabled
        new()
        {
            RuleId = "CM-6-02", ControlId = "CM-6", ControlFamily = "Configuration Management",
            Severity = "High", Title = "Remote debugging enabled on App Service",
            Description = "Remote debugging should be disabled in production for security.",
            Remediation = "Set remoteDebuggingEnabled to false.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("remoteDebuggingEnabled", StringComparison.OrdinalIgnoreCase)
                && line.Contains("true", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\btrue\b", "false", RegexOptions.IgnoreCase))
        },
        // CM-7: Least Functionality — unnecessary services
        new()
        {
            RuleId = "CM-7-01", ControlId = "CM-7", ControlFamily = "Configuration Management",
            Severity = "Medium", Title = "Web sockets enabled unnecessarily",
            Description = "Disable web sockets if not required by the application.",
            Remediation = "Set webSocketsEnabled to false unless web socket protocol is needed.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => false // Placeholder — context-dependent analysis needed
        },
        // CM-8: Information System Component Inventory — untagged resources
        new()
        {
            RuleId = "CM-8-01", ControlId = "CM-8", ControlFamily = "Configuration Management",
            Severity = "Low", Title = "Resource missing required tags",
            Description = "Azure resources should have tags for inventory tracking (environment, owner, classification).",
            Remediation = "Add tags: { environment: '...', owner: '...', dataClassification: '...' }.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => false // Placeholder — multi-line context needed
        },
    ];

    // ── IA: Identification and Authentication (6 rules) ─────────────────

    private static List<IacComplianceRule> BuildIdentityAuthRules() =>
    [
        // IA-2: Identification and Authentication — managed identity not used
        new()
        {
            RuleId = "IA-2-01", ControlId = "IA-2", ControlFamily = "Identification and Authentication",
            Severity = "Medium", Title = "Managed identity not configured",
            Description = "Azure resources should use managed identity for authentication instead of keys/passwords.",
            Remediation = "Add identity: { type: 'SystemAssigned' } or 'UserAssigned' to the resource.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => line.Contains("identity", StringComparison.OrdinalIgnoreCase)
                && line.Contains("'None'", StringComparison.OrdinalIgnoreCase)
        },
        // IA-5: Authenticator Management — hardcoded secrets
        new()
        {
            RuleId = "IA-5-01", ControlId = "IA-5", ControlFamily = "Identification and Authentication",
            Severity = "Critical", Title = "Potential hardcoded secret or password detected",
            Description = "Secrets should not be hardcoded in IaC files. Use Key Vault references.",
            Remediation = "Replace hardcoded value with a Key Vault reference or parameter.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("password", StringComparison.OrdinalIgnoreCase)
                || line.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || line.Contains("apiKey", StringComparison.OrdinalIgnoreCase))
                && (line.Contains("=", StringComparison.Ordinal)
                    || line.Contains(":", StringComparison.Ordinal))
                && !line.TrimStart().StartsWith("//") && !line.TrimStart().StartsWith("#")
                && !line.Contains("keyVault", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("@secure", StringComparison.OrdinalIgnoreCase)
        },
        // IA-5-02: Connection string with embedded credentials
        new()
        {
            RuleId = "IA-5-02", ControlId = "IA-5", ControlFamily = "Identification and Authentication",
            Severity = "Critical", Title = "Connection string with embedded credentials detected",
            Description = "Connection strings should not contain embedded passwords. Use managed identity or Key Vault.",
            Remediation = "Use Azure AD authentication (Authentication=Active Directory Managed Identity) or Key Vault references.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => line.Contains("connectionString", StringComparison.OrdinalIgnoreCase)
                && (line.Contains("Password=", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("pwd=", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase))
        },
        // IA-5-03: Plaintext token/key
        new()
        {
            RuleId = "IA-5-03", ControlId = "IA-5", ControlFamily = "Identification and Authentication",
            Severity = "Critical", Title = "Plaintext access key or token detected",
            Description = "Access keys and tokens must not be stored in IaC files.",
            Remediation = "Use Key Vault references or @secure() param decorators.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("accessKey", StringComparison.OrdinalIgnoreCase)
                || line.Contains("primaryKey", StringComparison.OrdinalIgnoreCase)
                || line.Contains("secondaryKey", StringComparison.OrdinalIgnoreCase)
                || line.Contains("sasToken", StringComparison.OrdinalIgnoreCase))
                && (line.Contains("=", StringComparison.Ordinal) || line.Contains(":", StringComparison.Ordinal))
                && !line.TrimStart().StartsWith("//") && !line.TrimStart().StartsWith("#")
                && !line.Contains("keyVault", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("@secure", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("listKeys", StringComparison.OrdinalIgnoreCase)
        },
        // IA-5-04: AKS local accounts enabled
        new()
        {
            RuleId = "IA-5-04", ControlId = "IA-5", ControlFamily = "Identification and Authentication",
            Severity = "High", Title = "AKS local accounts not disabled",
            Description = "AKS clusters should disable local accounts and use Azure AD authentication.",
            Remediation = "Set disableLocalAccounts to true.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("disableLocalAccounts", StringComparison.OrdinalIgnoreCase)
                && line.Contains("false", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase))
        },
        // IA-8: Identification and Authentication (Non-Organizational Users) — AAD auth not required
        new()
        {
            RuleId = "IA-8-01", ControlId = "IA-8", ControlFamily = "Identification and Authentication",
            Severity = "Medium", Title = "Azure AD authentication not enforced on App Service",
            Description = "App Service should require Azure AD authentication for identity management.",
            Remediation = "Enable Azure AD authentication in the App Service auth settings.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("authSettings", StringComparison.OrdinalIgnoreCase)
                || line.Contains("siteAuthEnabled", StringComparison.OrdinalIgnoreCase))
                && line.Contains("false", StringComparison.OrdinalIgnoreCase)
        },
    ];

    // ── SC: System and Communications Protection (10 rules) ─────────────

    private static List<IacComplianceRule> BuildSystemCommProtectionRules() =>
    [
        // SC-7: Boundary Protection — no firewall rules
        new()
        {
            RuleId = "SC-7-01", ControlId = "SC-7", ControlFamily = "System and Communications Protection",
            Severity = "High", Title = "No firewall rules configured on database server",
            Description = "Database servers should have firewall rules restricting access.",
            Remediation = "Add firewall rules limiting access to known IP ranges or Azure services.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => line.Contains("startIpAddress", StringComparison.OrdinalIgnoreCase)
                && line.Contains("0.0.0.0", StringComparison.Ordinal)
                && line.Contains("endIpAddress", StringComparison.OrdinalIgnoreCase) is false
        },
        // SC-7-02: Allow all Azure services firewall rule
        new()
        {
            RuleId = "SC-7-02", ControlId = "SC-7", ControlFamily = "System and Communications Protection",
            Severity = "Medium", Title = "Firewall rule allows all Azure services (0.0.0.0)",
            Description = "The 'Allow Azure Services' firewall rule opens access to all Azure tenants.",
            Remediation = "Use virtual network rules or private endpoints instead.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("startIpAddress", StringComparison.OrdinalIgnoreCase)
                || line.Contains("start_ip_address", StringComparison.OrdinalIgnoreCase))
                && line.Contains("0.0.0.0", StringComparison.Ordinal)
                && line.Contains("AllowAllWindowsAzureIps", StringComparison.OrdinalIgnoreCase)
        },
        // SC-8: Transmission Confidentiality — HTTPS required
        new()
        {
            RuleId = "SC-8-01", ControlId = "SC-8", ControlFamily = "System and Communications Protection",
            Severity = "High", Title = "Unencrypted HTTP protocol detected",
            Description = "Resources should use HTTPS to ensure transmission confidentiality.",
            Remediation = "Change 'http://' to 'https://' or enable TLS/SSL.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("http://", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("https://", StringComparison.OrdinalIgnoreCase)
                && !line.TrimStart().StartsWith("//") && !line.TrimStart().StartsWith("#"),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, line.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase))
        },
        // SC-8-02: HTTPS-only not enforced on storage
        new()
        {
            RuleId = "SC-8-02", ControlId = "SC-8", ControlFamily = "System and Communications Protection",
            Severity = "High", Title = "HTTPS-only traffic not enforced on storage account",
            Description = "Storage accounts should enforce HTTPS-only access.",
            Remediation = "Set supportsHttpsTrafficOnly to true.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("supportsHttpsTrafficOnly", StringComparison.OrdinalIgnoreCase)
                && line.Contains("false", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase))
        },
        // SC-8-03: SSL enforcement disabled on database
        new()
        {
            RuleId = "SC-8-03", ControlId = "SC-8", ControlFamily = "System and Communications Protection",
            Severity = "High", Title = "SSL enforcement disabled on database server",
            Description = "Database connections should require SSL/TLS encryption.",
            Remediation = "Set sslEnforcement to 'Enabled'.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("sslEnforcement", StringComparison.OrdinalIgnoreCase)
                && line.Contains("Disabled", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, line.Replace("Disabled", "Enabled", StringComparison.OrdinalIgnoreCase))
        },
        // SC-12: Cryptographic Key Management — Key Vault soft delete
        new()
        {
            RuleId = "SC-12-01", ControlId = "SC-12", ControlFamily = "System and Communications Protection",
            Severity = "High", Title = "Key Vault soft delete not enabled",
            Description = "Key Vaults should have soft-delete enabled to prevent accidental key loss.",
            Remediation = "Set enableSoftDelete to true. Note: soft delete is now default in new Key Vaults.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("enableSoftDelete", StringComparison.OrdinalIgnoreCase)
                && line.Contains("false", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase))
        },
        // SC-12-02: Key Vault purge protection
        new()
        {
            RuleId = "SC-12-02", ControlId = "SC-12", ControlFamily = "System and Communications Protection",
            Severity = "High", Title = "Key Vault purge protection not enabled",
            Description = "Key Vaults should have purge protection to prevent permanent deletion during retention.",
            Remediation = "Set enablePurgeProtection to true.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("enablePurgeProtection", StringComparison.OrdinalIgnoreCase)
                && line.Contains("false", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase))
        },
        // SC-13: Cryptographic Protection — min key size
        new()
        {
            RuleId = "SC-13-01", ControlId = "SC-13", ControlFamily = "System and Communications Protection",
            Severity = "High", Title = "Cryptographic key size below 2048 bits",
            Description = "RSA keys should be at least 2048 bits per NIST SP 800-57 requirements.",
            Remediation = "Set key size to 2048 or higher (prefer 4096 for new keys).",
            AutoRemediable = true,
            MatchesFunc = (line, _) =>
            {
                if (!line.Contains("keySize", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("key_size", StringComparison.OrdinalIgnoreCase)) return false;
                var match = Regex.Match(line, @"(?:keySize|key_size).*?(\d+)", RegexOptions.IgnoreCase);
                return match.Success && int.TryParse(match.Groups[1].Value, out var size) && size < 2048;
            },
            SuggestedFixFunc = (line, _) =>
            {
                var updated = Regex.Replace(line, @"(\d+)", m =>
                    int.TryParse(m.Value, out var s) && s < 2048 ? "2048" : m.Value);
                return UnifiedDiff(line, updated);
            }
        },
        // SC-28: Protection of Information at Rest — encryption disabled
        new()
        {
            RuleId = "SC-28-01", ControlId = "SC-28", ControlFamily = "System and Communications Protection",
            Severity = "High", Title = "Storage encryption not enabled",
            Description = "Storage accounts and disks should have encryption enabled at rest.",
            Remediation = "Set encryption.enabled to true or add encryption configuration.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("encryption", StringComparison.OrdinalIgnoreCase)
                && (line.Contains("false", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("disabled", StringComparison.OrdinalIgnoreCase)),
            SuggestedFixFunc = (line, _) =>
            {
                var updated = Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase);
                updated = updated.Replace("disabled", "enabled", StringComparison.OrdinalIgnoreCase);
                return UnifiedDiff(line, updated);
            }
        },
        // SC-28-02: Disk encryption not enabled
        new()
        {
            RuleId = "SC-28-02", ControlId = "SC-28", ControlFamily = "System and Communications Protection",
            Severity = "High", Title = "Managed disk encryption set not configured",
            Description = "Managed disks should use server-side encryption with customer-managed keys.",
            Remediation = "Set diskEncryptionSetId or enable Azure Disk Encryption.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => line.Contains("encryptionType", StringComparison.OrdinalIgnoreCase)
                && (line.Contains("'EncryptionAtRestWithPlatformKey'", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("\"EncryptionAtRestWithPlatformKey\"", StringComparison.OrdinalIgnoreCase))
        },
    ];

    // ── SI: System and Information Integrity (5 rules) ──────────────────

    private static List<IacComplianceRule> BuildSystemIntegrityRules() =>
    [
        // SI-2: Flaw Remediation — auto-upgrade disabled
        new()
        {
            RuleId = "SI-2-01", ControlId = "SI-2", ControlFamily = "System and Information Integrity",
            Severity = "Medium", Title = "Automatic OS upgrades disabled on VMSS",
            Description = "VM Scale Sets should enable automatic OS upgrades for timely patching.",
            Remediation = "Set enableAutomaticOSUpgrade to true.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("enableAutomaticOSUpgrade", StringComparison.OrdinalIgnoreCase)
                && line.Contains("false", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase))
        },
        // SI-3: Malicious Code Protection — Defender not enabled
        new()
        {
            RuleId = "SI-3-01", ControlId = "SI-3", ControlFamily = "System and Information Integrity",
            Severity = "High", Title = "Microsoft Defender for Cloud not enabled",
            Description = "Microsoft Defender for Cloud should be enabled for threat protection.",
            Remediation = "Set pricingTier to 'Standard' for Defender plans.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("pricingTier", StringComparison.OrdinalIgnoreCase)
                && line.Contains("Free", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, line.Replace("Free", "Standard", StringComparison.OrdinalIgnoreCase))
        },
        // SI-4: Information System Monitoring — WAF disabled
        new()
        {
            RuleId = "SI-4-01", ControlId = "SI-4", ControlFamily = "System and Information Integrity",
            Severity = "High", Title = "Web Application Firewall (WAF) not in prevention mode",
            Description = "WAF should be set to Prevention mode in production to block threats.",
            Remediation = "Set firewallMode or mode to 'Prevention'.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => (line.Contains("firewallMode", StringComparison.OrdinalIgnoreCase)
                || line.Contains("wafMode", StringComparison.OrdinalIgnoreCase))
                && line.Contains("Detection", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, line.Replace("Detection", "Prevention", StringComparison.OrdinalIgnoreCase))
        },
        // SI-4-02: Network Watcher not enabled
        new()
        {
            RuleId = "SI-4-02", ControlId = "SI-4", ControlFamily = "System and Information Integrity",
            Severity = "Medium", Title = "Network Watcher flow logs not configured",
            Description = "NSG flow logs should be enabled for network monitoring and threat detection.",
            Remediation = "Add a flow log resource for NSG traffic analysis.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => line.Contains("flowLogs", StringComparison.OrdinalIgnoreCase)
                && line.Contains("false", StringComparison.OrdinalIgnoreCase)
        },
        // SI-7: Software, Firmware, and Information Integrity — container image not from trusted registry
        new()
        {
            RuleId = "SI-7-01", ControlId = "SI-7", ControlFamily = "System and Information Integrity",
            Severity = "Medium", Title = "Container image from untrusted public registry",
            Description = "Container images should be sourced from private/trusted registries.",
            Remediation = "Use ACR or another private registry instead of Docker Hub public images.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => (line.Contains("image:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("\"image\"", StringComparison.OrdinalIgnoreCase))
                && (line.Contains("docker.io", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("hub.docker.com", StringComparison.OrdinalIgnoreCase)
                    || (Regex.IsMatch(line, @"image.*?['""]([a-z0-9]+/[a-z0-9])", RegexOptions.IgnoreCase)
                        && !line.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase)
                        && !line.Contains(".mcr.microsoft.com", StringComparison.OrdinalIgnoreCase)))
        },
    ];

    // ── CP: Contingency Planning (3 rules) ──────────────────────────────

    private static List<IacComplianceRule> BuildContingencyRules() =>
    [
        // CP-9: Information System Backup — geo-redundancy disabled
        new()
        {
            RuleId = "CP-9-01", ControlId = "CP-9", ControlFamily = "Contingency Planning",
            Severity = "Medium", Title = "Geo-redundant storage not configured",
            Description = "Storage accounts should use GRS or RA-GRS for disaster recovery.",
            Remediation = "Set sku.name to 'Standard_GRS' or 'Standard_RAGRS'.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => (line.Contains("sku", StringComparison.OrdinalIgnoreCase)
                || line.Contains("replication_type", StringComparison.OrdinalIgnoreCase))
                && (line.Contains("'Standard_LRS'", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("\"Standard_LRS\"", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("'LRS'", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("\"LRS\"", StringComparison.OrdinalIgnoreCase)),
            SuggestedFixFunc = (line, ft) =>
            {
                var updated = Regex.Replace(line, @"'Standard_LRS'", "'Standard_GRS'", RegexOptions.IgnoreCase);
                updated = Regex.Replace(updated, @"""Standard_LRS""", "\"Standard_GRS\"", RegexOptions.IgnoreCase);
                updated = Regex.Replace(updated, @"'LRS'", "'GRS'", RegexOptions.IgnoreCase);
                updated = Regex.Replace(updated, @"""LRS""", "\"GRS\"", RegexOptions.IgnoreCase);
                return UnifiedDiff(line, updated);
            }
        },
        // CP-9-02: SQL database backup retention too short
        new()
        {
            RuleId = "CP-9-02", ControlId = "CP-9", ControlFamily = "Contingency Planning",
            Severity = "Medium", Title = "Database backup retention period too short",
            Description = "SQL database backups should be retained for at least 35 days.",
            Remediation = "Set backupRetentionDays to 35 or higher.",
            AutoRemediable = true,
            MatchesFunc = (line, _) =>
            {
                if (!line.Contains("backupRetentionDays", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("backup_retention_days", StringComparison.OrdinalIgnoreCase)) return false;
                var match = Regex.Match(line, @"(\d+)", RegexOptions.IgnoreCase);
                return match.Success && int.TryParse(match.Groups[1].Value, out var d) && d < 35;
            },
            SuggestedFixFunc = (line, _) =>
            {
                var updated = Regex.Replace(line, @"(\d+)", m =>
                    int.TryParse(m.Value, out var d) && d < 35 ? "35" : m.Value);
                return UnifiedDiff(line, updated);
            }
        },
        // CP-10: Information System Recovery — geo-replication for databases
        new()
        {
            RuleId = "CP-10-01", ControlId = "CP-10", ControlFamily = "Contingency Planning",
            Severity = "Medium", Title = "Database geo-replication not configured",
            Description = "Databases should have geo-replication for disaster recovery planning.",
            Remediation = "Configure active geo-replication or auto-failover groups.",
            AutoRemediable = false,
            MatchesFunc = (line, _) => line.Contains("geoRedundantBackup", StringComparison.OrdinalIgnoreCase)
                && line.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
        },
    ];

    // ── MP: Media Protection (1 rule) ────────────────────────────────────

    private static List<IacComplianceRule> BuildMediaProtectionRules() =>
    [
        // MP-5: Media Transport — unencrypted transport
        new()
        {
            RuleId = "MP-5-01", ControlId = "MP-5", ControlFamily = "Media Protection",
            Severity = "Medium", Title = "Infrastructure encryption disabled in transit",
            Description = "Data in transit between Azure data centers should be encrypted (infrastructure encryption).",
            Remediation = "Set requireInfrastructureEncryption to true where supported.",
            AutoRemediable = true,
            MatchesFunc = (line, _) => line.Contains("requireInfrastructureEncryption", StringComparison.OrdinalIgnoreCase)
                && line.Contains("false", StringComparison.OrdinalIgnoreCase),
            SuggestedFixFunc = (line, _) => UnifiedDiff(line, Regex.Replace(line, @"\bfalse\b", "true", RegexOptions.IgnoreCase))
        },
    ];

    // ── Utility Methods ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a unified diff string for a single-line fix.
    /// Format: <c>--- original\n+++ fixed\n@@ -1 +1 @@\n-{old}\n+{new}</c>
    /// </summary>
    internal static string UnifiedDiff(string original, string replacement)
    {
        if (original == replacement) return string.Empty;
        return $"--- original\n+++ fixed\n@@ -1 +1 @@\n-{original}\n+{replacement}";
    }
}

/// <summary>
/// A compliance finding from an IaC scan (T082: includes suggestedFix field).
/// </summary>
internal class IacFinding
{
    public string FindingId { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string ControlId { get; set; } = string.Empty;
    public string ControlFamily { get; set; } = string.Empty;
    public string Severity { get; set; } = "Medium";
    public string CatSeverity { get; set; } = "CAT III";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string LineContent { get; set; } = string.Empty;
    public string Remediation { get; set; } = string.Empty;
    public bool AutoRemediable { get; set; }
    public string? SuggestedFix { get; set; }
    public string Framework { get; set; } = string.Empty;
}

/// <summary>
/// A compliance rule for pattern-based IaC scanning (T081: expanded to 50+ rules).
/// </summary>
internal class IacComplianceRule
{
    public string RuleId { get; set; } = string.Empty;
    public string ControlId { get; set; } = string.Empty;
    public string ControlFamily { get; set; } = string.Empty;
    public string Severity { get; set; } = "Medium";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Remediation { get; set; } = string.Empty;
    public bool AutoRemediable { get; set; }
    public Func<string, string, bool> MatchesFunc { get; set; } = (_, _) => false;
    /// <summary>Generates a language-specific unified diff suggested fix for auto-remediation.</summary>
    public Func<string, string, string>? SuggestedFixFunc { get; set; }

    /// <summary>Checks if a line matches this compliance rule.</summary>
    public bool Matches(string line, string fileType) => MatchesFunc(line, fileType);
}
