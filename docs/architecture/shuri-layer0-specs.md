# Shuri — Layer 0 Architecture Specs (Product/App-Facing)

Issues covered: #658, #656, #749, #667, #665, #733, #730, #732, #731

Skipped (already handled): #651, #639, #641
Deferred to Cyborg (pipeline/infra-internal): #652, #650, #649, #653, #654, #659, #660, #661, #662, #663, #664, #668, #763

---

## Spec 1 — Issue #658: Port/Contract Mismatch (Terraform target_port vs Dockerfile EXPOSE)

### Problem Statement
The issue title claims `target_port = 8080` vs `EXPOSE 3001`. Verified source shows
`infra/terraform/main.tf:312` sets `target_port = 3001` and `Dockerfile` uses `EXPOSE 3001` —
the mismatch is already resolved in current code. However, there is no CI guard to prevent
this from drifting again. The real active risk is `docker-compose.mcp.yml` exposing the
`ato-chat` service with `ASPNETCORE_ENVIRONMENT` defaulting to `Production` (sibling issue #657).

### Chosen Approach
1. Add a CI lint step that asserts the `EXPOSE` port in `Dockerfile` equals `target_port` in
   `infra/terraform/main.tf`.
2. No source change to Dockerfile or Terraform needed (values already agree).

### Module/Contract Boundaries
- `Dockerfile` → `EXPOSE 3001` (read-only reference)
- `infra/terraform/main.tf` → `target_port = 3001` (read-only reference)
- `.github/workflows/` — add port-contract lint job

### Files Touched
- `.github/workflows/ci.yml` (or new `port-contract-lint.yml`) — new step

### Acceptance Criteria
- CI fails if `EXPOSE` port ≠ Terraform `target_port`
- PR confirms current values agree at 3001

### Migration Shape
CI-only addition. No runtime changes.

---

## Spec 2 — Issue #656: appsettings.json Hardcodes Azure OpenAI Endpoint

### Problem Statement
`src/Ato.Copilot.Mcp/appsettings.json` contains:
```json
"AzureAi": {
  "Endpoint": "https://ato-copilot-ai.openai.azure.com/",
  ...
}
```
This real Azure endpoint is baked into source. Any non-prod environment, fork, or
contributor will either fail silently (wrong endpoint) or accidentally hit the
production AI resource. It violates twelve-factor configuration hygiene.

### Chosen Approach
1. Replace the hardcoded endpoint with `""` in `appsettings.json`.
2. Extend `AzureAiOptions` validation to throw `InvalidOperationException` at startup
   if `Endpoint` is empty outside Development/Testing environments.
3. Document required env var (`ATO_AZUREAI__ENDPOINT`) in `.env.example` and
   `scripts/bootstrap.sh`.

### Module/Contract Boundaries
- `src/Ato.Copilot.Mcp/appsettings.json` — endpoint becomes `""`
- `src/Ato.Copilot.Core/Configuration/AzureAiOptions.cs` — add validator
- `src/Ato.Copilot.Mcp/Program.cs` — register `IValidateOptions<AzureAiOptions>`
- `.env.example` (new) — documents required vars
- `scripts/bootstrap.sh` — mention `ATO_AZUREAI__ENDPOINT`

### Files Touched
- `src/Ato.Copilot.Mcp/appsettings.json`
- `src/Ato.Copilot.Core/Configuration/AzureAiOptions.cs`
- `src/Ato.Copilot.Mcp/Program.cs`
- `.env.example`
- `scripts/bootstrap.sh`

### Acceptance Criteria
- `appsettings.json` contains no Azure hostname
- App startup fails fast with clear message if endpoint is empty in non-Dev environment
- `.env.example` documents `ATO_AZUREAI__ENDPOINT`
- CI passes (env var injected via GitHub secret)

### Migration Shape
`appsettings.json` diff only. All existing deployments must set `ATO_AZUREAI__ENDPOINT`
in environment or Key Vault reference. No DB migration.

---

## Spec 3 — Issue #749: No Startup Guard for Dev Auth Bypass Mode

### Problem Statement
`ALLOW_DEV_AUTH_BYPASS=true` skips all JWT validation. The middleware checks
`IsDevelopment()` before allowing bypass, but there is no fail-fast startup check
that prevents the flag from running silently if `ASPNETCORE_ENVIRONMENT` is not
correctly set in a deployment pipeline. A misconfigured container could ship
bypass active into production.

### Chosen Approach
1. Upgrade the existing `ValidateDevBypassSafety` method in `Program.cs` (~line 1372)
   from a warning-log to a hard throw in non-Development, non-Testing environments.
2. Extract into a dedicated `DevBypassStartupGuard` class that also covers
   `CacAuth:SimulationMode` (overlaps with #667).
3. Register as an `IStartupFilter` so it runs before any endpoint is reachable.
4. Write unit tests covering all environment × flag combinations.

### Module/Contract Boundaries
- `src/Ato.Copilot.Mcp/Program.cs` — upgrade warning → throw
- `src/Ato.Copilot.Mcp/StartupGuards/DevBypassStartupGuard.cs` (new)
- `tests/Ato.Copilot.Tests.Unit/StartupGuards/DevBypassStartupGuardTests.cs` (new)

### Files Touched
- `src/Ato.Copilot.Mcp/Program.cs`
- `src/Ato.Copilot.Mcp/StartupGuards/DevBypassStartupGuard.cs` (new)
- `tests/Ato.Copilot.Tests.Unit/StartupGuards/DevBypassStartupGuardTests.cs` (new)

### Acceptance Criteria
- App refuses to start (non-zero exit) if bypass flags are active in Production
- Unit tests: Production+bypass=throw, Development+bypass=log-only, Production+no-bypass=ok
- Existing warn-level log preserved alongside the new throw

### Migration Shape
Code-only. No config changes. Backward-compatible for dev environments.

---

## Spec 4 — Issue #667: Dev Simulated Identity Defaults to CSP.Admin

### Problem Statement
The default dev simulated identity uses `CSP.Admin` — maximum privilege. This masks
authorization gaps during development because all role checks pass for every developer.
Real AO users running lower-privilege roles never encounter the failures developers skip.

### Chosen Approach
1. Change the default simulated identity role from `CSP.Admin` to `Compliance.SystemOwner`
   in `appsettings.Development.json`.
2. Add a startup warning (non-fatal) in `SimulationGate.cs` when a simulated identity
   carries `CSP.Admin`.
3. Document how to locally override to `CSP.Admin` in `docs/dev/contributing.md`.

### Module/Contract Boundaries
- `appsettings.Development.json` — change default simulated role
- `src/Ato.Copilot.Mcp/Endpoints/Auth/SimulationGate.cs` — add CSP.Admin warning
- `docs/dev/contributing.md` — local override instructions

### Files Touched
- `appsettings.Development.json`
- `src/Ato.Copilot.Mcp/Endpoints/Auth/SimulationGate.cs`
- `docs/dev/contributing.md`

### Acceptance Criteria
- Default dev simulated role is NOT `CSP.Admin`
- Startup warning logged when CSP.Admin is used in simulation
- Docs explain how to elevate locally for CSP-Admin testing

### Migration Shape
Config change only. Developers needing CSP.Admin override via `appsettings.Development.local.json`
(already gitignored).

---

## Spec 5 — Issue #665: OpenTelemetry Defaults to localhost:4317

### Problem Statement
`appsettings.json` has `"OpenTelemetry": { "Enabled": true, "OtlpEndpoint": "http://localhost:4317" }`.
With `Enabled: true` by default, every environment without a local OTEL collector
(staging, prod, CI) logs connection errors on every startup and trace export cycle.
This noise buries real errors and wastes resources.

### Chosen Approach
1. Change default `Enabled` to `false` in base `appsettings.json`.
2. Set `Enabled: true` in `appsettings.Development.json` and where a collector is available.
3. Production/staging enables OTEL via `ATO_OPENTELEMETRY__ENABLED=true` and
   `ATO_OPENTELEMETRY__OTLPENDPOINT=<collector-url>` env vars.

### Module/Contract Boundaries
- `src/Ato.Copilot.Mcp/appsettings.json` — `Enabled: false`
- `src/Ato.Copilot.Mcp/appsettings.Development.json` — `Enabled: true`
- `docker-compose.mcp.yml` — ensure collector env var set

### Files Touched
- `src/Ato.Copilot.Mcp/appsettings.json`
- `src/Ato.Copilot.Mcp/appsettings.Development.json` (create if absent)
- `docker-compose.mcp.yml`

### Acceptance Criteria
- `dotnet run` in a fresh environment without a collector produces zero OTLP connection errors
- Development docker-compose still exports telemetry
- Production deployments can enable via env var

### Migration Shape
Config-only. No code changes. Existing deployments relying on OTEL must set the env var.

---

## Spec 6 — Issue #733: RBAC via Manual HashSet

### Problem Statement
Authorization checks in MCP endpoint handlers use ad-hoc `IsInRole` string calls and
manual `HashSet<string>` comparisons rather than ASP.NET Core policy-based authorization.
Adding a role requires hunting every call site. No declarative audit surface exists.
Testing authorization in isolation is not possible.

### Chosen Approach
1. Define named ASP.NET Core authorization policies via `services.AddAuthorization()`:
   `Policy.CspAdmin`, `Policy.SocAnalyst`, `Policy.ComplianceViewer`,
   `Policy.ComplianceEditor`, `Policy.AuthorizingOfficial`, etc.
2. Replace bare `IsInRole("CSP.Admin")` call-sites with
   `[Authorize(Policy = ...)]` on Minimal API endpoint groups, or
   `IAuthorizationService.AuthorizeAsync` for runtime checks.
3. Role name constants already exist in `ComplianceRoles.cs` — policies reference those
   constants, not magic strings.
4. Write unit tests using `AuthorizationPolicyBuilder` verifying each policy admits the
   correct roles and rejects others.

### Module/Contract Boundaries
- `src/Ato.Copilot.Core/Constants/ComplianceRoles.cs` — authoritative role names (existing)
- `src/Ato.Copilot.Mcp/Authorization/Policies.cs` (new) — policy definitions
- `src/Ato.Copilot.Mcp/Program.cs` — register policies
- `src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs` — replace manual IsInRole
- `tests/Ato.Copilot.Tests.Unit/Authorization/PolicyTests.cs` (new)

### Files Touched
- `src/Ato.Copilot.Core/Constants/ComplianceRoles.cs`
- `src/Ato.Copilot.Mcp/Authorization/Policies.cs` (new)
- `src/Ato.Copilot.Mcp/Program.cs`
- `src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs`
- `tests/Ato.Copilot.Tests.Unit/Authorization/PolicyTests.cs` (new)

### Acceptance Criteria
- Zero bare `IsInRole` string calls remain in endpoint handlers
- All RBAC decisions flow through named ASP.NET Core policies
- Each policy has at least one passing + one failing unit test
- Adding a new role requires only: add to `ComplianceRoles.cs`, update one policy

### Migration Shape
Purely additive. Existing behavior preserved. Can be done incrementally per endpoint group.
No DB migration.

---

## Spec 7 — Issue #730: Audit Log Retention — Add Floor Validator

### Problem Statement
Issue #730 reports audit log retention at 730 days. Code verification shows
`appsettings.json` already sets `AuditLogRetentionDays: 2555` and the C# model default
is also 2555 — **the default is already correct**. The gap is: no startup validator
that hard-fails if an operator misconfigures a sub-floor value, and no documented
minimum tied to NARA GRS 3.2 / FedRAMP AU-11. Alert and snapshot retention (730 days)
is a separate, shorter-lived value that may cause confusion.

### Chosen Approach
1. Add `IValidateOptions<RetentionOptions>` that hard-fails startup if
   `AuditLogRetentionDays < 2555`.
2. Emit a warning (non-fatal) if `AlertRetentionDays < 365` or
   `WeeklySnapshotRetentionDays < 365`.
3. Add XML doc comment to `GatewayOptions.AuditLogRetentionDays` citing NARA GRS 3.2
   and FedRAMP AU-11.

### Module/Contract Boundaries
- `src/Ato.Copilot.Core/Configuration/GatewayOptions.cs` — add doc + floor constant
- `src/Ato.Copilot.Core/Configuration/RetentionOptionsValidator.cs` (new)
- `src/Ato.Copilot.Mcp/Program.cs` — register validator
- `tests/Ato.Copilot.Tests.Unit/Configuration/RetentionOptionsValidatorTests.cs` (new)

### Files Touched
- `src/Ato.Copilot.Core/Configuration/GatewayOptions.cs`
- `src/Ato.Copilot.Core/Configuration/RetentionOptionsValidator.cs` (new)
- `src/Ato.Copilot.Mcp/Program.cs`
- `tests/Ato.Copilot.Tests.Unit/Configuration/RetentionOptionsValidatorTests.cs` (new)

### Acceptance Criteria
- App refuses to start if `AuditLogRetentionDays < 2555`
- Default in `appsettings.json` remains 2555 (no change)
- Unit tests: validator rejects 730, accepts 2555, accepts 3650

### Migration Shape
Code-only. No DB migration. No config change needed (defaults already correct).

---

## Spec 8 — Issue #732: No Registration Path from /systems View

### Problem Statement
The global `/systems` page has no button or CTA to register a new system.
Users see existing systems but cannot start the registration wizard. `SystemsNewRoute`
exists at `/systems/new` and is registered in `App.tsx`, but no UI element links to it
from the systems list surface.

### Chosen Approach
1. Add a "Register System" primary action button to the `/systems` page header.
2. Button navigates to `/systems/new` (already registered).
3. Gate the button with a role check (e.g., `Compliance.SystemOwner` or `CSP.Admin`);
   unauthorized users see it disabled or hidden.
4. Add an empty-state CTA when zero systems exist.

### Module/Contract Boundaries
- Systems list page component (to be located in `src/Ato.Copilot.Dashboard/src/pages/`)
- `src/Ato.Copilot.Dashboard/src/pages/SystemsNewRoute.tsx` — no change
- `src/Ato.Copilot.Dashboard/src/App.tsx` — route already registered

### Files Touched
- `src/Ato.Copilot.Dashboard/src/pages/SystemsPage.tsx` (or `SystemsRoute.tsx`)
- Possibly `src/Ato.Copilot.Dashboard/src/components/EmptyState.tsx`

### Acceptance Criteria
- `/systems` page shows "Register System" button in header
- Clicking navigates to `/systems/new`
- Button is role-gated
- Empty state shows CTA when no systems exist

### Migration Shape
Frontend-only. No API change. No DB migration.

---

## Spec 9 — Issue #731: Org Row Click Is a No-Op

### Problem Statement
Org rows in the portfolio table render as visually clickable (cursor affordance) but
`onClick` is unbound or a no-op. Users expect navigation to an org workspace; nothing happens.
This is a false affordance confirmed also in issue #626.

### Chosen Approach
1. Locate the portfolio org table component under `src/Ato.Copilot.Dashboard/src/features/portfolio/`.
2. Add `onClick={() => navigate(`/orgs/${org.id}`)}` to org row elements.
3. Confirm `/orgs/:id` route exists in `App.tsx`; add a stub org workspace page if missing.
4. Add keyboard accessibility: `tabIndex={0}` + `onKeyDown` (Enter/Space) handler on the row.

### Module/Contract Boundaries
- `src/Ato.Copilot.Dashboard/src/features/portfolio/` — org table component
- `src/Ato.Copilot.Dashboard/src/App.tsx` — route registration
- `src/Ato.Copilot.Dashboard/src/pages/OrgWorkspacePage.tsx` (new if route missing)

### Files Touched
- `src/Ato.Copilot.Dashboard/src/features/portfolio/PortfolioTable.tsx` (or equivalent)
- `src/Ato.Copilot.Dashboard/src/App.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/OrgWorkspacePage.tsx` (possibly new)

### Acceptance Criteria
- Clicking an org row navigates to org workspace
- Keyboard-accessible (Enter/Space activates navigation)
- If navigation target is missing, row is not styled as clickable (no false affordance)

### Migration Shape
Frontend-only. No API change. No DB migration.

---

## Coordination Note — Cyborg Boundary

Cyborg owns pipeline-internal Layer 0 issues:
CI conditional gates (#652), deprecated workflow (#650), invalid AI deployment name in CI (#649),
wipe-and-reseed workflow (#653), seed hardcoding (#654), integration test CI gap (#659),
Azure CLI in Dockerfile (#662), Terraform SQL password in Key Vault (#663),
`global.json` rollForward (#664), CODEOWNERS (#666), Terraform provider pinning (#763).

No overlap with the nine specs above.
