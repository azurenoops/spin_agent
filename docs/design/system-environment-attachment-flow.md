# Organization registration, system attachment, and monitoring activation

Proposed UX, 2026-09-29. Source audit only; no live flow verification or product changes.

## Verified current implementation

- Organization onboarding renders `Step5AzureSubscriptions.tsx`, labeled Step 3 in the UI, and registers organization subscriptions.
- `AzureSettingsPage.tsx` provides ongoing registration management at `/settings/azure-subscriptions`.
- `AssessmentEnvironment.tsx` hosts `AssessmentEnvironmentPanel.tsx` for system-specific selection of registered subscriptions. The panel saves attachment separately from connectivity checks and currently gates mutation on `canRunAssessments`.
- The onboarding picker says downstream features auto-scope without re-prompting. This wording should be aligned with the explicit system-attachment and source-access requirements; organization registration alone does not establish those states.

## Proposed placement

1. Organization onboarding or Organization settings → Azure subscriptions: register eligible subscriptions for organization use. No automatic attachment to all systems.
2. System definition → Environment: canonical shared environment attachment and resource-scope workflow. Select organization subscriptions, choose system resources, review and save. System creation may offer this same workflow, with an explicit configure-later state. This is proposed behavior, not verified current system-onboarding behavior.
3. Assessments and Continuous monitoring: reuse the canonical environment; link back to edit it. Validate the access and prerequisites specific to each activity. Monitoring additionally requires reviewed baseline, rules and explicit enablement.

Permissions for shared environment management must be designed and enforced independently of merely displaying a user role. Existing assessment access controls must not be bypassed by moving the panel. Organization registration permissions do not automatically grant system mutation or monitoring enablement.

Scope changes show impacted assessments and monitoring rules; stage required review, keep historical records and reveal newly uncovered resources. Never silently broaden monitoring to the entire subscription.

Organizations can document systems without a CSP relationship. Provider authorization inheritance is a separate workflow from Azure subscription attachment. Provider-owned subscriptions are not automatically made available to consuming organizations; explicit authorized access or shared evidence is required.

## Mock interpretation

The board illustrates organization registration, system attachment and blocked monitoring setup. Names/counts are illustrative. Disabled monitoring activation is the default when required sources fail; any future partial-monitoring option needs an explicit degraded state and documented missing coverage.

[View mock board](assets/conmon-system-monitoring/04-environment-attachment-flow.png). Generated with built-in image generation. Use the actual product logo and existing brand copy in implementation; generated logo/slogan approximations are not branding changes. Render the correct current wizard step and never infer configuration completion from illustrative screen data.

## Generation prompt

Use case ui-mockup. Create one high resolution portrait design board containing THREE spacious legible desktop UI screens stacked vertically for SPIN security compliance software. White cards pale lavender background navy typography purple indigo buttons thin line icons, SPIN orbit wordmark, same enterprise style as earlier conversation. Small label PROPOSED FLOW • Illustrative data. Each screen has compact organization context top bar. Screen1 organization onboarding modal: heading 'Register organization subscriptions', subtitle 'Make subscriptions available for your systems.' Context 'SPIN Demo Organization · Organization setup'. Cloud Azure Government. Three checked table rows App production / Data production / Shared network, each status 'Available to register'. Right summary '3 selected' and explanatory concise text 'Choose system resources after registration.' Primary 'Register subscriptions', secondary 'Back'. Footer link 'Also available in Organization settings → Azure subscriptions'. Blue info 'Registration does not attach subscriptions to a system or enable monitoring.' Screen2 full app with left nav Overview, System definition selected, Controls & evidence, Assessment & risk, ATO Readiness, Continuous monitoring. Breadcrumb Systems / SPIN Demo System / Environment. Heading 'Attach this system’s environment'. Stepper '1 Subscriptions → 2 Resource scope → 3 Review'. Three checked cards App production selected scope rg-app-prod 42 resources; Data production rg-data-prod 18 resources; Shared network 6 selected resources. Right summary 'SPIN Demo System', '3 subscriptions', '66 selected resources', 'Shared resources can support multiple systems'. Secondary link Manage organization subscriptions. Primary 'Review attachment'. Footer note 'Assessments and monitoring reuse this environment. Each checks its own access.' No secret credential fields. Screen3 same app left nav Continuous monitoring selected. Heading 'Enable monitoring for this system'. Subtitle 'Using the environment attached in System definition.' Four-step bar Scope complete / Access & sources current / Baseline / Enable. Table App production healthy; Data production access denied; Shared network healthy. Amber status '2 of 3 subscriptions ready · Resolve access before full coverage'. Right panel 'Next steps' '1 Restore Data production access' action Recheck access, '2 Review system baseline', '3 Review rules and enable'. Primary disabled Enable monitoring with visible reason 'Required access checks incomplete'. Secondary Save setup. Footer 'Environment attached · Monitoring not enabled'. Do not invent ATO approvals. Focus usability and clear distinction org registration vs system attachment vs monitoring activation. Crisp readable generous layouts.
