# SPIN roles and permissions — current source inventory

Audited September 22, 2026 in the current working tree of `feature/1002-role-aware-workspaces`. This is a source-level inventory of the central role definitions, workspace resolver, system permission policy, role-assignment policy, selected endpoint guards, and compatibility policies. It is not a claim that every endpoint/channel has been tested for consistent enforcement. Existing uncommitted changes were left intact. No roles or permissions were changed by this audit.

## Access layers

1. Authentication establishes directory/object identity. A contact or Person record alone does not grant access.
2. Explicit organization membership grants an ordinary workspace; CSP Admin grants the CSP workspace. Support is a separately validated, time-limited mode.
3. Organization and system assignments determine RMF actions. A system override takes precedence over inherited system assignments, then organization defaults, then applicable legacy assignments. All applicable roles contribute permissions; a browser persona is not authority.
4. Compliance role claims remain in named policies and tool/legacy paths. They are not interchangeable with persisted RMF assignments.
5. Azure/CAC/PIM checks can add requirements to cloud operations. They do not grant ATO authority.

## Platform and organization roles

| Role | Code identity | Current purpose and boundary |
|---|---|---|
| CSP Administrator | `CSP.Admin` | Hosting provider workspace, provider administration and explicit support entry. The workspace resolver exposes membership management. The system access service grants provider oversight read access, not RMF mutations. Ordinary organization access still requires membership. |
| SOC Analyst | `Auth.SocAnalyst`; policy alias `SOC.Analyst` | Special security/login-audit access. Not a general CSP workspace entitlement and not an RMF role. Alias support is explicit in the named policy; individual audit service behavior must also be respected. |
| Organization Administrator | `OrganizationRole.Administrator` | Organization management, membership administration, system visibility and RMF role assignment. Has no RMF enum counterpart; does not automatically author narratives, manage evidence or issue ATOs. |
| ISSM — Information System Security Manager | `Issm` | System management, security implementation oversight, profile editing, narratives, evidence, assessments, remediation and selected role assignments. Organization-level ISSM also enables system creation. |
| ISSO — Information System Security Officer | `Isso` | Day-to-day narrative authoring, evidence, assessments, remediation and responsibility review; limited role assignment. |
| SCA — Security Control Assessor | Organization `Assessor` → RMF `Sca` | Assessment plans/reports, validation links and evidence integrity verification. The central RunAssessments flag is currently ISSM/ISSO, so “SCA can perform every assessment operation” would be inaccurate. |
| Authorizing Official | `AuthorizingOfficial` | System authorization decisions and the central remediation permission. Does not automatically receive authoring, evidence-management or role-assignment authority. |
| System Owner | `SystemOwner` | System profile/business/implementation context. Current central permissions grant read and profile edit; the title does not confer all implementation write permissions. |
| Mission Owner | `MissionOwner` | Mission/business context. Current central permissions grant read and profile edit; no implicit responsibility confirmation or ATO authority. |

Organization enums contain seven entries (Administrator plus six RMF roles). The RMF enum contains six entries. `Assessor` and `Sca` are the same mapped role, not two user types. The RMF SystemOwner comment associates it with the Engineer persona, while the compliance role `PlatformEngineer` is a separate claim.

## Central system operation matrix

Each row below describes the current permission policy, not every possible operation hidden behind a screen. Active membership, accessible target, applicable assignment and endpoint/domain checks still apply. Additional workflow checks may restrict an otherwise permitted action.

| Permission/action | Roles granted by current policy |
|---|---|
| Read system | Any of the six effective RMF roles; organization Administrator; authorized CSP oversight |
| Edit system profile | ISSM, Mission Owner, System Owner |
| Manage system | ISSM |
| Create a system | Organization-level ISSM, through workspace permissions |
| Author narratives | ISSM, ISSO |
| Review narratives | ISSM |
| Manage evidence | ISSM, ISSO |
| Verify evidence integrity | ISSM, ISSO, SCA, with readable system |
| Run assessments (`CanRunAssessments`) | ISSM, ISSO |
| Generate SAP | ISSM, SCA |
| Finalize SAP | ISSM, SCA |
| Generate SAR | ISSM, SCA |
| Manage validation links | SCA |
| Manage remediation | ISSM, ISSO, AO |
| Create remediation tasks | ISSM |
| Move remediation tasks / own tasks | ISSM, ISSO |
| Move any remediation task | ISSM |
| Decide authorization | AO |
| Assign system roles | Organization Administrator, ISSM, ISSO; target-role restrictions below |
| Subscribe/unsubscribe and write responsibility review | Effective assigned ISSM/ISSO via the dedicated responsibility authorization service |

The four workspace flags are `CanManageMemberships`, `CanManageOrganization`, `CanAccessCsp`, and `CanCreateSystem`. The resolver grants organization management to organization Administrator, CSP access only in the CSP workspace, system creation to organization ISSM, and membership management when the authenticated user is CSP Admin or the selected organization Administrator. Membership endpoints still enforce their target and actor rules.

## Who may assign RMF roles

| Actor | Assignable targets in the central matrix |
|---|---|
| Organization Administrator | All six RMF roles, including AO |
| ISSM | ISSM, ISSO, SCA, System Owner, Mission Owner; not AO |
| ISSO | System Owner, Mission Owner |
| AO, SCA, System Owner, Mission Owner | None from these roles alone |

The policy has a bootstrap bypass; this is a controlled initialization path, not an ordinary role. Initial organization Administrator enrollment is a separate operation from identity membership and RMF role assignment.

The separation-of-duties detector produces warnings when assigning AO or SCA to a person with conflicting organization assignments (ISSM, ISSO or SystemOwner). It is a warning-producing service, not proof that every conflicting combination is universally blocked. Its inspected lookup is directional; do not present it as a complete symmetric deny policy.

## Additional compliance claims already defined

These must be accounted for in migration and tool permissions, but should not become duplicate RMF role choices merely because they exist.

| Claim | Named policy membership / inspected behavior |
|---|---|
| `Compliance.Administrator` | Reader, Writer, ComplianceAdministrator; full legacy Kanban helper permissions. Not synonymous with organization Administrator or AO. |
| `Compliance.SecurityLead` | Reader and Writer; legacy Kanban board/task creation, assignment, movement, comments/moderation and export. |
| `Compliance.Analyst` | Reader and Writer; legacy Kanban self-assign, move-own and comment. Middleware denies listed approval tools without Administrator. |
| `Compliance.Auditor` | Reader; legacy Kanban export. Middleware denies its listed write tools without Administrator. |
| `Compliance.Viewer` | Reader only in named policies; no explicit Kanban helper grants. |
| `Compliance.PlatformEngineer` | Reader and Writer; no explicit Kanban helper entry. Separate from persisted SystemOwner assignments. |
| `Compliance.AuthorizingOfficial` | Reader and AuthorizationDecisionIssuer, not Writer. Scoped system decisions still depend on the persisted AO assignment. |

Named policies: `Policy:CspAdmin`, `Policy:SocAnalyst`, `Policy:ComplianceReader`, `Policy:ComplianceWriter`, `Policy:ComplianceAdministrator`, `Policy:AuthorizationDecisionIssuer`.

The middleware's coarse compliance allowlist does not include SecurityLead or AuthorizingOfficial, unlike the Reader policy. It has separate workspace handling and Development bypasses. Therefore policy membership alone is insufficient evidence of end-to-end permission across legacy, REST and tool paths. This inconsistency needs route-specific tests before calling the complete permission system reconciled.

## Other named permission constants

Declared constants are not proof that every operation enforces them or that all roles receive them.

- Compliance: `GenerateDocuments`, `ExecuteRemediation`, `CollectEvidence`, `RunAssessment` (all prefixed `Compliance.`).
- Kanban: `CreateBoard`, `CreateTask`, `AssignAny`, `SelfAssign`, `MoveOwn`, `MoveAny`, `CloseWithoutValidation`, `Comment`, `DeleteAnyComment`, `Export`, `Archive` (prefixed `Kanban.`).
- PIM: `ActivateRole`, `DeactivateRole`, `ExtendRole`, `ApproveRequest`, `DenyRequest`, `ViewHistory`, `ManageJitAccess`, `MapCertificate` (prefixed `Pim.`).

The inspected legacy Kanban helper grants all eleven actions to Compliance.Administrator; SecurityLead gets CreateBoard/CreateTask/AssignAny/MoveOwn/MoveAny/Comment/DeleteAnyComment/Export; Analyst gets SelfAssign/MoveOwn/Comment; Auditor gets Export. Other roles have no helper entry. This is distinct from the scoped remediation matrix above.

## CSP UI permissions still to settle

The current platform role list does not define separate CSP Capability Author, CSP Reviewer, CSP Publisher, CSP Organization Manager, or CSP Support Operator roles. Those are possible permission splits, not implemented role names.

The approved publication issue [#1028](https://github.com/azurenoops/spin_agent/issues/1028) explicitly requires a provider reviewer permission and a self-approval policy. Resolve that contract before presenting publication approval as available. The support issue [#1032](https://github.com/azurenoops/spin_agent/issues/1032) adds reason/ticket capture; support remains a mode, not customer RMF authority.

Recommended next design decision: define fine-grained provider permissions for catalog authoring, review, publication, organization provisioning and support, then decide whether CSP Admin bundles them or delegates them to separate provider roles. This is a recommendation, not an existing implementation finding. Do not add another generic “super admin” that bypasses system authorization.

## Source index

- [Platform policies and role names](../../src/Ato.Copilot.Mcp/Authorization/Policies.cs)
- [Compliance and permission constants](../../src/Ato.Copilot.Core/Constants/ComplianceRoles.cs)
- [Organization roles](../../src/Ato.Copilot.Core/Models/Onboarding/OrganizationRoleAssignment.cs)
- [RMF roles](../../src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs)
- [Organization/RMF mapping](../../src/Ato.Copilot.Core/Services/Roles/OrganizationRoleToRmfRoleMap.cs)
- [Workspace resolution](../../src/Ato.Copilot.Mcp/Services/Tenancy/WorkspaceService.cs)
- [Workspace contracts](../../src/Ato.Copilot.Mcp/Services/Tenancy/WorkspaceContracts.cs)
- [System permissions and precedence](../../src/Ato.Copilot.Core/Services/Roles/SystemWorkspaceAccessPolicy.cs)
- [System permission fields](../../src/Ato.Copilot.Core/Interfaces/Tenancy/ISystemWorkspaceAccessService.cs)
- [System access service](../../src/Ato.Copilot.Core/Services/Tenancy/SystemWorkspaceAccessService.cs)
- [Endpoint operation checks](../../src/Ato.Copilot.Mcp/Authorization/SystemWorkspaceOperationAuthorization.cs)
- [Role assignment matrix](../../src/Ato.Copilot.Core/Services/Roles/RoleAuthorizationService.cs)
- [Separation-of-duties warnings](../../src/Ato.Copilot.Core/Services/Roles/SoDConflictDetector.cs)
- [Evidence verification](../../src/Ato.Copilot.Core/Services/Roles/EvidenceIntegrityVerificationPolicy.cs)
- [Responsibility endpoints](../../src/Ato.Copilot.Mcp/Endpoints/CapabilitySubscriptionEndpoints.cs)
- [Compliance middleware](../../src/Ato.Copilot.Mcp/Middleware/ComplianceAuthorizationMiddleware.cs)
- [Legacy Kanban matrix](../../src/Ato.Copilot.Agents/Compliance/Services/KanbanPermissionsHelper.cs)
- [UI role labels](../../src/Ato.Copilot.Dashboard/src/features/workspaces/workspaceRoles.ts)

No automated tests or live authorization scenarios were run for this inventory. Existing policy test files were located, but their existence is not reported as a passing test result.
