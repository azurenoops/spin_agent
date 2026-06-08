// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — SCAP/STIG Viewer Import: CKL XML Parser
// Parses DISA STIG Viewer .ckl files into typed ParsedCklFile DTOs.
// See specs/017-scap-stig-import/spec.md §3.1 for CKL format documentation.
//
// Task #175 (Epic #131): Refactored from XDocument to XmlReader streaming so
// that 5,000-entry CKL files are processed without loading the full DOM into
// memory. Memory usage is proportional to a single VULN entry rather than
// the entire file.
//
// XmlReader positioning contract:
//   ReadTextContent() is used instead of ReadElementContentAsString() in all
//   while(reader.Read()) loops. ReadTextContent() leaves the reader ON the
//   closing tag so the loop's next Read() goes to the correct sibling.
//   ReadElementContentAsString() advances PAST the closing tag which causes
//   the outer loop's Read() to double-advance and skip the next sibling.
// ═══════════════════════════════════════════════════════════════════════════

using System.Text;
using System.Xml;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services.ScanImport;

/// <summary>Interface for parsing DISA STIG Viewer CKL XML files.</summary>
public interface ICklParser
{
    /// <summary>
    /// Parses a CKL file from raw bytes into a <see cref="ParsedCklFile"/> DTO.
    /// </summary>
    ParsedCklFile Parse(byte[] fileContent, string fileName);
}

/// <summary>Exception thrown when CKL parsing fails.</summary>
public class CklParseException : Exception
{
    public string FileName { get; }

    public CklParseException(string fileName, string message)
        : base($"Failed to parse CKL file '{fileName}': {message}") => FileName = fileName;

    public CklParseException(string fileName, string message, Exception innerException)
        : base($"Failed to parse CKL file '{fileName}': {message}", innerException) => FileName = fileName;
}

/// <summary>
/// Parses DISA STIG Viewer CKL XML files into typed <see cref="ParsedCklFile"/> DTOs
/// using <see cref="XmlReader"/> streaming. Memory O(single VULN) regardless of file size.
/// </summary>
public class CklParser : ICklParser
{
    private readonly ILogger<CklParser> _logger;

    public CklParser(ILogger<CklParser> logger) => _logger = logger;

    /// <inheritdoc />
    public ParsedCklFile Parse(byte[] fileContent, string fileName)
    {
        if (fileContent is null || fileContent.Length == 0)
            throw new CklParseException(fileName, "File is empty.");

        var settings = new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments   = true,
            DtdProcessing    = DtdProcessing.Ignore, // CKL files reference DISA DTDs not on classpath
        };

        try
        {
            using var stream = new MemoryStream(fileContent);
            using var reader = XmlReader.Create(stream, settings);
            return ParseChecklist(reader, fileName);
        }
        catch (CklParseException) { throw; }
        catch (XmlException ex)
        {
            _logger.LogWarning(ex, "Malformed XML in CKL file {FileName}", fileName);
            throw new CklParseException(fileName,
                $"Malformed XML at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}", ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Top-level parser
    // ─────────────────────────────────────────────────────────────────────

    private ParsedCklFile ParseChecklist(XmlReader reader, string fileName)
    {
        // Advance to root element
        while (reader.Read() && reader.NodeType != XmlNodeType.Element) { }

        if (reader.Name != "CHECKLIST")
            throw new CklParseException(fileName, "Missing root <CHECKLIST> element.");

        CklAssetInfo? asset = null;
        CklStigInfo? stigInfo = null;
        var entries = new List<ParsedCklEntry>();

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.Name)
            {
                case "ASSET":
                    asset = ParseAsset(reader);
                    break;
                case "STIG_INFO":
                    stigInfo = ParseStigInfo(reader);
                    break;
                case "VULN":
                    var entry = ParseVuln(reader);
                    if (entry is not null)
                        entries.Add(entry);
                    break;
            }
        }

        asset    ??= new CklAssetInfo(null, null, null, null, null, null);
        stigInfo ??= new CklStigInfo(null, null, null, null);

        _logger.LogDebug(
            "Parsed CKL file {FileName}: {EntryCount} VULN entries, benchmark={BenchmarkId}",
            fileName, entries.Count, stigInfo.StigId);

        return new ParsedCklFile(asset, stigInfo, entries);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Sub-parsers — each has its own while(reader.Read()) loop and uses
    // ReadTextContent() to avoid the double-advance bug described in the
    // file header.
    // ─────────────────────────────────────────────────────────────────────

    private static CklAssetInfo ParseAsset(XmlReader reader)
    {
        string? hostName = null, hostIp = null, hostFqdn = null,
                hostMac = null, assetType = null, targetKey = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "ASSET")
                break;
            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.Name)
            {
                case "HOST_NAME":   hostName  = ReadTextContent(reader); break;
                case "HOST_IP":     hostIp    = ReadTextContent(reader); break;
                case "HOST_FQDN":   hostFqdn  = ReadTextContent(reader); break;
                case "HOST_MAC":    hostMac   = ReadTextContent(reader); break;
                case "ASSET_TYPE":  assetType = ReadTextContent(reader); break;
                case "TARGET_KEY":  targetKey = ReadTextContent(reader); break;
            }
        }

        return new CklAssetInfo(hostName, hostIp, hostFqdn, hostMac, assetType, targetKey);
    }

    private static CklStigInfo ParseStigInfo(XmlReader reader)
    {
        var siData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentName = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "STIG_INFO")
                break;
            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.Name)
            {
                case "SID_NAME":
                    currentName = ReadTextContent(reader);
                    break;
                case "SID_DATA":
                    var data = ReadTextContent(reader) ?? string.Empty;
                    if (!string.IsNullOrEmpty(currentName))
                    {
                        siData[currentName] = data;
                        currentName = null;
                    }
                    break;
            }
        }

        return new CklStigInfo(
            StigId:      siData.GetValueOrDefault("stigid"),
            Version:     siData.GetValueOrDefault("version"),
            ReleaseInfo: siData.GetValueOrDefault("releaseinfo"),
            Title:       siData.GetValueOrDefault("title"));
    }

    private static ParsedCklEntry? ParseVuln(XmlReader reader)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cciRefs = new List<string>();
        string? status = null, findingDetails = null, comments = null,
                severityOverride = null, severityJustification = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "VULN")
                break;
            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.Name)
            {
                case "STIG_DATA":
                    ParseStigData(reader, attributes, cciRefs);
                    break;
                case "STATUS":
                    status = ReadTextContent(reader);
                    break;
                case "FINDING_DETAILS":
                    findingDetails = ReadTextContent(reader);
                    break;
                case "COMMENTS":
                    comments = ReadTextContent(reader);
                    break;
                case "SEVERITY_OVERRIDE":
                    severityOverride = ReadTextContent(reader);
                    break;
                case "SEVERITY_JUSTIFICATION":
                    severityJustification = ReadTextContent(reader);
                    break;
            }
        }

        var vulnId = attributes.GetValueOrDefault("Vuln_Num") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(vulnId))
            return null;

        return new ParsedCklEntry(
            VulnId:                vulnId,
            RuleId:                attributes.GetValueOrDefault("Rule_ID"),
            StigVersion:           attributes.GetValueOrDefault("Rule_Ver"),
            RuleTitle:             attributes.GetValueOrDefault("Rule_Title"),
            Severity:              attributes.GetValueOrDefault("Severity") ?? string.Empty,
            Status:                status ?? string.Empty,
            FindingDetails:        NullIfEmpty(findingDetails),
            Comments:              NullIfEmpty(comments),
            SeverityOverride:      NullIfEmpty(severityOverride),
            SeverityJustification: NullIfEmpty(severityJustification),
            CciRefs:               cciRefs,
            GroupTitle:            attributes.GetValueOrDefault("Group_Title"));
    }

    /// <summary>
    /// Reads a single STIG_DATA element:
    ///   &lt;STIG_DATA&gt;&lt;VULN_ATTRIBUTE&gt;name&lt;/VULN_ATTRIBUTE&gt;&lt;ATTRIBUTE_DATA&gt;value&lt;/ATTRIBUTE_DATA&gt;&lt;/STIG_DATA&gt;
    /// </summary>
    private static void ParseStigData(
        XmlReader reader,
        Dictionary<string, string> attributes,
        List<string> cciRefs)
    {
        if (reader.IsEmptyElement) return;

        string? attrName = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "STIG_DATA")
                break;
            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.Name)
            {
                case "VULN_ATTRIBUTE":
                    attrName = ReadTextContent(reader);
                    break;
                case "ATTRIBUTE_DATA":
                    var attrData = ReadTextContent(reader) ?? string.Empty;
                    if (!string.IsNullOrEmpty(attrName))
                    {
                        if (string.Equals(attrName, "CCI_REF", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrWhiteSpace(attrData))
                                cciRefs.Add(attrData);
                        }
                        else
                        {
                            attributes[attrName] = attrData;
                        }
                        attrName = null;
                    }
                    break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads text content of the current element by manually walking Text/CDATA nodes.
    /// On return the reader is positioned ON the closing tag; the outer
    /// while(reader.Read()) will then advance to the correct next sibling.
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

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
