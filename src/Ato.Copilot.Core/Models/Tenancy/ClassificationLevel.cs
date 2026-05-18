namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Default information classification level for documents and artifacts
/// produced under a <see cref="Tenant"/>.
/// Drives default markings on generated SSP/SAP/SAR documents.
/// See feature 048 spec FR-001.
/// </summary>
public enum ClassificationLevel
{
    /// <summary>Public / unclassified.</summary>
    Unclassified = 0,

    /// <summary>Controlled Unclassified Information.</summary>
    CUI = 1,

    /// <summary>Secret (Collateral).</summary>
    Secret = 2
}
