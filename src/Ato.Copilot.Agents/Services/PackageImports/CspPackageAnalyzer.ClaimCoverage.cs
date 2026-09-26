using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private static void EnrichClaims(AnalysisSession session, IReadOnlySet<string>? eligible = null)
    {
        if (session.AnalysisProfileVersion < 2) return;
        foreach (var entry in session.Entries.Where(entry => eligible is null || eligible.Contains(entry.Key)))
        {
            session.Cancellation.ThrowIfCancellationRequested();
            var previousProfile = entry.AnalysisProfileVersion;
            entry.AnalysisProfileVersion = 2;
            if (entry.Status != CspPackageEntryStatus.Processed) continue;
            var segments = AnalysisSources(session, entry).ToArray();
            try
            {
                if (previousProfile < 2 && entry.HasRetainedProfile)
                    entry.Process(complete: false);
                if (previousProfile < 2 && entry.MediaType == "application/json")
                {
                    foreach (var segment in segments)
                    {
                        using var document = JsonDocument.Parse(segment.Text);
                        if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                        var kind = ParseKind(StringProperty(document.RootElement, "kind"));
                        if (kind is { } value && IsClaimKind(value))
                            ExtractJsonClaim(session, segment, value, document.RootElement);
                    }
                }
                ExtractLabeledClaims(session, entry, segments);
            }
            catch (BudgetExceededException exception) { entry.Fail(exception.Code, exception.Message); }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                entry.Fail("CLAIM_ANALYSIS_TIMEOUT", "Bounded claim matching timed out. Split the source or supply explicit structured declarations.");
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException)
            {
                entry.Set(CspPackageEntryStatus.Unreadable, "CLAIM_SOURCE_INVALID",
                    "A declaration cannot be represented with bounded, source-supported fields. Review the original and supply a corrected structured declaration.");
            }
        }
    }

    private static void CompleteFamilyCoverage(AnalysisSession session, IReadOnlySet<string>? eligible = null)
    {
        if (session.AnalysisProfileVersion < 2) return;
        foreach (var entry in session.Entries.OrderByDescending(entry => entry.Depth))
        {
            if (eligible is not null && !eligible.Contains(entry.Key) && entry.AnalysisProfileVersion < 2) continue;
            session.Cancellation.ThrowIfCancellationRequested();
            var sources = AnalysisSources(session, entry).ToArray();
            var drafts = session.Candidates.Where(candidate => candidate.Citations.Any(citation => citation.EntryKey == entry.Key)).ToArray();
            var children = session.Entries.Where(child => child.ParentKey == entry.Key).ToArray();
            entry.FamilyCoverage = Enum.GetValues<CspPackageClaimFamily>().Select(family =>
            {
                var status = entry.Status switch
                {
                    CspPackageEntryStatus.Unsupported => CspPackageFamilyCoverageStatus.Unsupported,
                    CspPackageEntryStatus.Unreadable or CspPackageEntryStatus.Failed => CspPackageFamilyCoverageStatus.Failed,
                    _ => CspPackageFamilyCoverageStatus.Unavailable
                };
                var reason = entry.Reason ?? "This claim family has not been fully analyzed.";
                var modelComplete = sources.Length > 0 && entry.FamilyAnalyzedSegmentKeys.TryGetValue(family, out var analyzed)
                    && sources.All(source => analyzed.Contains(source.Key));
                var deterministicComplete = entry.AnalysisComplete && entry.SemanticallyAnalyzedSegmentKeys.Count == 0;
                if (entry.Status is CspPackageEntryStatus.Processed or CspPackageEntryStatus.Excluded
                    && (deterministicComplete || modelComplete))
                {
                    status = drafts.Any(draft => ClaimFamily(draft.Kind) == family)
                        ? CspPackageFamilyCoverageStatus.Analyzed : CspPackageFamilyCoverageStatus.NoDeclarations;
                    reason = null;
                }
                var childCoverage = children.Select(child => child.FamilyCoverage.FirstOrDefault(value => value.Family == family)).ToArray();
                if (children.Length > 0 && entry.EnumerationComplete)
                {
                    if (childCoverage.Any(value => value is null || !FamilyComplete(value)))
                    {
                        status = CspPackageFamilyCoverageStatus.Unavailable;
                        reason = "Contained source has incomplete family coverage.";
                    }
                    else if (sources.Length == 0 && entry.Status == CspPackageEntryStatus.Processed)
                    {
                        status = childCoverage.Any(value => value!.Status == CspPackageFamilyCoverageStatus.Analyzed)
                            ? CspPackageFamilyCoverageStatus.Analyzed : CspPackageFamilyCoverageStatus.NoDeclarations;
                        reason = null;
                    }
                }
                return new CspPackageFamilyCoverage(family, status, reason);
            }).ToArray();
            if (entry.Status == CspPackageEntryStatus.Processed && entry.FamilyCoverage.Any(value => !FamilyComplete(value)))
                entry.Set(entry.Status, entry.ReasonCode ?? "CLAIM_FAMILY_ANALYSIS_UNAVAILABLE",
                    entry.Reason ?? "Some claim families remain unanalyzed. Retry with a compatible analyzer or review the retained source.", false);
        }
    }

    private static bool FamilyComplete(CspPackageFamilyCoverage coverage) =>
        coverage.Status is CspPackageFamilyCoverageStatus.Analyzed or CspPackageFamilyCoverageStatus.NoDeclarations;

    private static IReadOnlyList<CspPackageFamilyCoverage> ValidateSemanticFamilies(JsonElement root,
        IReadOnlyList<SemanticProposal> proposals, int profile)
    {
        if (profile < 2) return [];
        if (!root.TryGetProperty("familyCoverage", out var values) || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() != Enum.GetValues<CspPackageClaimFamily>().Length)
            throw new InvalidDataException("Profile 2 requires an explicit analysis result for every claim family.");
        var result = new List<CspPackageFamilyCoverage>();
        foreach (var value in values.EnumerateArray())
        {
            SemanticProperties(value, ["family", "status"]);
            var familyName = SemanticString(value, "family", 40, true)!;
            var statusName = SemanticString(value, "status", 40, true)!;
            if (!Enum.GetNames<CspPackageClaimFamily>().Contains(familyName, StringComparer.Ordinal)
                || statusName is not ("Analyzed" or "NoDeclarations"))
                throw new InvalidDataException("The semantic provider must explicitly analyze every family or leave the batch incomplete.");
            var family = Enum.Parse<CspPackageClaimFamily>(familyName);
            if (result.Any(item => item.Family == family)
                || statusName == "NoDeclarations" && proposals.Any(item => ClaimFamily(item.Draft.Kind) == family))
                throw new InvalidDataException("Family coverage is duplicate or contradicts the proposed declarations.");
            result.Add(new(family, Enum.Parse<CspPackageFamilyCoverageStatus>(statusName)));
        }
        return result;
    }
}
