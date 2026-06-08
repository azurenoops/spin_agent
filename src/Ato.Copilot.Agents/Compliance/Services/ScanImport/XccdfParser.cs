// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — SCAP/STIG Import: XCCDF Parser
// Parses SCAP Compliance Checker XCCDF TestResult XML files.
//
// Task #175 (Epic #131): Refactored from XDocument to XmlReader streaming so
// that 5,000-entry XCCDF files are processed without loading the full DOM into
// memory. Supports XCCDF 1.1 and 1.2 namespace variants via local-name matching.
//
// XmlReader positioning contract:
//   All methods in this file follow a consistent Read() loop convention where
//   ReadTextContent() is used instead of ReadElementContentAsString() in the
//   main parse loops. ReadTextContent() leaves the reader ON the closing tag;
//   the outer while(reader.Read()) then advances to the next sibling correctly.
//   ReadElementContentAsString() advances PAST the closing tag, which causes
//   the outer loop's Read() to skip the next sibling — that bug is avoided
//   by using ReadTextContent() everywhere in outer loops.
// ═══════════════════════════════════════════════════════════════════════════

using System.Globalization;
using System.Text;
using System.Xml;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services.ScanImport;

/// <summary>
/// Parses XCCDF (Extensible Configuration Checklist Description Format) results XML.
/// Supports both XCCDF 1.1 and 1.2 namespace variations.
/// </summary>
public interface IXccdfParser
{
    /// <summary>
    /// Parse XCCDF TestResult XML bytes into a <see cref="ParsedXccdfFile"/>.
    /// </summary>
    ParsedXccdfFile Parse(byte[] content, string fileName);
}

/// <summary>Exception thrown when XCCDF parsing fails.</summary>
public class XccdfParseException : Exception
{
    public string FileName { get; }

    public XccdfParseException(string fileName, string message)
        : base($"XCCDF parse error in '{fileName}': {message}") => FileName = fileName;

    public XccdfParseException(string fileName, string message, Exception inner)
        : base($"XCCDF parse error in '{fileName}': {message}", inner) => FileName = fileName;
}

/// <summary>
/// XmlReader-streaming XCCDF parser. Memory O(single rule-result) regardless of file size.
/// </summary>
public class XccdfParser : IXccdfParser
{
    private readonly ILogger<XccdfParser> _logger;

    public XccdfParser(ILogger<XccdfParser> logger) => _logger = logger;

    public ParsedXccdfFile Parse(byte[] content, string fileName)
    {
        if (content is null || content.Length == 0)
            throw new XccdfParseException(fileName, "File is empty.");

        var settings = new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments   = true,
            DtdProcessing    = DtdProcessing.Ignore,
        };

        try
        {
            using var stream = new MemoryStream(content);
            using var reader = XmlReader.Create(stream, settings);
            return ParseFile(reader, fileName);
        }
        catch (XccdfParseException) { throw; }
        catch (XmlException ex)
        {
            throw new XccdfParseException(fileName, $"Invalid XML: {ex.Message}", ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // ParseFile — top-level streaming state machine
    // ─────────────────────────────────────────────────────────────────────

    private ParsedXccdfFile ParseFile(XmlReader reader, string fileName)
    {
        string? benchmarkHref = null, title = null, target = null, targetAddress = null;
        DateTime? startTime = null, endTime = null;
        decimal? score = null, maxScore = null;
        var targetFacts = new Dictionary<string, string>();
        var results     = new List<ParsedXccdfResult>();

        bool insideTestResult  = false;
        bool foundTestResult   = false;
        bool insideTargetFacts = false;

        while (reader.Read())
        {
            // ── Element open ─────────────────────────────────────────────
            if (reader.NodeType == XmlNodeType.Element)
            {
                var ln = reader.LocalName;

                if (ln == "TestResult")
                {
                    insideTestResult = true;
                    foundTestResult  = true;
                    startTime = ParseTimestamp(reader.GetAttribute("start-time"));
                    endTime   = ParseTimestamp(reader.GetAttribute("end-time"));
                    if (reader.IsEmptyElement) insideTestResult = false;
                    continue; // do not fall through to the insideTestResult guard
                }

                if (!insideTestResult) continue;

                // All elements below require ReadTextContent() — NOT ReadElementContentAsString()
                // because we are inside a while(reader.Read()) loop (see file header comment).
                switch (ln)
                {
                    case "benchmark":
                        benchmarkHref = reader.GetAttribute("href");
                        // Consume element with ReadTextContent (ignore content) so reader
                        // lands ON </benchmark>; outer loop's Read() → next sibling correctly.
                        ReadTextContent(reader);
                        break;

                    case "title":
                        title = ReadTextContent(reader); // leaves reader ON </title>
                        break;

                    case "target":
                        target = ReadTextContent(reader);
                        break;

                    case "target-address":
                        targetAddress = ReadTextContent(reader);
                        break;

                    case "identity":
                        // Informational only — consume with ReadTextContent so reader
                        // lands ON </identity> and the outer loop's Read() advances
                        // correctly to the next sibling (NOT Skip() which advances past it).
                        ReadTextContent(reader);
                        break;

                    case "target-facts":
                        insideTargetFacts = true;
                        break;

                    case "fact" when insideTargetFacts:
                        var factName = reader.GetAttribute("name");
                        if (!reader.IsEmptyElement && factName is not null)
                            targetFacts[factName] = ReadTextContent(reader) ?? string.Empty;
                        else if (!reader.IsEmptyElement)
                            reader.Skip();
                        break;

                    case "score":
                        if (!reader.IsEmptyElement)
                        {
                            var maxAttr = reader.GetAttribute("maximum");
                            var scoreText = ReadTextContent(reader) ?? string.Empty;
                            if (decimal.TryParse(scoreText, NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                score = s;
                            if (decimal.TryParse(maxAttr, NumberStyles.Any, CultureInfo.InvariantCulture, out var m))
                                maxScore = m;
                        }
                        break;

                    case "rule-result":
                        // ParseRuleResult has its OWN while(reader.Read()) loop and
                        // leaves the reader ON </rule-result>. The outer loop's next
                        // Read() correctly advances to the next sibling.
                        var rr = ParseRuleResult(reader);
                        if (rr is not null) results.Add(rr);
                        break;
                }
            }
            // ── Element close ────────────────────────────────────────────
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                switch (reader.LocalName)
                {
                    case "target-facts":
                        insideTargetFacts = false;
                        break;
                    case "TestResult":
                        insideTestResult = false;
                        break;
                }
            }
        }

        if (!foundTestResult)
            throw new XccdfParseException(fileName,
                "No <TestResult> element found in XCCDF XML. " +
                "Verify the file uses a recognized XCCDF namespace " +
                "(http://checklists.nist.gov/xccdf/1.1 or /1.2) or no namespace.");

        _logger.LogInformation(
            "Parsed XCCDF file '{FileName}': {ResultCount} rule-results, target={Target}, score={Score}/{MaxScore}",
            fileName, results.Count, target ?? "(unknown)", score, maxScore);

        return new ParsedXccdfFile(
            BenchmarkHref: benchmarkHref, Title: title, Target: target,
            TargetAddress: targetAddress, StartTime: startTime, EndTime: endTime,
            Score: score, MaxScore: maxScore, TargetFacts: targetFacts, Results: results);
    }

    // ─────────────────────────────────────────────────────────────────────
    // ParseRuleResult — sub-parser with its own read loop
    // ─────────────────────────────────────────────────────────────────────

    private static ParsedXccdfResult? ParseRuleResult(XmlReader reader)
    {
        var idref    = reader.GetAttribute("idref") ?? string.Empty;
        var severity = reader.GetAttribute("severity") ?? "unknown";

        decimal weight = 0;
        if (decimal.TryParse(reader.GetAttribute("weight"), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var w))
            weight = w;

        var timestamp = ParseTimestamp(reader.GetAttribute("time"));

        if (reader.IsEmptyElement) return null;

        string? result = null, message = null, checkRef = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "rule-result")
                break;

            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.LocalName)
            {
                case "result":
                    result = ReadTextContent(reader);
                    break;

                case "message":
                    message = ReadTextContent(reader);
                    break;

                case "check":
                    if (!reader.IsEmptyElement)
                        checkRef = ParseCheckRef(reader);
                    break;
            }
        }

        if (string.IsNullOrEmpty(idref)) return null;

        return new ParsedXccdfResult(
            RuleIdRef:       idref,
            ExtractedRuleId: ExtractRuleId(idref),
            Result:          (result ?? "unknown").ToLowerInvariant(),
            Severity:        severity.ToLowerInvariant(),
            Weight:          weight,
            Timestamp:       timestamp,
            Message:         string.IsNullOrWhiteSpace(message) ? null : message,
            CheckRef:        checkRef);
    }

    private static string? ParseCheckRef(XmlReader reader)
    {
        string? name = null;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "check")
                break;
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "check-content-ref")
            {
                name = reader.GetAttribute("name");
                if (!reader.IsEmptyElement) reader.Skip();
            }
        }
        return name;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads text content of the current element by manually walking Text/CDATA nodes.
    /// On return the reader is positioned ON the element's closing tag, so the calling
    /// while(reader.Read()) loop will advance to the NEXT sibling correctly.
    /// Returns null if empty or whitespace-only.
    /// </summary>
    private static string? ReadTextContent(XmlReader reader)
    {
        if (reader.IsEmptyElement) return null;

        var sb = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                sb.Append(reader.Value);
        }
        var text = sb.ToString().Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Extract DISA rule ID from XCCDF idref.
    /// E.g., <c>xccdf_mil.disa.stig_rule_SV-254239r849090_rule</c> → <c>SV-254239r849090_rule</c>
    /// </summary>
    internal static string ExtractRuleId(string idref)
    {
        if (string.IsNullOrEmpty(idref)) return string.Empty;
        const string prefix = "xccdf_mil.disa.stig_rule_";
        if (idref.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return idref[prefix.Length..];
        if (idref.StartsWith("SV-", StringComparison.OrdinalIgnoreCase))
            return idref;
        return idref;
    }

    private static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt : null;
    }
}
