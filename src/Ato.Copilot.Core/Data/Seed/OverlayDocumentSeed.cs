using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Data.Seed;

/// <summary>
/// Seed data for CNSSI-1253 SC and SI overlay guidance.
/// These extend NIST 800-53 Rev 5 for National Security Systems.
/// </summary>
public static class OverlayDocumentSeed
{
    public static IEnumerable<OverlayDocument> GetSeedData() =>
    [
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Type = "CNSSI-1253",
            Title = "CNSSI-1253 SC Family Overlay — System and Communications Protection",
            ControlId = "SC-1",
            Content = "CNSSI-1253 NSS Overlay for SC-1: Organizations operating National Security Systems must implement SC controls at the HIGH baseline. CNSS-approved cryptographic modules (FIPS 140-3 validated) are mandatory for all data in transit and at rest. Refer to CNSSI No. 1253 Annex D for NSS-specific parameter assignments.",
            SourceReference = "CNSSI No. 1253 Annex D, SC Family",
            IsActive = true,
            CreatedBy = "seed"
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Type = "CNSSI-1253",
            Title = "CNSSI-1253 SI Family Overlay — System and Information Integrity",
            ControlId = "SI-1",
            Content = "CNSSI-1253 NSS Overlay for SI-1: National Security Systems require continuous integrity monitoring at the HIGH baseline. Anti-malware tools must be CNSS-approved. Integrity verification of software and firmware is mandatory before deployment. Refer to CNSSI No. 1253 Annex D for SI parameter assignments.",
            SourceReference = "CNSSI No. 1253 Annex D, SI Family",
            IsActive = true,
            CreatedBy = "seed"
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
            Type = "SECNAVINST",
            Title = "SECNAVINST 5239.3C — Navy RMF Policy Overlay for AC Controls",
            ControlId = "AC-1",
            Content = "SECNAVINST 5239.3C requires all DON information systems to implement access control policies consistent with the DON RMF Process Guide. AC-1 must reference SECNAVINST 5239.3C as the governing authority. CIO N2/N6 is the DAA for Navy IT systems. Access control policies must be reviewed annually.",
            SourceReference = "SECNAVINST 5239.3C, Para 5.a",
            IsActive = true,
            CreatedBy = "seed"
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000004"),
            Type = "DoD-8140",
            Title = "DoD 8140 Cyberspace Workforce Overlay for IA Controls",
            ControlId = "AT-1",
            Content = "DoD Instruction 8140.01 requires all DoD personnel with privileged access to information systems to hold DCWF role-qualified certifications. AT-1 policies must reference DoDI 8140.01 and specify the applicable DCWF work roles. IAT Level II or III certification required for system administrators.",
            SourceReference = "DoDI 8140.01, Enclosure 3",
            IsActive = true,
            CreatedBy = "seed"
        }
    ];
}
