# Provider allocation linked to system environment

Proposed refinement based on the user's Flankspeed onboarding model: the CSP allocates a subscription to a mission-owner organization, then an authorized mission owner applies the allocation and a reviewed resource scope to a system. This is a design requirement, not a verified description of existing code or all Flankspeed deployments.

Provider hosting and Connected environments are two views over shared subscription/allocation identity, with distinct hosting, access, scope, inheritance and monitoring states. An allocation is organization-level and may serve multiple systems. A system may span multiple allocations/subscriptions.

Use existing organization subscription registration and provider relationship models where compatible. No duplicate subscription registration on applying an allocation. Record provider ownership and consuming-organization entitlement without broadening Azure or application permissions. A hosting offer alone does not make a provider subscription selectable.

Provider hosting is the primary entry for CSP allocations; organization-owned subscription attachment remains available. Source release/coverage, system resource scope, assessment access, monitoring health and reviewed control responsibilities remain independently traceable.

Applying an allocation must persist its hosting link, environment reference and selected system scope atomically or expose a recoverable explicit incomplete state. It must never report success with hidden partial writes. Preserve the approved boundary and baseline while staging changes that require review. Retain historical evidence on allocation withdrawal.

Use actual application shell and logo in implementation. Counts and allocation names in mocks are illustrative. Role display is informational. Resource discovery failure means unavailable, not empty. Do not imply that allocation grants access, enables monitoring, accepts inheritance or grants ATO.

## Built-in image generation prompt

Mock: [Provider allocation and environment](assets/conmon-system-monitoring/06-provider-allocation-environment.png).

Visual clarification: the generated board incorrectly labels Azure IL5 Shared Services as Subscription; that is the provider offering. Implementation must show Offering: Azure IL5 Shared Services and Subscription: FS Mission production, with its actual Azure subscription identity in details. Preserve one Security Capabilities navigation item. Monitoring not enabled is a neutral setup state, not necessarily an error. Existing environment references should be reused rather than always created.

undefined
