# Provider offerings demonstration catalog

**SYNTHETIC DEMONSTRATION ONLY**

This locally generated, fictional portfolio demonstrates a provider package review;
it does not represent Microsoft service commitments, DoD authorization, an IL5
approval, a customer entitlement, or an actual provider assessment. “Azure IL5”
is a scenario label, not an authorization assertion. All dates, measurements,
service teams, references, and evidence are invented.

## Design and acceptance record

Two independent service boundaries are required:

- **Azure IL5 · Shared services**: provider-managed Azure Government scenario,
  eight capabilities, available baseline **1.2**, proposed **1.3** with exactly
  two capability updates (audit collection and backup recovery).
- **Microsoft 365 · Collaboration**: SaaS scenario, five collaboration
  capabilities, separate service boundary, release **1.0**. “Flank Speed Demo”
  is a fictional tenant display name, not the actual Flank Speed environment.

Keep published-baseline sources readable. Do not manufacture encrypted files,
real access grants, connectors, subscriptions, approvals, or operational evidence.
The moderate logging evidence-delivery delay is a disclosed synthetic finding,
not a broken parser fixture. Detailed working papers stay PRIVATE.

## Build and inspect locally

From the repository root:

```bash
python3 demos/provider-offerings/build_demo.py
python3 demos/provider-offerings/validate_demo.py
```

The generator uses only Python's standard library. It writes only beneath this
directory. Outputs include editable Markdown and DOCX, printable PDF, CSV matrices
and indexes, structured JSON, and deterministic ZIP archives. No application,
database, network, identity, or runtime operation is performed.

## Ingestion rules

Read `CATALOG.md` for the generated file inventory, counts, and walkthrough.
Upload **only `review-source.zip`** for each offering to obtain deterministic
review candidates without AI. Each ZIP contains exactly six native structured
JSON documents: scope/components, capabilities, customer duties, synthetic
reference/decision, assessment finding, and corrective plan. Every document uses
only recognized declaration collections and fields, so the parser can account
for the entire baseline without a model call. PDF/DOCX/CSV are companion evidence, not an
alternate structured inventory. `customer-bundle.zip` is for customer download,
**not** the deterministic analyzer input.

The parser contract is the native structured inventory shown in
`tests/Ato.Copilot.Tests.Integration/Tenancy/ProviderMissionWorkflowHttpTests.cs`
(`Source`), with typed claims from
`CspPackageAnalyzer.StructuredClaims.cs` and `CspPackageClaim.cs`.
This is **not OSCAL**. Recognized top-level collections are used exclusively;
synthetic labels and duties are in supported string fields, not unknown metadata.
`ExtractJson` marks native `JsonInventoryOnly` declarations complete;
`AnalyzeSemanticsAsync` selects only processed entries that are **not** complete.
`CompleteFamilyCoverage` explicitly accounts for all five claim families on the
deterministic path; a family absent from a particular document is
`NoDeclarations`, not an unanalyzed family. A six-document ZIP has seven analyzer
entries when its container is counted. Verify these expectations in the actual
receipt; no runtime analyzer result is fabricated by the local validator.
Review, source acceptance, publication, mission association, and adoption must
still be performed through the real application. Source claims do not grant
authorization or resolve customer responsibilities.

Release labels 1.2, 1.3 and 1.0 describe source-document editions only. The
application may use integer publication revisions; do not invent a semantic-version
API or confuse a document edition with a persisted publication revision.

## Synthetic hosting identities

The internal manifest supplies explicit `hostingScopes` and `environments` per
dataset for the separately reviewed loader workflow. Azure uses the real contract
discriminator `kind: Azure` and cloud enum `AzureUSGovernment`, with invented
directory/subscription GUIDs and one fictional resource-group path. Microsoft 365
uses `kind: Service`, environment `Microsoft365DoD`, and a clearly synthetic
service/tenant reference—never an Azure subscription or a discovered connector.
These exact identities appear in the service guide and supported boundary claim
fields (`scope`, `environment`, `resourceIds`) in source document 01. Manifest-only
hosting objects are **not** inserted as unsupported fields in an analyzer document.
They grant no access and must never be used to contact a real cloud environment.

The typed demonstration decision uses exact `statusAsStated: Approved`, matching
the supported recorded-source vocabulary in `ProviderDecisionEligibility`.
Its decision type remains **fictional demonstration distribution approval; NOT
ATO**. The synthetic subject, issuer, scope, conditions and qualifications must
remain attached to that status. “Approved” does not mean approved for real
operation; a loader must not convert this example to an ATO assertion.

The generated internal manifest maps stable source IDs to exact JSON locators,
documents, controls, and SHA-256 hashes. It is not an input package and must not
be uploaded with the inventory. PRIVATE documents are deliberately absent from
all customer and review-source archives; set actual attachment access rules in
the application as well—filenames alone are not access control.

## Validation limits

The local validator checks generated structure, links, hashes, archive contents,
DOCX XML, PDF envelope, synthetic markings, and the two-update release diff.
It does not claim to execute the C# analyzer or publish a release. The ingesting
operator must verify the real API result against the candidate counts in
`CATALOG.md` and inspect PDF/DOCX rendering locally before presentation.
