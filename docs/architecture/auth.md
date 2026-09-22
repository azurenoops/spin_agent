# Authentication Architecture

> This page describes the runtime auth flows added by
> [Feature 051 — First-Class Login Experience](../features/051-login.md).
> It complements
> [Feature 003 — CAC/PIV Authentication + PIM](https://github.com/azurenoops/ato-copilot/tree/main/specs/003-cac-auth-pim)
> (server-side JWT validation + role activation) and
> [Feature 048 — Tenant Isolation](https://github.com/azurenoops/ato-copilot/tree/main/specs/048-tenant-isolation)
> (CSP-Admin impersonation + tenant query filter).

## Surfaces and flows

Security Posture Intelligence Navigator supports **three distinct auth flows** depending on the
client surface. They all converge on the same server-side
`CacAuthenticationMiddleware` + `LoginAuditEvents` table.

```mermaid
sequenceDiagram
    autonumber

    actor User
    participant SPA as Dashboard SPA<br/>(React 19 + MSAL.js)
    participant VSC as VS Code Extension<br/>(MSAL Node)
    participant Teams as M365 Teams Bot<br/>(Bot Framework)
    participant Entra as Microsoft Entra ID<br/>(public / .us)
    participant Mcp as MCP API<br/>(/api/auth/* + Cac middleware)
    participant DB as AtoCopilotContext<br/>(LoginAuditEvents)
    participant Cache as IDistributedCache<br/>(Redis / in-memory)

    rect rgb(232, 241, 255)
    Note over User,DB: Dashboard MSAL flow (US1, US2, US3, US10)
    User->>SPA: GET /dashboard/systems/abc-123
    SPA->>SPA: RequireAuth → no session
    SPA->>Mcp: GET /api/auth/login-config (anonymous)
    Mcp-->>SPA: { branding, defaultMethod, msal, simulation? }
    SPA->>User: render /login?return=/dashboard/systems/abc-123
    User->>SPA: click "Sign in with CAC/PIV"
    SPA->>Entra: msalInstance.loginRedirect()
    Entra-->>SPA: id_token + access_token (redirect callback)
    SPA->>Mcp: GET /api/auth/me (Bearer)
    Mcp->>Cache: debounce key `login-success:<oid-hash>:<tenant>`
    Mcp->>DB: AppendAsync LoginSuccess (debounced 5 min)
    Mcp-->>SPA: { oid, persona, homeTenant, pimRoles, tenantMemberships }
    alt single membership
        SPA->>SPA: navigate(return)
    else multiple memberships OR CSP-Admin
        SPA->>SPA: navigate(/select-tenant)
        User->>SPA: pick T-Coastal + check "Remember"
        SPA->>Mcp: POST /api/auth/select-tenant { tenantId, remember:true }
        Mcp->>DB: AppendAsync TenantSwitch
        Mcp-->>SPA: Set-Cookie ato-remembered-tenant=<HMAC-signed>
        SPA->>SPA: navigate(return)
    end
    end

    rect rgb(232, 255, 232)
    Note over User,DB: VS Code device-code flow (US5)
    User->>VSC: @ato run compliance assessment
    VSC->>VSC: SecretStorage.get('ato-token') → miss
    VSC->>Entra: POST /devicecode (cloud from Auth:Cloud)
    Entra-->>VSC: { user_code, verification_uri }
    VSC->>User: showInformationMessage(user_code)
    VSC->>VSC: env.openExternal(verification_uri)
    User->>Entra: enter user_code + sign in with CAC/PIV
    VSC->>Entra: POST /token (polling)
    Entra-->>VSC: access_token + refresh_token
    VSC->>VSC: SecretStorage.store('ato-token', tokens)
    VSC->>Mcp: GET /api/compliance/... (Bearer)
    Mcp->>DB: AppendAsync LoginSuccess (Surface=VSCode)
    end

    rect rgb(255, 245, 232)
    Note over User,DB: M365 Teams branching (US6)
    User->>Teams: @mention bot
    Teams->>Teams: lookup IIdentityLinkStore[teams-tenant + oid]
    alt no link AND Auth:TeamsSso:Mode = Optional/Required
        Teams->>Entra: SSO token-exchange via Bot Framework
        Entra-->>Teams: access_token
    else SSO unavailable OR Mode=Disabled
        Teams->>User: Adaptive Card "Sign in" button
        User->>Teams: click → OAuthPrompt → Entra
        Entra-->>Teams: access_token
    end
    Teams->>Teams: IIdentityLinkStore.bind(teams-tenant + oid)
    Teams->>Mcp: any subsequent /api/* (Bearer)
    Mcp->>DB: AppendAsync LoginSuccess (Surface=M365)
    end

    rect rgb(255, 235, 235)
    Note over User,DB: Throttle short-circuit (Phase 13.1 / US10)
    User->>Mcp: 21st GET /api/auth/me with same X-Forwarded-For
    Mcp->>Cache: PeekAsync(ip, identity)
    Cache-->>Mcp: { Allowed=false, RetryAfter }
    Mcp->>DB: AppendAsync LoginFailure(throttled=true)
    Mcp-->>User: 429 TOO_MANY_LOGINS + Retry-After
    end
```

## Middleware ordering (`Program.cs`)

```text
CorrelationIdMiddleware           # W3C activity + Items["CorrelationId"]
SerilogRequestLogging
CorsMiddleware
RateLimiter                       # Per-endpoint sliding window (Feature 006)
RequestSizeLimitMiddleware
CacAuthenticationMiddleware       # JWT validation + amr=mfa,rsa
LoginThrottleMiddleware           # NEW Feature 051 — peek → short-circuit 429
TenantResolutionMiddleware        # Feature 048
ComplianceAuthorizationMiddleware # Tier 2 CAC gate, PIM tier enforcement
RequestMetricsMiddleware
AuditLoggingMiddleware            # Compliance audit (separate from LoginAuditEvents)
```

The throttle middleware is placed **after** `CacAuthenticationMiddleware`
so that the identity key (`oid` claim) is resolvable when present —
unauthenticated requests fall back to the `anonymous` identity bucket.

## Storage

| Concern | Storage | Lifetime |
| --- | --- | --- |
| `LoginAuditEvents` (hot table) | `AtoCopilotContext` DbSet | 13 months (FR-036a) |
| Login audit cold archive | `AzureBlobAppendArchiveSink` (prod) / `FileSystemArchiveSink` (dev) | Indefinite (immutable append-blob) |
| Throttle counters | `IDistributedCache` — Redis in prod, in-process memory in dev/test | 60-second sliding bucket |
| Remembered-tenant cookie | First-party HMAC-SHA256 signed cookie, no server mirror | `Auth:RememberTenantCookieDays` (default 30 days) |
| Impersonation cookie | First-party HMAC-SHA256 signed cookie (Feature 048) | `Auth:Impersonation:Lifetime` (default 1 hour) |
| MSAL.js access token | localStorage (dashboard) / SecretStorage (VS Code) | Token's `exp` claim — MSAL silent-renewal handles refresh |

## Configuration surface

```jsonc
// appsettings.json
{
  "Auth": {
    "DefaultMethod": "Cac",                   // Cac | Entra
    "IdleTimeoutMinutes": 30,
    "RememberTenantCookieDays": 30,
    "Cloud": "AzureUSGovernment",             // AzurePublic | AzureUSGovernment
    "VSCode": { "DeviceCodeProvider": "EntraDirect" },
    "Throttle": {
      "Development": { "PerIpPerMinute": 100, "PerIdentityPerMinute": 100 },
      "Production":  { "PerIpPerMinute": 20,  "PerIdentityPerMinute": 10  }
    },
    "TeamsSso": { "Mode": "Optional" },       // Required | Optional | Disabled
    "Archive": {
      "Sink": "AzureBlobAppend",              // AzureBlobAppend | FileSystem
      "RunHourUtc": 2
    },
    "Cookie": { "SigningKey": "<from-Key-Vault>" }  // Required outside Development
  }
}
```

`AuthOptionsValidator` fails the host at startup if `Cookie:SigningKey`
is missing in any non-`Development` environment.

## Cross-reference

- Feature 051 spec — [specs/051-login/spec.md](https://github.com/azurenoops/ato-copilot/blob/051-login/specs/051-login/spec.md)
- Feature 051 contracts — [specs/051-login/contracts/](https://github.com/azurenoops/ato-copilot/tree/051-login/specs/051-login/contracts)
- Feature 048 tenant isolation — [docs/architecture/tenant-isolation.md](./tenant-isolation.md)
