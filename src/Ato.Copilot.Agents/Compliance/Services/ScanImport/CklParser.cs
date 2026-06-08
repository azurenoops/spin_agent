// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — SCAP/STIG Viewer Import: CKL XML Parser
// Parses DISA STIG Viewer .ckl files into typed ParsedCklFile DTOs.
// See specs/017-scap-stig-import/spec.md §3.1 for CKL format documentation.
//
// Task #175 (Epic #131): Refactored from XDocument to XmlReader streaming so
// that 5,000-entry CKL files are processed without loading the full DOM into
// memory. Memory footprint is proportional to a single VULN entry, not the
// entire file.
// ═══════════════════════════════════════════════════════════════════════════

using System.Xml;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services.ScanImport;

/// <summary>
/// Interface for parsing DISA STIG Viewer CKL XML files.
/// </summary>
public interface ICklParser
{
    /// <summary>
    /// Parses a CKL file from raw bytes into a <see cref="ParsedCklFile"/> DTO.
    /// </summary>
    /// <param name="fileContent">Raw CKL file bytes (UTF-8 XML).</param>
    /// <param name="fileName">Original file name for error messages.</param>
    /// <returns>Parsed CKL data.</returns>
    /// <exception cref="CklParseException">Thrown when XML is malformed or missing required elements.</exception>
    ParsedCklFile Parse(byte[] fileContent, string fileName);
}

/// <summary>
/// Exception thrown when CKL parsing fails.
/// </summary>
public class CklParseException : Exception
{
    /// <summary>Original file name that failed to parse.</summary>
    public string FileName { get; }

    public CklParseException(string fileName, string message)
        : base($"Failed to parse CKL file '{fileName}': {message}")
    {
        FileName = fileName;
    }

    public CklParseException(string fileName, string message, Exception innerException)
        : base($"Failed to parse CKL file '{fileName}': {message}", innerException)
    {
        FileName = fileName;
    }
}

/// <summary>
/// Parses DISA STIG Viewer CKL XML files into typed <see cref="ParsedCklFile"/> DTOs.
/// Uses <see cref="XmlReader"/> streaming to handle enterprise-scale CKL files (5,000+
/// VULN entries) without loading the full DOM into memory. Memory usage is proportional
/// to a single VULN entry rather than the entire file.
/// </summary>
public class CklParser : ICklParser
{
    private readonly ILogger<CklParser> _logger;

    public CklParser(ILogger<CklParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ParsedCklFile Parse(byte[] fileContent, string fileName)
    {
        if (fileContent is null || fileContent.Length == 0)
            throw new CklParseException(fileName, "File is empty.");

        var settings = new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            DtdProcessing = DtdProcessing.Ignore,   // CKL files reference DISA DTDs not on classpath
        };

        try
        {
            using var stream = new MemoryStream(fileContent);
            using var reader = XmlReader.Create(stream, settings);

            return ParseChecklist(reader, fileName);
        }
        catch (CklParseException)
        {
            throw;
        }
        catch (XmlException ex)
        {
            _logger.LogWarning(ex, "Malformed XML in CKL file {FileName}", fileName);
            throw new CklParseException(fileName,
                $"Malformed XML at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}", ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Core streaming parser
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

        asset ??= new CklAssetInfo(null, null, null, null, null, null);
        stigInfo ??= new CklStigInfo(null, null, null, null);

        _logger.LogDebug(
            "Parsed CKL file {FileName}: {EntryCount} VULN entries, benchmark={BenchmarkId}",
            fileName, entries.Count, stigInfo.StigId);

        return new ParsedCklFile(asset, stigInfo, entries);
    }

    private static CklAssetInfo ParseAsset(XmlReader reader)
    {
        string? hostName = null, hostIp = null, hostFqdn = null,
                hostMac = null, assetType = null, targetKey = null;

        // Read until </ASSET>
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "ASSET")
                break;

            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.Name)
            {
                case "HOST_NAME":    hostName   = ReadElementText(reader); break;
                case "HOST_IP":      hostIp     = ReadElementText(reader); break;
                case "HOST_FQDN":   hostFqdn   = ReadElementText(reader); break;
                case "HOST_MAC":     hostMac    = ReadElementText(reader); break;
                case "ASSET_TYPE":   assetType  = ReadElementText(reader); break;
                case "TARGET_KEY":   targetKey  = ReadElementText(reader); break;
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
                    currentName = ReadElementText(reader);
                    break;

                case "SID_DATA":
                    var data = ReadElementText(reader) ?? string.Empty;
                    if (!string.IsNullOrEmpty(currentName))
                    {
                        siData[currentName] = data;
                        currentName = null;
                    }
                    break;
            }
        }

        return new CklStigInfo(
            StigId: siData.GetValueOrDefault("stigid"),
            Version: siData.GetValueOrDefault("version"),
            ReleaseInfo: siData.GetValueOrDefault("releaseinfo"),
            Title: siData.GetValueOrDefault("title"));
    }

    private static ParsedCklEntry? ParseVuln(XmlReader reader)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cciRefs = new List<string>();
        string? status = null, findingDetails = null, comments = null,
                severityOverride = null, severityJustification = null;

        // STIG_DATA state machine: name element comes before data element
        string? pendingAttrName = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "VULN")
                break;

            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.Name)
            {
                case "VULN_ATTRIBUTE":
                    pendingAttrName = ReadElementText(reader);
                    break;

                case "ATTRIBUTE_DATA":
                    var attrData = ReadElementText(reader) ?? string.Empty;
                    if (!string.IsNullOrEmpty(pendingAttrName))
                    {
                        if (string.Equals(pendingAttrName, "CCI_REF", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrWhiteSpace(attrData))
                                cciRefs.Add(attrData);
                        }
                        else
                        {
                            attributes[pendingAttrName] = attrData;
                        }
                        pendingAttrName = null;
                    }
                    break;

                case "STATUS":               status               = ReadElementText(reader); break;
                case "FINDING_DETAILS":      findingDetails       = ReadElementText(reader); break;
                case "COMMENTS":             comments             = ReadElementText(reader); break;
                case "SEVERITY_OVERRIDE":    severityOverride     = ReadElementText(reader); break;
                case "SEVERITY_JUSTIFICATION": severityJustification = ReadElementText(reader); break;
            }
        }

        var vulnId = attributes.GetValueOrDefault("Vuln_Num") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(vulnId))
            return null; // Skip VULN entries without a VulnId

        return new ParsedCklEntry(
            VulnId:               vulnId,
            RuleId:               attributes.GetValueOrDefault("Rule_ID"),
            StigVersion:          attributes.GetValueOrDefault("Rule_Ver"),
            RuleTitle:            attributes.GetValueOrDefault("Rule_Title"),
            Severity:             attributes.GetValueOrDefault("Severity") ?? string.Empty,
            Status:               status ?? string.Empty,
            FindingDetails:       NullIfEmpty(findingDetails),
            Comments:             NullIfEmpty(comments),
            SeverityOverride:     NullIfEmpty(severityOverride),
            SeverityJustification: NullIfEmpty(severityJustification),
            CciRefs:              cciRefs,
            GroupTitle:           attributes.GetValueOrDefault("Group_Title"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the text content of the current element, then advances past its end tag.
    /// Returns null if the element is empty or contains only whitespace.
    /// </summary>
    private static string? ReadElementText(XmlReader reader)
    {
        if (reader.IsEmptyElement) return null;
        var text = reader.ReadElementContentAsString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
