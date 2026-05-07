namespace Ato.Copilot.Core.Onboarding;

/// <summary>
/// Canonical <see cref="Ato.Copilot.Core.Interfaces.Storage.IFileStorageProvider"/>
/// key generators for onboarding-wizard artifacts. Centralizing these helpers ensures
/// the layout described in plan.md §"Storage container layout" is honored consistently
/// across the storage, retention, and audit paths (FR-090 / FR-091).
/// </summary>
public static class WizardStorageKeys
{
    /// <summary>Custom document templates (Step 6).</summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="templateId"><see cref="Ato.Copilot.Core.Models.Onboarding.OrganizationDocumentTemplate.Id"/>.</param>
    /// <param name="filename">Original client filename (sanitized by caller).</param>
    public static string Template(Guid tenantId, Guid templateId, string filename)
        => $"wizard/templates/{tenantId:D}/{templateId:D}/{filename}";

    /// <summary>eMASS bulk imports (Step 3).</summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="sessionId"><see cref="Ato.Copilot.Core.Models.Onboarding.EmassImportSession.Id"/>.</param>
    /// <param name="filename">Original client filename (sanitized by caller).</param>
    public static string EmassImport(Guid tenantId, Guid sessionId, string filename)
        => $"wizard/imports/emass/{tenantId:D}/{sessionId:D}/{filename}";

    /// <summary>SSP PDF ingestion (Step 4).</summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="sessionId"><see cref="Ato.Copilot.Core.Models.Onboarding.SspPdfImportSession.Id"/>.</param>
    /// <param name="filename">Original client filename (sanitized by caller).</param>
    public static string SspPdfImport(Guid tenantId, Guid sessionId, string filename)
        => $"wizard/imports/ssp-pdf/{tenantId:D}/{sessionId:D}/{filename}";
}
