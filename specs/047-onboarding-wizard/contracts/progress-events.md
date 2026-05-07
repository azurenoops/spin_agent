# SignalR Progress Events — Onboarding Wizard

**Feature**: 047-onboarding-wizard · **Spec**: [spec.md](../spec.md) · **Plan**: [plan.md](../plan.md) · **Research**: [research.md](../research.md)

## Hub

The wizard reuses the existing `NotificationHub` mounted at `/hubs/notifications` (research §R2).
Per-tenant fan-out is implemented through SignalR groups; **no new hub is introduced**.

## Group naming

| Group | When clients are added |
|-------|------------------------|
| `wizard-{tenantId}` | Added in `NotificationHub.OnConnectedAsync` if the connecting user has the in-app `Administrator` RMF role for `tenantId`. |
| `wizard-{tenantId}-job-{jobId}` | Added when the React `BackgroundJobProgress` component subscribes via `connection.invoke("SubscribeToWizardJob", jobId)` after fetching the initial status; lets the UI focus on a single in-flight job. |

Authorization at connection time is enforced by `OnboardingAuthorizationFilter` /
`OnboardingAdministratorPolicy` (research §R10) — a non-admin client never enters either group.

## Server-to-client method

```csharp
// Mirrors SignalRSspExportNotifier shape.
await _hub.Clients.Group($"wizard-{tenantId}").SendAsync("WizardJobStatus", evt);
```

Method name on the wire: **`WizardJobStatus`**.

## Event payload

```jsonc
{
  "jobId":      "f7c9e4b6-...-uuid",
  "tenantId":   "a8b1c0d2-...-uuid",
  "jobType":    "EmassParse | EmassCommit | SspPdfExtract | NarrativeSeedIndex | TemplateValidation | ExportRerender | ImportRerender",
  "status":     "Queued | InProgress | Succeeded | Failed | Cancelled",
  "percent":    37,                       // null when not applicable
  "message":    "Parsing system 3 of 12 (system_3.xlsx)",
  "errorCode":  null,                     // string when status == Failed (Constitution VII)
  "suggestion": null,                     // string when status == Failed
  "timestamp":  "2026-05-07T14:42:31.211Z",
  "context": {                            // optional, jobType-specific
    "sessionId":    "..uuid..",           // EmassImportSession.Id / SspPdfImportSession.Id
    "dependencyId": "..uuid..",           // for ExportRerender / ImportRerender
    "fileName":     "string",
    "currentItem":  "system_3.xlsx",
    "totalItems":   12,
    "processedItems": 3
  }
}
```

### Status-by-status guarantees

| status | percent | message | errorCode | suggestion | Notes |
|--------|---------|---------|-----------|-----------|-------|
| `Queued` | `null` or `0` | "Queued" or longer | `null` | `null` | Emitted exactly once per job. |
| `InProgress` | 0..99 | progress narrative | `null` | `null` | Server emits at most one per second per job to bound chattiness. |
| `Succeeded` | `100` | result summary | `null` | `null` | Emitted exactly once. After this event the client SHOULD fetch `GET /api/onboarding/jobs/{jobId}` once to retrieve the durable result snapshot. |
| `Failed` | last known | error narrative | required | required | Emitted exactly once. `errorCode` is one of the wizard error codes; `suggestion` is a plain-language remediation hint. |
| `Cancelled` | last known | cancellation reason | optional | optional | Emitted exactly once. |

## Polling fallback (FR-066)

Clients MUST treat SignalR as a progress channel only. If the SignalR connection drops or
returns no events for more than **`OnboardingOptions:Progress:PollingFallbackSeconds`**
(default `10`), the React `BackgroundJobProgress` component falls back to polling
`GET /api/onboarding/jobs/{jobId}` at a default cadence of **2 s** until the job finishes or the
SignalR connection re-establishes.

The polling endpoint is idempotent and authoritative — it always returns the persisted
`WizardJobStatus` row (data-model §12).

## Reload / disconnect recovery

On wizard mount or step navigation, the dashboard:

1. Calls `GET /api/onboarding/jobs?status=Queued,InProgress&limit=20` (covered indirectly by the
   imports / templates endpoints) to discover any in-flight jobs for the tenant.
2. Subscribes the SignalR connection to `wizard-{tenantId}` and (per visible job)
   `wizard-{tenantId}-job-{jobId}`.
3. Renders status from the persisted row immediately — no event replay is required because the
   row is durable.

This satisfies FR-066: a SignalR disconnect, page reload, or browser-tab change MUST NOT cause a
successful background job to appear failed.

## Wizard error codes (Constitution VII)

The set below is exhaustive for v1. Each MUST be accompanied by a plain-language `message` and
`suggestion`.

| `errorCode` | When it occurs |
|-------------|---------------|
| `WIZARD_BOOTSTRAP_RACE` | Two simultaneous first-users hit `POST /api/onboarding/start`; one wins, one is told to retry. |
| `WIZARD_AUTH_FORBIDDEN` | Caller lacks `Administrator` after onboarding completed (FR-009). |
| `WIZARD_LAST_ADMIN_PROTECTED` | Attempt to remove the last `Administrator` without designating a replacement. |
| `WIZARD_ARM_CONSENT_REQUIRED` | Subscription enumeration without ARM scope consent (FR-070a). |
| `WIZARD_ARM_TOKEN_EXPIRED` | Delegated ARM token expired during enumeration. |
| `WIZARD_ARM_NO_SUBSCRIPTIONS_VISIBLE` | User has no visible subscriptions (FR-073 / FR-075). |
| `WIZARD_ARM_UNREACHABLE` | ARM endpoint unreachable (transient infra). |
| `WIZARD_EMASS_INVALID_FORMAT` | Uploaded eMASS file fails structure check (FR-031). |
| `WIZARD_EMASS_TOO_LARGE` | Upload exceeds `Limits:EmassMaxBytes` (FR-036). |
| `WIZARD_SSP_PDF_NO_TEXT_LAYER` | Image-only PDF (FR-044). |
| `WIZARD_SSP_PDF_PASSWORD_PROTECTED` | Encrypted / password-protected PDF (FR-044). |
| `WIZARD_SSP_PDF_UNREADABLE` | Other parse failure (FR-044). |
| `WIZARD_SSP_PDF_UNKNOWN_FRAMEWORK` | Non-NIST 800-53 control framework (FR-045). |
| `WIZARD_TEMPLATE_WRONG_FORMAT` | Wrong file format for slot (FR-081). |
| `WIZARD_TEMPLATE_TOO_LARGE` | Upload exceeds `Limits:TemplateMaxBytes` (FR-088). |
| `WIZARD_TEMPLATE_VALIDATION_WARNINGS` | Template accepted but flagged non-compliant (FR-084). |
| `WIZARD_TEMPLATE_DEFAULT_PROTECTED` | Cannot delete a template currently marked default (FR-096). |
| `WIZARD_DEPENDENT_CONFIRM_REQUIRED` | Delete blocked pending explicit confirmation flag (FR-096). |
| `WIZARD_QUOTA_EXCEEDED` | Per-tenant storage budget exceeded (FR-054 / FR-088). |
| `WIZARD_JOB_FAILED` | Background-job worker error; original artifact retained (FR-065). |
| `WIZARD_JOB_CANCELLED` | Admin or system cancelled the job. |
