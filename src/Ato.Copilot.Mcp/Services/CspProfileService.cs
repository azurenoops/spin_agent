using System.Text.Json;
using System.Text.Json.Serialization;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Loads CSP inheritance profiles from JSON files at startup and provides
/// profile listing and matching against a system's control baseline.
/// </summary>
public class CspProfileService
{
    private readonly List<CspProfile> _profiles = new();
    private readonly ILogger<CspProfileService> _logger;

    public CspProfileService(ILogger<CspProfileService> logger, IWebHostEnvironment env)
    {
        _logger = logger;

        var profileDir = Path.Combine(env.ContentRootPath, "..", "..", "src", "seed-data", "csp-profiles");
        if (!Directory.Exists(profileDir))
        {
            // Try relative from working directory
            profileDir = Path.Combine(Directory.GetCurrentDirectory(), "src", "seed-data", "csp-profiles");
        }

        if (Directory.Exists(profileDir))
        {
            foreach (var file in Directory.GetFiles(profileDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var profile = JsonSerializer.Deserialize<CspProfile>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (profile != null)
                    {
                        // If services[] is populated, flatten into Controls for backward compat
                        if (profile.Services.Count > 0)
                        {
                            profile.Controls = profile.Services
                                .SelectMany(s => s.Controls)
                                .ToList();
                        }

                        _profiles.Add(profile);
                        _logger.LogInformation(
                            "Loaded CSP profile: {Name} ({ServiceCount} services, {Count} controls)",
                            profile.Name, profile.Services.Count, profile.Controls.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load CSP profile from {File}", file);
                }
            }
        }
        else
        {
            _logger.LogWarning("CSP profile directory not found: {Dir}", profileDir);
        }
    }

    public IReadOnlyList<CspProfile> GetProfiles() => _profiles.AsReadOnly();

    public CspProfile? GetProfile(string profileId) =>
        _profiles.FirstOrDefault(p => p.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase));

    public ProfileMatchResult MatchProfile(CspProfile profile, IReadOnlyList<string> baselineControlIds,
        IReadOnlyDictionary<string, string> existingDesignations, string conflictResolution)
    {
        var matched = new List<ProfileControlMapping>();
        var unmatched = 0;
        var conflicts = 0;
        var willSetInherited = 0;
        var willSetShared = 0;
        var willSetCustomer = 0;
        var willSkip = 0;

        var baselineSet = new HashSet<string>(baselineControlIds, StringComparer.OrdinalIgnoreCase);

        foreach (var control in profile.Controls)
        {
            if (!baselineSet.Contains(control.ControlId))
            {
                unmatched++;
                continue;
            }

            var hasExisting = existingDesignations.TryGetValue(control.ControlId, out var existingType)
                && !string.IsNullOrEmpty(existingType) && existingType != "Undesignated";

            if (hasExisting)
            {
                conflicts++;
                if (conflictResolution == "skip")
                {
                    willSkip++;
                    continue;
                }
            }

            matched.Add(control);

            switch (control.InheritanceType.ToLowerInvariant())
            {
                case "inherited": willSetInherited++; break;
                case "shared": willSetShared++; break;
                case "customer": willSetCustomer++; break;
            }
        }

        return new ProfileMatchResult
        {
            ProfileName = profile.Name,
            MatchedControls = matched.Count + willSkip,
            UnmatchedControls = unmatched,
            WillSetInherited = willSetInherited,
            WillSetShared = willSetShared,
            WillSetCustomer = willSetCustomer,
            WillSkipExisting = willSkip,
            Conflicts = conflicts,
            MappingsToApply = matched
        };
    }

    // ─── DTOs ───────────────────────────────────────────────────────────────

    public class CspProfile
    {
        [JsonPropertyName("profileId")]
        public string ProfileId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("baselineLevel")]
        public string BaselineLevel { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("controls")]
        public List<ProfileControlMapping> Controls { get; set; } = new();

        [JsonPropertyName("services")]
        public List<CspService> Services { get; set; } = new();
    }

    public class CspService
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("controls")]
        public List<ProfileControlMapping> Controls { get; set; } = new();
    }

    public class ProfileControlMapping
    {
        [JsonPropertyName("controlId")]
        public string ControlId { get; set; } = string.Empty;

        [JsonPropertyName("inheritanceType")]
        public string InheritanceType { get; set; } = string.Empty;

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("customerResponsibility")]
        public string? CustomerResponsibility { get; set; }
    }

    public class ProfileMatchResult
    {
        public string ProfileName { get; set; } = string.Empty;
        public int MatchedControls { get; set; }
        public int UnmatchedControls { get; set; }
        public int WillSetInherited { get; set; }
        public int WillSetShared { get; set; }
        public int WillSetCustomer { get; set; }
        public int WillSkipExisting { get; set; }
        public int Conflicts { get; set; }
        public List<ProfileControlMapping> MappingsToApply { get; set; } = new();
    }
}
