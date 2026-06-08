// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — SCAP/STIG Import: XCCDF Parser
// Parses SCAP Compliance Checker XCCDF TestResult XML files.
//
// Task #175 (Epic #131): Refactored from XDocument to XmlReader streaming so
// that 5,000-entry XCCDF files are processed without loading the full DOM into
// memory. Supports XCCDF 1.1 and 1.2 namespace variants via local-name matching.
// ═══════════════════════════════════════════════════════════════════════════

using System.Globalization;
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
    /// <param name="content">Raw XML bytes.</param>
    /// <param name="fileName">Original file name (for error messages).</param>
    /// <returns>Parsed XCCDF file with all rule-results.</returns>
    /// <exception cref="XccdfParseException">File is not valid XCCDF XML.</exception>
    ParsedXccdfFile Parse(byte[] content, string fileName);
}

/// <summary>
/// Exception thrown when XCCDF parsing fails.
/// </summary>
public class XccdfParseException : Exception
{
    public string FileName { get; }

    public XccdfParseException(string fileName, string message)
        : base($"XCCDF parse error in '{fileName}': {message}")
    {
        FileName = fileName;
    }

    public XccdfParseException(string fileName, string message, Exception inner)
        : base($"XCCDF parse error in '{fileName}': {message}", inner)
    {
        FileName = fileName;
    }
}

/// <summary>
/// Implementation of <see cref="IXccdfParser"/>. Uses <see cref="XmlReader"/> streaming
/// to support XCCDF 1.1 (<c>http://checklists.nist.gov/xccdf/1.1</c>) and
/// XCCDF 1.2 (<c>http://checklists.nist.gov/xccdf/1.2</c>) without loading the
/// entire XML DOM into memory.
/// </summary>
public class XccdfParser : IXccdfParser
{
    private readonly ILogger<XccdfParser> _logger;

    public XccdfParser(ILogger<XccdfParser> logger)
    {
        _logger = logger;
    }

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
        catch (XccdfParseException)
        {
            throw;
        }
        catch (XmlException ex)
        {
            throw new XccdfParseException(fileName, $"Invalid XML: {ex.Message}", ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Core streaming parser
    // ─────────────────────────────────────────────────────────────────────

    private ParsedXccdfFile ParseFile(XmlReader reader, string fileName)
    {
        // State accumulated across elements
        string? benchmarkHref = null;
        string? title = null;
        string? target = null;
        string? targetAddress = null;
        DateTime? startTime = null;
        DateTime? endTime = null;
        decimal? score = null;
        decimal? maxScore = null;
        var targetFacts = new Dictionary<string, string>();
        var results = new List<ParsedXccdfResult>();

        bool insideTestResult = false;
        bool insideTargetFacts = false;
        string? pendingFactName = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                var localName = reader.LocalName;

                // Enter TestResult (may be root or inside Benchmark)
                if (localName == "TestResult")
                {
                    insideTestResult = true;
                    startTime = ParseTimestamp(reader.GetAttribute("start-time"));
                    endTime   = ParseTimestamp(reader.GetAttribute("end-time"));
                    continue;
                }

                if (!insideTestResult) continue;

                switch (localName)
                {
                    case "benchmark":
                        benchmarkHref = reader.GetAttribute("href");
                        if (!reader.IsEmptyElement)
                            reader.Skip(); // no child content needed
                        break;

                    case "title":
                        title = reader.IsEmptyElement ? null : reader.ReadElementContentAsString().Trim();
                        break;

                    case "target":
                        target = reader.IsEmptyElement ? null : reader.ReadElementContentAsString().Trim();
                        break;

                    case "target-address":
                        targetAddress = reader.IsEmptyElement ? null : reader.ReadElementContentAsString().Trim();
                        break;

                    case "target-facts":
                        insideTargetFacts = true;
                        break;

                    case "fact" when insideTargetFacts:
                        pendingFactName = reader.GetAttribute("name");
                        if (!reader.IsEmptyElement && pendingFactName is not null)
                        {
                            var factValue = reader.ReadElementContentAsString();
                            targetFacts[pendingFactName] = factValue;
                        }
                        break;

                    case "score":
                        var maxAttr = reader.GetAttribute("maximum");
                        if (!reader.IsEmptyElement)
                        {
                            var scoreText = reader.ReadElementContentAsString();
                            if (decimal.TryParse(scoreText, NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                score = s;
                            if (decimal.TryParse(maxAttr, NumberStyles.Any, CultureInfo.InvariantCulture, out var m))
                                maxScore = m;
                        }
                        break;

                    case "rule-result":
                        var rr = ParseRuleResult(reader);
                        if (rr is not null)
                            results.Add(rr);
                        break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                switch (reader.LocalName)
                {
                    case "target-facts":
                        insideTargetFacts = false;
                        pendingFactName = null;
                        break;
                    case "TestResult":
                        insideTestResult = false;
                        break;
                }
            }
        }

        if (results.Count == 0 && !insideTestResult)
            throw new XccdfParseException(fileName, "No <TestResult> element found in XCCDF XML.");

        _logger.LogInformation(
            "Parsed XCCDF file '{FileName}': {ResultCount} rule-results, target={Target}, score={Score}/{MaxScore}",
            fileName, results.Count, target ?? "(unknown)", score, maxScore);

        return new ParsedXccdfFile(
            BenchmarkHref: benchmarkHref,
            Title:         title,
            Target:        target,
            TargetAddress: targetAddress,
            StartTime:     startTime,
            EndTime:       endTime,
            Score:         score,
            MaxScore:      maxScore,
            TargetFacts:   targetFacts,
            Results:       results);
    }

    private static ParsedXccdfResult? ParseRuleResult(XmlReader reader)
    {
        var idref    = reader.GetAttribute("idref") ?? string.Empty;
        var severity = reader.GetAttribute("severity") ?? "unknown";

        decimal weight = 0;
        if (decimal.TryParse(reader.GetAttribute("weight"), NumberStyles.Any, CultureInfo.InvariantCulture, out var w))
            weight = w;

        var timestamp = ParseTimestamp(reader.GetAttribute("time"));

        if (reader.IsEmptyElement)
            return null;

        string? result  = null;
        string? message = null;
        string? checkRef = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "rule-result")
                break;

            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.LocalName)
            {
                case "result":
                    result = reader.IsEmptyElement ? null : reader.ReadElementContentAsString().Trim();
                    break;

                case "message":
                    message = reader.IsEmptyElement ? null : reader.ReadElementContentAsString().Trim();
                    break;

                case "check":
                    // look for check-content-ref inside
                    if (!reader.IsEmptyElement)
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "check")
                                break;
                            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "check-content-ref")
                            {
                                checkRef = reader.GetAttribute("name");
                                if (!reader.IsEmptyElement)
                                    reader.Skip();
                            }
                        }
                    }
                    break;
            }
        }

        if (string.IsNullOrEmpty(idref)) return null;

        return new ParsedXccdfResult(
            RuleIdRef:      idref,
            ExtractedRuleId: ExtractRuleId(idref),
            Result:         (result ?? "unknown").ToLowerInvariant(),
            Severity:       severity.ToLowerInvariant(),
            Weight:         weight,
            Timestamp:      timestamp,
            Message:        string.IsNullOrWhiteSpace(message) ? null : message,
            CheckRef:       checkRef);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

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
            ? dt
            : null;
    }
}
