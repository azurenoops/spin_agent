namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Marker interface for the CSP ATO-document parser dispatcher (Feature 048
/// FR-101). T198 owns the actual parsing surface (PDF / OSCAL JSON / DOCX /
/// XLSX dispatch) — this empty interface exists so the FR-110 startup audit
/// (<c>CspInheritanceReuseAuditHealthCheck</c>) can begin enforcing
/// "exactly one DI registration" the moment T198 / T206 land, without
/// requiring a follow-up edit to the audit's enforced-interface list.
/// </summary>
/// <remarks>
/// <para>
/// Per the Reuse-First Audit (<c>specs/048-tenant-isolation/research-reuse-audit.md</c>),
/// the dispatcher reuses existing parsers (<c>SspPdfExtractionService</c> for
/// PDF, the existing <c>DocumentFormat.OpenXml</c> + <c>ClosedXML</c> packages
/// for DOCX / XLSX, and a net-new minimal <c>OscalSspJsonParser</c> for
/// OSCAL — Feature 022 is OSCAL export-only). The dispatcher itself owns
/// zero parsing logic — it only routes <c>contentType</c> to the correct
/// existing parser.
/// </para>
/// <para>
/// The interface body is populated by T198 (RED test) and T202 (GREEN
/// implementation). Until then, the FR-110 health check no-ops against
/// this type because no DI registration exists.
/// </para>
/// </remarks>
public interface ICspAtoDocumentParser
{
    // Surface defined by T198 (test) + T202 (implementation).
    // Intentionally empty until those tasks land.
}
