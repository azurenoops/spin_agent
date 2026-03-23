using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// SAR CRUD, lifecycle management, and Word document export.
/// </summary>
public interface ISecurityAssessmentReportService
{
    Task<SecurityAssessmentReport> CreateSarAsync(
        string systemId,
        CreateSarRequest request,
        string createdBy = "mcp-user",
        CancellationToken cancellationToken = default);

    Task<SecurityAssessmentReport?> GetSarAsync(
        string sarId,
        CancellationToken cancellationToken = default);

    Task<SecurityAssessmentReport?> GetSarForSystemAsync(
        string systemId,
        CancellationToken cancellationToken = default);

    Task<SarSection> EditSectionAsync(
        string sarId,
        SarSectionType sectionType,
        EditSarSectionRequest request,
        string editedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    Task<SecurityAssessmentReport> SubmitForReviewAsync(
        string sarId,
        string submittedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    Task<SecurityAssessmentReport> ReviewSarAsync(
        string sarId,
        ReviewSarRequest request,
        string reviewedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    Task<Stream> ExportToWordAsync(
        string sarId,
        CancellationToken cancellationToken = default);
}
