using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ato.Copilot.Core.Interfaces.Compliance;

// FedRAMP Schematron advisory validation types -- Feature 076 T007

/// <summary>
/// Result of a FedRAMP Schematron advisory validation pass.
/// This result is always advisory -- it never blocks document export.
/// </summary>
/// <param name="IsCompliant">True when no Schematron violations were detected.</param>
/// <param name="Violations">Zero or more typed violations found during validation.</param>
/// <param name="AdvisoryOnly">Always <c>true</c>; callers must not treat violations as blocking.</param>
public sealed record FedRampSchematronResult(
    bool IsCompliant,
    List<SchematronViolation> Violations,
    bool AdvisoryOnly = true);

/// <summary>
/// A single FedRAMP Schematron constraint violation.
/// </summary>
/// <param name="Severity">Severity level: <c>high</c>, <c>medium</c>, or <c>low</c>.</param>
/// <param name="Path">JSON path or logical location within the OSCAL document.</param>
/// <param name="Message">Human-readable description of the violated constraint.</param>
/// <param name="RuleId">Optional Schematron rule identifier (e.g. <c>req-impl-status</c>).</param>
public sealed record SchematronViolation(
    string Severity,
    string Path,
    string Message,
    string? RuleId = null);

/// <summary>
/// Advisory-only FedRAMP Schematron validation service.
/// Runs key FedRAMP constraint checks against an OSCAL JSON document and
/// returns typed violations. <b>Never throws on violations and never blocks export.</b>
/// </summary>
public interface IFedRampSchematronService
{
    /// <summary>
    /// Validates an OSCAL JSON document against FedRAMP Schematron constraints
    /// for the given document type.
    /// </summary>
    /// <param name="oscalJson">The OSCAL document serialised as JSON.</param>
    /// <param name="documentType">
    /// Document type: <c>ssp</c>, <c>poam</c>, <c>assessment-results</c>, or <c>assessment-plan</c>.
    /// </param>
    /// <returns>
    /// A <see cref="FedRampSchematronResult"/> with <see cref="FedRampSchematronResult.AdvisoryOnly"/>
    /// always set to <c>true</c>.
    /// </returns>
    Task<FedRampSchematronResult> ValidateAsync(string oscalJson, string documentType);
}
