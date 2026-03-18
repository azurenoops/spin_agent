using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Produces OSCAL 1.1.2 SSP JSON from entity data (Feature 022).
/// </summary>
public class OscalSspExportService : IOscalSspExportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OscalSspExportService> _logger;

    // ─── Static OSCAL Constants ──────────────────────────────────────────────

    private static readonly JsonSerializerOptions PrettyOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
    };

    private static readonly JsonSerializerOptions CompactOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
    };

    internal const string ProfileUriLow =
        "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_LOW-baseline_profile.json";
    internal const string ProfileUriModerate =
        "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_MODERATE-baseline_profile.json";
    internal const string ProfileUriHigh =
        "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_HIGH-baseline_profile.json";

    internal const string OscalVersion = "1.1.2";

    public OscalSspExportService(
        IServiceScopeFactory scopeFactory,
        ILogger<OscalSspExportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OscalExportResult> ExportAsync(
        string registeredSystemId,
        bool includeBackMatter = true,
        bool prettyPrint = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredSystemId, nameof(registeredSystemId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var warnings = new List<string>();

        // Load all entity data
        var system = await db.RegisteredSystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == registeredSystemId, cancellationToken)
            ?? throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{registeredSystemId}' not found.");

        var categorization = await db.SecurityCategorizations
            .AsNoTracking()
            .Include(sc => sc.InformationTypes)
            .FirstOrDefaultAsync(sc => sc.RegisteredSystemId == registeredSystemId, cancellationToken);

        var baseline = await db.ControlBaselines
            .AsNoTracking()
            .Include(b => b.Inheritances)
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == registeredSystemId, cancellationToken);

        var implementations = await db.ControlImplementations
            .AsNoTracking()
            .Where(ci => ci.RegisteredSystemId == registeredSystemId)
            .OrderBy(ci => ci.ControlId)
            .ToListAsync(cancellationToken);

        var roles = await db.RmfRoleAssignments
            .AsNoTracking()
            .Where(r => r.RegisteredSystemId == registeredSystemId && r.IsActive)
            .ToListAsync(cancellationToken);

        var boundaries = await db.AuthorizationBoundaries
            .AsNoTracking()
            .Where(b => b.RegisteredSystemId == registeredSystemId && b.IsInBoundary)
            .ToListAsync(cancellationToken);

        var interconnections = await db.SystemInterconnections
            .AsNoTracking()
            .Include(ic => ic.Agreements)
            .Where(ic => ic.RegisteredSystemId == registeredSystemId && ic.Status != InterconnectionStatus.Terminated)
            .ToListAsync(cancellationToken);

        var sspSections = await db.SspSections
            .AsNoTracking()
            .Where(s => s.RegisteredSystemId == registeredSystemId)
            .ToListAsync(cancellationToken);

        var contingencyPlan = await db.ContingencyPlanReferences
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RegisteredSystemId == registeredSystemId, cancellationToken);

        // Approved deviations for deviation props and back-matter resources (Feature 035)
        var deviations = await db.Deviations
            .AsNoTracking()
            .Where(d => d.RegisteredSystemId == registeredSystemId
                && d.Status == DeviationStatus.Approved)
            .ToListAsync(cancellationToken);

        // Build component UUID map for cross-references
        var componentMap = new Dictionary<string, string>(); // ResourceId → UUID
        foreach (var b in boundaries)
            componentMap[b.ResourceId] = Guid.NewGuid().ToString();

        // Build party UUID map for role → party cross-references
        var partyMap = new Dictionary<string, string>(); // UserId → UUID
        foreach (var r in roles)
            if (!partyMap.ContainsKey(r.UserId))
                partyMap[r.UserId] = Guid.NewGuid().ToString();

        var sectionMap = sspSections.ToDictionary(s => s.SectionNumber);

        // Build each OSCAL section
        var metadata = BuildMetadata(system, roles, partyMap, warnings);
        var importProfile = BuildImportProfile(baseline, warnings);
        var systemChars = BuildSystemCharacteristics(system, categorization, sectionMap, warnings);
        var systemImpl = BuildSystemImplementation(boundaries, roles, baseline, componentMap, partyMap, warnings);
        var controlImpl = BuildControlImplementation(system, implementations, baseline, componentMap, deviations, warnings);

        Dictionary<string, object>? backMatter = null;
        var backMatterCount = 0;
        if (includeBackMatter)
        {
            backMatter = BuildBackMatter(interconnections, contingencyPlan, deviations, warnings);
            if (backMatter.TryGetValue("resources", out var resources) && resources is List<Dictionary<string, object>> resList)
                backMatterCount = resList.Count;
        }

        // Assemble top-level structure
        var ssp = new Dictionary<string, object>
        {
            ["uuid"] = Guid.NewGuid().ToString(),
            ["metadata"] = metadata,
            ["import-profile"] = importProfile,
            ["system-characteristics"] = systemChars,
            ["system-implementation"] = systemImpl,
            ["control-implementation"] = controlImpl
        };

        if (backMatter != null)
            ssp["back-matter"] = backMatter;

        var root = new Dictionary<string, object>
        {
            ["system-security-plan"] = ssp
        };

        var opts = prettyPrint ? PrettyOpts : CompactOpts;
        var json = JsonSerializer.Serialize(root, opts);

        var stats = new OscalStatistics(
            ControlCount: implementations.Count,
            ComponentCount: boundaries.Count,
            InventoryItemCount: boundaries.Count,
            UserCount: roles.Count,
            BackMatterResourceCount: backMatterCount);

        _logger.LogInformation(
            "Exported OSCAL 1.1.2 SSP for system '{SystemId}': {Controls} controls, {Components} components, {Warnings} warnings",
            registeredSystemId, implementations.Count, boundaries.Count, warnings.Count);

        return new OscalExportResult(json, warnings, stats);
    }

    // ─── OSCAL Section Builders ──────────────────────────────────────────────

    internal static Dictionary<string, object> BuildMetadata(
        RegisteredSystem system,
        List<RmfRoleAssignment> roles,
        Dictionary<string, string> partyMap,
        List<string> warnings)
    {
        var metadata = new Dictionary<string, object>
        {
            ["title"] = $"{system.Name} System Security Plan",
            ["last-modified"] = DateTime.UtcNow.ToString("o"),
            ["version"] = "1.0",
            ["oscal-version"] = OscalVersion
        };

        if (roles.Count == 0)
        {
            warnings.Add("No RMF role assignments found. Metadata roles, parties, and responsible-parties are empty.");
            metadata["roles"] = Array.Empty<object>();
            metadata["parties"] = Array.Empty<object>();
            metadata["responsible-parties"] = Array.Empty<object>();
            return metadata;
        }

        // Roles
        var oscalRoles = roles
            .Select(r => r.RmfRole)
            .Distinct()
            .Select(role => new Dictionary<string, object>
            {
                ["id"] = MapRoleId(role),
                ["title"] = MapRoleTitle(role)
            })
            .ToList();
        metadata["roles"] = oscalRoles;

        // Parties
        var parties = partyMap
            .Select(kvp =>
            {
                var role = roles.First(r => r.UserId == kvp.Key);
                var party = new Dictionary<string, object>
                {
                    ["uuid"] = kvp.Value,
                    ["type"] = "person"
                };
                if (role.UserDisplayName != null)
                    party["name"] = role.UserDisplayName;
                return party;
            })
            .ToList();
        metadata["parties"] = parties;

        // Responsible parties
        var responsibleParties = roles
            .GroupBy(r => MapRoleId(r.RmfRole))
            .Select(g => new Dictionary<string, object>
            {
                ["role-id"] = g.Key,
                ["party-uuids"] = g.Select(r => partyMap[r.UserId]).Distinct().ToList()
            })
            .ToList();
        metadata["responsible-parties"] = responsibleParties;

        return metadata;
    }

    internal static Dictionary<string, object> BuildImportProfile(
        ControlBaseline? baseline,
        List<string> warnings)
    {
        var importProfile = new Dictionary<string, object>();

        if (baseline == null)
        {
            warnings.Add("No control baseline found. Import-profile href is omitted.");
            return importProfile;
        }

        var href = baseline.BaselineLevel?.ToLowerInvariant() switch
        {
            "low" => ProfileUriLow,
            "moderate" => ProfileUriModerate,
            "high" => ProfileUriHigh,
            _ => null
        };

        if (href != null)
        {
            importProfile["href"] = href;
        }
        else
        {
            warnings.Add($"Unrecognized baseline level '{baseline.BaselineLevel}'. Import-profile href is omitted.");
        }

        return importProfile;
    }

    internal static Dictionary<string, object> BuildSystemCharacteristics(
        RegisteredSystem system,
        SecurityCategorization? categorization,
        Dictionary<int, SspSection> sectionMap,
        List<string> warnings)
    {
        var sc = new Dictionary<string, object>
        {
            ["system-name"] = system.Name,
            ["description"] = system.Description ?? "System description not provided."
        };

        if (!string.IsNullOrWhiteSpace(system.Acronym))
            sc["system-name-short"] = system.Acronym;

        // Security sensitivity level
        if (categorization != null)
        {
            sc["security-sensitivity-level"] = categorization.OverallCategorization.ToString().ToLowerInvariant();

            // System information with information types
            if (categorization.InformationTypes.Count > 0)
            {
                sc["system-information"] = new Dictionary<string, object>
                {
                    ["information-types"] = categorization.InformationTypes.Select(it => new Dictionary<string, object>
                    {
                        ["uuid"] = Guid.NewGuid().ToString(),
                        ["title"] = it.Name,
                        ["description"] = $"SP 800-60 ID: {it.Sp80060Id}, Category: {it.Category ?? "N/A"}",
                        ["categorization-ids"] = new[] { it.Sp80060Id },
                        ["confidentiality-impact"] = new Dictionary<string, string>
                        {
                            ["base"] = it.ConfidentialityImpact.ToString().ToLowerInvariant()
                        },
                        ["integrity-impact"] = new Dictionary<string, string>
                        {
                            ["base"] = it.IntegrityImpact.ToString().ToLowerInvariant()
                        },
                        ["availability-impact"] = new Dictionary<string, string>
                        {
                            ["base"] = it.AvailabilityImpact.ToString().ToLowerInvariant()
                        }
                    }).ToList()
                };
            }

            // Security impact level
            sc["security-impact-level"] = new Dictionary<string, string>
            {
                ["security-objective-confidentiality"] = categorization.ConfidentialityImpact.ToString().ToLowerInvariant(),
                ["security-objective-integrity"] = categorization.IntegrityImpact.ToString().ToLowerInvariant(),
                ["security-objective-availability"] = categorization.AvailabilityImpact.ToString().ToLowerInvariant()
            };
        }
        else
        {
            warnings.Add("No security categorization found. Using placeholder values.");
            sc["security-sensitivity-level"] = "not-yet-determined";
            sc["security-impact-level"] = new Dictionary<string, string>
            {
                ["security-objective-confidentiality"] = "low",
                ["security-objective-integrity"] = "low",
                ["security-objective-availability"] = "low"
            };
        }

        // Authorization boundary from §11
        if (sectionMap.TryGetValue(11, out var s11) && !string.IsNullOrWhiteSpace(s11.Content))
            sc["authorization-boundary"] = new Dictionary<string, string> { ["description"] = s11.Content };
        else
            sc["authorization-boundary"] = new Dictionary<string, string> { ["description"] = "Authorization boundary not yet defined." };

        // Network architecture from §6
        if (sectionMap.TryGetValue(6, out var s6) && !string.IsNullOrWhiteSpace(s6.Content))
            sc["network-architecture"] = new Dictionary<string, string> { ["description"] = s6.Content };

        // Data flow from §7
        if (sectionMap.TryGetValue(7, out var s7) && !string.IsNullOrWhiteSpace(s7.Content))
            sc["data-flow"] = new Dictionary<string, string> { ["description"] = s7.Content };

        // Status
        sc["status"] = new Dictionary<string, string>
        {
            ["state"] = system.OperationalStatus switch
            {
                OperationalStatus.Operational => "operational",
                OperationalStatus.UnderDevelopment => "under-development",
                OperationalStatus.Disposed => "disposition",
                OperationalStatus.MajorModification => "under-major-modification",
                _ => "operational"
            }
        };

        return sc;
    }

    internal static Dictionary<string, object> BuildSystemImplementation(
        List<AuthorizationBoundary> boundaries,
        List<RmfRoleAssignment> roles,
        ControlBaseline? baseline,
        Dictionary<string, string> componentMap,
        Dictionary<string, string> partyMap,
        List<string> warnings)
    {
        var si = new Dictionary<string, object>();

        // Users from RmfRoleAssignment
        if (roles.Count > 0)
        {
            si["users"] = roles.Select(r => new Dictionary<string, object>
            {
                ["uuid"] = partyMap.TryGetValue(r.UserId, out var pid) ? pid : Guid.NewGuid().ToString(),
                ["role-ids"] = new[] { MapRoleId(r.RmfRole) },
                ["props"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["name"] = "privilege-level",
                        ["value"] = r.RmfRole == RmfRole.AuthorizingOfficial ? "privileged" : "non-privileged"
                    }
                }
            }).ToList();
        }
        else
        {
            si["users"] = Array.Empty<object>();
        }

        // Components from AuthorizationBoundary resources
        if (boundaries.Count > 0)
        {
            si["components"] = boundaries.Select(b => new Dictionary<string, object>
            {
                ["uuid"] = componentMap.TryGetValue(b.ResourceId, out var cid) ? cid : Guid.NewGuid().ToString(),
                ["type"] = MapComponentType(b.ResourceType),
                ["title"] = b.ResourceName ?? b.ResourceId,
                ["description"] = $"Azure resource {b.ResourceType}: {b.ResourceName ?? b.ResourceId}",
                ["status"] = new Dictionary<string, string> { ["state"] = "operational" },
                ["props"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["name"] = "asset-id",
                        ["value"] = b.ResourceId
                    }
                }
            }).ToList();

            // Inventory items
            si["inventory-items"] = boundaries.Select(b => new Dictionary<string, object>
            {
                ["uuid"] = Guid.NewGuid().ToString(),
                ["description"] = $"{b.ResourceName ?? b.ResourceId} ({b.ResourceType})",
                ["implemented-components"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["component-uuid"] = componentMap.TryGetValue(b.ResourceId, out var cid) ? cid : ""
                    }
                }
            }).ToList();
        }
        else
        {
            si["components"] = Array.Empty<object>();
            si["inventory-items"] = Array.Empty<object>();
        }

        // Leveraged authorizations from ControlInheritance providers
        if (baseline?.Inheritances != null)
        {
            var providers = baseline.Inheritances
                .Where(i => i.InheritanceType == InheritanceType.Inherited && !string.IsNullOrWhiteSpace(i.Provider))
                .Select(i => i.Provider!)
                .Distinct()
                .ToList();

            if (providers.Count > 0)
            {
                si["leveraged-authorizations"] = providers.Select(p => new Dictionary<string, object>
                {
                    ["uuid"] = Guid.NewGuid().ToString(),
                    ["title"] = $"{p} FedRAMP Authorization",
                    ["party-uuid"] = Guid.NewGuid().ToString(),
                    ["date-authorized"] = DateTime.UtcNow.ToString("yyyy-MM-dd")
                }).ToList();
            }
        }

        return si;
    }

    internal static Dictionary<string, object> BuildControlImplementation(
        RegisteredSystem system,
        List<ControlImplementation> implementations,
        ControlBaseline? baseline,
        Dictionary<string, string> componentMap,
        List<Deviation> deviations,
        List<string> warnings)
    {
        var ci = new Dictionary<string, object>
        {
            ["description"] = $"Control implementation narratives for {system.Name}."
        };

        if (implementations.Count == 0)
        {
            warnings.Add("No control implementation narratives found. Implemented-requirements is empty.");
            ci["implemented-requirements"] = Array.Empty<object>();
            return ci;
        }

        // Build inheritance map for responsible-roles
        var inheritanceMap = baseline?.Inheritances
            ?.ToDictionary(i => i.ControlId, i => i, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ControlInheritance>(StringComparer.OrdinalIgnoreCase);

        // Build deviation lookup by control ID (Feature 035)
        var devByControl = deviations
            .GroupBy(d => d.ControlId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        ci["implemented-requirements"] = implementations.Select(impl =>
        {
            var props = new List<Dictionary<string, string>>
            {
                new()
                {
                    ["name"] = "implementation-status",
                    ["value"] = impl.ImplementationStatus switch
                    {
                        ImplementationStatus.Implemented => "implemented",
                        ImplementationStatus.PartiallyImplemented => "partial",
                        ImplementationStatus.Planned => "planned",
                        ImplementationStatus.NotApplicable => "not-applicable",
                        _ => "planned"
                    }
                }
            };

            // Add deviation prop if control has an active deviation
            if (devByControl.TryGetValue(impl.ControlId, out var dev))
            {
                props.Add(new Dictionary<string, string>
                {
                    ["name"] = "deviation-type",
                    ["value"] = dev.DeviationType switch
                    {
                        DeviationType.FalsePositive => "false-positive",
                        DeviationType.RiskAcceptance => "risk-acceptance",
                        DeviationType.Waiver => "waiver",
                        _ => "risk-acceptance"
                    },
                    ["ns"] = "https://ato-copilot.azurenoops.io/ns/oscal"
                });
            }

            var req = new Dictionary<string, object>
            {
                ["uuid"] = Guid.NewGuid().ToString(),
                ["control-id"] = impl.ControlId.ToLowerInvariant(),
                ["description"] = impl.Narrative ?? "Not documented.",
                ["props"] = props.ToArray()
            };

            // Add responsible-roles from inheritance
            if (inheritanceMap.TryGetValue(impl.ControlId, out var inh))
            {
                req["responsible-roles"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["role-id"] = inh.InheritanceType switch
                        {
                            InheritanceType.Inherited => "provider",
                            InheritanceType.Shared => "shared",
                            _ => "customer"
                        }
                    }
                };
            }

            // Add by-components cross-references
            if (componentMap.Count > 0)
            {
                req["by-components"] = componentMap.Values.Take(1).Select(uuid => new Dictionary<string, object>
                {
                    ["component-uuid"] = uuid,
                    ["uuid"] = Guid.NewGuid().ToString(),
                    ["description"] = impl.Narrative ?? "Not documented."
                }).ToList();
            }

            return req;
        }).ToList();

        return ci;
    }

    internal static Dictionary<string, object> BuildBackMatter(
        List<SystemInterconnection> interconnections,
        ContingencyPlanReference? contingencyPlan,
        List<Deviation> deviations,
        List<string> warnings)
    {
        var resources = new List<Dictionary<string, object>>();

        // ISA/MOU from InterconnectionAgreement
        foreach (var ic in interconnections)
        {
            foreach (var agreement in ic.Agreements.Where(a => !string.IsNullOrWhiteSpace(a.DocumentReference)))
            {
                resources.Add(new Dictionary<string, object>
                {
                    ["uuid"] = Guid.NewGuid().ToString(),
                    ["title"] = agreement.Title ?? $"ISA — {ic.TargetSystemName}",
                    ["description"] = $"Interconnection agreement for {ic.TargetSystemName} ({agreement.AgreementType})",
                    ["rlinks"] = new[]
                    {
                        new Dictionary<string, string>
                        {
                            ["href"] = agreement.DocumentReference!,
                            ["media-type"] = "application/pdf"
                        }
                    }
                });
            }
        }

        // Contingency plan reference
        if (contingencyPlan != null && !string.IsNullOrWhiteSpace(contingencyPlan.DocumentLocation))
        {
            resources.Add(new Dictionary<string, object>
            {
                ["uuid"] = Guid.NewGuid().ToString(),
                ["title"] = contingencyPlan.DocumentTitle ?? "Information System Contingency Plan",
                ["description"] = $"Contingency plan v{contingencyPlan.DocumentVersion ?? "1.0"}",
                ["rlinks"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["href"] = contingencyPlan.DocumentLocation,
                        ["media-type"] = "application/pdf"
                    }
                }
            });
        }

        // Deviation resources (Feature 035)
        foreach (var dev in deviations)
        {
            var typeLabel = dev.DeviationType switch
            {
                DeviationType.FalsePositive => "False Positive",
                DeviationType.RiskAcceptance => "Risk Acceptance",
                DeviationType.Waiver => "Waiver",
                _ => "Deviation"
            };

            resources.Add(new Dictionary<string, object>
            {
                ["uuid"] = dev.Id,
                ["title"] = $"{typeLabel}: {dev.ControlId}",
                ["description"] = dev.Justification,
                ["props"] = new object[]
                {
                    new Dictionary<string, string> { ["name"] = "deviation-type", ["value"] = typeLabel.ToLowerInvariant().Replace(' ', '-') },
                    new Dictionary<string, string> { ["name"] = "severity", ["value"] = dev.CatSeverity.ToString() },
                    new Dictionary<string, string> { ["name"] = "expiration-date", ["value"] = dev.ExpirationDate.ToString("yyyy-MM-dd") },
                    new Dictionary<string, string> { ["name"] = "reviewed-by", ["value"] = dev.ReviewedBy ?? "" },
                    new Dictionary<string, string> { ["name"] = "compensating-controls", ["value"] = dev.CompensatingControls ?? "" }
                }
            });
        }

        return new Dictionary<string, object>
        {
            ["resources"] = resources
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string MapRoleId(RmfRole role) => role switch
    {
        RmfRole.AuthorizingOfficial => "authorizing-official",
        RmfRole.Issm => "information-system-security-manager",
        RmfRole.Isso => "information-system-security-officer",
        RmfRole.Sca => "security-control-assessor",
        RmfRole.SystemOwner => "system-owner",
        _ => role.ToString().ToLowerInvariant()
    };

    private static string MapRoleTitle(RmfRole role) => role switch
    {
        RmfRole.AuthorizingOfficial => "Authorizing Official",
        RmfRole.Issm => "Information System Security Manager",
        RmfRole.Isso => "Information System Security Officer",
        RmfRole.Sca => "Security Control Assessor",
        RmfRole.SystemOwner => "System Owner",
        _ => role.ToString()
    };

    private static string MapComponentType(string azureResourceType) =>
        azureResourceType.ToLowerInvariant() switch
        {
            var t when t.Contains("virtualmachines") => "this-system",
            var t when t.Contains("webapp") || t.Contains("appservice") => "software",
            var t when t.Contains("database") || t.Contains("sql") => "software",
            var t when t.Contains("network") || t.Contains("firewall") => "interconnection",
            var t when t.Contains("storage") => "software",
            _ => "this-system"
        };
}
