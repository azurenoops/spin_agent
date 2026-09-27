using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>One projection of reviewed profile values for all existing document builders.</summary>
internal static class ApprovedProfileDocumentData
{
    internal static int DestinationSection(ProfileSectionType type) => type switch
    {
        ProfileSectionType.MissionAndPurpose => 1,
        ProfileSectionType.UsersAndAccess => 12,
        ProfileSectionType.EnvironmentAndDeployment => 6,
        ProfileSectionType.DataTypes => 2,
        ProfileSectionType.PortsProtocolsAndServices => 11,
        ProfileSectionType.LeveragedAuthorizations => 9,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    internal sealed record Section(string SectionId, string ApprovalId, string Hash, ProfileSectionType Type,
        string ReviewedBy, DateTime ReviewedAt, JsonElement Content);

    internal static async Task CaptureAsync(AtoCopilotContext db, SystemProfileSection section, ProfileAuditEntry audit, CancellationToken ct)
    {
        var users = await db.UserCategories.AsNoTracking().Where(x => x.SystemProfileSectionId == section.Id)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.CategoryName, x.Description, x.ApproximateCount, x.AccessMethod, x.DataSensitivityLevel, x.SortOrder }).ToListAsync(ct);
        var data = await db.DataTypeEntries.AsNoTracking().Where(x => x.SystemProfileSectionId == section.Id)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.DataTypeName, x.Description, x.SensitivityClassification, x.Source, x.Destination, x.ApplicableRegulations, x.SortOrder }).ToListAsync(ct);
        var ports = await db.PpsEntries.AsNoTracking().Where(x => x.SystemProfileSectionId == section.Id)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.PortOrRange, x.Protocol, x.ServiceName, x.Direction, x.Justification, x.SortOrder }).ToListAsync(ct);
        var authorizations = await db.LeveragedAuthorizations.AsNoTracking().Where(x => x.SystemProfileSectionId == section.Id)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.ProviderName, x.AuthorizationType, x.AuthorizationDate, x.CoveredControlFamilies, x.SortOrder }).ToListAsync(ct);
        audit.SnapshotJson = JsonSerializer.Serialize(new
        {
            scalarContent = section.DraftContent, userCategories = users, dataTypes = data,
            portsProtocolsServices = ports, leveragedAuthorizationReferences = authorizations
        });
        audit.SnapshotHash = Hash(audit.SnapshotJson);
        section.ApprovedSnapshotId = audit.Id;
    }

    internal static async Task<List<Section>> LoadAsync(AtoCopilotContext db, string systemId, List<string> gaps, CancellationToken ct)
    {
        var sections = await db.SystemProfileSections.AsNoTracking().Where(x => x.RegisteredSystemId == systemId)
            .OrderBy(x => x.SectionType).ToListAsync(ct);
        var ids = sections.Select(x => x.ApprovedSnapshotId).OfType<string>().ToArray();
        var approvals = await db.ProfileAuditEntries.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var result = new List<Section>();
        foreach (var section in sections)
        {
            if (section.ApprovedSnapshotId == null || !approvals.TryGetValue(section.ApprovedSnapshotId, out var audit)
                || audit.SystemProfileSectionId != section.Id || audit.Action != "Approved"
                || audit.SnapshotJson == null || Hash(audit.SnapshotJson) != audit.SnapshotHash)
            {
                gaps.Add($"Profile {section.SectionType}: retained approved scalar/child-row snapshot is unavailable. Review this section; current child rows cannot prove historical approval.");
                continue;
            }
            using var json = JsonDocument.Parse(audit.SnapshotJson);
            result.Add(new(section.Id, audit.Id, audit.SnapshotHash!, section.SectionType, audit.PerformedBy, audit.PerformedAt, json.RootElement.Clone()));
        }
        return result;
    }

    internal static string Render(Section section)
    {
        var text = new StringBuilder();
        text.AppendLine($"### Approved profile: {section.Type}");
        text.AppendLine($"Approval: {section.ApprovalId}; SHA-256: {section.Hash}");
        foreach (var property in section.Content.EnumerateObject())
        {
            if (property.Name == "scalarContent")
            {
                var scalar = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(scalar))
                {
                    try { using var parsed = JsonDocument.Parse(scalar); AppendValues(text, parsed.RootElement); }
                    catch (JsonException) { text.AppendLine(scalar); }
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
                foreach (var row in property.Value.EnumerateArray()) { text.AppendLine(); AppendValues(text, row); }
        }
        return text.ToString();
    }

    private static void AppendValues(StringBuilder text, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) { text.AppendLine(value.ToString()); return; }
        foreach (var property in value.EnumerateObject())
            if (property.Value.ValueKind != JsonValueKind.Null)
                text.AppendLine($"{property.Name}: {property.Value}");
    }

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
