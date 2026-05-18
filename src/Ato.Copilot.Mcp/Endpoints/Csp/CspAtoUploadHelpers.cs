using Ato.Copilot.Core.Configuration.Tenancy;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

/// <summary>
/// Shared orchestration for the wizard-time
/// <c>POST /api/csp/onboarding/atos/upload</c> endpoint and the
/// post-onboarding <c>POST /api/csp/inherited-components/import</c> endpoint
/// (Feature 048 US9 / FR-099 / FR-104). Both endpoints expose the same
/// pipeline: parse → extract → map → persist capabilities, with shared
/// totals + per-file telemetry.
/// </summary>
/// <remarks>
/// <para>
/// The orchestration is HTTP-agnostic: it operates on a sequence of
/// <see cref="UploadFile"/> tuples (raw <see cref="Stream"/>, content type,
/// file name) and returns a structured <see cref="UploadResult"/> envelope
/// that the endpoint layer renders as JSON.
/// </para>
/// <para>
/// Failure semantics:
/// <list type="bullet">
///   <item>Unknown content type or file extension that the parser cannot
///         dispatch → <see cref="UploadException"/> with code
///         <c>UNSUPPORTED_ATO_DOCUMENT</c> (HTTP 400).</item>
///   <item>Malformed payload that the parser rejects via
///         <see cref="InvalidDataException"/> →
///         <see cref="UploadException"/> with code <c>PARSE_FAILED</c>
///         (HTTP 400).</item>
///   <item>Successful parse with zero candidate components → file is
///         counted as accepted (<c>documentsAccepted++</c>), zero
///         components/capabilities, no exception.</item>
///   <item>AI mapper unavailable for any component →
///         <c>aiMappingAvailable = false</c> on the aggregate result; the
///         underlying components are still persisted with zero
///         auto-generated capabilities (operator can re-run remap from the
///         dashboard).</item>
/// </list>
/// </para>
/// </remarks>
internal static class CspAtoUploadHelpers
{
    /// <summary>One uploaded file ready for parser dispatch.</summary>
    public sealed record UploadFile(Stream Content, string ContentType, string FileName);

    /// <summary>Per-file row in the upload response.</summary>
    public sealed record FileResult(
        string FileName,
        string SourceFormat,
        int ComponentsExtracted,
        int CapabilitiesMapped,
        int CapabilitiesNeedsReview);

    /// <summary>Aggregate result of an upload batch.</summary>
    public sealed record UploadResult(
        int DocumentsAccepted,
        int ComponentsExtracted,
        int CapabilitiesMapped,
        int CapabilitiesNeedsReview,
        bool AiMappingAvailable,
        IReadOnlyList<FileResult> Files);

    /// <summary>
    /// Domain exception that the endpoint layer maps directly onto
    /// <c>{ "errorCode": ErrorCode, "message": Message }</c>.
    /// </summary>
    public sealed class UploadException : Exception
    {
        public string ErrorCode { get; }
        public int StatusCode { get; }

        public UploadException(string errorCode, string message, int statusCode = 400)
            : base(message)
        {
            ErrorCode = errorCode;
            StatusCode = statusCode;
        }
    }

    /// <summary>
    /// Parses every file, extracts candidate components into the database
    /// under the supplied <paramref name="cspProfileId"/>, runs the AI
    /// capability mapper per component, persists the resulting capabilities,
    /// and returns the aggregate counts. The same actor stamps every row.
    /// </summary>
    public static async Task<UploadResult> OrchestrateAsync(
        IReadOnlyList<UploadFile> files,
        Guid cspProfileId,
        string actor,
        ICspAtoDocumentParser parser,
        ICspComponentExtractionService extractionService,
        ICspCapabilityMappingService mappingService,
        IOptions<CspInheritedOptions> options,
        Func<IReadOnlyList<CspInheritedCapability>, CancellationToken, Task> persistCapabilities,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(extractionService);
        ArgumentNullException.ThrowIfNull(mappingService);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(persistCapabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (files.Count == 0)
        {
            throw new UploadException(
                "VALIDATION_FAILED",
                "At least one file must be supplied.",
                statusCode: 422);
        }

        var threshold = options.Value.MappingConfidenceThreshold;
        var fileResults = new List<FileResult>(files.Count);
        var documentsAccepted = 0;
        var totalComponents = 0;
        var totalMapped = 0;
        var totalNeedsReview = 0;
        var aiMappingAvailable = true;

        foreach (var file in files)
        {
            // ── Parse ───────────────────────────────────────────────────
            ParsedAtoDocument parsed;
            try
            {
                parsed = await parser
                    .ParseAsync(file.Content, file.ContentType, file.FileName, ct)
                    .ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                throw new UploadException(
                    "UNSUPPORTED_ATO_DOCUMENT",
                    $"Content type '{file.ContentType}' (file '{file.FileName}') is not in the ATO-document allow-list: {ex.Message}");
            }
            catch (InvalidDataException ex)
            {
                throw new UploadException(
                    "PARSE_FAILED",
                    $"Failed to parse '{file.FileName}': {ex.Message}");
            }

            // ── Extract + persist components ────────────────────────────
            var components = await extractionService
                .ExtractAsync(parsed, cspProfileId, actor, ct)
                .ConfigureAwait(false);

            var fileMapped = 0;
            var fileNeedsReview = 0;

            // ── Map capabilities + persist ──────────────────────────────
            foreach (var component in components)
            {
                var mappingResult = await mappingService
                    .MapAsync(component, threshold, ct)
                    .ConfigureAwait(false);

                if (!mappingResult.AiMappingAvailable)
                {
                    aiMappingAvailable = false;
                }

                var capabilities = new List<CspInheritedCapability>(
                    mappingResult.Mapped.Count + mappingResult.NeedsReview.Count);
                capabilities.AddRange(mappingResult.Mapped);
                capabilities.AddRange(mappingResult.NeedsReview);
                if (capabilities.Count > 0)
                {
                    await persistCapabilities(capabilities, ct).ConfigureAwait(false);
                }

                fileMapped += mappingResult.Mapped.Count;
                fileNeedsReview += mappingResult.NeedsReview.Count;
            }

            documentsAccepted++;
            totalComponents += components.Count;
            totalMapped += fileMapped;
            totalNeedsReview += fileNeedsReview;

            fileResults.Add(new FileResult(
                FileName: file.FileName,
                SourceFormat: parsed.Format.ToString(),
                ComponentsExtracted: components.Count,
                CapabilitiesMapped: fileMapped,
                CapabilitiesNeedsReview: fileNeedsReview));
        }

        return new UploadResult(
            DocumentsAccepted: documentsAccepted,
            ComponentsExtracted: totalComponents,
            CapabilitiesMapped: totalMapped,
            CapabilitiesNeedsReview: totalNeedsReview,
            AiMappingAvailable: aiMappingAvailable,
            Files: fileResults);
    }
}
