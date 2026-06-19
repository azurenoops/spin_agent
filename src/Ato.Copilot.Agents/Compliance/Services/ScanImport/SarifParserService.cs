// =============================================================================
//  SarifParserService.cs
//  Ato.Copilot.Agents — Compliance / Services / ScanImport
//  Issue #422 — AO Posture API (W10 cATO Gap Closure)
//
//  ISarifParserService implementation.
//  3-tier CWE→NIST 800-53 mapping + CAT severity derivation + fingerprint dedup.
//  STATELESS — registered as Singleton.
//  No DB writes — controller/service layer owns persistence after ParseAsync returns.
// =============================================================================

#nullable enable

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services.ScanImport;

/// <summary>
/// Parses SARIF 2.1.0 documents into normalized <see cref="SarifFindingDto"/> records
/// with NIST 800-53 Rev.5 control mapping and DoD CAT severity derivation.
/// </summary>
/// <remarks>
/// Singleton-safe: all shared state is read-only (static maps + compiled patterns from
/// <see cref="CweNistMappings"/> and <see cref="SarifTagPatterns"/>).
/// </remarks>
public sealed class SarifParserService : ISarifParserService
{
    private static readonly Regex CweInRuleIdPattern = new(
        @"CWE-(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NistControlIdPattern = new(
        @"^[A-Z]{2}-\d+(?:\(\d+\))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ILogger<SarifParserService> _logger;

    public SarifParserService(ILogger<SarifParserService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SarifImportResult> ParseAsync(
        JsonDocument sarifPayload,
        Guid systemId,
        string pipelineId,
        string pipelineRun,
        IReadOnlySet<string>? existingFingerprints = null,
        CancellationToken cancellationToken = default)
    {
        var importId = Guid.NewGuid();

        try
        {
            var root = sarifPayload.RootElement;

            // Enforce SARIF 2.1.0
            if (!root.TryGetProperty("version", out var versionEl) ||
                versionEl.GetString() is not "2.1.0")
            {
                var received = root.TryGetProperty("version", out var v) ? v.GetString() : "absent";
                throw new PayloadValidationException(
                    $"SARIF version must be \"2.1.0\". Received: {received}");
            }

            if (!root.TryGetProperty("runs", out var runsEl) ||
                runsEl.ValueKind != JsonValueKind.Array)
                throw new PayloadValidationException("SARIF document missing required 'runs' array.");

            var allFindings = new List<SarifFindingDto>();
            var controlMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            var unmappedRules = new List<UnmappedRuleInfo>();
            var parseErrors = new List<string>();
            var fingerprintsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existing = existingFingerprints is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(existingFingerprints, StringComparer.OrdinalIgnoreCase);

            int findingsImported = 0;
            int findingsDeduplicated = 0;

            foreach (var run in runsEl.EnumerateArray())
            {
                var toolName = ExtractToolName(run);
                var ruleMap = BuildRuleMap(run);

                if (!run.TryGetProperty("results", out var resultsEl) ||
                    resultsEl.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var result in resultsEl.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        ProcessResult(result, ruleMap, toolName, systemId, pipelineRun,
                            fingerprintsSeen, existing, unmappedRules, controlMappings,
                            allFindings, ref findingsImported, ref findingsDeduplicated);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        var err = $"Failed to parse SARIF result: {ex.Message}";
                        parseErrors.Add(err);
                        _logger.LogWarning(ex,
                            "Skipping unparseable SARIF result. Run={Run}", pipelineRun);
                    }
                }
            }

            return Task.FromResult(new SarifImportResult
            {
                ImportId = importId,
                SystemId = systemId,
                PipelineId = pipelineId,
                PipelineRun = pipelineRun,
                SarifVersion = "2.1.0",
                FindingsImported = findingsImported,
                FindingsDeduplicated = findingsDeduplicated,
                Findings = allFindings,
                ControlMappings = controlMappings,
                UnmappedRuleCount = unmappedRules.Count,
                UnmappedFindingCount = unmappedRules.Sum(u => u.OccurrenceCount),
                UnmappedRules = unmappedRules,
                ParseErrors = parseErrors,
                ProcessedAt = DateTimeOffset.UtcNow,
            });
        }
        catch (PayloadValidationException) { throw; }
        catch (JsonException jex)
        {
            throw new PayloadValidationException(
                $"SARIF payload contains invalid JSON: {jex.Message}", jex);
        }
    }

    // ─── Per-result processor ────────────────────────────────────────────────

    private void ProcessResult(
        JsonElement result,
        Dictionary<string, SarifRuleDescriptor> ruleMap,
        string toolName,
        Guid systemId,
        string pipelineRun,
        HashSet<string> fingerprintsSeen,
        HashSet<string> existing,
        List<UnmappedRuleInfo> unmappedRules,
        Dictionary<string, IReadOnlyList<string>> controlMappings,
        List<SarifFindingDto> allFindings,
        ref int findingsImported,
        ref int findingsDeduplicated)
    {
        var ruleId = result.TryGetProperty("ruleId", out var rid)
            ? rid.GetString() ?? string.Empty
            : string.Empty;

        ruleMap.TryGetValue(ruleId, out var rule);

        var level = GetLevel(result);
        var securitySeverity = ParseSecuritySeverity(rule?.Properties);
        var catTier = DeriveCategory(level, securitySeverity);

        if (catTier is null) return; // Informational — discard

        ExtractLocation(result, out var locationUri, out var startLine);
        var fingerprint = ComputeFingerprint(result, systemId, ruleId, locationUri, startLine);

        if (!fingerprintsSeen.Add(fingerprint))
        {
            findingsDeduplicated++;
            return;
        }

        bool isNew;
        if (existing.Contains(fingerprint))
        {
            isNew = false;
            findingsDeduplicated++;
        }
        else
        {
            isNew = true;
            findingsImported++;
        }

        var message = result.TryGetProperty("message", out var msgEl)
            ? (msgEl.TryGetProperty("text", out var txtEl) ? txtEl.GetString() : null) ?? ruleId
            : ruleId;

        var tags = (IReadOnlyList<string>)(rule?.Tags ?? []);
        var (nistIds, cweIds, _) = ResolveNistControls(ruleId, rule, toolName);

        if (nistIds.Count == 0)
        {
            TrackUnmappedRule(ruleId, toolName, pipelineRun, tags, securitySeverity, unmappedRules);

            allFindings.Add(new SarifFindingDto
            {
                RuleId = ruleId,
                RuleDescription = rule?.ShortDescription ?? ruleId,
                Level = level,
                CvssScore = (decimal?)securitySeverity,
                CatTier = catTier.Value,
                NistControlIds = [],
                CweIds = cweIds,
                LocationUri = locationUri,
                LocationRegion = startLine.HasValue ? $"line {startLine}" : null,
                FingerprintHash = fingerprint,
                Message = message,
                Tags = tags,
                IsNew = isNew,
            });
        }
        else
        {
            foreach (var controlId in nistIds)
            {
                allFindings.Add(new SarifFindingDto
                {
                    RuleId = ruleId,
                    RuleDescription = rule?.ShortDescription ?? ruleId,
                    Level = level,
                    CvssScore = (decimal?)securitySeverity,
                    CatTier = catTier.Value,
                    NistControlIds = [controlId],
                    CweIds = cweIds,
                    LocationUri = locationUri,
                    LocationRegion = startLine.HasValue ? $"line {startLine}" : null,
                    FingerprintHash = fingerprint,
                    Message = message,
                    Tags = tags,
                    IsNew = isNew,
                });
            }

            foreach (var cwe in cweIds)
            {
                if (!controlMappings.ContainsKey(cwe))
                    controlMappings[cwe] = nistIds.AsReadOnly();
            }
        }
    }

    private void TrackUnmappedRule(
        string ruleId, string toolName, string pipelineRun,
        IReadOnlyList<string> tags, double? securitySeverity,
        List<UnmappedRuleInfo> unmappedRules)
    {
        var existing = unmappedRules.FirstOrDefault(u => u.RuleId == ruleId);
        if (existing is null)
        {
            unmappedRules.Add(new UnmappedRuleInfo(ruleId, toolName, 1, tags, securitySeverity));
            _logger.LogWarning(
                "SARIF rule could not be mapped to NIST 800-53. " +
                "RuleId={RuleId} Tool={ToolName} Run={PipelineRun} Tags={Tags}",
                ruleId, toolName, pipelineRun, string.Join(",", tags));
        }
        else
        {
            var idx = unmappedRules.IndexOf(existing);
            unmappedRules[idx] = existing with { OccurrenceCount = existing.OccurrenceCount + 1 };
        }
    }

    // ─── Private helpers ────────────────────────────────────────────────────

    private static string ExtractToolName(JsonElement run)
    {
        if (run.TryGetProperty("tool", out var tool) &&
            tool.TryGetProperty("driver", out var driver) &&
            driver.TryGetProperty("name", out var name))
            return name.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static Dictionary<string, SarifRuleDescriptor> BuildRuleMap(JsonElement run)
    {
        var map = new Dictionary<string, SarifRuleDescriptor>(StringComparer.OrdinalIgnoreCase);

        if (!run.TryGetProperty("tool", out var tool) ||
            !tool.TryGetProperty("driver", out var driver) ||
            !driver.TryGetProperty("rules", out var rules) ||
            rules.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var rule in rules.EnumerateArray())
        {
            var id = rule.TryGetProperty("id", out var rid) ? rid.GetString() : null;
            if (id is null) continue;

            var shortDesc = rule.TryGetProperty("shortDescription", out var sd)
                ? (sd.TryGetProperty("text", out var t) ? t.GetString() : null) : null;

            List<string>? nistProps = null;
            List<string>? tagList = null;
            Dictionary<string, object>? props = null;
            string? rawCwe = null;
            List<string>? relCwes = null;

            if (rule.TryGetProperty("properties", out var propsEl))
            {
                props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                if (propsEl.TryGetProperty("nist", out var nistEl))
                {
                    nistProps = nistEl.ValueKind == JsonValueKind.Array
                        ? nistEl.EnumerateArray()
                            .Where(e => e.ValueKind == JsonValueKind.String)
                            .Select(e => e.GetString()!)
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList()
                        : nistEl.ValueKind == JsonValueKind.String ? [nistEl.GetString()!] : null;
                }

                if (propsEl.TryGetProperty("tags", out var tagsEl) &&
                    tagsEl.ValueKind == JsonValueKind.Array)
                {
                    tagList = tagsEl.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .ToList();
                }

                if (propsEl.TryGetProperty("security-severity", out var ss))
                    props["security-severity"] = ss.ToString();

                if (propsEl.TryGetProperty("cwe", out var cweEl) &&
                    cweEl.ValueKind == JsonValueKind.String)
                    rawCwe = cweEl.GetString();
            }

            if (rule.TryGetProperty("relationships", out var rels) &&
                rels.ValueKind == JsonValueKind.Array)
            {
                foreach (var rel in rels.EnumerateArray())
                {
                    if (!rel.TryGetProperty("target", out var tgt)) continue;
                    if (!tgt.TryGetProperty("toolComponent", out var tc)) continue;
                    if (tc.TryGetProperty("name", out var tcName) &&
                        string.Equals(tcName.GetString(), "CWE", StringComparison.OrdinalIgnoreCase) &&
                        tgt.TryGetProperty("id", out var tid) &&
                        tid.GetString() is { Length: > 0 } tidStr)
                    {
                        relCwes ??= [];
                        var norm = tidStr.StartsWith("CWE-", StringComparison.OrdinalIgnoreCase)
                            ? tidStr : $"CWE-{tidStr}";
                        relCwes.Add(norm.ToUpperInvariant());
                    }
                }
            }

            map[id] = new SarifRuleDescriptor(id, shortDesc, nistProps, tagList, props, rawCwe, relCwes);
        }

        return map;
    }

    private static string GetLevel(JsonElement result)
    {
        return result.TryGetProperty("level", out var lvl)
            ? lvl.GetString()?.ToLowerInvariant() ?? "none"
            : "none";
    }

    private static double? ParseSecuritySeverity(Dictionary<string, object>? props)
    {
        if (props is null || !props.TryGetValue("security-severity", out var raw)) return null;
        var str = raw?.ToString()?.Trim();
        return double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }

    /// <summary>Oracle spec §5 — first-match-wins CAT tier derivation.</summary>
    private static CatSeverity? DeriveCategory(string level, double? cvss)
    {
        if (cvss >= 9.0 && level == "error")  return CatSeverity.CatI;
        if (level == "error" || cvss >= 7.0)  return CatSeverity.CatII;
        if (cvss >= 4.0 || level == "warning") return CatSeverity.CatIII;
        return null;
    }

    private static void ExtractLocation(JsonElement result, out string? locationUri, out int? startLine)
    {
        locationUri = null;
        startLine = null;

        if (!result.TryGetProperty("locations", out var locs) ||
            locs.ValueKind != JsonValueKind.Array) return;

        foreach (var loc in locs.EnumerateArray())
        {
            if (!loc.TryGetProperty("physicalLocation", out var phys)) continue;

            if (phys.TryGetProperty("artifactLocation", out var al) &&
                al.TryGetProperty("uri", out var uri))
                locationUri = uri.GetString();

            if (phys.TryGetProperty("region", out var region) &&
                region.TryGetProperty("startLine", out var sl))
                startLine = sl.GetInt32();

            return;
        }
    }

    private static string ComputeFingerprint(
        JsonElement result, Guid systemId, string ruleId, string? locationUri, int? startLine)
    {
        if (result.TryGetProperty("fingerprints", out var fps) &&
            fps.ValueKind == JsonValueKind.Object)
        {
            if (fps.TryGetProperty("primaryLocationLineHash/v1", out var p1) &&
                p1.GetString() is { Length: > 0 } s1) return s1;

            if (fps.TryGetProperty("partialFingerprints/v1", out var p2) &&
                p2.GetString() is { Length: > 0 } s2) return s2;
        }

        var raw = $"{systemId}|{ruleId}|{locationUri ?? string.Empty}|{startLine?.ToString(CultureInfo.InvariantCulture) ?? "0"}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private (List<string> nistIds, List<string> cweIds, string confidence) ResolveNistControls(
        string ruleId, SarifRuleDescriptor? rule, string toolName)
    {
        // Tier 1 — explicit nist property
        if (rule?.NistProperties is { Count: > 0 })
        {
            var validated = rule.NistProperties
                .Select(NormalizeControlId)
                .Where(id => NistControlIdPattern.IsMatch(id))
                .Distinct().ToList();

            if (validated.Count > 0)
                return (validated, ExtractCweIds(ruleId, rule), "HIGH-T1");
        }

        // Tier 2 — tag pattern matching
        if (rule?.Tags is { Count: > 0 })
        {
            var tier2 = new List<string>();
            foreach (var tag in rule.Tags)
            {
                foreach (var pat in SarifTagPatterns.Tier2Patterns)
                {
                    var m = pat.Match(tag);
                    if (!m.Success) continue;
                    var id = NormalizeControlId(m.Groups[1].Value);
                    if (NistControlIdPattern.IsMatch(id)) tier2.Add(id);
                    break;
                }
            }
            var d2 = tier2.Distinct().ToList();
            if (d2.Count > 0)
                return (d2, ExtractCweIds(ruleId, rule), "HIGH-T2");
        }

        // Tier 3 — CWE lookup
        var cweIds = ExtractCweIds(ruleId, rule);
        if (cweIds.Count > 0)
        {
            var tier3 = cweIds
                .Where(CweNistMappings.Map.ContainsKey)
                .SelectMany(cwe => CweNistMappings.Map[cwe])
                .Distinct().ToList();

            if (tier3.Count > 0)
                return (tier3, cweIds, "MEDIUM-T3");
        }

        // Tier 3b — tool heuristic
        var heuristic = ApplyToolHeuristic(ruleId, toolName);
        if (heuristic.Count > 0)
            return (heuristic, cweIds, "MEDIUM-H");

        return ([], cweIds, "UNMAPPED");
    }

    private static List<string> ExtractCweIds(string ruleId, SarifRuleDescriptor? rule)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in CweInRuleIdPattern.Matches(ruleId))
            set.Add($"CWE-{m.Groups[1].Value}");

        if (rule?.RelationshipCwes is { Count: > 0 })
            foreach (var c in rule.RelationshipCwes)
                set.Add(c.ToUpperInvariant());

        if (!string.IsNullOrEmpty(rule?.RawCweProperty))
        {
            var f = rule.RawCweProperty.StartsWith("CWE-", StringComparison.OrdinalIgnoreCase)
                ? rule.RawCweProperty : $"CWE-{rule.RawCweProperty}";
            set.Add(f.ToUpperInvariant());
        }

        if (rule?.Tags is { Count: > 0 })
            foreach (var tag in rule.Tags)
                foreach (Match m in SarifTagPatterns.CweInTag.Matches(tag))
                    set.Add($"CWE-{m.Groups[1].Value}");

        return [.. set];
    }

    private static List<string> ApplyToolHeuristic(string ruleId, string toolName)
    {
        var tl = toolName.ToLowerInvariant().Replace(" ", string.Empty);

        // CodeQL keyword heuristic
        if (tl.Contains("codeql"))
        {
            var ruleName = ruleId.Split('/')[^1];
            var keywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sql-injection"] = "CWE-89", ["sqli"] = "CWE-89",
                ["xss"] = "CWE-79", ["cross-site-scripting"] = "CWE-79",
                ["command-injection"] = "CWE-78",
                ["path-injection"] = "CWE-22", ["path-traversal"] = "CWE-22",
                ["ssrf"] = "CWE-918", ["xxe"] = "CWE-611",
                ["csrf"] = "CWE-352", ["deserialization"] = "CWE-502",
                ["hard-coded"] = "CWE-798", ["hardcoded"] = "CWE-798",
                ["credentials"] = "CWE-798", ["cleartext"] = "CWE-312",
                ["missing-auth"] = "CWE-306", ["improper-auth"] = "CWE-287",
                ["upload"] = "CWE-434",
            };

            foreach (var (keyword, cwe) in keywords)
            {
                if (ruleName.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                    CweNistMappings.Map.TryGetValue(cwe, out var controls))
                    return [.. controls.Distinct()];
            }
        }

        // Checkov rule overrides + provider heuristic
        if (tl.Contains("checkov") || tl.Contains("bridgecrew"))
        {
            var overrides = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["CKV_AZURE_36"] = ["SC-7"], ["CKV_AZURE_39"] = ["SC-7"],
                ["CKV_AZURE_5"]  = ["IA-2"], ["CKV_AZURE_8"]  = ["IA-2"],
                ["CKV_AZURE_13"] = ["SC-28"], ["CKV_AZURE_16"] = ["SC-28"],
                ["CKV_AZURE_41"] = ["AU-2", "AU-9"], ["CKV_AZURE_42"] = ["AU-2", "AU-9"],
            };

            if (overrides.TryGetValue(ruleId, out var ov)) return [.. ov];

            var m = Regex.Match(ruleId, @"^CKV2?_([A-Z]+)_\d+", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var defaults = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AZURE"] = ["CM-6", "SC-7", "SC-28"],
                    ["K8S"] = ["CM-6", "SC-7", "AC-2"],
                    ["DOCKER"] = ["CM-6", "SI-3"],
                    ["GCP"] = ["CM-6", "SC-7"],
                    ["TERRAFORM"] = ["CM-6"],
                };
                if (defaults.TryGetValue(m.Groups[1].Value.ToUpperInvariant(), out var def))
                    return [.. def];
            }
        }

        // MSDO CredScan override
        if ((tl.Contains("microsoft") || tl.Contains("msdo")) &&
            Regex.IsMatch(ruleId, @"^CS\d{3}", RegexOptions.IgnoreCase))
            return ["IA-5"];

        return [];
    }

    private static string NormalizeControlId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return id;
        var n = id.Trim().ToUpperInvariant();
        n = Regex.Replace(n, @"\s*\(\s*(\d+)\s*\)", "($1)");
        n = Regex.Replace(n, @"\.REV\.\d+$", string.Empty);
        return n;
    }
}

/// <summary>Internal SARIF rule descriptor — assembled from tool.driver.rules[].</summary>
internal sealed record SarifRuleDescriptor(
    string Id,
    string? ShortDescription,
    List<string>? NistProperties,
    List<string>? Tags,
    Dictionary<string, object>? Properties,
    string? RawCweProperty,
    List<string>? RelationshipCwes);
