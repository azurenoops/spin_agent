# Research: CAC Simulation Mode

**Feature**: `027-cac-simulation-mode` | **Date**: 2026-03-12

## R1: Interface Placement — *(Superseded)*

**Status**: Superseded — no new interface is needed.

**Original decision**: Place `ISmartCardService` in `Ato.Copilot.Core.Interfaces.Auth`.

**Why superseded**: Post-research analysis of the full auth pipeline revealed that the existing `CacAuthenticationMiddleware` already serves as the abstraction layer between identity sources and downstream services. No smart card abstraction exists or is used anywhere in the codebase — the entire auth system operates through JWT → claims → session. Simulation mode is implemented as a ~30-line middleware branch that synthesizes a `ClaimsPrincipal` from configuration, making a separate interface unnecessary.

**Impact**: Eliminates `ISmartCardService.cs`, `SimulatedSmartCardService.cs`, `SmartCardService.cs`, and `SmartCardIdentity.cs` from the implementation scope.

---

## R2: `CacAuthOptions` Extension vs Separate Config Class

**Decision**: Extend `CacAuthOptions` with `SimulationMode` bool and a nested `SimulatedIdentityOptions` sub-object.

**Rationale**: The current `CacAuthOptions` class is minimal (2 properties: `DefaultSessionTimeoutHours`, `MaxSessionTimeoutHours`). The spec config keys `CacAuth:SimulationMode` and `CacAuth:SimulatedIdentity:*` map directly to extending this class. The .NET Options pattern supports nested POCO binding natively — `IOptions<CacAuthOptions>` will bind `CacAuth:SimulatedIdentity:UserPrincipalName` to a nested property automatically.

Add to `CacAuthOptions`:

```csharp
public bool SimulationMode { get; set; }
public SimulatedIdentityOptions? SimulatedIdentity { get; set; }
```

With a separate POCO for the nested object:

```csharp
public class SimulatedIdentityOptions
{
    public string UserPrincipalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? CertificateThumbprint { get; set; }
    public List<string> Roles { get; set; } = [];
}
```

**Alternatives considered**: Separate `SimulationModeSettings` class with its own config section — rejected because it splits CAC auth configuration across two `IOptions<>` registrations.

---

## R3: Middleware Identity Injection Pattern

**Decision**: Insert a simulation-mode branch in `CacAuthenticationMiddleware.InvokeAsync` that synthesizes a `ClaimsPrincipal` from config and sets `context.User`.

**Rationale**: The existing middleware already demonstrates the exact pattern at line ~183:

```csharp
var claims = new List<Claim>();
foreach (var claim in jwt.Claims)
    claims.Add(new Claim(claim.Type, claim.Value));
var identity = new ClaimsIdentity(claims, "Bearer");
context.User = new ClaimsPrincipal(identity);
```

For simulation mode, replace the current `Development` bypass block with:

```csharp
if (_cacAuthOptions.SimulationMode && environment == "Development")
{
    var simId = _cacAuthOptions.SimulatedIdentity
        ?? throw new InvalidOperationException(
            "CacAuth:SimulatedIdentity configuration required when SimulationMode=true");

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, simId.UserPrincipalName),
        new(ClaimTypes.Name, simId.DisplayName),
        new("preferred_username", simId.UserPrincipalName),
        new("amr", "mfa"),
        new("amr", "rsa"),
    };
    foreach (var role in simId.Roles)
        claims.Add(new(ClaimTypes.Role, role));

    context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Simulated"));
    context.Items["ClientType"] = ClientType.Simulated;
    await _next(context);
    return;
}
```

Key design points:
- Authentication scheme name `"Simulated"` (not `"Bearer"`) — makes `ClaimsIdentity.AuthenticationType` distinguishable
- `amr` claims `mfa` + `rsa` injected so downstream CAC-check code passes
- `ClientType.Simulated` flows into `CacSession` creation for FR-013 audit tagging
- Non-Development environments with `SimulationMode = true` log a security warning and fall through to real auth

**Alternatives considered**:
- Separate `SimulationAuthenticationMiddleware` — rejected; adds pipeline complexity for a simple conditional branch
- Custom `IAuthenticationHandler` — overkill since the codebase uses custom middleware, not `[Authorize]` attributes

---

## R4: Evidence Exclusion Pattern (FR-014)

**Decision**: The exclusion filter operates at evidence content generation time via the `ClientType.Simulated` marker.

**Rationale**: `EvidenceStorageService.CollectEvidenceAsync` does **not** currently reference `CacSession` records. Evidence is collected from Azure services (Policy, Defender) and stored with `CollectedBy = "Security Posture Intelligence Navigator (automated)"`. The `ComplianceEvidence` model has no `IsSimulated` flag or `CacSession` foreign key.

FR-014 is a **forward-looking guard** for AC-2, AU-2, AU-3 controls where session/audit data would appear in evidence content. Implementation approach:

1. Add `ClientType.Simulated` to the `ClientType` enum (currently: `VSCode`, `Teams`, `Web`, `CLI`)
2. When evidence collection expands to include session data, filter with `.Where(s => s.ClientType != ClientType.Simulated)`
3. Add an XML doc comment on `ClientType.Simulated` documenting the exclusion contract

**Alternatives considered**:
- `IsSimulated` bool on `ComplianceEvidence` — rejected; evidence artifacts aren't simulated (they come from real Azure services), only sessions are
- Post-hoc filtering in evidence retrieval — rejected; better to never include simulated data than filter later

---

## R5: DI Override Pattern for Integration Tests (FR-016)

**Decision**: Use the existing `WebApplication.CreateBuilder` + `UseTestServer()` pattern with per-fixture `CacAuthOptions` configuration.

**Rationale**: The existing integration test (`AuthEndpointIntegrationTests`) does **not** use `WebApplicationFactory<TStartup>`. It manually constructs the host:

```csharp
var builder = WebApplication.CreateBuilder(...);
builder.Services.Configure<CacAuthOptions>(...);
builder.WebHost.UseTestServer();
_app = builder.Build();
```

For per-fixture identity swapping (FR-016):

```csharp
builder.Services.Configure<CacAuthOptions>(opts =>
{
    opts.SimulationMode = true;
    opts.SimulatedIdentity = new SimulatedIdentityOptions
    {
        UserPrincipalName = "test.issm@dev.mil",
        DisplayName = "Test ISSM",
        Roles = ["ISSM"]
    };
});
```

Each test class creates its own `WebApplication` instance with a different identity via `IAsyncLifetime`. No runtime swap needed — the spec confirms "single identity per startup."

**Alternatives considered**:
- `WebApplicationFactory<T>.WithWebHostBuilder` — codebase doesn't use this pattern; would be inconsistent
- `IOptionsMonitor<T>` for runtime swap — over-engineered for single-identity-per-startup requirement

---

## R6: Real `SmartCardService` Implementation — *(Superseded)*

**Status**: Superseded — no service abstraction is needed.

**Original decision**: Create `ISmartCardService` interface with a placeholder real implementation that throws `NotSupportedException`.

**Why superseded**: The codebase has zero physical smart card interaction. The entire auth pipeline is JWT → `CacAuthenticationMiddleware` → `ClaimsPrincipal` → `HttpUserContext` → downstream services. All downstream services (`ICacSessionService`, `IPimService`, `ICertificateRoleResolver`, `IUserContext`) are identity-source-agnostic — they consume claims from `context.User`, not from any smart card API.

Simulation mode only needs to replace how `context.User` gets populated (synthesize claims from config instead of parsing JWT), which is a middleware branch, not a service replacement. No placeholder service or `NotSupportedException` is needed.

**Impact**: Eliminates the need for any service-level abstraction. The middleware IS the abstraction.
