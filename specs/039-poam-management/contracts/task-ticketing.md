# Task-owned external tickets

This contract supersedes bidirectional/webhook claims for the new remediation task
surface only. Legacy POA&M ticket references remain separate and are not migrated.

## Supported mode and API

`/api/dashboard/systems/{systemId}/tasks/{taskId}/ticket`:

- GET: configuration availability, `canManage`, `mode: ManualPullOnly`,
  `webhooksSupported: false`, `bidirectionalSupported: false`, and nullable `link`.
- POST `/create`: explicitly create one remote Jira Task or ServiceNow record.
- POST `/link`: `{externalRef,rowVersion?}`; resolve an existing ticket through
  the configured provider before retaining its snapshot.
- POST `/refresh`: `{rowVersion}`; read status and assignee only.
- POST `/unlink`: `{rowVersion}`; retain local history; never delete a remote ticket.

The browser cancels obsolete GET requests with `AbortSignal` on task/system
changes, replacement reloads, and unmount. External mutations are not blindly
retried or treated as rolled back when the drawer closes.

Link fields: `id`, `provider`, `externalRef`, `externalUrl`, `externalStatus`,
`externalAssignee`, `lastSuccessfulSyncAt`, `state`, `rowVersion`,
`correlationKey`, `lastError`. The correlation key is stable for tenant/system/task.
ServiceNow uses stable `sys_id` identifiers, not human-readable incident numbers.

Every operation checks current authenticated tenant/system permissions and the
task's retained owner through `RemediationScope.TaskSystemAsync` (explicit task
system and finding/board assessment provenance). Subscription IDs never establish
ownership, and contradictory/missing provenance is denied; inaccessible resources
are not disclosed.
Writes require ManageRemediation; configuration requires ManageSystem. Access is
rechecked after remote I/O before a snapshot can be persisted/returned.

## Durability and safety

The create lease is persisted before HTTP. Unique task identity plus optimistic
concurrency prevent duplicate local claims. Once create is attempted it is never
automatically attempted again, including after unlink. A crash, timeout, failed
response, or missing remote identifier leaves a retained uncertain/pending state;
users recover by locating the correlation key externally and linking the existing
ticket. No blind retry, expiry-based recreation, automatic loops, or remote delete.
Link/unlink/refresh audit records commit with local changes. Refresh failures retain
the last successful snapshot and timestamp. Remote closure cannot modify local task,
finding, verification, milestone, or POA&M states.

## Connector and credential boundaries

Reuse the Jira and ServiceNow provider abstraction and system configuration.
Dashboard configuration accepts `provider`, `baseUrl`, `projectKey`,
`apiKeySecretName`, and `syncEnabled`; no raw tokens or fabricated field mappings.
Secret references resolve only through server configuration
`Ticketing:Credentials:{reference}` (which may be supplied by the deployment secret
configuration provider). References and resolved secrets are never returned in
dashboard responses. Hosts must be HTTPS and explicitly allowlisted by deployment
configuration `Ticketing:AllowedHosts`; redirects are disabled. Connection-enabled
means manual operations are enabled, not scheduled synchronization.

Unsupported: incoming events/webhooks, bidirectional updates, automatic status
mapping, conflict reconciliation, automatic retries, provider search UI, and Azure
DevOps. Users may search the provider UI manually using the retained correlation key.
Synthetic HTTP handlers are the only connector validation used during development.

## Local manual verification

With a configured synthetic/test connector: select a remediation task, link an
existing identifier, refresh, and unlink. Verify read-only status/assignee and retained
audit records; local task and POA&M status must not change when remote status closes.
Repeated create and timeout recovery must never send a second POST. Revoke system
access and verify GET and every action deny access. Production connectors require
an operator-managed HTTPS host allowlist and credential reference mapping.
