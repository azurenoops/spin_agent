using System.IO.Compression;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Templates.Validators;

/// <summary>
/// Result of a template-validation pass. Warnings are surfaced to the admin in
/// the Step 6 UI; <see cref="MissingPlaceholders"/> lists known placeholder
/// tokens that the org template must contain (FR-082..FR-085).
/// </summary>
public sealed record TemplateValidationOutcome(
    bool IsCompliant,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> MissingPlaceholders);

/// <summary>
/// Validates an uploaded org template against the matching slot's structural
/// requirements (research §R5).
/// </summary>
public interface IOrganizationTemplateValidator
{
    Task<TemplateValidationOutcome> ValidateAsync(
        Stream content,
        string originalFileName,
        CancellationToken ct = default);
}

/// <summary>
/// Validates a DOCX template by scanning the embedded <c>word/document.xml</c>
/// for required placeholder tokens. Templates without a text layer or missing
/// any required placeholder are flagged non-compliant with a warning per gap.
/// </summary>
public sealed class DocxTemplateValidator : IOrganizationTemplateValidator
{
    private readonly IReadOnlyList<string> _required;

    public DocxTemplateValidator(IEnumerable<string> requiredPlaceholders)
    {
        _required = requiredPlaceholders.ToList();
    }

    public async Task<TemplateValidationOutcome> ValidateAsync(
        Stream content, string originalFileName, CancellationToken ct = default)
    {
        await using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        string body;
        try
        {
            using var zip = new ZipArchive(buffered, ZipArchiveMode.Read, leaveOpen: false);
            var entry = zip.GetEntry("word/document.xml");
            if (entry is null)
            {
                return new TemplateValidationOutcome(
                    false,
                    new[] { $"'{originalFileName}' is not a valid DOCX (missing word/document.xml)." },
                    Array.Empty<string>());
            }
            using var sr = new StreamReader(entry.Open());
            body = await sr.ReadToEndAsync(ct);
        }
        catch (InvalidDataException)
        {
            return new TemplateValidationOutcome(
                false,
                new[] { $"'{originalFileName}' is not a valid DOCX archive." },
                Array.Empty<string>());
        }

        var missing = _required
            .Where(p => !body.Contains(p, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var warnings = missing
            .Select(p => $"Required placeholder '{p}' is missing from the template body.")
            .ToList();
        return new TemplateValidationOutcome(missing.Count == 0, warnings, missing);
    }
}
