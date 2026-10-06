using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Agents.Compliance.Services;

public partial class SystemProfileService
{
    private static bool EquivalentAccessContext(string? current, string? incoming)
    {
        if (current == incoming) return true;
        if (current is null || incoming is null) return false;
        try
        {
            return System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(current),
                System.Text.Json.Nodes.JsonNode.Parse(incoming));
        }
        catch (JsonException)
        {
            // Legacy free-text context remains supported; distinct text is a substantive change.
            return false;
        }
    }

    public async Task<UserCategoryReviewPermissions> GetUserCategoryReviewPermissionsAsync(
        string systemId, string userId, RmfRole? simulatedRole = null, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        if (!await db.RegisteredSystems.AnyAsync(s => s.Id == systemId && s.IsActive, cancellationToken))
            return new(false, false);
        return new(
            await HasRoleAsync(db, systemId, userId, [RmfRole.MissionOwner], simulatedRole, cancellationToken),
            await HasRoleAsync(db, systemId, userId, [RmfRole.Issm], simulatedRole, cancellationToken));
    }

    public async Task<SystemProfileSection> ReviewUserCategoryAsync(
        string systemId, string categoryId, string action, int expectedRevision, string userId,
        string? comments = null, RmfRole? simulatedRole = null, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await EnsureSystemExistsAsync(db, systemId, cancellationToken);
        if (action is not ("submit" or "withdraw" or "approve" or "request_revision") ||
            expectedRevision < 1 || comments?.Length > 2000)
            throw InvalidChild("Invalid review action, revision or comments.");
        await RequireRoleAsync(db, systemId, userId,
            action is "submit" or "withdraw" ? [RmfRole.MissionOwner] : [RmfRole.Issm],
            simulatedRole, cancellationToken);
        var section = await db.SystemProfileSections.Include(s => s.UserCategories)
            .SingleOrDefaultAsync(s => s.RegisteredSystemId == systemId &&
                s.SectionType == ProfileSectionType.UsersAndAccess, cancellationToken);
        var row = section?.UserCategories.SingleOrDefault(c => c.Id == categoryId &&
            !(c.PendingDeletion && c.GovernanceStatus == SspSectionStatus.Approved));
        if (section is null || row is null)
            throw new InvalidOperationException("CATEGORY_NOT_FOUND: User category not found.");
        if (row.Revision != expectedRevision)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Refresh the category before reviewing it.");
        if (action == "request_revision" && string.IsNullOrWhiteSpace(comments))
            throw InvalidChild("Comments are required when requesting revision.");
        var previous = row.GovernanceStatus;
        var allowed = action == "submit"
            ? previous is SspSectionStatus.Draft or SspSectionStatus.NeedsRevision
            : previous == SspSectionStatus.UnderReview;
        if (!allowed)
            throw new InvalidOperationException("INVALID_STATUS: This category is not eligible for the requested review action.");
        row.Revision++;
        row.GovernanceStatus = action switch
        {
            "submit" => SspSectionStatus.UnderReview,
            "withdraw" => SspSectionStatus.Draft,
            "approve" => SspSectionStatus.Approved,
            _ => SspSectionStatus.NeedsRevision
        };
        if (action == "submit")
        {
            row.SubmittedBy = userId;
            row.SubmittedAt = DateTime.UtcNow;
            row.ReviewerComments = null;
        }
        else if (action is "approve" or "request_revision")
        {
            row.ReviewedBy = userId;
            row.ReviewedAt = DateTime.UtcNow;
            row.ReviewerComments = comments;
        }
        var audit = AddUserCategoryAudit(db, section, row,
            action switch { "submit" => "Submitted", "withdraw" => "Withdrawn", "approve" => "Approved", _ => "RevisionRequested" },
            userId, previous, comments);
        if (action == "approve")
        {
            row.ApprovedSnapshotId = audit.Id;
            var aggregate = AddAuditEntry(db, section, "Approved", userId,
                section.GovernanceStatus, section.GovernanceStatus, "Independent user-category baseline projection; not section approval.");
            await ApprovedProfileDocumentData.CaptureAsync(db, section, aggregate, cancellationToken, scalarApproval: false);
        }
        // Serialize aggregate snapshot changes and simultaneous row actions through the parent
        // concurrency token, without granting approval to the parent or sibling rows.
        section.LastEditedAt = DateTime.UtcNow;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: The category or section changed. Refresh and try again.");
        }
        return section;
    }

    private static ProfileAuditEntry AddUserCategoryAudit(
        AtoCopilotContext db, SystemProfileSection section, UserCategory row, string action,
        string userId, SspSectionStatus previous, string? comments = null)
    {
        var audit = AddAuditEntry(db, section, "UserCategory" + action, userId, previous, row.GovernanceStatus, comments);
        audit.UserCategoryId = row.Id;
        audit.UserCategoryRevision = row.Revision;
        audit.SnapshotJson = JsonSerializer.Serialize(new
        {
            row.Id, row.CategoryName, row.Description, row.ApproximateCount, row.AccessMethod,
            row.IdentityType, row.PrivilegeLevel, row.Affiliation, row.AuthenticationMethod, row.ResponsibleOwner,
            row.UserLocations, row.PermittedEnvironments, row.AuthorizedDataTypes,
            row.DataSensitivityLevel, row.SortOrder, row.PendingDeletion, row.Revision
        });
        audit.SnapshotHash = ApprovedProfileDocumentData.Hash(audit.SnapshotJson);
        return audit;
    }

    private static SspSectionStatus AggregateProfileStatus(SystemProfileSection section)
    {
        if (section.SectionType != ProfileSectionType.UsersAndAccess)
            return section.GovernanceStatus;
        var states = section.UserCategories
            .Where(c => !(c.PendingDeletion && c.GovernanceStatus == SspSectionStatus.Approved))
            .Select(c => c.GovernanceStatus).Append(section.GovernanceStatus).ToList();
        if (states.Contains(SspSectionStatus.NeedsRevision)) return SspSectionStatus.NeedsRevision;
        if (!section.UserCategories.Any(c => !c.PendingDeletion) ||
            states.Any(s => s is SspSectionStatus.Draft or SspSectionStatus.NotStarted))
            return SspSectionStatus.Draft;
        return states.Contains(SspSectionStatus.UnderReview) ? SspSectionStatus.UnderReview : SspSectionStatus.Approved;
    }
}
