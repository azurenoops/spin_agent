using System.Text;
using System.Text.RegularExpressions;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private static readonly Regex ClaimLabels = LabeledRegex(
        @"\b(?<label>Reference\s+ID|Title|Subject\s+kind|Subject|Issuing\s+authority|Decision\s+type|" +
        @"Decision\s+date|Expiration\s+date|Scope|Conditions|Exclusions|Assessor|Assessment\s+date|" +
        @"Synthetic\s+review\s+date|Review\s+date|Related\s+component|Related\s+capability|Related\s+finding|" +
        @"Control(?:\s+IDs?)?|Fictional\s+observation|Observation|Test\s+severity|Severity|Status|" +
        @"Corrective\s+action|Owner|Milestone|Target|Due\s+date|Closure\s+evidence|" +
        @"Submitted\s+evidence|Evidence\s+reference)\s*:");
    private static readonly Regex ClaimRecordIds = LabeledRegex(@"\b(?<id>(?:FIND|POAM)-[A-Za-z0-9][A-Za-z0-9_-]*)\b");
    private static readonly Regex ClaimAuthorizationHeading = LabeledRegex(@"\bauthorization\s+(?:reference|decision)\b");
    private static readonly Regex ClaimBoundaryHeading = LabeledRegex(@"\bauthorization\s+boundary\b");
    private static readonly Regex ClaimAssessmentHeading = LabeledRegex(@"\bassessment\s+(?:scope|summary|findings)\b");
    private static readonly Regex ClaimIncludedBoundary = LabeledRegex(
        @"^(?:the\s+)?(?:(?:provider|authorization|system)\s+)?boundary\s+includes\b");
    private static readonly Regex ClaimExcludedBoundary = LabeledRegex(
        @"\b(?:are\s+outside\s+(?:the\s+)?(?:(?:provider|authorization|system)\s+)?boundary|" +
        @"are\s+excluded\s+from\s+(?:the\s+)?(?:(?:provider|authorization|system)\s+)?boundary|" +
        @"(?:(?:provider|authorization|system)\s+)?boundary\s+excludes)\b");
    private static readonly Regex ClaimConditionsHeading = LabeledRegex(@"\b(?:Illustrative\s+conditions\b\s*:?|Conditions\s*:)");
    private static readonly Regex ClaimSignatureHeading = LabeledRegex(@"\bSignature\b\s*:?");
    private static readonly Regex ClaimExclusion = LabeledRegex(@"\b(?:excluded|outside)\b");
    private static readonly Regex ClaimQualification = LabeledRegex(
        @"\b(?:synthetic|fictional|illustrative|unconfirmed|unverified|unsigned|excluded|outside|only|" +
        @"not\s+(?:a\s+)?(?:valid|real|formal)|no\s+(?:actual|signature|seal|real|part)|" +
        @"no\s+legal|must\s+not|cannot|retain\s+responsibility|remain\s+distinguishable)\b");
    private static readonly Regex ClaimMilestoneDeadline = LabeledRegex(@"\bby\s+(?<date>.+)$");
    private static readonly Regex ClaimExplicitId = LabeledRegex(@"[A-Za-z0-9][A-Za-z0-9_.:-]*");
    private static readonly Regex ClaimUncertainBoundary = LabeledRegex(
        @"\b(?:if|unless|might|may|could|would|should|proposed|requested|not|never|whether)\b");

    private static Regex LabeledRegex(string expression) => new(expression,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(250));

    private static void ExtractLabeledClaims(AnalysisSession session, Entry entry,
        IReadOnlyList<CspPackageSourceSegment> segments)
    {
        session.Cancellation.ThrowIfCancellationRequested();
        // Structured decoders own their schemas; a string inside JSON/XML is not a prose declaration.
        if (entry.MediaType is not ("application/pdf" or "text/plain" or "text/markdown")
            && !entry.ArchivePath.Contains("!/word/", StringComparison.Ordinal))
            return;
        try
        {
            var retained = segments.Where(segment => segment.EntryKey == entry.Key).ToArray();
            if (entry.MediaType == "application/pdf")
            {
                foreach (var segment in retained)
                {
                    session.Cancellation.ThrowIfCancellationRequested();
                    ExtractLabeledDocument(session, new LabeledClaimDocument([segment], session.Cancellation));
                }
            }
            else
                ExtractLabeledDocument(session, new LabeledClaimDocument(retained, session.Cancellation));
        }
        catch (RegexMatchTimeoutException)
        {
            entry.Fail("LABELED_CLAIM_PARSE_TIMEOUT",
                "Labeled declarations exceeded the bounded parser time limit. Split or simplify the source and retry; coverage is incomplete.");
        }
    }

    private static void ExtractLabeledDocument(AnalysisSession session, LabeledClaimDocument document)
    {
        if (document.Text.Length == 0) return;
        session.Cancellation.ThrowIfCancellationRequested();
        var fields = new List<LabeledClaimField>();
        foreach (Match match in ClaimLabels.Matches(document.Text))
        {
            session.Cancellation.ThrowIfCancellationRequested();
            fields.Add(new(string.Join(" ", match.Groups["label"].Value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant(),
                match.Index, match.Index + match.Length));
        }
        for (var index = 0; index < fields.Count - 1; index++)
            fields[index] = fields[index] with { NextStart = fields[index + 1].Start };
        var qualifications = LabeledQualifications(session, document);
        var records = LabeledRecords(session, document, fields);
        ExtractLabeledBoundaries(session, document, qualifications);
        ExtractLabeledDecisions(session, document, fields, records, qualifications);
        foreach (var record in records)
        {
            session.Cancellation.ThrowIfCancellationRequested();
            if (record.IsFinding)
                ExtractLabeledFinding(session, document, fields, record, records[0].Start, qualifications);
            else
                ExtractLabeledPoam(session, document, fields, record, qualifications);
        }
        // Recognized declarations are proposals, not proof that the remaining prose was analyzed.
    }

    private static List<LabeledClaimRecord> LabeledRecords(AnalysisSession session,
        LabeledClaimDocument document, IReadOnlyList<LabeledClaimField> fields)
    {
        var records = new List<LabeledClaimRecord>();
        foreach (Match match in ClaimRecordIds.Matches(document.Text))
        {
            session.Cancellation.ThrowIfCancellationRequested();
            if (!LabeledRecordStart(document.Text, match.Index)) continue;
            var first = LabeledFieldsInRange(fields, match.Index + match.Length, document.Text.Length).FirstOrDefault();
            if (first is null) continue;
            if (first.Start - (match.Index + match.Length) > 300) continue;
            var heading = document.Text[(match.Index + match.Length)..first.Start].Trim();
            if (heading.Contains('.') || heading.Contains(':')
                || ClaimRecordIds.IsMatch(heading))
                continue;
            var finding = match.Groups["id"].Value.StartsWith("FIND-", StringComparison.OrdinalIgnoreCase);
            var supported = finding
                ? first.Name is "related component" or "related capability" or "control" or "control id" or "control ids"
                    or "observation" or "fictional observation" or "severity" or "test severity" or "assessor" or "assessment date"
                : first.Name is "owner" or "corrective action" or "milestone" or "target" or "due date" or "status";
            if (!supported) continue;
            var id = match.Groups["id"].Value;
            records.Add(new(id, finding, match.Index, document.Text.Length,
                heading.Length == 0 ? id : heading, document.SourceAt(match.Index)));
        }
        for (var index = 0; index < records.Count - 1; index++)
            records[index] = records[index] with { End = records[index + 1].Start };
        return records;
    }

    private static bool LabeledRecordStart(string text, int start)
    {
        var afterLineBreak = false;
        for (var index = start - 1; index >= 0; index--)
        {
            if (text[index] is '\n' or '\r') afterLineBreak = true;
            if (!char.IsWhiteSpace(text[index]))
                return text[index] != ':' && (afterLineBreak || text[index] is '.' or '!' or '?');
        }
        return true;
    }

    private static void ExtractLabeledBoundaries(AnalysisSession session, LabeledClaimDocument document,
        IReadOnlyList<string> qualifications)
    {
        var heading = ClaimBoundaryHeading.Match(document.Text);
        if (!heading.Success) return;
        foreach (var sentence in document.Sentences(heading.Index + heading.Length, document.Text.Length))
        {
            session.Cancellation.ThrowIfCancellationRequested();
            if (ClaimUncertainBoundary.IsMatch(sentence.Value)) continue;
            var relationship = ClaimIncludedBoundary.IsMatch(sentence.Value) ? "Included"
                : ClaimExcludedBoundary.IsMatch(sentence.Value) ? "Excluded" : null;
            if (relationship is null) continue;
            EmitClaim(session, sentence.Segment, CspPackageCandidateKind.BoundaryClaim, sentence.Value, null,
                new CspPackageClaim
                {
                    Boundary = new() { Relationship = relationship, Scope = sentence.Value },
                    Qualifications = qualifications
                });
        }
    }

    private static void ExtractLabeledDecisions(AnalysisSession session, LabeledClaimDocument document,
        IReadOnlyList<LabeledClaimField> fields, IReadOnlyList<LabeledClaimRecord> records,
        IReadOnlyList<string> qualifications)
    {
        var heading = ClaimAuthorizationHeading.Match(document.Text);
        if (!heading.Success) return;
        var references = fields.Where(field => field.Name == "reference id" && field.Start > heading.Index).ToArray();
        for (var index = 0; index < references.Length; index++)
        {
            session.Cancellation.ThrowIfCancellationRequested();
            var start = references[index].Start;
            var end = index + 1 < references.Length ? references[index + 1].Start : document.Text.Length;
            end = records.Where(record => record.Start > start && record.Start < end)
                .Select(record => record.Start).DefaultIfEmpty(end).Min();
            var reference = LabeledValue(document, fields, start, end, "reference id");
            if (reference is null) continue;
            var authority = LabeledValue(document, fields, start, end, "issuing authority");
            var decisionDate = LabeledValue(document, fields, start, end, "decision date");
            var expirationDate = LabeledValue(document, fields, start, end, "expiration date");
            var scope = LabeledValue(document, fields, start, end, "scope");
            if (authority is null && decisionDate is null && expirationDate is null && scope is null) continue;
            var subject = LabeledValue(document, fields, start, end, "subject", "title");
            var subjectKind = LabeledValue(document, fields, start, end, "subject kind");
            var conditions = new List<string>();
            var conditionHeading = ClaimConditionsHeading.Match(document.Text, start, end - start);
            if (conditionHeading.Success)
            {
                var conditionStart = conditionHeading.Index + conditionHeading.Length;
                var signature = ClaimSignatureHeading.Match(document.Text, conditionStart, end - conditionStart);
                conditions.AddRange(document.Sentences(conditionStart, signature.Success ? signature.Index : end)
                    .Select(sentence => sentence.Value));
            }
            var exclusions = document.Sentences(start, end).Where(sentence => ClaimExclusion.IsMatch(sentence.Value))
                .Select(sentence => sentence.Value).ToArray();
            EmitClaim(session, document.SourceAt(start), CspPackageCandidateKind.AuthorizationDecisionClaim,
                subject ?? reference, reference, new CspPackageClaim
                {
                    AuthorizationDecision = new()
                    {
                        SubjectKind = subjectKind is "Provider" or "InheritedCloud" or "MissionSystem" ? subjectKind : "Unspecified",
                        Subject = subject,
                        Reference = reference,
                        Authority = authority,
                        DecisionType = LabeledValue(document, fields, start, end, "decision type"),
                        StatusAsStated = LabeledValue(document, fields, start, end, "status"),
                        DecisionDate = decisionDate,
                        ExpirationDate = expirationDate,
                        Scope = scope,
                        Conditions = conditions,
                        Exclusions = exclusions
                    },
                    SourceAliases = [reference],
                    Qualifications = qualifications
                });
        }
    }

    private static void ExtractLabeledFinding(AnalysisSession session, LabeledClaimDocument document,
        IReadOnlyList<LabeledClaimField> fields, LabeledClaimRecord record, int firstRecordStart,
        IReadOnlyList<string> qualifications)
    {
        var assessor = LabeledValue(document, fields, record.Start, record.End, "assessor");
        var assessmentDate = LabeledValue(document, fields, record.Start, record.End, "assessment date", "synthetic review date", "review date");
        if (ClaimAssessmentHeading.IsMatch(document.Text[..firstRecordStart]))
        {
            assessor ??= LabeledValue(document, fields, 0, firstRecordStart, "assessor");
            assessmentDate ??= LabeledValue(document, fields, 0, firstRecordStart,
                "assessment date", "synthetic review date", "review date");
        }
        EmitClaim(session, record.Segment, CspPackageCandidateKind.AssessmentFinding, record.Name, record.Id,
            new CspPackageClaim
            {
                AssessmentFinding = new()
                {
                    SourceFindingId = record.Id,
                    Observation = LabeledValue(document, fields, record.Start, record.End, "fictional observation", "observation"),
                    SeverityAsStated = LabeledValue(document, fields, record.Start, record.End, "test severity", "severity"),
                    StatusAsStated = LabeledValue(document, fields, record.Start, record.End, "status"),
                    Assessor = assessor,
                    AssessmentDate = assessmentDate,
                    ControlIds = LabeledIds(session, document, fields, record, "control", "control id", "control ids"),
                    EvidenceReferences = LabeledValues(document, fields, record.Start, record.End, "evidence reference")
                },
                Relationships = LabeledRelationships(session, document, fields, record),
                SourceAliases = [record.Id],
                Qualifications = qualifications
            });
    }

    private static void ExtractLabeledPoam(AnalysisSession session, LabeledClaimDocument document,
        IReadOnlyList<LabeledClaimField> fields, LabeledClaimRecord record, IReadOnlyList<string> qualifications)
    {
        var milestones = new List<CspClaimMilestone>();
        foreach (var field in LabeledFieldsInRange(fields, record.Start, record.End)
                     .Where(field => field.Name is "milestone" or "target" or "due date"))
        {
            session.Cancellation.ThrowIfCancellationRequested();
            var value = LabeledFieldValue(document, field, record.End);
            if (value is null) continue;
            if (field.Name == "milestone")
            {
                var deadline = ClaimMilestoneDeadline.Match(value);
                milestones.Add(new(value, deadline.Success ? deadline.Groups["date"].Value.Trim() : null));
            }
            else if (milestones.Count > 0 && milestones[^1].DueDate is null)
                milestones[^1] = milestones[^1] with { DueDate = value };
            else
                milestones.Add(new(null, value));
        }
        EmitClaim(session, record.Segment, CspPackageCandidateKind.PoamItem, record.Name, record.Id,
            new CspPackageClaim
            {
                PoamItem = new()
                {
                    SourcePoamId = record.Id,
                    CorrectiveAction = LabeledValue(document, fields, record.Start, record.End, "corrective action"),
                    OwnerAsStated = LabeledValue(document, fields, record.Start, record.End, "owner"),
                    StatusAsStated = LabeledValue(document, fields, record.Start, record.End, "status"),
                    Milestones = milestones,
                    RequiredClosureEvidence = LabeledValues(document, fields, record.Start, record.End, "closure evidence"),
                    SubmittedEvidenceReferences = LabeledValues(document, fields, record.Start, record.End, "submitted evidence")
                },
                Relationships = LabeledRelationships(session, document, fields, record),
                SourceAliases = [record.Id],
                Qualifications = qualifications
            });
    }

    private static IReadOnlyList<CspClaimRelationship> LabeledRelationships(AnalysisSession session,
        LabeledClaimDocument document, IReadOnlyList<LabeledClaimField> fields, LabeledClaimRecord record)
    {
        var relationships = new List<CspClaimRelationship>();
        var mappings = record.IsFinding
            ? new[] { ("related component", "FindingComponent"), ("related capability", "FindingCapability") }
            : new[] { ("related finding", "PoamFinding") };
        foreach (var (label, kind) in mappings)
            foreach (var id in LabeledIds(session, document, fields, record, label))
                relationships.Add(new(kind, id));
        return relationships;
    }

    private static IReadOnlyList<string> LabeledIds(AnalysisSession session, LabeledClaimDocument document,
        IReadOnlyList<LabeledClaimField> fields, LabeledClaimRecord record, params string[] names)
    {
        var ids = new List<string>();
        foreach (var value in LabeledValues(document, fields, record.Start, record.End, names))
        {
            session.Cancellation.ThrowIfCancellationRequested();
            // Only explicit delimited identifiers are accepted, never prose or expanded numeric ranges.
            var pieces = value.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var piece in pieces)
            {
                session.Cancellation.ThrowIfCancellationRequested();
                var match = ClaimExplicitId.Match(piece);
                if (match.Success && match.Index == 0 && match.Length == piece.Length)
                    ids.Add(piece);
            }
        }
        return ids;
    }

    private static string? LabeledValue(LabeledClaimDocument document, IReadOnlyList<LabeledClaimField> fields,
        int start, int end, params string[] names) =>
        LabeledValues(document, fields, start, end, names).FirstOrDefault();

    private static string[] LabeledValues(LabeledClaimDocument document, IReadOnlyList<LabeledClaimField> fields,
        int start, int end, params string[] names) =>
        LabeledFieldsInRange(fields, start, end).Where(field => names.Contains(field.Name, StringComparer.Ordinal))
            .Select(field => LabeledFieldValue(document, field, end)).OfType<string>().ToArray();

    private static IEnumerable<LabeledClaimField> LabeledFieldsInRange(
        IReadOnlyList<LabeledClaimField> fields, int start, int end)
    {
        var left = 0;
        var right = fields.Count;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (fields[middle].Start < start) left = middle + 1;
            else right = middle;
        }
        for (var index = left; index < fields.Count && fields[index].Start < end; index++)
            yield return fields[index];
    }

    private static string? LabeledFieldValue(LabeledClaimDocument document, LabeledClaimField field, int end)
    {
        end = Math.Min(field.NextStart, end);
        var start = field.ValueStart;
        while (start < end && char.IsWhiteSpace(document.Text[start])) start++;
        if (start >= end) return null;
        end = Math.Min(end, document.SourceEndAt(start));
        for (var index = start; index < end; index++)
        {
            if (document.Text[index] is '\r' or '\n'
                || document.Text[index] is '.' or '!' or '?' && (index + 1 == end || char.IsWhiteSpace(document.Text[index + 1])))
            {
                end = index;
                break;
            }
        }
        var value = document.Text[start..end].Trim();
        return value.Length == 0 ? null : value;
    }

    private static IReadOnlyList<string> LabeledQualifications(AnalysisSession session, LabeledClaimDocument document)
    {
        var qualifications = new List<string>();
        foreach (var sentence in document.Sentences(0, document.Text.Length))
        {
            session.Cancellation.ThrowIfCancellationRequested();
            if (ClaimQualification.IsMatch(sentence.Value) && !qualifications.Contains(sentence.Value, StringComparer.Ordinal))
                qualifications.Add(sentence.Value);
        }
        return qualifications;
    }

    private sealed record LabeledClaimField(string Name, int Start, int ValueStart)
    {
        public int NextStart { get; init; } = int.MaxValue;
    }
    private sealed record LabeledClaimRecord(string Id, bool IsFinding, int Start, int End, string Name,
        CspPackageSourceSegment Segment);
    private sealed record LabeledClaimSentence(string Value, CspPackageSourceSegment Segment);

    private sealed class LabeledClaimDocument
    {
        private readonly List<(int Start, int End, CspPackageSourceSegment Segment)> _sources = [];
        private readonly CancellationToken _cancellation;
        public string Text { get; }

        public LabeledClaimDocument(IReadOnlyList<CspPackageSourceSegment> segments, CancellationToken cancellation)
        {
            _cancellation = cancellation;
            var text = new StringBuilder();
            foreach (var segment in segments)
            {
                cancellation.ThrowIfCancellationRequested();
                if (text.Length > 0) text.Append('\n');
                var start = text.Length;
                text.Append(segment.Text);
                _sources.Add((start, text.Length, segment));
            }
            Text = text.ToString();
        }

        public CspPackageSourceSegment SourceAt(int position) =>
            _sources.First(source => source.Start <= position && source.End > position).Segment;

        public int SourceEndAt(int position) =>
            _sources.First(source => source.Start <= position && source.End > position).End;

        public IEnumerable<LabeledClaimSentence> Sentences(int start, int end)
        {
            foreach (var source in _sources)
            {
                _cancellation.ThrowIfCancellationRequested();
                var sentenceStart = Math.Max(start, source.Start);
                var sourceEnd = Math.Min(end, source.End);
                for (var index = sentenceStart; index < sourceEnd; index++)
                {
                    if ((index & 4095) == 0) _cancellation.ThrowIfCancellationRequested();
                    if (Text[index] is not ('.' or '!' or '?' or '\r' or '\n') && index + 1 < sourceEnd)
                        continue;
                    if (Text[index] is '.' or '!' or '?' && index + 1 < sourceEnd && !char.IsWhiteSpace(Text[index + 1]))
                        continue;
                    var value = Text[sentenceStart..(index + 1)].Trim();
                    if (value.Length > 0) yield return new(value, source.Segment);
                    sentenceStart = index + 1;
                }
            }
        }
    }
}
