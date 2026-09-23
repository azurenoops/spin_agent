# Security Model

> RBAC roles, PIM integration, CAC authentication, audit logging, and data protection.

---

## Table of Contents

- [Authentication](#authentication)
- [RBAC Roles](#rbac-roles)
- [PIM — Privileged Identity Management](#pim--privileged-identity-management)
- [Tool-Level Authorization Tiers](#tool-level-authorization-tiers)
- [Authorization Flow](#authorization-flow)
- [Audit Logging](#audit-logging)
- [Data Protection](#data-protection)
- [Session Management](#session-management)

---

## Authentication

### CAC/PIV Certificate Authentication

Security Posture Intelligence Navigator supports Common Access Card (CAC) and Personal Identity Verification (PIV) certificate authentication for DoD environments.

**Flow:**
1. Client presents certificate via JWT `amr` claim
2. `CacAuthenticationMiddleware` validates certificate
3. Certificate mapped to role via `CertificateRoleMapping` entity
4. Session created as `CacSession` with configurable timeout

**CAC Tools:**

| Tool | Purpose |
|------|---------|
| `cac_status` | Check CAC/PIV session status |
| `cac_sign_out` | End CAC session |
| `cac_set_timeout` | Configure session timeout duration |
| `cac_map_certificate` | Map certificate to compliance role |

### CAC Simulation Mode

For local development without a physical smart card, `CacAuthenticationMiddleware` supports a simulation mode that synthesizes a `ClaimsPrincipal` from configuration.

**How it works:**

1. `CacAuth:SimulationMode` set to `true` in `appsettings.Development.json`
2. `CacAuth:SimulatedIdentity` provides UPN, display name, optional thumbprint, and roles
3. Middleware creates a `ClaimsPrincipal` with claims matching real CAC auth (`NameIdentifier`, `Name`, `preferred_username`, `amr`, `Role`, `x5t`)
4. `ClientType.Simulated` is set on the request — distinguishable from real sessions
5. Startup validation ensures identity config is complete before the app starts

**Safety guards:**

- Simulation **only activates** when `ASPNETCORE_ENVIRONMENT=Development`
- In Production/Staging the flag is silently ignored and a security warning is logged
- `appsettings.json` (production config) must never contain simulation keys
- `ClientType.Simulated` sessions are excluded from compliance evidence (FR-014)

See [Getting Started — Engineer](../getting-started/engineer.md#cac-simulation-mode-local-development) for configuration details.

### Azure Entra ID (Fallback)

When CAC is not available, standard Azure Entra ID authentication is supported via JWT bearer tokens with Microsoft Identity Web.

---

## RBAC Roles

Seven compliance roles with hierarchical permissions:

| Role | Constant | Access Level | Feature 015 Use |
|------|----------|-------------|-----------------|
| `Administrator` | `ComplianceRoles.Administrator` | Full access to all operations | System configuration |
| `Auditor` | `ComplianceRoles.Auditor` | Read-only access + audit trails | SCA assessment workflow |
| `Analyst` | `ComplianceRoles.Analyst` | Assessment and monitoring | Assessment analysis |
| `Viewer` | `ComplianceRoles.Viewer` | Read-only assessments/status | Dashboard viewing |
| `SecurityLead` | `ComplianceRoles.SecurityLead` | Create/assign tasks, approve | ISSM workflow |
| `PlatformEngineer` | `ComplianceRoles.PlatformEngineer` | Work tasks, comment, self-assign | Engineer workflow |
| `AuthorizingOfficial` | `ComplianceRoles.AuthorizingOfficial` | Authorization decisions, risk acceptance | **AO-exclusive** (NEW) |

### Permission Sets

**Compliance Permissions:**
- `ViewCompliance` — View assessments and compliance status
- `RunAssessments` — Execute compliance scans
- `ManageRemediation` — Create and execute remediation plans
- `ManageSettings` — Modify configuration

**Kanban Permissions:**
- `ViewBoard`, `CreateTask`, `AssignTask`, `MoveTask`, `Comment`
- `ExecuteRemediation`, `CollectEvidence`, `Export`
- `BulkUpdate`, `ArchiveBoard`, `ManageBoard`

**PIM Permissions:**
- `ViewEligible`, `ActivateRole`, `DeactivateRole`, `ListActive`
- `ExtendRole`, `ApproveRequest`, `DenyRequest`, `ViewHistory`

---

## PIM — Privileged Identity Management

Azure AD Privileged Identity Management (PIM) provides just-in-time role elevation for privileged operations.

### PIM Tools (15)

| Tool | Tier | Description |
|------|------|-------------|
| `pim_list_eligible` | Read | List eligible PIM roles for current user |
| `pim_list_active` | Read | List currently active PIM roles |
| `pim_activate_role` | Write | Activate an eligible role with justification |
| `pim_deactivate_role` | Write | Deactivate a currently active role |
| `pim_extend_role` | Write | Extend an active PIM session |
| `pim_approve_request` | Write | Approve a pending PIM request (SecurityLead/Admin only) |
| `pim_deny_request` | Write | Deny a pending PIM request (SecurityLead/Admin only) |
| `pim_history` | Read | View PIM activation history (Auditor gets cross-user) |

### JIT VM Access Tools (3)

| Tool | Tier | Description |
|------|------|-------------|
| `jit_request_access` | Write | Request just-in-time VM access |
| `jit_list_sessions` | Read | List active JIT sessions |
| `jit_revoke_access` | Write | Revoke active JIT access |

### Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `DefaultActivationDurationHours` | 4 | Default PIM activation duration |
| `MaxActivationDurationHours` | 8 | Maximum allowed duration |
| `DefaultJitDurationHours` | 3 | Default JIT VM access duration |
| `MaxJitDurationHours` | 24 | Maximum JIT duration |
| `RequireTicketNumber` | false | Require support ticket for activation |
| `AutoDeactivateAfterRemediation` | false | Auto-deactivate after remediation |

### High-Privilege Roles

Roles classified as high-privilege requiring additional justification:
- Owner
- User Access Administrator
- Security Administrator
- Global Administrator
- Privileged Role Administrator

### Ticket Number Validation

When `RequireTicketNumber` is enabled, tickets must match one of:

| Provider | Pattern |
|----------|---------|
| ServiceNow | `^SNOW-[A-Z]+-\d+$` |
| Jira | `^[A-Z]{2,10}-\d+$` |
| Remedy | `^HD-\d+$` |
| Azure DevOps | `^AB#\d+$` |

---

## Tool-Level Authorization Tiers

Every tool is classified into a security tier via `AuthTierClassification`:

| Tier | PIM Level | Requirements | Examples |
|------|-----------|-------------|----------|
| **Tier 1** | `None` | No special auth | `compliance_status`, `compliance_chat`, Kanban reads |
| **Tier 2a** | `Read` | Active PIM role (any level) | `compliance_assess`, `compliance_collect_evidence`, `pim_list_eligible` |
| **Tier 2b** | `Write` | PIM Contributor+ role | `compliance_remediate`, `pim_activate_role`, `jit_request_access` |

### RMF Tool Tier Assignments

| RMF Phase | Tools | Default Tier |
|-----------|-------|-------------|
| Prepare | Registration, boundary, role assignment | Tier 2b (Write) |
| Categorize | Categorization, info type suggestion | Tier 2b (Write) |
| Select | Baseline, tailoring, inheritance | Tier 2b (Write) |
| Implement | Narratives, SSP generation | Tier 2b (Write) |
| Assess | Assessment, snapshots, SAR | Tier 2a (Read) for SCA |
| Authorize | Authorization, risk acceptance | Tier 2b (Write, AO-only) |
| Monitor | ConMon plan, reports, dashboard | Tier 2a (Read) for viewing |

---

## Authorization Flow

`ComplianceAuthorizationMiddleware` enforces security in this order:

```
Request
  │
  ├─ Health/dev endpoint? → BYPASS
  │
  ├─ CAC configured?
  │   └─ Verify CacSession → 401 if invalid/expired
  │
  ├─ Tool identified?
  │   ├─ Get PIM tier classification
  │   ├─ Tier = None? → ALLOW
  │   ├─ Tier = Read? → Check any active PIM role → 403 if none
  │   └─ Tier = Write? → Check Contributor+ PIM role → 403 if insufficient
  │
  ├─ RBAC role check
  │   └─ User role ∈ tool.RolesRequired? → 403 if not
  │
  └─ ALLOW → continue to endpoint
```

### ComplianceAgent Auth Gate

`CheckAuthGateAsync()` in the ComplianceAgent adds an additional layer:

1. Check if tool requires `AuthorizingOfficial` role
2. If user lacks role, check PIM eligible roles
3. If eligible, offer inline PIM activation
4. If not eligible, return authorization error

---

## Audit Logging

### AuditLoggingMiddleware

Captures every HTTP request with:

| Field | Description |
|-------|-------------|
| Correlation ID | Unique distributed tracing ID |
| User ID | Redacted (first 8 chars + `***`) |
| Agent Name | Which agent handled the request |
| Tool Name | Which tool was executed |
| HTTP Method | GET, POST, etc. |
| Path | Request path |
| Status Code | HTTP response status |
| Duration (ms) | Processing time |

### AuditLogEntry Entity

Immutable database record for compliance audit trails:

| Field | Description |
|-------|-------------|
| Id | Unique identifier |
| Timestamp | UTC timestamp |
| UserId | Redacted user identifier |
| Action | Tool name or operation |
| Details | Structured JSON of operation parameters |
| Outcome | Success, Failure, Error |
| CorrelationId | Request correlation ID |

### Retention

| Data Type | Retention Period | Configurable |
|-----------|-----------------|-------------|
| Assessments | 1,095 days (3 years) | Yes |
| Audit Logs | 2,555 days (7 years) | Yes |
| Alerts | 365 days (1 year) | Yes |

---

## Data Protection

### Sensitive Data Handling

- `SensitiveDataDestructuringPolicy` — Serilog policy that redacts PII from log output
- User IDs truncated to 8 characters in logs
- Certificate data never logged
- Connection strings masked in configuration dumps

### Integrity Verification

- Assessment snapshots use SHA-256 integrity hashes
- Evidence records include content hashes for tamper detection
- `compliance_verify_evidence` recomputes and compares hashes

### Concurrency Control

- `ConcurrentEntity` base class with `RowVersion` (Guid)
- Automatic version regeneration on `SaveChangesAsync`
- `DbUpdateConcurrencyException` returned as `CONCURRENCY_CONFLICT`

---

## Session Management

### CAC Sessions

- Configurable timeout (default varies)
- `SessionCleanupHostedService` removes expired sessions
- Sessions stored in `CacSessions` DbSet
- Sign-out available via `cac_sign_out` tool

### PIM Sessions

- Time-limited activation (default 4h, max 8h)
- Extension available via `pim_extend_role`
- Auto-deactivation offer after privileged operations
- History tracked for audit via `pim_history`

### JIT VM Access

- Time-limited access (default 3h, max 24h)
- Revocable via `jit_revoke_access`
- Sessions tracked in `JitRequests` DbSet

---

## Enterprise Hardening (Feature 029)

### Path Sanitization

All file path parameters in tool actions are validated by `PathSanitizationService`:

- **Canonicalization**: Uses `Path.GetFullPath()` to resolve `.` and `..` segments
- **Boundary check**: Ensures the canonical path starts with the allowed base directory
- **Blocked patterns**: Rejects null bytes, shell metacharacters, and URIs in paths
- Applies to parameters: `filePath`, `path`, `file`, `outputPath`, `inputPath`

### Rate Limiting

Per-endpoint sliding window rate limiting via ASP.NET Core `SlidingWindowRateLimiter`:

- **Default**: 30 requests per 60 seconds, 2 segments per window
- **Per-client partitioning**: Rate limits partitioned by authenticated user OID
- **Response**: 429 with `Retry-After` header and structured error JSON
- Configurable via `RateLimiting:Policies` in appsettings

### Input Validation

- **Request size limit**: `RequestSizeLimitMiddleware` enforces a configurable maximum (default 32KB)
- **Body validation**: All chat and tool payloads validated before processing
- **Script sanitization**: `ScriptSanitizationService` validates remediation scripts before execution
