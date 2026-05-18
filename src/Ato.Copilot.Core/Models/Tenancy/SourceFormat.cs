namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Source format of the CSP ATO artifact that produced a
/// <see cref="CspInheritedComponent"/> (Feature 048 FR-100).
/// </summary>
public enum SourceFormat
{
    /// <summary>PDF SSP / SAR / etc. parsed via <c>ISspPdfExtractionService</c> (Feature 047).</summary>
    Pdf = 0,

    /// <summary>DOCX parsed via <c>DocumentFormat.OpenXml</c> text walker.</summary>
    Docx = 1,

    /// <summary>OSCAL JSON SSP parsed via <c>OscalSspJsonParser</c> (net-new in Feature 048).</summary>
    OscalJson = 2,

    /// <summary>FedRAMP / SAR / POAM XLSX parsed via <c>ClosedXML</c>.</summary>
    Xlsx = 3,

    /// <summary>eMASS export ZIP. Parser dispatches to embedded files.</summary>
    EmassZip = 4,

    /// <summary>Created manually by a CSP-Admin via the dashboard.</summary>
    Manual = 5,
}
