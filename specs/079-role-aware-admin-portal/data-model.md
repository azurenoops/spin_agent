# Data Model: Role-Aware SPIN Admin Portal

## Effective access (computed, not persisted)

`EffectiveAccessSnapshot`

- subject object id and display name;
- generated timestamp and version;
- active destination id;
- list of `WorkspaceDestination`.

`WorkspaceDestination`

- stable destination id;
- workspace kind: `Administration` or `System`;
- scope kind: `Organization`, `Provider`, `Platform`, or `System`;
- scope id and display name;
- action identifiers;
- assignment badges;
- availability and onboarding state;
- default flag.

## Explicit access grant (deferred)

No scoped-grant entity or schema change is part of this implementation.
Existing linked people and active role assignments are sufficient for
multi-organization administration. Provider-to-customer system sharing remains
unavailable until its grant lifecycle, approval policy, and audit requirements
are specified and implemented.

The following candidate shape is retained for that future feature and is not a
current runtime contract:

- `Id Guid`
- `TenantId Guid`
- `SubjectObjectId Guid`
- `ScopeKind string`
- `ScopeId string`
- `Action string`
- `Source string`
- `Reason string?`
- `GrantedBy Guid`
- `GrantedAt DateTimeOffset`
- `ExpiresAt DateTimeOffset?`
- `RevokedAt DateTimeOffset?`
- `RevokedBy Guid?`

Unique active grant: subject + scope kind + scope id + action.

Existing organization, system, and CSP assignments remain authoritative for
their current domains. Platform operations use an explicit configured object-id
allowlist; role-name strings do not grant platform access.

## Invariants

- A grant never changes tenant ownership of a resource.
- A provider grant never creates membership or an RMF role.
- A workspace selection never creates a grant.
- Revoked and expired grants are excluded from effective access.
- Cross-tenant customer-system grants require an explicit target tenant and
  system.
