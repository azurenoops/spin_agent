# Role-aware administration

SPIN administration is an independent workspace capability. It does not imply
system or ATO access.

## Access principles

- Organization administration comes from an active tenant Administrator
  assignment linked to the authenticated Entra object id.
- Provider administration comes from the verified `CSP.Admin` group mapping.
- System access comes from applicable organization/system RMF assignments.
  Explicit provider-to-customer system sharing is not implemented and therefore
  fails closed.
- Platform operations require an authenticated object id in the configured
  platform operations allowlist. The
  `Compliance.PlatformEngineer` fallback role is not sufficient.
- Viewing, editing, assessing, approving, and administering are separate actions.
- Workspace selection is a preference and context switch, never a grant.

## Design source

The six administration boards documented by
`docs/design/admin-pages-mock-index.md` define visual composition and state
treatment. Illustrative records, labels, and controls do not define
authorization.

## Compatibility

Legacy CSP cross-tenant customer-content access is broader than the target
model. New deployments default to no provider access to customer-system
content. Existing deployments must inventory legitimate support access and
migrate it to a future explicit sharing model before the time-bounded
compatibility option is removed.

The provider compatibility path is separately controlled by
`ProviderAdministration:AllowLegacyCustomerContent` and
`ProviderAdministration:LegacyCustomerContentExpiresAt`. It defaults to
disabled and requires a future expiry.

The deployment migration endpoint now requires the caller's Entra object id in
`PlatformOperations:AuthorizedObjectIds`. A temporary CSP compatibility path is
available only when both settings are configured:

```json
{
  "PlatformOperations": {
    "AllowLegacyCspAdminMigration": true,
    "LegacyCspAdminMigrationExpiresAt": "2027-01-01T00:00:00Z"
  }
}
```

The compatibility switch defaults to `false`, requires a future expiry, and
does not convert the operation into an Azure tenant transfer. Existing preview,
typed deployment-name confirmation, and irreversible-operation semantics remain.

## Implemented route posture

- `/administration` uses the shared shell and server-provided actions.
- Existing organization profile, onboarding/setup, subscription registration,
  imports, templates, administrative audit, provider setup, and migration
  workflows are reused.
- Legacy organization settings, subscription, audit, and migration bookmarks
  redirect only after the matching effective action is verified.
- Knowledge-management bookmarks fail closed because no verified
  default-administrator permission maps to that tool.
- Notifications are bound to the authenticated `oid`; a query-string `userId`
  can no longer select another user's notifications or preferences.
- Audit queries are pinned to the active effective tenant and reject a different
  requested tenant or actor-tenant scope.
- One identity may administer multiple organizations when each organization has
  a linked directory person and an active Administrator assignment. Each
  organization remains a separate destination; actions are never merged.
- The signed remembered-tenant cookie selects a verified organization context.
  Tenant resolution revalidates the linked person and active assignment on each
  request, records it as `SelectedTenantId` (not impersonation), and applies the
  effective tenant to downstream onboarding queries.
- System and ATO authorization covers dashboard APIs plus the direct
  `/api/systems`, `/api/v1/systems`, and `/api/roles/system` import, export,
  download, and role paths. Named-system requests require a matching authorized
  destination and action.

## Remaining gaps

- Provider customer-organization management and released-offering mutation APIs
  are not yet available through the portal; the shell reports unavailable state
  rather than presenting decorative controls.
- FAST allocation intake remains unavailable.
- Explicit provider-to-customer system sharing/support grants are not yet
  modeled. Provider administration cannot access customer-system content unless
  the time-bounded legacy compatibility option is intentionally enabled.
