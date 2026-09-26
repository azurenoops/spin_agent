# Provider source package — approval mocks

Open `index.html` directly in a browser. All data is synthetic; there are no API calls or persistent writes.

## Superseding ownership

The authorization-led request places offering, decision, boundary, package
versions and source review under **Authorizations**. The Security Capabilities
card is a linked summary, not an independent decision editor or import workflow.
Use [the authorization mock](../provider-authorization-mocks/index.html) for
the primary entry. The source-preview and candidate-review interaction patterns
remain reusable; sample data and prior simulated saves are not application
contracts.

Optional onboarding now offers **Import an existing authorization package** and
hands detailed review to Authorizations after receipt. Onboarding still never
shows inventory review or implies approval/publication. Microsoft references,
provider decisions and mission decisions stay distinct. Hosted subscriptions
and technical telemetry do not establish authorized workload coverage.

Use the scenario selector to review populated, empty, missing authorization, independent lookup failures, and edit-conflict states. Expand/collapse the card, open a sample document, edit or record a reference, cancel/save, and recover from a conflict. Reload resets preview data.

Screenshots:
- `01-source-package.png`: expanded source documents and authorization reference.
- `02-authorization-form.png`: reference editor.
- `03-empty-state.png`: initial empty package.
- `04-mobile.png`: 390px mobile layout.

Companion contract: [UI/API flow](../provider-source-package-flow.md).

Verification: browser checks passed for edit-conflict recovery, preview save, lookup retry, document preview, and mobile horizontal overflow. Desktop and mobile screenshots were captured. No production code changed; full application tests were not run. The surrounding catalog/navigation provide visual context, not complete navigation. Manage provider components displays a handoff message. Import is intentionally outside this proposal.

Manual approval: inspect the screenshots, then open the HTML and use the scenario selector and buttons. Remove this directory to undo the mock artifacts.

## Onboarding extension

Open `onboarding.html` for the revised journey: upload a sample ATO package → background processing → finish onboarding → portal review queue → review components and capabilities → approve → publish. The original source-package mock links to it.

The onboarding screens intentionally omit extracted record lists. Use the Screen selector to inspect incomplete analysis. The mock blocks approval/publication until file coverage and both record types have been reviewed; a simulated replacement resets review.

Additional screenshots:
- `05-onboarding-upload.png`
- `06-onboarding-processing.png`
- `07-portal-review.png`
- `08-package-attention.png`
- `09-onboarding-mobile-review.png`

Browser verification passed: onboarding handoff; six individual record reviews; acknowledgement required before marking reviewed; approval before publication; incomplete-package gate; reprocessing resets review; no horizontal overflow at 390px; no browser script errors. Screenshots use sample data only. Rejection, duplicate resolution, subset approval, and real extraction are specified in the flow document but not simulated in this focused mock. Application behavior remains unchanged.
