# Synthetic CSP ATO test package

All content is fictional and clearly marked SYNTHETIC TEST DATA / NOT A VALID ATO. These are test fixtures, not an actual authorization submission or a complete compliance baseline.

## Start here

1. In a local test deployment, sign in as CSP.Admin and open the CSP onboarding ATO document upload step.
2. Select `harbor-ato-clean.zip` as an archive. It contains a nine-page PDF and a reduced OSCAL-shaped component JSON file. If testing direct file upload, use the PDF and JSON files supplied alongside the ZIP instead.
3. Observe upload/processing status, then finish onboarding.
4. Under the intended workflow, open Security Capabilities → Review imports. Confirm imported components and capabilities are unpublished and need review. Review evidence, approve the exact revisions, then explicitly publish.
5. Test `harbor-ato-needs-attention.zip` in a separate test workspace or reset fixture state first. It repeats the clean content and adds failure cases. Reimporting in the same workspace is useful only when intentionally testing duplicate handling.

## Important current implementation limitation

These archives were structurally validated, but not uploaded to the application. Source inspection in this conversation found limited PDF extraction and automatic publication of provider drafts during onboarding completion/import. Do not interpret current automatic publication as a successful test of the desired review gate. Use a disposable local test provider, particularly if unrelated draft records exist. The reduced JSON matches the current parser's component projection; it is not a complete or schema-validated OSCAL SSP.

## Clean package

Two archive entries:
- `01-harbor-synthetic-ato-package.pdf`: SSP, four component implementation sections, customer responsibility matrix, assessment summary, remediation plan and fictional decision reference.
- `02-harbor-components.json`: four repeated component identities for structured extraction and reconciliation.

Target semantic expectations: four unique components and six capabilities. JSON and PDF describe the same components, not eight separate components. Exact AI-generated names/counts are not guaranteed by the current implementation. See `expected-results.json` for component/capability identifiers, mappings, provenance pages and target assertions. Evidence cited in the fixture is illustrative and unverified.

## Needs-attention package

Five archive entries: the same two clean sources plus:
- `03-encrypted-appendix.pdf`: valid password-protected PDF. Password `fixture-only-password` is a public test password, not a real credential. Expect unreadable/encrypted status unless explicitly decrypted through a supported flow.
- `04-unsupported-note.txt`: text attachment outside the currently recognized archive dispatch types. Expect explicit unsupported/excluded status rather than silent omission.
- `05-malformed-source.json`: intentionally invalid JSON. Expect a parse failure recorded against this entry.

The intended workflow must account for all five entries and must not label the package fully processed or auto-approve its candidates. Exact supported/excluded categories can follow the eventual implementation contract. The current ZIP parser may skip the failure entries, which exposes the known coverage gap.

## Expected publication and authorization behavior

- Every generated component/capability begins unpublished and awaiting human review.
- Upload receipt, successful processing and onboarding completion do not publish.
- High mapping confidence does not approve records.
- Finding and remediation IDs are not capabilities.
- The fictional decision reference remains unconfirmed until reviewed; it never creates a mission system ATO.
- Editing an approved revision invalidates approval. Retry/reupload must not duplicate records or publish unrelated drafts.

## Verification performed

ZIP CRC integrity checked; clean archive has 2 entries and needs-attention archive has 5. PDF has 9 readable-text pages. Structured JSON parses and contains 4 components. The encrypted appendix is actually encrypted. Package SHA-256 values and entry lists are in `package-manifest.json`. No application acceptance result is claimed.
