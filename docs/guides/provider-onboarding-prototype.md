# Provider onboarding interactive prototype

This local prototype demonstrates the proposed first-login routing and six-stage
provider setup with isolated synthetic data. It intentionally stops before any
provider or offering workspace.
It does not call SPIN Agent APIs or mutate provider, identity, package,
authorization, assessment, cloud, or monitoring records.

## Launch

From the repository root:

```bash
cd src/Ato.Copilot.Dashboard
npm ci
npm run dev -- --host 127.0.0.1 --port 5179 --strictPort
```

Open:

```text
http://127.0.0.1:5179/provider-onboarding-prototype.html
```

The prototype stores state in browser local storage under
`spin-provider-onboarding-prototype-v1`. Use **Reset prototype** to remove it.

## Connected walkthroughs

### Existing authorization

1. Choose **New authorized provider administrator**.
2. Start setup and confirm the prefilled PEO Digital identity.
3. Record or defer contacts.
4. Keep the Flank Speed portfolio and Flank Speed Azure offering.
5. Choose **We have an existing authorization**.
6. Optionally select a synthetic eMASS ZIP, XML, JSON, spreadsheet, document,
   or PDF package. Run **Analyze synthetic eMASS package** to inventory the
   package, propose available authorization facts, and identify unresolved
   scope, authority, condition, posture, and customer-coverage gaps.
7. Review and optionally use the proposed facts. Unknown dates and boundary
   identity may remain blank. Proposed facts remain unverified.
8. Choose the synthetic source upload path, select a local test file, simulate an
   uncertain response, and reconcile the original receipt.
9. Advance processing and extraction. The records remain awaiting review.
10. Finish setup and review the unresolved scope follow-up on the onboarding
    completion screen. Use **Open provider workspace** only when ready to leave
    the onboarding summary.

Use **Simulate separate authorized review** only when demonstrating the final
recorded-decision state. This action is deliberately separate from receipt and
processing.

### Initial authorization

Choose **We are preparing for initial authorization**, defer records if needed,
and finish setup. The onboarding completion screen shows package-preparation
follow-up and no recorded authorization decision.

### Deferred setup

Choose **Add an offering later**, **We need to determine the authorization
scope**, and **Add records later**. Setup can finish while the onboarding
completion screen shows accountable follow-up work.

### Invited ISSO

Choose **Invited user joining an existing provider**, review the separate
identity, membership, offering, and role facts, then confirm membership. The
prototype confirms that provider registration was not repeated and stops before
opening the assigned workspace.

### Save, resume, and recovery

Use **Save & finish later**, reload the page, select
**Administrator resuming unfinished setup**, and resume the same locally stored
draft. For source recovery, the uncertain response retains one original request
and blocks another submission until reconciliation.

## Prototype limitations

- All people, memberships, roles, invitations, packages, decisions, deadlines,
  and work assignments are synthetic browser state.
- Selecting a local file retains its name only. No file bytes are uploaded.
- The eMASS package engine is a deterministic UI simulation. It does not parse
  package bytes, validate an eMASS export, or create canonical imported records.
- Simulated invitation confirmation and access requests do not send messages or
  grant access.
- Offering, portfolio, authorization-intent, boundary, decision, assessment,
  and monitoring records are not persisted to a server.
- Cloud discovery and monitoring are not connected.
- Provider and offering workspace experiences are intentionally outside this
  onboarding-only prototype. The completion link demonstrates the handoff but
  does not embed or preload the workspace.
- A displayed recorded decision is a simulated reviewed state, not an issued or
  independently verified authorization.
- Responsive and keyboard behavior is implemented for review, but this
  prototype is not a substitute for production accessibility and user
  acceptance testing.

## Verification

Run focused unit and browser journeys:

```bash
cd src/Ato.Copilot.Dashboard
npm test -- --run src/__tests__/onboarding/ProviderOnboardingPrototype.test.tsx

PLAYWRIGHT_BASE_URL=http://127.0.0.1:5179 \
  npm run test:e2e -- \
  e2e/tests/provider-onboarding-prototype.spec.ts \
  --project=chromium --reporter=list \
  --output=test-results/provider-onboarding-prototype
```

Build both the normal Dashboard and standalone prototype entry:

```bash
cd src/Ato.Copilot.Dashboard
npm run build
```
