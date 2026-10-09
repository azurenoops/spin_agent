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
    internal const string DataHandlingInterpretation = "Information handling values are recorded declarations. Profile review does not establish approved FIPS/SP800-60 CIA categorization, a privacy determination, CUI authority or an authorization decision. Verify the named source records separately.";
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

    internal static async Task CaptureAsync(AtoCopilotContext db, SystemProfileSection section, ProfileAuditEntry audit,
        CancellationToken ct, bool scalarApproval = true)
    {
        if (section.SectionType == ProfileSectionType.UsersAndAccess)
        {
            await CaptureUserCategoriesAsync(db, section, audit, scalarApproval, ct);
            return;
        }
        var users = await db.UserCategories.AsNoTracking().Where(x => x.SystemProfileSectionId == section.Id)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.CategoryName, x.Description, x.ApproximateCount, x.AccessMethod, x.DataSensitivityLevel, x.SortOrder,
                x.IdentityType, x.PrivilegeLevel, x.Affiliation, x.AuthenticationMethod, x.ResponsibleOwner,
                x.UserLocations, x.PermittedEnvironments, x.AuthorizedDataTypes }).ToListAsync(ct);
        var data = await db.DataTypeEntries.AsNoTracking().Where(x => x.SystemProfileSectionId == section.Id)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.DataTypeName, x.Description, x.SensitivityClassification, x.Source, x.Destination, x.ApplicableRegulations, x.SortOrder,
                x.CuiCategory, x.ConfidentialityImpact, x.IntegrityImpact, x.AvailabilityImpact, x.PrivacyApplicability,
                x.RetentionRule, x.DisposalMethod, x.CategorizationRationale, x.CategorizationReference }).ToListAsync(ct);
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
            if (section.SectionType == ProfileSectionType.UsersAndAccess)
            {
                if (!json.RootElement.TryGetProperty("individualUserCategoryReview", out var mode) || !mode.GetBoolean())
                {
                    gaps.Add("Profile UsersAndAccess: legacy section approval does not establish individual user-category approval; review each category.");
                    continue;
                }
                if (!json.RootElement.GetProperty("accessContextApproved").GetBoolean())
                    gaps.Add("Profile UsersAndAccess: scalar access context has not been independently approved.");
                var rows = await db.UserCategories.AsNoTracking().Where(c => c.SystemProfileSectionId == section.Id).ToListAsync(ct);
                if (!rows.Any(c => !c.PendingDeletion))
                    gaps.Add("Profile UsersAndAccess: no active user categories are recorded.");
                foreach (var row in rows.Where(c => c.ApprovedSnapshotId is null && !c.PendingDeletion))
                    gaps.Add($"Profile UsersAndAccess: category {row.Id} has no independently approved baseline; its working revision is excluded.");
            }
            result.Add(new(section.Id, audit.Id, audit.SnapshotHash!, section.SectionType, audit.PerformedBy, audit.PerformedAt, json.RootElement.Clone()));
        }
        return result;
    }

    private static async Task CaptureUserCategoriesAsync(
        AtoCopilotContext db, SystemProfileSection section, ProfileAuditEntry audit, bool scalarApproval, CancellationToken ct)
    {
        var rows = await db.UserCategories.Where(c => c.SystemProfileSectionId == section.Id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);
        var users = new List<JsonElement>();
        var approvalIds = new List<string>();
        foreach (var row in rows.Where(c => c.ApprovedSnapshotId is not null))
        {
            var approved = await db.ProfileAuditEntries.FindAsync([row.ApprovedSnapshotId!], ct);
            if (approved is null || approved.UserCategoryId != row.Id ||
                approved.SystemProfileSectionId != section.Id || approved.Action != "UserCategoryApproved" ||
                approved.SnapshotJson is null || Hash(approved.SnapshotJson) != approved.SnapshotHash)
                throw new InvalidOperationException("INVALID_APPROVED_SNAPSHOT: Retained user-category approval cannot be verified.");
            using var json = JsonDocument.Parse(approved.SnapshotJson);
            approvalIds.Add(approved.Id);
            if (!json.RootElement.GetProperty("PendingDeletion").GetBoolean())
                users.Add(json.RootElement.Clone());
        }
        var contextApproved = scalarApproval || section.ApprovedContent is not null;
        if (!contextApproved && section.ApprovedSnapshotId is not null)
        {
            var prior = await db.ProfileAuditEntries.FindAsync([section.ApprovedSnapshotId], ct);
            if (prior?.SnapshotJson is not null && Hash(prior.SnapshotJson) == prior.SnapshotHash)
            {
                using var previous = JsonDocument.Parse(prior.SnapshotJson);
                contextApproved = previous.RootElement.TryGetProperty("accessContextApproved", out var approved) && approved.GetBoolean();
            }
        }
        audit.SnapshotJson = JsonSerializer.Serialize(new
        {
            scalarContent = scalarApproval ? section.DraftContent : section.ApprovedContent,
            userCategories = users.OrderBy(c => c.GetProperty("SortOrder").GetInt32())
                .ThenBy(c => c.GetProperty("Id").GetString()).ToArray(),
            dataTypes = Array.Empty<object>(), portsProtocolsServices = Array.Empty<object>(),
            leveragedAuthorizationReferences = Array.Empty<object>(), individualUserCategoryReview = true,
            accessContextApproved = contextApproved,
            userCategoryApprovalIds = approvalIds
        });
        audit.SnapshotHash = Hash(audit.SnapshotJson);
        section.ApprovedSnapshotId = audit.Id;
    }

    internal static string Render(Section section)
    {
        var text = new StringBuilder();
        text.AppendLine(section.Type == ProfileSectionType.UsersAndAccess
            ? "### Retained UsersAndAccess baselines (independent category and access-context reviews)"
            : $"### Approved profile: {section.Type}");
        text.AppendLine($"{(section.Type == ProfileSectionType.UsersAndAccess ? "Source snapshot" : "Approval")}: {section.ApprovalId}; SHA-256: {section.Hash}");
        if (section.Type == ProfileSectionType.DataTypes)
            text.AppendLine(DataHandlingInterpretation);
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
            else if (property.Value.ValueKind == JsonValueKind.Array && property.Name != "userCategoryApprovalIds")
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
