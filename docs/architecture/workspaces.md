# CSP and Organization Workspaces (Approved Design)

## Draft workspace-testing checkpoint

Local SQLite startup requires SQLite-compatible boundary-migration sentinel DDL.
The real hosted migration, including repeat startup, is covered by a SQLite
regression test rather than only simulating its data transformation in memory.
Development simulation may start before its configured organization exists.
Such pre-workspace login events belong to the existing system audit tenant;
simulation must not create organizations or memberships to satisfy an audit FK.
The selected simulation cookie must be resolved on subsequent requests, rather
than authenticating as the legacy default identity. For local verification,
select CSP Admin, then check that `/api/auth/me` identifies that administrator
and offers the provider workspace. An unknown selection must return 401.
Organization personas still require explicit memberships and system roles.
The identity-selection regression first passed 34 simulation tests. Live
verification then exposed a missing `CspProfiles` table: SQLite startup uses
migrations and explicit schema additions, whereas only SQL Server runs the
missing-model-table pass. The tenancy schema module now creates `CspProfiles`
idempotently without inserting a profile row. Real SQLite startup/read/restart
tests cover both deployment modes and preserve an explicitly created profile.
The combined focused run passed 47 tests. The local Dashboard proxy then
returned 204 for simulated sign-in and 200 for `/api/auth/me`, with the selected
CSP Admin identity and an ordinary Hosting CSP workspace. CSP onboarding remains
Pending; this API smoke check does not replace browser/manual acceptance.

This branch is being published for local workspace testing, not as a completed
or merge-ready implementation of #1002. The latest completed backend run built
with zero errors and 121 warnings; 6,357 unit tests passed, while integration
tests recorded 1,223 passes, 64 failures and 20 skips. The last full Dashboard
run passed 1,160 tests. Subsequent focused checks are reported separately.

Use an isolated development database and the existing [local setup](../../README.md#quick-start);
do not point this draft's schema upgrades at production data. Test ordinary
organization entry, workspace switching in two tabs, system-role restrictions,
support exit, and the provider/organization/system narrative library entry
points described below. Synthetic browser fixtures are not proof of live
identity-provider or persistence behavior.

Known open gates include the remaining integration failures, authorized write
routes blocked before their existing policies, build warnings, final coverage,
and live/manual acceptance. The agreed next bounded investigation is a role
assignment returning HTTP 500 where its contract expects 403; that fix is not
part of this initial testing checkpoint. (Approved Design)

**Status:** Design approved by the user on 2026-09-21 for issue
[#1002](https://github.com/azurenoops/spin_agent/issues/1002).
Implementation was subsequently authorized. Release verification and publishing
approval remain pending. This page does not describe a shipped feature.

## CI simulation fixture configuration regression

GitHub CI run `35795659028`, attempt 1, tested `210e8d7b` and reported
1,286 integration passes, two failures and 20 skips. The two protected MCP
requests in `SimulationModeIntegrationTests` expected 200 but returned 401.
Their sequential request logs show development authentication fallback, an
anonymous principal, and rejection by the MCP handler.

Both fixtures registered their simulation `Configure<CacAuthOptions>` before the
shared MCP service graph bound the deployment's `CacAuth` section. That later
binding could overwrite the fixture's enablement and persona. The regression
explicitly supplies disabled deployment simulation defaults so local Development
configuration cannot hide this ordering defect. Fixture-specific settings now use
`PostConfigure<CacAuthOptions>` to take precedence after normal binding.
Production configuration, identity enforcement, and the existing successful
protected-request expectations are unchanged.

Red/green verification reproduced exactly the two CI failures before the fix;
all six simulation integration tests then passed, including assertions for
authenticated simulated identities, directory/object IDs and exact role sets.
The 38 focused simulation unit tests also passed, including disabled simulation
and non-Development safety checks. The full local Release integration run passed
1,296 tests with zero failures and 20 skips, using the workflow's environment
settings and `ATO_REQUIRE_DOCKER_TESTS=1`. The skipped test names match the failed
CI artifact exactly. This full run also includes the unpushed Chat regression
tests. Build warnings remain; local macOS success is not proof of GitHub Linux
success. CI is not green until a subsequent published run confirms it.

For a local rerun from the repository root:

```bash
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  -c Release --no-restore \
  --filter 'FullyQualifiedName~SimulationModeIssoIntegrationTests|FullyQualifiedName~SimulationModeEngineerIntegrationTests'
```

Expected result: six passes. User manual acceptance remains open. Rollback is
limited to reverting this fixture correction; there are no production,
database, dependency, or workflow changes.

## Docker organization-library routing regression

Live acceptance on 2026-09-22 found that the Dashboard nginx configuration did
not proxy `/api/narrative-library`: GET returned the SPA HTML and the multipart
import POST returned nginx HTML with status 405, without reaching the MCP
handler. Provider and system library prefixes already have proxy routes.

The organization-library root and child paths must reach the existing MCP
handlers without rewriting the path, dropping cookies/workspace headers, or
changing authorization. The upload route must admit a 5 MiB file plus multipart
overhead; the backend retains its own 5 MiB file validation. Keep SPA deep-link
fallback and unrelated API routes unchanged. Verify both successful scoped
imports and unauthorized requests through the running Dashboard, not only
directly against the MCP port.

Verification: three focused Vitest configuration tests pass, as do Dashboard
`tsc --noEmit`, the Docker production build and `nginx -t`. The build still emits
warnings. Focused ESLint validation could not run because the local Dashboard
checkout has no ESLint executable; no dependency changes were made.

Through the rebuilt Dashboard, library GET returned JSON 200, imports returned
201, and publication returned 200. Cross-organization reference reads returned
404. A deliberately invalid 5 MiB CSV reached backend validation (400
`INVALID_IMPORT`); a 6 MiB file plus multipart framing returned nginx 413. Both
probes left the reference count unchanged.

The user-approved local acceptance dataset contains Alpha/Beta tenants, ten
memberships, three systems, two organization Administrators, two organization
ISSM assignments and seven explicit system-role assignments. These were created
through supported APIs, not seeded by startup. Organization ISSM is required
to register systems; Administrator alone must not gain that system operation.
Both tenants completed normal onboarding. Two synthetic organization references
are published; one provider reference remains a private draft.

Live checks verified seven personas' assigned-system access, scoped denials,
provider-draft denial, organization-reference isolation, Mission Owner's
Alpha-only role versus Beta's Mission Owner/System Owner union, and membership
revocation/restoration without affecting Alpha access. Mission Owner portfolio
tabs showed their respective organizations before and after refresh, once data
finished loading. These are local simulation/API/browser checks, not production
Entra acceptance or user manual sign-off. Background 403s still occur on other
surfaces; this proxy correction does not establish that all workspace routes
or the outstanding GitHub integration job pass.

## Local simulation switch and personas

Simulation uses the existing `CacAuth:SimulationMode` startup setting. The base
configuration sets it to `false`; Development configuration sets it to `true`.
Override it in the backend process environment, then restart that process:

```bash
# Enable local demos (both settings are required).
export ASPNETCORE_ENVIRONMENT=Development
export ATO_CACAUTH__SIMULATIONMODE=true

# Disable simulation, including in Development.
export ATO_CACAUTH__SIMULATIONMODE=false
```

For production, set `ASPNETCORE_ENVIRONMENT=Production` and
`ATO_CACAUTH__SIMULATIONMODE=false`, with real authentication configured.
Non-Development environments reject simulation even if the flag is mistakenly
true. There is no in-app toggle or parallel enable flag. These are process
environment variables; a direct `dotnet run` does not load `.env` automatically.

When disabled, `/api/auth/login-config` omits the simulation descriptor,
`POST /api/auth/simulate` returns bare 404 without session cookies, and existing
simulation cookies cannot synthesize an identity. Re-enabling simulation can
make a retained selection cookie usable again; disabling is not cookie deletion.

Development offers the original CSP Admin, ISSO and SOC Analyst identities plus:

| Persona | Identity key | Object ID |
|---|---|---|
| Organization Admin | `dev-orgadmin` | `10000000-0000-0000-0000-000000000004` |
| Mission Owner | `dev-mission-owner` | `10000000-0000-0000-0000-000000000005` |
| System Owner | `dev-system-owner` | `10000000-0000-0000-0000-000000000006` |
| ISSM | `dev-issm` | `10000000-0000-0000-0000-000000000007` |
| SCA | `dev-sca` | `10000000-0000-0000-0000-000000000008` |
| Authorizing Official | `dev-ao` | `10000000-0000-0000-0000-000000000009` |

Their directory ID is `00000000-0000-0000-0000-000000000001`. An administrator
must explicitly bind that directory ID and the selected object ID to a Person
through **Manage memberships**, then assign the appropriate organization/system
role. The six new identity descriptors deliberately carry no global role claims.
Persona labels do not grant access; "No tenant assignment" is expected for an
unprovisioned organization persona. No demo tenants or assignments are seeded.

Manual checks: enable simulation and confirm nine choices on `/login`; select
CSP Admin and confirm `/api/auth/me` reports its object ID and CSP workspace.
Disable simulation and restart; confirm the choices disappear and direct
simulation POSTs return 404. Re-enable for further local testing.

### Docker Desktop testing snapshot

The local Compose stack uses SQL Server and Redis with the production Dashboard
Dockerfile (nginx on container port 8080). The Azure build workflow uses the same
MCP, Dashboard and Chat Dockerfiles and targets `linux/amd64`; use
`DOCKER_DEFAULT_PLATFORM=linux/amd64` for matching local image architecture.
This is image/build parity, not a claim of Azure identity, network or hosting
parity. Existing local volumes are retained.

The production Dashboard bundle deliberately excludes the simulation picker.
For an isolated local demo, leave the backend in Development and set
`ATO_CACAUTH__SIMULATIONMODE=true`. Compose forwards this existing startup switch.
From the browser console on the local Dashboard origin, use:

```javascript
const response = await fetch('/api/auth/simulate?identityId=dev-cspadmin', {
  method: 'POST',
});
if (response.status !== 204) throw new Error(`Simulation failed: ${response.status}`);
location.assign('/');
```

Use another configured identity key to switch personas. Never enable this mode
on a production deployment. AI is disabled for the local workspace-only checks.

Known switch acceptance gap: the endpoint tests return bare 404 when disabled,
but a live unauthenticated Development request returned 401. The live descriptor
was null and a retained simulation cookie was rejected with 401. The on/off
switch blocks simulation, but the full-pipeline 404 contract is not yet verified.
That mismatch is not silently waived by the passing focused tests.

Docker snapshot status: 53 focused unit tests and two existing simulation HTTP
integration tests passed. The first `linux/amd64` Compose build failed during
NuGet restore (`NU1301`, TLS unexpected EOF from `api.nuget.org`). Independent
HTTPS probes to that endpoint failed from both macOS and the same Linux SDK
image. No successful Docker startup or Docker browser acceptance is claimed.
TLS verification was not disabled, and no host-built binaries were substituted.
The supplied Defender event confirms a non-overridable `CustomBlockList` rule
for `api.nuget.org`, affecting `com.docker.backend`. Host-run development servers
were stopped; the role-assignment 500 follow-up has not started.

#### Approved package source for local builds

The user confirmed that Microsoft public Azure Artifacts feeds are approved for
this machine. The `dotnet-public` service index returned HTTP 200 from the
`linux/amd64` .NET SDK container. This verifies connectivity, not availability of
every required package/version or a successful application build.
The source URI is also listed in the
[.NET runtime repository's NuGet configuration](https://github.com/dotnet/runtime/blob/main/NuGet.config).

The bounded build change adds a `NUGET_SOURCE` build argument to the MCP and Chat
Dockerfiles and forwards it through Compose. The default remains
`https://api.nuget.org/v3/index.json`, preserving the Azure workflow's source.
An explicitly selected source replaces that default for restore; publishing must
reuse the restored assets rather than implicitly restoring from the default.
No package versions, runtime settings, security controls or TLS checks change.
Use only an organization-approved source; build arguments must not contain
credentials. Private authenticated feeds require a separate secrets mechanism.

Reproduction command for this approved local feed (full restore remains blocked
by the missing packages listed below):

```bash
NUGET_SOURCE=https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json \
DOCKER_DEFAULT_PLATFORM=linux/amd64 \
ATO_AZUREAI__ENABLED=false \
ATO_CACAUTH__SIMULATIONMODE=true \
docker compose -f docker-compose.mcp.yml up --build --wait --wait-timeout 180
```

If a required package/version is absent, stop and report it; do not silently add
another feed or change dependency versions. Unset `NUGET_SOURCE` and rebuild to
restore the default source on a machine where that source is approved. This does
not require removing database volumes.

The real Docker restore reached the feed and restored the State project, but
Chat restore failed with `NU1101` for these package IDs:

- `Azure.AI.Agents.Persistent`
- `Azure.ResourceManager.Monitor`
- `Azure.ResourceManager.PolicyInsights`
- `Azure.ResourceManager.ResourceGraph`
- `Azure.ResourceManager.SecurityCenter`
- `QuestPDF`
- `ClosedXML.Parser`
- `RBush`

Compose then canceled the parallel MCP restore and Chat frontend install. The
public feed is not a complete replacement for this application's dependencies;
no successful image build or running Docker application is claimed.

The same log showed that Chat's Channels project was absent during the initial
restore. Its project file must be copied before restore so that publishing with
`--no-restore` has assets for every referenced project. That build-input repair
does not resolve missing packages in the external feed.

Validation: 60 focused unit tests passed, including the five new Docker source
and restore-input cases and the existing simulation/schema/deployment checks.
Compose's rendered configuration forwards the selected source to both backend
builds. These checks do not replace a successful image build or browser E2E run.

#### Bounded offline-package feasibility check

The user approved checking existing NuGet packages for offline Docker restore.
The first staged set contained the 204 package versions in the MCP and Chat
application asset graphs. Each archive's SHA-512 matched its archive sidecar;
the distinct NuGet metadata content hash matched the project asset record.
Their recorded source was `api.nuget.org`; no new network download was used.

Both project restores completed in a fresh `linux/amd64` SDK container with
network access disabled and an initially empty package-install directory.
Only package archives and project files were supplied, not host-built binaries.
However, `NU1603` warnings showed approximate version substitutions in referenced
projects. This is not accepted as an exact-version build result.

The approved follow-up included all six referenced project asset graphs and
verified the additional 16 archives (220 total). Both restores then completed
without warnings in a fresh, network-disabled `linux/amd64` SDK container, with
`NU1603` treated as an error. Comparing the resulting package versions and content
hashes against all six existing project graphs found zero differences.

Exact-version offline NuGet restore is therefore verified. The user approved
connecting that package-only input to the production Dockerfiles and attempting
local startup. Building the images, starting the application stack and browser
acceptance remain separate verification gates.

The opt-in `docker-compose.offline.yml` override supplies a BuildKit named context
from `NUGET_OFFLINE_PACKAGES`, a directory containing only verified `.nupkg`
archives. Each backend mounts it read-only during restore at `/nuget-feed`.
The ordinary Dockerfiles have an empty default stage for that context; without
the override they continue using the normal online source. No package directory
is copied into the runtime image, and no application binaries come from the host.
The override requires an explicit package directory rather than silently falling
back to a network source.

```bash
export NUGET_OFFLINE_PACKAGES=/absolute/path/to/verified-nupkg-directory
DOCKER_DEFAULT_PLATFORM=linux/amd64 \
ATO_AZUREAI__ENABLED=false \
ATO_CACAUTH__SIMULATIONMODE=true \
docker compose -f docker-compose.mcp.yml -f docker-compose.offline.yml \
  up --build --wait --wait-timeout 180
```

This is offline **NuGet restore**, not a fully disconnected image build: base
images, OS packages and npm dependencies still need their normal approved
sources or existing Docker cache. Roll back the opt-in mode by omitting the
offline Compose override on a machine with access to its approved online feed.
Do not remove database volumes to change package sources.

The first real image build with this override successfully completed both MCP
and Chat NuGet restores. The focused suite now passes 62 tests. Full image
completion and startup are still blocked: both frontend `npm ci` steps reported
`Exit handler never called!`, and Chat subsequently failed to load
`ajv/package.json`.

The cached Chat npm debug log records repeated `ECONNRESET` failures downloading
tarballs from `registry.npmjs.org`, followed by the internal npm error and exit
code 0. Thus the install was incomplete despite Docker marking its step done.
The cause of those npm connection resets has not been verified as a Defender
rule. No frontend dependency versions, npm checks or security settings were
changed to hide this failure. Further npm-source investigation requires its own
bounded continuation; the role-assignment 500 follow-up remains unstarted.

The approved npm follow-up verified an existing Microsoft-hosted registry from
both lockfiles:
`https://ms-feed-25.pkgs.visualstudio.com/1es-public/_packaging/npm-public/npm/registry/`.
Fresh installs in the same amd64 Node images installed 490 Dashboard packages
and 1,479 Chat packages, with their build-tool entry points present. No lockfile
or package version was changed. npm's audit reported 13 Dashboard advisories
(6 high) and 50 Chat advisories (26 high); those findings are not remediated or
waived by a successful install.

The bounded npm build change adds `NPM_REGISTRY` to both frontend Dockerfiles and
forwards it through the base Compose file. The default remains
`https://registry.npmjs.org`. For the approved local build above, also export:

```bash
export NPM_REGISTRY=https://ms-feed-25.pkgs.visualstudio.com/1es-public/_packaging/npm-public/npm/registry/
```

The public feed was verified for the current lockfiles, not assumed to contain
future versions. Unset `NPM_REGISTRY` to return to the normal registry on a
machine whose policy permits it. No TLS, package integrity or audit checks are
disabled.

Latest Docker verification: all three application images built successfully and
were inspected as `linux/amd64`. The related unit suite passes 66 tests.
Dashboard `tsc --noEmit` passed locally; Chat `tsc --noEmit` passed using the
exact container-installed dependencies (host Chat dependencies were absent).
Default MCP and Chat Dockerfile checks passed without an offline context.
Dependency manifests and lockfiles remain unchanged.

Startup then reached healthy SQL Server and Redis, but MCP terminated during
`NarrativeLibrarySchemaAdditions.ApplyAsync`. SQL Server rejected the generated
`BeforeContent nvarchar(8000)` declaration with error 2717: the maximum bounded
`nvarchar` length is 4000. The same statement also declares
`ProposedContent nvarchar(8000)`. This is a schema-startup blocker, not a package
restore failure. The MCP restart loop was stopped without deleting any volumes;
Dashboard and Chat did not reach running acceptance in that attempt. The
narrative correction below is separate from the package-source work.

The authorized SQL Server fix is limited to replacing the two invalid bounded
Unicode declarations with `nvarchar(max)`, matching SQL Server's EF mapping for
the existing `[MaxLength(8000)]` properties. The application limit, SQLite schema,
tenant scope and approval behavior remain unchanged. A real SQL Server regression
must verify fresh additive creation, 8,000-character Unicode persistence, correct
column types and a repeat application preserving the row. No existing table is
dropped or shortened. Reverting the source change restores the startup defect;
no data-destructive rollback is needed or authorized.

SQL correction verification: the new real SQL Server integration test first
reproduced the same invalid-length exception, then passed after the two-column
change (1 passed, 0 skipped). It verifies full-length Unicode contents, non-null
`nvarchar(max)` columns, the unchanged EF 8,000-character maximum and row
preservation after repeat schema application. The existing targeted unit suite
also passed all 19 tests. Both backend images rebuilt as `linux/amd64`.

With the existing database volumes preserved, MCP now starts healthy and
`http://localhost:3002/health` returns HTTP 200 on this checkout's configured host
port. SQL Server and Redis remain healthy. Browser acceptance is still blocked:
Dashboard nginx exits with `unknown "force_single_tenant" variable`, and Chat's
`/health` returns HTTP 500 while JWT bearer options reject the metadata/authority
configuration. These are separate observed failures; their fixes are not part
of the SQL correction. The Dashboard restart loop was stopped, not masked by
changing its health check. No authentication or HTTPS checks were disabled.
Manual workspace testing, broader integration failures and the PR release gates
remain pending.

The authorized Dashboard follow-up is limited to defining the existing
`FORCE_SINGLE_TENANT` runtime setting as empty by default in its image. The nginx
entrypoint substitutes only defined environment variables; without that default,
the literal placeholder remains in the rendered configuration and nginx rejects
it as an unknown variable. Empty preserves the existing CSP behavior, while a
deployment can still set `true` for the existing organization-only UI behavior.
Verification must cover the image default and explicit empty/true values with
real nginx configuration checks, then HTTP/browser startup through Compose.
No authentication, tenant membership or Chat configuration changes are included.

Dashboard follow-up verification: the new runtime-default unit test failed before
the image change, then all 12 targeted Docker contract tests passed. The rebuilt
image passed real `nginx -t` checks and exact rendered-setting checks with the
default, explicit empty and explicit `true` values. Compose now reports Dashboard
healthy at `http://localhost:5173`; `/` and proxied `/api/health` return HTTP 200.
Browser simulation returned 204 and `/api/auth/me` returned 200 with the selected
`dev-cspadmin` identity and `CSP.Admin` provider workspace. Browser snapshots show
the provider portfolio and narrative-library page rendering.

This is startup/simulated-sign-in verification, not a complete E2E pass. A
portfolio-heading wait timed out while the observed route changed, and the
browser reported 403 responses; their causes were not investigated in this
bounded startup fix. Chat remains unhealthy with the previously recorded JWT
configuration failure. Manual role/workspace acceptance and existing release
gates remain open. Reverting the Dashboard image default reintroduces the unset
placeholder failure; no volume reset or data rollback is required.

The bounded role-assignment follow-up reproduced the recorded
AuthorizingOfficial-to-SystemOwner denial as HTTP 500. The detailed request log
shows `RoleAssignmentEndpoints.TryGetTenantId` throwing because the isolated
`RoleAuthorizationMatrixCoverageTests` host does not register `ITenantContext`.
The exception occurs before role authorization, not in the denial response.
The correction is limited to that fixture's missing scoped context dependency,
preserving its existing legacy claim-based scenario and production endpoint
behavior. The regression must retain HTTP 403 and `RBAC_ROLE_ASSIGN_DENIED`,
assert the exact caller/target roles and prove no target assignment was written.
This does not establish the cause of the Docker browser's 403 responses or the
remaining full-suite failures.

Verification after the fixture correction: all 29 disallowed role-matrix cases
pass, including the original AuthorizingOfficial-to-SystemOwner reproduction.
Each asserts the exact HTTP/error/role envelope and preservation of the sole
original caller assignment. No production authorization code changed, and no
Docker restart or rebuild is required. The broader integration suite and browser
acceptance were not rerun or declared green. To independently repeat this check:

```bash
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  --no-restore --filter 'FullyQualifiedName~RoleAuthorizationMatrixCoverageTests'
```

Rollback is limited to reverting the test-fixture correction; there are no
runtime configuration or database changes to undo.

CI follow-up started from run `35762556193`, integration job `106865970043`
at commit `942f72b2`: 1,223 passed, 64 failed and 20 skipped. The job's request
log also shows the separate `RoleAssignmentEndpointsTests` fixture throwing for
missing `ITenantContext` before processing its onboarding role requests. All
eight tests fail in a local reproduction. Its correction is likewise limited
to registering the scoped context, preserving the existing role-service and
last-administrator assertions. Separate CI HTTP 401 failures require independent
authentication-path investigation; no CI bypass, skip or assertion relaxation
is authorized as a way to make the lane pass.

The onboarding fixture correction passes all 37 role tests together (eight
onboarding cases plus the 29-cell denial matrix). The CI fallback-metadata HTTP
test also reproduces its 401 locally: its minimal host submits an anonymous
request to a chat boundary that now requires authenticated identity and validated
tenant context. That fixture must supply an explicit synthetic single-tenant
identity/context, following the existing MCP contract tests, while retaining
the backend metadata/warning assertions. Production authentication stays intact.

All 44 focused tests now pass (the two role fixtures and all fallback tests).
The full Release integration lane has not yet been rerun; this result does not
make CI green. The observed CI command uses Release, Development configuration,
SQLite `Data Source=:memory:` and `ATO_REQUIRE_DOCKER_TESTS=1`. A matching full-lane
run, followed by an approved push and actual CI verification, remains required.
Both corrections are test-host changes only, with no runtime or data rollback.

The full local Release run at `6ce64d0a` completed with 1,262 passed, 26 failed and
20 skipped (1,308 total), with Docker tests required. The next approved group
is eight failures in the person and organization-context onboarding fixtures.
Sequential request logs verify both throw for missing scoped `ITenantContext`
before endpoint logic. Person promotion's JSON parse errors follow its failed
person-creation request. The bounded correction registers the existing context
in those two fixtures and checks creation/promotion status before parsing or
continuing. Existing persistence, audit, missing-tenant denial, validation and
duplicate-link conflict expectations are retained. No production endpoint or
authorization change is included.

The two corrected onboarding fixtures now pass all nine of their tests (the
eight previously failing cases plus directory search). Combined with both role
fixtures, the Release run with CI settings passes 46 tests. The last full-run
inventory still contains 17 authentication-related 401 failures and one import
route 404 outside this group; their causes are not established by this result.
The full lane must be rerun after those groups are addressed.

The Kanban/PIM continuation reproduced representative chat requests as HTTP 401.
Their isolated hosts never set an authenticated user or bind an ambient tenant.
The chat path calls `WorkspaceService.Identity` through `WorkspaceChatScope`
before dispatch, while Development-mode compliance middleware does not supply
identity. The correction reuses the existing MCP routing fixture's synthetic
single-tenant identity binding through an explicitly invoked test-only helper.
Only routing/service fixtures use this helper; authentication and workspace
authorization tests must continue to exercise their own real boundary setup.
No process-wide auth bypass, membership seeding or response expectation change
is included.

Kanban/PIM verification passes all 51 Release tests across those two fixtures and
the MCP routing fixture from which the shared identity binding was extracted.
This includes the 13 Kanban/PIM failures from the last full-run inventory.
Together with the eight corrected onboarding cases, 21 of that run's 26 failures
now pass focused verification. Five inventory entries remain unaddressed:
two simulation-persona cases, anonymous Tier-1 access, multipart chat streaming
and the inheritance-import route. No full-lane or GitHub CI green result is
claimed from these targeted runs.

The next bounded investigation reproduced all five remaining entries. The user
confirmed that HTTP tool execution requires authenticated identity, including
calls labeled Tier 1; public discovery and health checks remain public. The
anonymous execution test will assert the exact identity-denial envelope instead
of expecting execution. Simulation tests must continue using the real CAC
simulation middleware, now with configured directory/object IDs and an explicit
single-tenant request binding. Multipart routing gets complete claims without
authenticating requests that omit its test authentication header. Import/apply
is registered: its current rejection originates in responsibility authorization
because the fixture lacks an assigned organization member, not route discovery.
The import/apply regression moves to the existing production-pipeline workspace
fixture with an explicitly seeded ISSO membership and system assignment. Its
deliberately invalid preview token must reach the handler and return HTTP 400
`INVALID_PREVIEW_TOKEN`, rather than merely accepting any non-404 response.
All 122 tests in the affected fixtures pass in Release, including the existing
ISSO/ISSM allow and Mission Owner/AO/Assessor/Administrator deny matrix. The
full CI-equivalent integration lane is the next gate; these focused results
do not establish a green GitHub run.

The full local CI-equivalent Release run at `d883a76f` completed on
2026-09-22: **1,288 passed, zero failed, 20 skipped (1,308 total)** in
3.0175 minutes. TRX comparison confirms the same 20 skipped test names as the
earlier 26-failure run; none were newly skipped. The run used CI's Development
environment, SQLite settings and `ATO_REQUIRE_DOCKER_TESTS=1`, with the local
Docker Desktop socket for SQL Server containers. Local execution was macOS
arm64, not the GitHub Linux runner. Actual GitHub CI verification awaits an
approved push and a new workflow run. The separate Chat JWT health failure and
manual workspace acceptance gates remain open; this result does not establish
that the PR is merge-ready.

## Purpose

### Docker Chat startup follow-up (2026-09-22)

Live Chat logs show JWT options initialization rejecting `/v2.0`: Compose does
not supply Entra settings, Development permits absent settings, and Chat still
constructs an authority from empty values. Keep Development's unconfigured mode
fail-closed for protected requests while permitting its existing public health
and info endpoints; never disable HTTPS metadata or token validation. Production
continues requiring Entra configuration at startup. Also set Chat's Compose
`ATO_SERVER__URLS` explicitly: its Development JSON currently binds container
loopback, making the published host port unreachable. Regression coverage must
exercise the actual Chat host, public 200 responses, protected 401 responses,
unchanged JWT validation flags and the Compose bind setting.

Verification: 11 focused integration tests and 11 focused unit tests pass,
including complete/partial/missing Entra settings, CAC token parity and
production configuration validation. Rebuilt only the Chat amd64 image using
the approved offline NuGet archives/npm registry, and recreated only Chat with
the existing volumes. Docker reports healthy. From the host, `/health` and
`/api/info` return 200; `/api/conversations` returns 401 both anonymously and
with an invalid bearer token. Chat now listens on `0.0.0.0:5001`. This clears
the JWT startup/port-reachability blocker, not authenticated Chat acceptance:
valid Entra configuration and a real authorized identity are still required.

Browser-assisted acceptance against the running Docker Dashboard:

| Check | Observed result |
|---|---|
| CSP ordinary landing and refresh | Provider portfolio and CSP.Admin scope render |
| Explicit workspace switch | Confirmation opens; chooser lists the authorized provider workspace; selecting it restores the provider URL |
| Provider Narrative Library | Real scoped page renders, with no published reference narratives |
| Mission Owner simulation | Identity selection returns 204, then explicit `NoTenantAssignment`; no membership was granted |
| Organization-role and two-organization/tab scenarios | Not verified; require explicitly assigned acceptance identities |
| Reference publication/consumption | Not verified; require scoped published reference fixtures |

The CSP identity was restored after the checks. Provider navigation makes a
background `/api/onboarding/organization-context` request that returns 403; the
sequential server trace records `CacPassthrough` forbidden while provider summary
and notification capabilities return 200. Its frontend call-site cause remains
uninvestigated. No customer memberships, role grants or reference content were
created during this pass. These checks are not user sign-off or proof of
production Entra authorization. Separately, GitHub integration run 35795659028
failed after the earlier local green run; that new failure remains undiagnosed.

The dashboard already resolves provider and organization variants of portfolio,
systems, components, capabilities and control pages. The proposed change makes
that distinction a consistent authenticated workspace, rather than deriving
navigation from a cached CSP endpoint probe or support impersonation.

The hosting CSP remains a single provider per deployment. Customer organizations
are isolation tenants; organizational subgroups and Entra directories are
different concepts and must not supply the active-organization label.

## Intended experiences

| Workspace | Responsibilities |
|---|---|
| CSP | Provider capabilities, shared-control definitions, provider evidence and narratives, customer organizations and authorized cross-organization oversight |
| Organization | Authorized systems, mission context, boundaries, subscribed capabilities, customer/shared responsibilities, narratives, evidence, assessments, remediation and monitoring |

Organization access does not confer administration or approval. Mission Owners
receive authorized mission/system responsibilities, not organization
administration, SCA review authority or AO approval authority. Multi-role users
retain their valid scoped permissions without turning a global persona into a
permission grant.

## Context is explicit and independent per tab

The user confirmed that separate tabs and copied deep links must retain
independent authorized contexts.

The proposed route families are:

- `/workspaces/csp/...`
- `/workspaces/organizations/{tenantId}/...`
- `/workspaces/organizations/{tenantId}/systems/{systemId}/...`
- `/workspaces/support/organizations/{tenantId}/...` (explicit audited support only)

These identifiers select context; they never authorize it. Each request must
validate the authenticated identity, membership, owning system and operation.
No authentication tokens belong in a route.

The workspace header shows the workspace, active organization, selected system
and effective roles. Loading, denied, missing and revoked contexts are explicit.
The previous organization's data must not remain visible under a new label.

Switching in one tab must not mutate ordinary scope in another tab. A remembered
organization may suggest a landing page only after revalidation. It cannot
override an authorized explicit deep link.

## Support is not ordinary workspace selection

CSP support impersonation remains a separate audited operation, visibly marked
with actor, target and expiration. An ordinary workspace switch neither starts
impersonation nor silently uses an impersonation cookie created in another tab.
Ending/expiring support must remove support-derived access.

The exact request/session migration is gated on the membership and scope
contracts; this document does not claim the current cookie-based behavior meets
the independent-tab requirement.

## Provider changes and customer approval

A provider mapping is not proof of inheritance or implementation. The customer
experience must show persisted Inherited, Shared, Customer or Undesignated
responsibility and its source. Missing baseline/allocation and pending review
are distinct states.

Provider changes should lead to affected-control review work. Approved customer
narratives remain unchanged until an authorized reviewer accepts a versioned
replacement. Authorization decisions are outside automatic propagation.

The Narrative Library from #1001 supplies provider/capability,
organization and system references. Those inputs remain references, not
implementation evidence. Library integration is a dependency, not a renamed
existing component-document list or a placeholder navigation link.

## Delivery dependencies

| Concern | Tracked work |
|---|---|
| Public login bootstrap and fresh CSP lifecycle | #943, #941, #944 |
| Authorized ordinary organization membership | #942 |
| Correct organization identity and stale-context handling | #950 |
| Server-authoritative Mission Profile permissions | #968, implemented by merged PR #1005; feature-branch synchronization pending |
| Responsibility-aware system inheritance | #957 |
| State-aware narrative review and scoped Narrative Library | #1001 |

Issue states and detailed merge gates belong to the feature planning artifacts.
An open dependency is not assumed to be implemented, and this feature cannot
be declared complete while required scope/permission/review integrations remain
missing.

## Validation and rollout

Planned verification includes unit tests, real local HTTP-pipeline integration
tests with tenant-resolution bypass disabled, browser persona journeys,
two-tab/history/deep-link cases and explicit negative authorization tests.
Synthetic UI mocks do not establish backend isolation or real persistence.

Legacy routes will resolve and redirect through authorized context. Previously
stored browser persona/settings values do not grant permissions. Rollout must
be coordinated between server and dashboard, with no production authorization
bypass and no automatic approval of customer artifacts.

Design and implementation approval are recorded. Dependency contracts, testing,
manual acceptance and publishing approval remain gates. Implementation progress
must not be confused with a shipped feature.

### First implementation increment

The branch now has typed workspace URL helpers and shared legacy system-alias
redirects that preserve query strings/fragments. It also repairs a duplicate
Mission Profile form encountered while integrating the server-permission fix.
Canonical workspace root routes and ordinary multi-organization selection now
consume the implemented workspace membership/request-scope contract.

Focused unit and synthetic-API browser tests verify this foundation, not the
complete authenticated workspace feature. The user authorized implementing the
#942 membership prerequisite on the same branch; provider responsibilities and
Narrative Library integration retain their separate dependency gates.

### Authenticated shell increment

Public login, callback and error routes do not mount organization, chat or
onboarding providers. Authenticated routes share `/me`. Canonical routes require
matching server workspace data and, for system routes, an authorized
`workspace-access` response before private providers or domain pages mount.
Incomplete new responses fail closed; genuinely old unscoped responses retain
legacy navigation. A legacy system bookmark with multiple authorized contexts
opens the explicit picker rather than guessing from a saved organization.

The shared identity lifetime includes the URL target and MSAL account identity
(issuer environment, directory, local object ID and home-account ID). Changing
the account invalidates the previous identity and private providers even when
the workspace URL is unchanged. Active-account events also invalidate that
lifetime; public routes still leave `/me` disabled. The authentication transport
owns which MSAL account supplies tokens, not the workspace shell.

The header labels provider, organization or audited-support mode, the active
server organization, selected system and all effective roles. Switching uses
React Router history and a paginated authorized-workspace picker, not selection
cookies or impersonation. A confirmation warns about unsaved changes on every
voluntary switch because form snapshot registries are local to individual forms.
Cancel keeps the current form mounted.

Organization membership administration is at `settings/memberships`; provider
administration is at `organizations/{organizationId}/memberships`, both beneath
their canonical workspace root. Server membership-management permissions gate
entry; provider routes additionally resolve the organization display name from
the tenancy API. Directory IDs are entered explicitly, not inferred from internal
organization identifiers.
Successful self-revocation in the ordinary organization workspace refreshes the
shared identity so its previous permissions and private providers are invalidated
immediately. The wrapper matches the organization, object ID and directory ID;
when the directory is absent, it also requires the current Person ID instead.
This fallback triggers a refresh only and never grants authority. Revoking another
person's membership or working in support mode does not remount the acting user's shell.

Provider organization/system rows open ordinary membership context. Their
separate **Audited support** actions require confirmation and use the existing
support endpoints. The server-driven support banner also renders for cookie-only
authentication. Ordinary selection does not end support in another tab.

Workspace support cookies are backed by a durable, actor-bound authorization
record. Exiting support revokes that presented session before deleting its cookie;
replaying the captured token must fail on another application instance as well
as on an existing hub connection. The server does not positively cache support
authorization. Existing stateless workspace support tokens are not automatically
enrolled by the schema upgrade: start a new audited support session after rollout.
Ordinary tabs and independently issued support sessions are not implicitly revoked.

Local synthetic checks (from `src/Ato.Copilot.Dashboard`):

```bash
npm exec tsc -- --noEmit
npm exec --yes --package=node@20 -- node node_modules/vitest/vitest.mjs run
# Start a dedicated server on a free port in a separate terminal:
npm exec --yes --package=node@20 -- node node_modules/vite/bin/vite.js \
  --host 127.0.0.1 --port 5186 --strictPort
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5186 \
  npm exec --yes --package=node@20 -- node node_modules/@playwright/test/cli.js \
  test e2e/tests/workspace-shell.spec.ts \
  e2e/tests/workspace-route-migration.spec.ts \
  e2e/tests/mission-profile-permissions.spec.ts --project=chromium
```

For manual acceptance against a local API, sign in normally as a Mission Owner,
open an assigned system's mission profile, confirm the organization and complete
role set, make an unsaved edit and cancel **Switch workspace**. Then confirm a
switch, refresh, use browser Back, and repeat in a second tab with another
authorized organization. As a CSP administrator, compare ordinary organization
entry with explicitly confirmed support and verify the banner/exit behavior.
Try a foreign organization/system URL and confirm recovery appears without
private page data. Synthetic browser journeys validate UI routing and request
ordering only; they do not establish backend isolation or production readiness.

### Notification transport and preferences

The notification center first requests
`GET /api/dashboard/notifications/capabilities` with the ordinary authenticated
API transport and current workspace selectors. REST access is based on the
server session; an MSAL account is not required for cookie/simulation sessions.
The response binds the recipient to the authenticated actor. The client never
substitutes a placeholder recipient or sends a different account's object ID.
Notification and progress clients share capability-response validation; a
real-time client connects only to its explicitly advertised hub path.

When real-time delivery is unavailable or disconnected and the server declares
`rest-polling`, the client polls at the supplied interval (currently 30 seconds).
It displays the reason real-time delivery is unavailable. A healthy registered
real-time connection suspends that fallback timer.
A SignalR connection is attempted only after the server confirms bearer
readiness. Token acquisition remains pinned to the original account, and each
reconnect rechecks capabilities. Workspace/account changes abort REST work,
discard late results, clear notification state and stop the previous connection.
Errors are visible with retry rather than presented as an empty successful list.
On narrow screens, the panel spans the header width with viewport gutters so
read and retry actions remain visible; desktop keeps the bell-anchored popover.

Preferences use the API's actual fields: POA&M overdue alerts, ATO expiration
alerts, compliance drift alerts and warning days. Email/Teams/Slack delivery
configuration is not supported by this preference endpoint and is not offered
by this panel. A failed preference read cannot silently create editable defaults.
Saving and loading are canceled when the organization context changes.

The preference database key is now unique on `(TenantId, UserId)`. Startup creates
that index before dropping the old global `UserId` index in a transaction.
Existing values are preserved, and migration errors fail startup. SQLite fresh
creation, upgrade, duplicate rejection and repeat startup have automated
coverage; execution of this upgrade against SQL Server still requires validation.

For local manual acceptance, use one authorized cookie/simulation identity with
membership in two organizations. Open the notification panel in each tab,
confirm each request has that tab's workspace selectors, and confirm a bearer-only
real-time notice appears without a failed SignalR connection. Wait 30 seconds
for an authorized list refresh. Mark a notification read, save different
preferences in each organization, reload, and verify the values remain separate.
Revoke membership and confirm the next refresh removes data and displays denial.

### Package, SSP and scan progress

Progress clients validate bearer readiness and the exact advertised hub before
connecting. Cookie-only sessions instead use the existing authenticated status
endpoints:

- Package: `GET /api/v1/systems/{systemId}/packages/{packageId}`
- SSP export: `GET /api/dashboard/systems/{systemId}/exports/{exportId}`
- Scan import: `GET /api/dashboard/systems/{systemId}/scans/import/{importId}/status`

Personal-notification `rest.available` and polling recommendations do not
authorize or schedule these separate resources. Default/scan polling uses five
seconds; the package shortcut retains its three-second cadence. Terminal states
stop polling. Status errors, including 401/403/404, stop automatic retries and
remain visible. Context changes cancel requests and prevent stale downloads or
completion callbacks.

SSP status polling cannot supply a percentage or detailed failure reason because
those fields are absent from the API response. It displays indeterminate status
and generic failure text rather than inventing details. Package real-time events
are hints to refetch authorized status; event-provided download URLs do not
override the authenticated download route.

For local acceptance, repeat package generation, ordinary SSP export and scan
import with cookie-only and bearer sessions. Switch to another authorized
organization while an operation is pending and verify no old-context completion
or download appears there. Revoke access or remove the import job and verify
polling stops with a visible error. Automated client tests use synthetic data;
live cookie/bearer acceptance remains outstanding.
