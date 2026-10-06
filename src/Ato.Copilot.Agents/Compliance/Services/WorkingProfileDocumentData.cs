using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Saved working contributions for explicit previews only; never substitutes for approved export sources.</summary>
internal static class WorkingProfileDocumentData
{
    internal sealed record Section(string SectionId, ProfileSectionType Type, string GovernanceStatus,
        string ReviewScope, string Hash, JsonElement Content);

    internal static async Task<List<Section>> LoadAsync(AtoCopilotContext db, string systemId, List<string> gaps, CancellationToken ct)
    {
        var sections = await db.SystemProfileSections.AsNoTracking()
            .Include(s => s.UserCategories)
            .Include(s => s.DataTypeEntries)
            .Include(s => s.PpsEntries)
            .Include(s => s.LeveragedAuthorizations)
            .Where(s => s.RegisteredSystemId == systemId)
            .OrderBy(s => s.SectionType).ToListAsync(ct);
        var result = new List<Section>();
        foreach (var section in sections)
        {
            var users = section.UserCategories
                .Where(c => !(c.PendingDeletion && c.GovernanceStatus == SspSectionStatus.Approved))
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
            var reviewScope = section.SectionType == ProfileSectionType.UsersAndAccess ? "AccessContext" : "Section";
            if (section.GovernanceStatus != SspSectionStatus.Approved)
                gaps.Add($"Working profile {section.SectionType} {reviewScope}: {section.GovernanceStatus}; displayed content is not approved for final generation.");
            foreach (var row in users.Where(c => c.GovernanceStatus != SspSectionStatus.Approved))
                gaps.Add($"Working user category {row.Id}, revision {row.Revision}: {row.GovernanceStatus}" +
                    (row.PendingDeletion ? " (pending removal)" : "") + "; displayed content is not an approved user baseline.");
            var content = JsonSerializer.SerializeToElement(new
            {
                scalarContent = section.DraftContent,
                governanceStatus = section.GovernanceStatus.ToString(),
                reviewScope, lastEditedBy = section.LastEditedBy, lastEditedAt = Timestamp(section.LastEditedAt),
                approvedSnapshotId = section.ApprovedSnapshotId,
                userCategories = users.Select(c => new
                {
                    id = c.Id, categoryName = c.CategoryName, description = c.Description,
                    approximateCount = c.ApproximateCount, accessMethod = c.AccessMethod,
                    dataSensitivityLevel = c.DataSensitivityLevel, sortOrder = c.SortOrder,
                    identityType = c.IdentityType, privilegeLevel = c.PrivilegeLevel, affiliation = c.Affiliation,
                    authenticationMethod = c.AuthenticationMethod, responsibleOwner = c.ResponsibleOwner,
                    userLocations = c.UserLocations, permittedEnvironments = c.PermittedEnvironments, authorizedDataTypes = c.AuthorizedDataTypes,
                    governanceStatus = c.GovernanceStatus.ToString(), revision = c.Revision,
                    pendingDeletion = c.PendingDeletion, approvedSnapshotId = c.ApprovedSnapshotId,
                    submittedBy = c.SubmittedBy, submittedAt = Timestamp(c.SubmittedAt),
                    reviewedBy = c.ReviewedBy, reviewedAt = Timestamp(c.ReviewedAt), reviewerComments = c.ReviewerComments
                }),
                dataTypeEntries = section.DataTypeEntries.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(c => new
                {
                    id = c.Id, dataTypeName = c.DataTypeName, description = c.Description,
                    sensitivityClassification = c.SensitivityClassification, source = c.Source, destination = c.Destination,
                    applicableRegulations = c.ApplicableRegulations, sortOrder = c.SortOrder,
                    cuiCategory = c.CuiCategory, confidentialityImpact = c.ConfidentialityImpact, integrityImpact = c.IntegrityImpact,
                    availabilityImpact = c.AvailabilityImpact, privacyApplicability = c.PrivacyApplicability,
                    retentionRule = c.RetentionRule, disposalMethod = c.DisposalMethod, categorizationRationale = c.CategorizationRationale,
                    categorizationReference = c.CategorizationReference
                }),
                ppsEntries = section.PpsEntries.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(c => new
                {
                    id = c.Id, portOrRange = c.PortOrRange, protocol = c.Protocol, serviceName = c.ServiceName,
                    direction = c.Direction, justification = c.Justification, sortOrder = c.SortOrder
                }),
                leveragedAuthorizations = section.LeveragedAuthorizations.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(c => new
                {
                    id = c.Id, providerName = c.ProviderName, authorizationType = c.AuthorizationType,
                    authorizationDate = c.AuthorizationDate, coveredControlFamilies = c.CoveredControlFamilies, sortOrder = c.SortOrder
                })
            });
            result.Add(new(section.Id, section.SectionType, section.GovernanceStatus.ToString(), reviewScope,
                ApprovedProfileDocumentData.Hash(content.GetRawText()), content));
        }
        return result;
    }

    private static string? Timestamp(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("O") : null;
}
