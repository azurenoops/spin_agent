using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

public partial class SystemProfileService
{
    private static void ReplaceDraftChildren(
        AtoCopilotContext db, SystemProfileSection section, IReadOnlyList<JsonElement> rows)
    {
        switch (section.SectionType)
        {
            case ProfileSectionType.UsersAndAccess:
                ReplaceChildren(db, section, section.UserCategories, rows,
                    ["categoryName", "description", "approximateCount", "accessMethod", "dataSensitivityLevel",
                        "identityType", "privilegeLevel", "affiliation", "authenticationMethod", "responsibleOwner",
                        "userLocations", "permittedEnvironments", "authorizedDataTypes"]);
                break;
            case ProfileSectionType.DataTypes:
                ReplaceChildren(db, section, section.DataTypeEntries, rows,
                    ["dataTypeName", "description", "sensitivityClassification", "source", "destination", "applicableRegulations",
                        "cuiCategory", "confidentialityImpact", "integrityImpact", "availabilityImpact", "privacyApplicability",
                        "retentionRule", "disposalMethod", "categorizationRationale", "categorizationReference"]);
                break;
            case ProfileSectionType.PortsProtocolsAndServices:
                ReplaceChildren(db, section, section.PpsEntries, rows,
                    ["portOrRange", "protocol", "serviceName", "direction", "justification"]);
                break;
            case ProfileSectionType.LeveragedAuthorizations:
                ReplaceChildren(db, section, section.LeveragedAuthorizations, rows,
                    ["providerName", "authorizationType", "authorizationDate", "coveredControlFamilies"]);
                break;
            default:
                if (rows.Count != 0)
                    throw InvalidChild("This section does not support child items.");
                break;
        }
    }

    private static void ReplaceChildren<T>(
        AtoCopilotContext db, SystemProfileSection section, ICollection<T> existing,
        IReadOnlyList<JsonElement> rows, string[] fields) where T : class, new()
    {
        var existingById = existing.ToDictionary(item => (string)db.Entry(item).Property("Id").CurrentValue!);
        var submittedIds = new HashSet<string>(StringComparer.Ordinal);
        var replacements = new List<T>();
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object)
                throw InvalidChild("Each child item must be an object.");

            var parsed = new T();
            var entry = db.Entry(parsed);
            var seenFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in row.EnumerateObject())
            {
                if (!seenFields.Add(property.Name))
                    throw InvalidChild($"Duplicate field '{property.Name}'.");
                if (property.Name == "_tempId") continue;
                if (parsed is UserCategory user)
                {
                    if (property.Name == "revision")
                    {
                        if (property.Value.ValueKind != JsonValueKind.Number ||
                            !property.Value.TryGetInt32(out var revision) || revision < 1)
                            throw InvalidChild("revision must be a positive integer.");
                        user.Revision = revision;
                        continue;
                    }
                    if (property.Name is "governanceStatus" or "submittedBy" or "submittedAt" or "reviewedBy" or
                        "reviewedAt" or "reviewerComments" or "approvedSnapshotId" or "canSubmit" or "canWithdraw" or "canReview")
                        continue;
                    if (property.Name == "pendingDeletion")
                    {
                        if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            throw InvalidChild("pendingDeletion must be a boolean.");
                        user.PendingDeletion = property.Value.GetBoolean();
                        continue;
                    }
                }
                if (property.Name == "id")
                {
                    if (property.Value.ValueKind == JsonValueKind.Null) continue;
                    if (property.Value.ValueKind != JsonValueKind.String)
                        throw InvalidChild("id must be a string.");
                    var id = property.Value.GetString()!;
                    if (!existingById.ContainsKey(id) || !submittedIds.Add(id))
                        throw InvalidChild("Child ID is duplicate or does not belong to this section.");
                    entry.Property("Id").CurrentValue = id;
                    continue;
                }
                if (property.Name == "sortOrder")
                {
                    // The array is authoritative; accept only a valid integer hint from GET/UI.
                    if (property.Value.ValueKind != JsonValueKind.Number ||
                        !property.Value.TryGetInt32(out var order) || order < 0)
                        throw InvalidChild("sortOrder must be a nonnegative integer.");
                    continue;
                }
                if (!fields.Contains(property.Name, StringComparer.Ordinal))
                    throw InvalidChild($"Unsupported child field '{property.Name}'.");

                var member = entry.Property(char.ToUpperInvariant(property.Name[0]) + property.Name[1..]);
                var type = member.Metadata.ClrType;
                var value = property.Value;
                if (value.ValueKind == JsonValueKind.Null)
                {
                    member.CurrentValue = null;
                }
                else if (type == typeof(string) && value.ValueKind == JsonValueKind.String)
                {
                    member.CurrentValue = value.GetString();
                }
                else if (type == typeof(int?) && value.ValueKind == JsonValueKind.Number &&
                         value.TryGetInt32(out var count) && count >= 0)
                {
                    member.CurrentValue = count;
                }
                else if (type == typeof(DateTime?) && value.ValueKind == JsonValueKind.String)
                {
                    var date = value.GetString();
                    if (string.IsNullOrEmpty(date))
                        member.CurrentValue = null;
                    else if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                 DateTimeStyles.None, out var parsedDate) || value.TryGetDateTime(out parsedDate))
                        member.CurrentValue = parsedDate;
                    else
                        throw InvalidChild($"{property.Name} must be an ISO date.");
                }
                else
                {
                    throw InvalidChild($"Invalid field type or value for '{property.Name}'.");
                }
            }

            entry.Property("SystemProfileSectionId").CurrentValue = section.Id;
            entry.Property("TenantId").CurrentValue = section.TenantId;
            entry.Property("SortOrder").CurrentValue = replacements.Count;
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(parsed, new ValidationContext(parsed), errors, validateAllProperties: true))
                throw InvalidChild(string.Join(" ", errors.Select(error => error.ErrorMessage)));
            if (parsed is UserCategory category)
            {
                if (category.IdentityType is not (null or "" or "Human" or "WorkloadIdentity")
                    || category.PrivilegeLevel is not (null or "" or "Privileged" or "NonPrivileged")
                    || category.Affiliation is not (null or "" or "Internal" or "External"))
                    throw InvalidChild("Identity type, privilege and affiliation must use supported documentation classifications.");
                var old = existingById.GetValueOrDefault(category.Id) as UserCategory;
                if (old is not null)
                {
                    foreach (var field in new[] { "identityType", "privilegeLevel", "affiliation", "authenticationMethod",
                        "responsibleOwner", "userLocations", "permittedEnvironments", "authorizedDataTypes" })
                        if (!seenFields.Contains(field))
                        {
                            var name = char.ToUpperInvariant(field[0]) + field[1..];
                            entry.Property(name).CurrentValue = db.Entry(old).Property(name).CurrentValue;
                        }
                    if (!seenFields.Contains("revision") || category.Revision != old.Revision)
                        throw new InvalidOperationException("CONCURRENCY_CONFLICT: Refresh the category and include its current revision.");
                    if (!seenFields.Contains("pendingDeletion")) category.PendingDeletion = old.PendingDeletion;
                    PrepareUserCategoryUpdate(db, section, old, category);
                }
                else if (category.PendingDeletion)
                    throw InvalidChild("A new category cannot be a pending deletion.");
                else category.Revision = 1;
            }
            if (parsed is DataTypeEntry data)
            {
                if (existingById.GetValueOrDefault(data.Id) is DataTypeEntry old)
                    foreach (var field in new[] { "cuiCategory", "confidentialityImpact", "integrityImpact", "availabilityImpact",
                        "privacyApplicability", "retentionRule", "disposalMethod", "categorizationRationale", "categorizationReference" })
                        if (!seenFields.Contains(field))
                        {
                            var name = char.ToUpperInvariant(field[0]) + field[1..];
                            entry.Property(name).CurrentValue = db.Entry(old).Property(name).CurrentValue;
                        }
                if (new[] { data.ConfidentialityImpact, data.IntegrityImpact, data.AvailabilityImpact }
                    .Any(value => value is not (null or "" or "Low" or "Moderate" or "High" or "Undetermined"))
                    || data.PrivacyApplicability is not (null or "" or "NoPii" or "PiiApplies" or "ReviewRequired" or "Undetermined"))
                    throw InvalidChild("Declared CIA impacts and privacy applicability must use supported classifications.");
                if (data.CategorizationReference is { Length: > 0 } reference && (reference.Any(char.IsControl) || reference.Contains('\\')
                    || !(reference.StartsWith('/') && !reference.StartsWith("//")
                        || Uri.TryCreate(reference, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)))
                    throw InvalidChild("Categorization references require HTTPS or an application-relative source URL.");
            }
            replacements.Add(parsed);
        }

        // Validate the complete replacement before changing tracked rows; SaveDraft performs
        // the only SaveChanges, keeping scalar values, children and draft audit atomic.
        foreach (var item in existing.ToList())
        {
            var id = (string)db.Entry(item).Property("Id").CurrentValue!;
            if (!submittedIds.Contains(id))
            {
                if (item is UserCategory category)
                {
                    if (category.PendingDeletion) continue;
                    if (category.GovernanceStatus == SspSectionStatus.UnderReview)
                        throw new InvalidOperationException("INVALID_STATUS: Withdraw the category before removing it.");
                    var previous = category.GovernanceStatus;
                    category.PendingDeletion = true;
                    category.Revision++;
                    category.GovernanceStatus = SspSectionStatus.Draft;
                    AddUserCategoryAudit(db, section, category, "RemovalRequested", section.LastEditedBy!, previous);
                    if (category.ApprovedSnapshotId is not null) continue;
                }
                db.Remove(item);
                existing.Remove(item);
            }
        }
        foreach (var parsed in replacements)
        {
            var id = (string)db.Entry(parsed).Property("Id").CurrentValue!;
            if (existingById.TryGetValue(id, out var item))
                db.Entry(item).CurrentValues.SetValues(parsed);
            else
            {
                existing.Add(parsed);
                db.Add(parsed);
            }
        }
    }

    private static InvalidOperationException InvalidChild(string message) =>
        new($"INVALID_INPUT: {message}");

    private static void PrepareUserCategoryUpdate(
        AtoCopilotContext db, SystemProfileSection section, UserCategory old, UserCategory replacement)
    {
        if (old.PendingDeletion && old.GovernanceStatus == SspSectionStatus.Approved)
            throw InvalidChild("This category has been removed by review; create a new category instead.");
        // Array normalization may move an unchanged row when a sibling is added/removed.
        // Display order is not a business-content edit and must not reset or unlock its review.
        var changed = old.CategoryName != replacement.CategoryName ||
            (old.Description ?? "") != (replacement.Description ?? "") ||
            old.ApproximateCount != replacement.ApproximateCount ||
            (old.AccessMethod ?? "") != (replacement.AccessMethod ?? "") ||
            (old.DataSensitivityLevel ?? "") != (replacement.DataSensitivityLevel ?? "") ||
            (old.IdentityType ?? "") != (replacement.IdentityType ?? "") ||
            (old.PrivilegeLevel ?? "") != (replacement.PrivilegeLevel ?? "") ||
            (old.Affiliation ?? "") != (replacement.Affiliation ?? "") ||
            (old.AuthenticationMethod ?? "") != (replacement.AuthenticationMethod ?? "") ||
            (old.ResponsibleOwner ?? "") != (replacement.ResponsibleOwner ?? "") ||
            (old.UserLocations ?? "") != (replacement.UserLocations ?? "") ||
            (old.PermittedEnvironments ?? "") != (replacement.PermittedEnvironments ?? "") ||
            (old.AuthorizedDataTypes ?? "") != (replacement.AuthorizedDataTypes ?? "") ||
            old.PendingDeletion != replacement.PendingDeletion;
        if (changed && old.GovernanceStatus == SspSectionStatus.UnderReview)
            throw new InvalidOperationException("INVALID_STATUS: Withdraw the category before editing it.");
        replacement.GovernanceStatus = old.GovernanceStatus;
        replacement.ApprovedSnapshotId = old.ApprovedSnapshotId;
        replacement.SubmittedBy = old.SubmittedBy;
        replacement.SubmittedAt = old.SubmittedAt;
        replacement.ReviewedBy = old.ReviewedBy;
        replacement.ReviewedAt = old.ReviewedAt;
        replacement.ReviewerComments = old.ReviewerComments;
        if (!changed)
        {
            replacement.Description = old.Description;
            replacement.AccessMethod = old.AccessMethod;
            replacement.DataSensitivityLevel = old.DataSensitivityLevel;
            replacement.IdentityType = old.IdentityType;
            replacement.PrivilegeLevel = old.PrivilegeLevel;
            replacement.Affiliation = old.Affiliation;
            replacement.AuthenticationMethod = old.AuthenticationMethod;
            replacement.ResponsibleOwner = old.ResponsibleOwner;
            replacement.UserLocations = old.UserLocations;
            replacement.PermittedEnvironments = old.PermittedEnvironments;
            replacement.AuthorizedDataTypes = old.AuthorizedDataTypes;
            return;
        }
        replacement.Revision++;
        replacement.GovernanceStatus = SspSectionStatus.Draft;
        AddUserCategoryAudit(db, section, replacement, "Drafted", section.LastEditedBy!, old.GovernanceStatus);
    }
}
