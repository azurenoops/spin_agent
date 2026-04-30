using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Agents.Document.Tools;

/// <summary>
/// Thin adapter tool that returns document status/progress using existing compliance services.
/// </summary>
public class DocumentStatusTool : BaseTool
{
    private readonly ISspService _sspService;
    private readonly INarrativeGovernanceService _governanceService;
    private readonly IDocumentTemplateService _templateService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public DocumentStatusTool(
        ISspService sspService,
        INarrativeGovernanceService governanceService,
        IDocumentTemplateService templateService,
        ILogger<DocumentStatusTool> logger) : base(logger)
    {
        _sspService = sspService;
        _governanceService = governanceService;
        _templateService = templateService;
    }

    public override string Name => "document_status";

    public override string Description =>
        "Get document-centric SSP narrative progress, governance status, and available templates for a system.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["document_type"] = new() { Name = "document_type", Description = "Optional template filter (ssp, sar, poam, rar)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var documentType = GetArg<string>(arguments, "document_type");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var progress = await _sspService.GetNarrativeProgressAsync(systemId, null, cancellationToken);
            var governance = await _governanceService.GetNarrativeApprovalProgressAsync(systemId, null, cancellationToken);
            var templates = await _templateService.ListTemplatesAsync(documentType, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    documents_url = $"/systems/{systemId}/documents",
                    narrative_progress = new
                    {
                        total_controls = progress.TotalControls,
                        completed_narratives = progress.CompletedNarratives,
                        draft_narratives = progress.DraftNarratives,
                        missing_narratives = progress.MissingNarratives,
                        completion_percent = progress.OverallPercentage
                    },
                    governance_progress = new
                    {
                        total_controls = governance.TotalControls,
                        approved = governance.TotalApproved,
                        under_review = governance.TotalUnderReview,
                        draft = governance.TotalDraft,
                        needs_revision = governance.TotalNeedsRevision,
                        not_started = governance.TotalNotStarted,
                        approval_percent = governance.OverallApprovalPercent,
                        review_queue_count = governance.ReviewQueue.Count
                    },
                    available_templates = templates.Select(t => new
                    {
                        template_id = t.TemplateId,
                        template_name = t.TemplateName,
                        document_type = t.DocumentType,
                        is_default = t.IsDefault,
                        uploaded_at = t.UploadedAt.ToString("O")
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "document_status failed for {SystemId}", systemId);
            return Error("DOCUMENT_STATUS_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

/// <summary>
/// Validates and normalizes source document references (including SharePoint links).
/// </summary>
public class DocumentContextSourceTool : BaseTool
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public DocumentContextSourceTool(ILogger<DocumentContextSourceTool> logger) : base(logger)
    {
    }

    public override string Name => "document_context_sources";

    public override string Description =>
        "Normalize reference source links (SharePoint and other URLs) for narrative generation context.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["source_url"] = new() { Name = "source_url", Description = "Single source URL", Type = "string", Required = false },
        ["source_urls"] = new() { Name = "source_urls", Description = "Source URLs as JSON array or comma/newline-delimited list", Type = "string", Required = false }
    };

    public override Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var urls = SourceParser.GetUrls(arguments);

        if (urls.Count == 0)
            return Task.FromResult(Error("INVALID_INPUT", "Provide 'source_url' or 'source_urls'."));

        var normalized = urls.Select(SourceParser.ParseSource).ToList();
        sw.Stop();

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            status = "success",
            data = new
            {
                source_count = normalized.Count,
                sources = normalized
            },
            metadata = Meta(sw)
        }, JsonOpts));
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

/// <summary>
/// Selects an RMF template profile using existing template management service.
/// </summary>
public class DocumentTemplateSelectorTool : BaseTool
{
    private readonly IDocumentTemplateService _templateService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public DocumentTemplateSelectorTool(
        IDocumentTemplateService templateService,
        ILogger<DocumentTemplateSelectorTool> logger) : base(logger)
    {
        _templateService = templateService;
    }

    public override string Name => "document_select_template";

    public override string Description =>
        "List and select custom RMF templates to use for document and narrative generation workflows.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["document_type"] = new() { Name = "document_type", Description = "Optional type filter (ssp, sar, poam, rar)", Type = "string", Required = false },
        ["template_id"] = new() { Name = "template_id", Description = "Optional template ID to select", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var documentType = GetArg<string>(arguments, "document_type");
        var templateId = GetArg<string>(arguments, "template_id");

        try
        {
            var templates = await _templateService.ListTemplatesAsync(documentType, cancellationToken);
            var selected = string.IsNullOrWhiteSpace(templateId)
                ? templates.FirstOrDefault(t => t.IsDefault) ?? templates.FirstOrDefault()
                : templates.FirstOrDefault(t => string.Equals(t.TemplateId, templateId, StringComparison.OrdinalIgnoreCase));

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    selected_template = selected == null ? null : new
                    {
                        template_id = selected.TemplateId,
                        template_name = selected.TemplateName,
                        document_type = selected.DocumentType,
                        is_default = selected.IsDefault,
                        uploaded_by = selected.UploadedBy,
                        uploaded_at = selected.UploadedAt.ToString("O")
                    },
                    templates = templates.Select(t => new
                    {
                        template_id = t.TemplateId,
                        template_name = t.TemplateName,
                        document_type = t.DocumentType,
                        is_default = t.IsDefault
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "document_select_template failed");
            return Error("DOCUMENT_TEMPLATE_SELECT_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

/// <summary>
/// Generates narrative content via existing SSP service and optionally saves draft,
/// while capturing reference sources/template context.
/// </summary>
public class DocumentNarrativeGenerateAdapterTool : BaseTool
{
    private readonly ISspService _sspService;
    private readonly IDocumentTemplateService _templateService;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IChatClient? _chatClient;
    private readonly GraphServiceClient? _graphClient;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public DocumentNarrativeGenerateAdapterTool(
        ISspService sspService,
        IDocumentTemplateService templateService,
        IHttpClientFactory? httpClientFactory,
        IChatClient? chatClient,
        GraphServiceClient? graphClient,
        ILogger<DocumentNarrativeGenerateAdapterTool> logger) : base(logger)
    {
        _sspService = sspService;
        _templateService = templateService;
        _httpClientFactory = httpClientFactory;
        _chatClient = chatClient;
        _graphClient = graphClient;
    }

    public override string Name => "document_generate_narrative";

    public override string Description =>
        "Generate a control narrative using existing SSP services, optionally applying reference documents and a custom template profile.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST control ID (e.g., AC-2)", Type = "string", Required = true },
        ["template_id"] = new() { Name = "template_id", Description = "Optional custom RMF template ID", Type = "string", Required = false },
        ["source_url"] = new() { Name = "source_url", Description = "Single source document URL", Type = "string", Required = false },
        ["source_urls"] = new() { Name = "source_urls", Description = "Source URLs as JSON array or comma/newline-delimited list", Type = "string", Required = false },
        ["save_draft"] = new() { Name = "save_draft", Description = "Save generated narrative to SSP (true/false, default false)", Type = "boolean", Required = false },
        ["change_reason"] = new() { Name = "change_reason", Description = "Optional change reason when save_draft=true", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var templateId = GetArg<string>(arguments, "template_id");
        var saveDraftRaw = GetArg<string>(arguments, "save_draft");
        var changeReason = GetArg<string>(arguments, "change_reason");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");

        var saveDraft = ParseBool(saveDraftRaw);
        var sources = SourceParser.GetUrls(arguments).Select(SourceParser.ParseSource).ToList();

        try
        {
            var suggestion = await _sspService.SuggestNarrativeAsync(systemId, controlId, cancellationToken);
            var narrativeText = suggestion.Narrative;

            var sourceEvidence = await FetchSourceEvidenceAsync(sources, controlId, cancellationToken);
            var aiNarrative = await GenerateAiNarrativeFromSourcesAsync(systemId, controlId, suggestion.Narrative, sourceEvidence, cancellationToken);
            if (!string.IsNullOrWhiteSpace(aiNarrative))
                narrativeText = aiNarrative;

            // Include user-selected source provenance and template marker in generated output.
            if (sources.Count > 0)
            {
                var sourceLines = string.Join("\n", sources.Select(s => $"- {s.FileName ?? s.SourceUrl}"));
                narrativeText += $"\n\nReference Sources Used:\n{sourceLines}";
            }

            if (sourceEvidence.Count > 0)
            {
                var groundedLines = string.Join("\n", sourceEvidence.Select(s => $"- {s.Label}: {s.Excerpt}"));
                narrativeText += $"\n\nSource Grounding Excerpts:\n{groundedLines}";
            }

            string? templateName = null;
            if (!string.IsNullOrWhiteSpace(templateId))
            {
                var templates = await _templateService.ListTemplatesAsync("ssp", cancellationToken);
                templateName = templates.FirstOrDefault(t => string.Equals(t.TemplateId, templateId, StringComparison.OrdinalIgnoreCase))?.TemplateName;
                if (!string.IsNullOrWhiteSpace(templateName))
                    narrativeText += $"\n\nTemplate Profile: {templateName}";
            }

            string? persistedId = null;
            int? version = null;
            if (saveDraft)
            {
                var effectiveReason = BuildChangeReason(changeReason, templateId, sources);
                var saved = await _sspService.WriteNarrativeAsync(
                    systemId,
                    controlId,
                    narrativeText,
                    null,
                    "mcp-user",
                    null,
                    effectiveReason,
                    cancellationToken);

                persistedId = saved.Id;
                version = saved.CurrentVersion;
            }

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    control_id = controlId,
                    suggested_narrative = narrativeText,
                    confidence = suggestion.Confidence,
                    references = suggestion.References,
                    source_context = sources,
                    source_evidence = sourceEvidence,
                    ai_used = !string.IsNullOrWhiteSpace(aiNarrative),
                    template_id = templateId,
                    template_name = templateName,
                    save_draft = saveDraft,
                    persisted_id = persistedId,
                    version_number = version,
                    documents_url = $"/systems/{systemId}/documents",
                    narratives_url = $"/systems/{systemId}/narratives"
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "document_generate_narrative failed for {SystemId}/{ControlId}", systemId, controlId);
            return Error("DOCUMENT_GENERATE_NARRATIVE_FAILED", ex.Message);
        }
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<SourceEvidenceSnippet>> FetchSourceEvidenceAsync(
        List<SourceParser.NormalizedSource> sources,
        string controlId,
        CancellationToken cancellationToken)
    {
        var results = new List<SourceEvidenceSnippet>();
        if (sources.Count == 0 || _httpClientFactory is null)
            return results;

        var client = _httpClientFactory.CreateClient("default");
        client.Timeout = TimeSpan.FromSeconds(12);

        foreach (var source in sources.Take(3))
        {
            if (!Uri.TryCreate(source.SourceUrl, UriKind.Absolute, out var uri))
                continue;

            if (source.SourceType == "SharePointLink")
            {
                var sharePointEvidence = await FetchSharePointSourceEvidenceAsync(source, controlId, cancellationToken);
                if (sharePointEvidence != null)
                {
                    results.Add(sharePointEvidence);
                    continue;
                }
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("Accept", "text/plain,text/html,application/json");

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogDebug("Source fetch skipped for {Url}: HTTP {Status}", source.SourceUrl, response.StatusCode);
                    continue;
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                if (!IsTextual(mediaType))
                {
                    Logger.LogDebug("Source fetch skipped for {Url}: unsupported media type {MediaType}", source.SourceUrl, mediaType);
                    continue;
                }

                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                var cleaned = NormalizeSourceText(raw, mediaType);
                if (string.IsNullOrWhiteSpace(cleaned))
                    continue;

                results.Add(new SourceEvidenceSnippet(
                    source.FileName ?? source.SourceDocId ?? source.SourceUrl,
                    source.SourceUrl,
                    mediaType,
                    Truncate(cleaned, 800)));
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Source fetch failed for {Url}", source.SourceUrl);
            }
        }

        return results;
    }

    private async Task<SourceEvidenceSnippet?> FetchSharePointSourceEvidenceAsync(
        SourceParser.NormalizedSource source,
        string controlId,
        CancellationToken cancellationToken)
    {
        if (_graphClient is null || !Uri.TryCreate(source.SourceUrl, UriKind.Absolute, out var uri))
            return null;

        // Sharing links (/:x:/r/... or _layouts/15/Doc.aspx?sourcedoc=) are resolved
        // via the Graph /shares endpoint, which handles external/cross-tenant links.
        if (IsSharePointSharingLink(uri))
            return await FetchSharePointBySharingLinkAsync(source, controlId, cancellationToken);

        if (!TryParseSharePointDocumentPath(uri, out var host, out var sitePath, out var documentPath))
            return null;

        try
        {
            var siteLookup = $"{host}:{sitePath}";
            var site = await _graphClient.Sites[siteLookup].GetAsync(cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(site?.Id))
                return null;

            var drive = await _graphClient.Sites[site.Id].Drive.GetAsync(cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(drive?.Id))
                return null;

            await using var contentStream = await _graphClient
                .Drives[drive.Id]
                .Root
                .ItemWithPath(documentPath)
                .Content
                .GetAsync(cancellationToken: cancellationToken);

            if (contentStream == null)
                return null;

            var fileName = source.FileName ?? Path.GetFileName(documentPath);
            var extracted = await ExtractDocumentTextAsync(contentStream, fileName, controlId, cancellationToken);
            if (string.IsNullOrWhiteSpace(extracted))
                return null;

            return new SourceEvidenceSnippet(
                fileName,
                source.SourceUrl,
                "application/vnd.microsoft.graph.sharepoint",
                Truncate(NormalizeSourceText(extracted, "text/plain"), 800));
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "SharePoint Graph retrieval failed for {Url}", source.SourceUrl);
            return null;
        }
    }

    /// <summary>
    /// Returns true for SharePoint sharing links that embed a document GUID
    /// (/_layouts/15/Doc.aspx?sourcedoc=... or /:x:/r/... patterns).
    /// These cannot be resolved by the path-based approach.
    /// </summary>
    private static bool IsSharePointSharingLink(Uri uri)
    {
        if (uri.AbsolutePath.Contains("/_layouts/15/", StringComparison.OrdinalIgnoreCase))
            return true;
        // e.g. https://tenant.sharepoint.com/:x:/r/teams/Site/...
        if (Regex.IsMatch(uri.AbsolutePath, @"^/:[a-z]:/[rs]/", RegexOptions.IgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Downloads a file referenced by a SharePoint sharing link using the
    /// Graph /shares/{encodedUrl}/driveItem endpoint.
    /// </summary>
    private async Task<SourceEvidenceSnippet?> FetchSharePointBySharingLinkAsync(
        SourceParser.NormalizedSource source,
        string controlId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Encode URL as base64url with the required u! prefix
            var urlBytes = System.Text.Encoding.UTF8.GetBytes(source.SourceUrl);
            var b64 = Convert.ToBase64String(urlBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var shareToken = $"u!{b64}";

            var driveItem = await _graphClient.Shares[shareToken].DriveItem
                .GetAsync(cancellationToken: cancellationToken);

            if (driveItem?.Id is null)
                return null;

            var fileName = source.FileName ?? driveItem.Name ?? "document.xlsx";

            await using var contentStream = await _graphClient.Shares[shareToken].DriveItem.Content
                .GetAsync(cancellationToken: cancellationToken);

            if (contentStream == null)
                return null;

            var extracted = await ExtractDocumentTextAsync(contentStream, fileName, controlId, cancellationToken);
            if (string.IsNullOrWhiteSpace(extracted))
                return null;

            Logger.LogInformation("Resolved SharePoint sharing link for {ControlId}: {File}", controlId, fileName);

            return new SourceEvidenceSnippet(
                fileName,
                source.SourceUrl,
                "application/vnd.microsoft.graph.sharepoint",
                Truncate(NormalizeSourceText(extracted, "text/plain"), 1200));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SharePoint sharing link resolution failed for {Url}: {Message}", source.SourceUrl, ex.Message);
            return null;
        }
    }

    private async Task<string?> GenerateAiNarrativeFromSourcesAsync(
        string systemId,
        string controlId,
        string baselineSuggestion,
        List<SourceEvidenceSnippet> sourceEvidence,
        CancellationToken cancellationToken)
    {
        if (_chatClient is null)
            return null;

        var sourceBlock = sourceEvidence.Count == 0
            ? "No source excerpts could be retrieved from the provided URLs."
            : string.Join("\n\n", sourceEvidence.Select((s, idx) =>
                $"Source {idx + 1}: {s.Label}\nURL: {s.SourceUrl}\nType: {s.MediaType}\nExcerpt:\n{s.Excerpt}"));

        var hasMsImpl = sourceEvidence.Any(s =>
            s.Excerpt.Contains("Microsoft Implementation Details:", StringComparison.OrdinalIgnoreCase));

        var prompt = $"""
You are drafting an RMF SSP control implementation narrative.
System ID: {systemId}
Control ID: {controlId}

Baseline narrative suggestion:
{baselineSuggestion}

Retrieved source excerpts:
{sourceBlock}

Instructions:
1) Produce a concise, audit-ready implementation narrative for the control.
2) Use source excerpts when relevant and do not invent technologies or processes.
3) If sources are insufficient, preserve baseline content and improve clarity only.
4) Do not include markdown headings.
{(hasMsImpl ? "5) Source excerpts contain 'Microsoft Implementation Details' from the official Microsoft NIST 800-53 Control Framework workbook. Treat this as the authoritative description of how the Azure/M365 platform implements the control. Incorporate this content as the primary implementation statement, then add system-specific context from the baseline suggestion." : string.Empty)}
""";

        try
        {
            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
            var text = response.Text?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "AI narrative synthesis failed for {SystemId}/{ControlId}", systemId, controlId);
            return null;
        }
    }

    private static bool IsTextual(string mediaType)
    {
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return true;

        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("text/xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSourceText(string raw, string mediaType)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = raw;
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) || raw.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            text = Regex.Replace(text, "<script[^>]*>[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<style[^>]*>[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
        }

        text = Regex.Replace(text, "\\s+", " ").Trim();
        return text;
    }

    private static bool TryParseSharePointDocumentPath(
        Uri uri,
        out string host,
        out string sitePath,
        out string documentPath)
    {
        host = uri.Host;
        sitePath = string.Empty;
        documentPath = string.Empty;

        var absolutePath = Uri.UnescapeDataString(uri.AbsolutePath);
        absolutePath = Regex.Replace(absolutePath, "^/:[^/]+:/[rs]/", "/", RegexOptions.IgnoreCase);

        var siteMatch = Regex.Match(
            absolutePath,
            "^/(?<kind>sites|teams)/(?<name>[^/]+)(?<rest>/.*)?$",
            RegexOptions.IgnoreCase);

        if (!siteMatch.Success)
            return false;

        sitePath = $"/{siteMatch.Groups["kind"].Value}/{siteMatch.Groups["name"].Value}";
        documentPath = siteMatch.Groups["rest"].Value.Trim('/');
        return !string.IsNullOrWhiteSpace(documentPath);
    }

    private static async Task<string?> ExtractDocumentTextAsync(
        Stream contentStream,
        string fileName,
        string controlId,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        using var memory = new MemoryStream();
        await contentStream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        if (extension == ".docx")
            return await ExtractDocxTextAsync(memory);

        if (extension == ".xlsx")
            return ExtractXlsxText(memory, controlId);

        if (extension is ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".html" or ".htm")
        {
            memory.Position = 0;
            using var reader = new StreamReader(memory, leaveOpen: true);
            return await reader.ReadToEndAsync();
        }

        return null;
    }

    private static async Task<string?> ExtractDocxTextAsync(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("word/document.xml");
        if (entry == null)
            return null;

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        var xml = await reader.ReadToEndAsync();
        var text = Regex.Replace(xml, "<[^>]+>", " ");
        return WebUtility.HtmlDecode(text);
    }

    private static string? ExtractXlsxText(Stream stream, string controlId)
    {
        stream.Position = 0;
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);
        var workbookPart = spreadsheet.WorkbookPart;
        if (workbookPart?.Workbook?.Sheets is null)
            return null;

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var controlRows = new List<string>();

        foreach (var sheet in workbookPart.Workbook.Sheets.OfType<Sheet>())
        {
            if (sheet.Id?.Value is null)
                continue;

            var worksheetPart = (WorksheetPart?)workbookPart.GetPartById(sheet.Id.Value);
            var sheetData = worksheetPart?.Worksheet?.GetFirstChild<SheetData>();
            if (sheetData is null)
                continue;

            var rows = sheetData.Elements<Row>().ToList();
            if (rows.Count == 0)
                continue;

            var headerCells = GetRowValues(rows[0], sharedStrings);
            var normalizedHeaders = headerCells.Select(NormalizeHeader).ToList();

            var controlIdx = FindColumnIndex(normalizedHeaders, "control", "controlid", "nistcontrol", "controlidentifier", "controlnumber");
            // Microsoft NIST 800-53 Control Framework workbook columns
            var msImplIdx = FindColumnIndex(normalizedHeaders,
                "microsoftimplementationdetails", "microsoftazureimplementation",
                "microsoftresponsibility", "microsoftazureresponsibility",
                "implementationdetails", "microsoftimplementation");
            var customerRespIdx = FindColumnIndex(normalizedHeaders,
                "customerresponsibility", "customerresponsibilitydescription",
                "customersresponsibility");
            var activityNameIdx = FindColumnIndex(normalizedHeaders, "activityname", "activity", "name");
            var activityDescIdx = FindColumnIndex(normalizedHeaders, "activitydescription", "description", "activitydetails");

            if (controlIdx < 0 && msImplIdx < 0 && activityNameIdx < 0 && activityDescIdx < 0)
                continue;

            foreach (var row in rows.Skip(1))
            {
                var values = GetRowValues(row, sharedStrings);
                if (values.Count == 0)
                    continue;

                if (controlIdx >= 0)
                {
                    var controlValue = GetValue(values, controlIdx);
                    if (!IsControlMatch(controlValue, controlId))
                        continue;
                }

                // Prefer Microsoft Implementation Details when present (authoritative)
                if (msImplIdx >= 0)
                {
                    var msImpl = GetValue(values, msImplIdx);
                    if (!string.IsNullOrWhiteSpace(msImpl))
                    {
                        var customerResp = customerRespIdx >= 0 ? GetValue(values, customerRespIdx) : string.Empty;
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"Microsoft Implementation Details: {msImpl}");
                        if (!string.IsNullOrWhiteSpace(customerResp))
                            sb.AppendLine($"Customer Responsibility: {customerResp}");
                        controlRows.Add(sb.ToString().Trim());
                        if (controlRows.Count >= 3)
                            break;
                        continue;
                    }
                }

                var activityName = activityNameIdx >= 0 ? GetValue(values, activityNameIdx) : string.Empty;
                var activityDescription = activityDescIdx >= 0 ? GetValue(values, activityDescIdx) : string.Empty;

                if (string.IsNullOrWhiteSpace(activityName) && string.IsNullOrWhiteSpace(activityDescription))
                    continue;

                var formatted = $"Activity Name: {activityName}\nActivity Description: {activityDescription}";
                controlRows.Add(formatted);
                if (controlRows.Count >= 3)
                    break;
            }

            if (controlRows.Count > 0)
                break;
        }

        if (controlRows.Count == 0)
            return null;

        return string.Join("\n\n", controlRows);
    }

    private static List<string> GetRowValues(Row row, SharedStringTable? sharedStrings)
    {
        var values = new List<string>();
        foreach (var cell in row.Elements<Cell>())
        {
            values.Add(GetCellValue(cell, sharedStrings));
        }

        return values;
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        var raw = cell.CellValue?.Text ?? cell.InnerText ?? string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(raw, out var sharedIndex) &&
            sharedStrings is not null)
        {
            return sharedStrings.ElementAtOrDefault(sharedIndex)?.InnerText?.Trim() ?? string.Empty;
        }

        return raw.Trim();
    }

    private static int FindColumnIndex(IReadOnlyList<string> headers, params string[] candidates)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (candidates.Any(candidate => header.Contains(candidate, StringComparison.OrdinalIgnoreCase)))
                return i;
        }

        return -1;
    }

    private static string NormalizeHeader(string value) =>
        Regex.Replace(value ?? string.Empty, "[^a-zA-Z0-9]", string.Empty).ToLowerInvariant();

    private static string GetValue(IReadOnlyList<string> values, int index) =>
        index >= 0 && index < values.Count ? values[index].Trim() : string.Empty;

    private static bool IsControlMatch(string sourceControl, string expectedControl)
    {
        var normalizedSource = NormalizeControlId(sourceControl);
        var normalizedExpected = NormalizeControlId(expectedControl);
        return !string.IsNullOrWhiteSpace(normalizedSource)
            && !string.IsNullOrWhiteSpace(normalizedExpected)
            && normalizedSource.Equals(normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeControlId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return Regex.Replace(value.Trim().ToUpperInvariant(), "[^A-Z0-9]", string.Empty);
    }

    private static string Truncate(string value, int maxLen)
    {
        if (value.Length <= maxLen)
            return value;

        return value[..maxLen].TrimEnd() + "...";
    }

    private static string BuildChangeReason(string? changeReason, string? templateId, List<SourceParser.NormalizedSource> sources)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(changeReason))
            parts.Add(changeReason.Trim());
        if (!string.IsNullOrWhiteSpace(templateId))
            parts.Add($"template:{templateId}");
        if (sources.Count > 0)
        {
            var ids = sources
                .Select(s => s.SourceDocId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var joined = string.Join(",", ids);
            if (!string.IsNullOrWhiteSpace(joined))
                parts.Add($"sources:{joined}");
        }

        return parts.Count == 0 ? "DocumentAgent generated narrative draft" : string.Join(" | ", parts);
    }

    private sealed record SourceEvidenceSnippet(
        string Label,
        string SourceUrl,
        string MediaType,
        string Excerpt);

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

internal static class SourceParser
{
    internal record NormalizedSource(
        string SourceType,
        string SourceUrl,
        string? SourceDocId,
        string? FileName,
        bool IsSupported,
        string? Warning,
        string RetrievedAtUtc);

    internal static List<string> GetUrls(Dictionary<string, object?> args)
    {
        var urls = new List<string>();
        var single = GetStringArg(args, "source_url");
        var many = GetStringArg(args, "source_urls");

        if (!string.IsNullOrWhiteSpace(single))
            urls.Add(single.Trim());

        if (!string.IsNullOrWhiteSpace(many))
        {
            var parsed = ParseMany(many);
            urls.AddRange(parsed);
        }

        return urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static NormalizedSource ParseSource(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return new NormalizedSource(
                "Unknown",
                sourceUrl,
                null,
                null,
                false,
                "Invalid URL format",
                DateTime.UtcNow.ToString("O"));
        }

        var host = uri.Host.ToLowerInvariant();
        var isSharePoint = host.Contains("sharepoint.com", StringComparison.OrdinalIgnoreCase);
        var sourceDocId = isSharePoint ? GetQueryValue(uri, "sourcedoc") : null;
        var fileName = GetQueryValue(uri, "file") ?? GetPathFileName(uri);

        if (!string.IsNullOrWhiteSpace(sourceDocId))
            sourceDocId = sourceDocId.Trim('{', '}');

        return new NormalizedSource(
            isSharePoint ? "SharePointLink" : "ExternalLink",
            sourceUrl,
            sourceDocId,
            fileName,
            true,
            null,
            DateTime.UtcNow.ToString("O"));
    }

    private static List<string> ParseMany(string input)
    {
        var trimmed = input.Trim();

        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<List<string>>(trimmed);
                return arr ?? new List<string>();
            }
            catch
            {
                // Fall back to delimiter split below.
            }
        }

        return trimmed
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static string? GetStringArg(Dictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value is null)
            return null;

        if (value is string s)
            return s;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String)
                return je.GetString();
            return je.GetRawText();
        }

        return value.ToString();
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var pairs = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;

            var k = Uri.UnescapeDataString(pair[..idx]);
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                continue;

            var v = pair[(idx + 1)..];
            return Uri.UnescapeDataString(v.Replace('+', ' '));
        }

        return null;
    }

    private static string? GetPathFileName(Uri uri)
    {
        var segments = uri.Segments;
        if (segments.Length == 0)
            return null;

        var last = segments[^1].Trim('/');
        if (string.IsNullOrWhiteSpace(last))
            return null;

        return Uri.UnescapeDataString(last);
    }
}
