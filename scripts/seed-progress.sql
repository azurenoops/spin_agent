-- ============================================================================
--  ATO Copilot — Seed varied RMF / ATO progress per registered system
--  Target:  AtoCopilot (SQL Server in docker compose, container ato-copilot-sql)
--  Idempotent: re-running is a no-op.
--  Tiers (May 6, 2026 reference date):
--    Polar Bear       — Tier 1 — Prepare only (empty)
--    Coastal Watch    — Tier 2 — Categorized & baselined, no implementation
--    Phoenix Falcon   — Tier 3 — Implementation in progress
--    Eagle Nest       — Tier 4 — Assessed (independent assessment complete)
--    Eagle Eye        — Tier 5 — Active ATO + continuous monitoring
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @now DATETIME2(7) = SYSUTCDATETIME();
DECLARE @actor NVARCHAR(200) = N'seed-progress';

-- ─── Resolve system IDs by Name ──────────────────────────────────────────────
DECLARE @ee NVARCHAR(36) = (SELECT Id FROM RegisteredSystems WHERE Name = N'Eagle Eye'      AND IsActive = 1);
DECLARE @en NVARCHAR(36) = (SELECT Id FROM RegisteredSystems WHERE Name = N'Eagle Nest'     AND IsActive = 1);
DECLARE @pf NVARCHAR(36) = (SELECT Id FROM RegisteredSystems WHERE Name = N'Phoenix Falcon' AND IsActive = 1);
DECLARE @cw NVARCHAR(36) = (SELECT Id FROM RegisteredSystems WHERE Name = N'Coastal Watch'  AND IsActive = 1);
DECLARE @pb NVARCHAR(36) = (SELECT Id FROM RegisteredSystems WHERE Name = N'Polar Bear'     AND IsActive = 1);

IF @ee IS NULL OR @en IS NULL OR @pf IS NULL OR @cw IS NULL OR @pb IS NULL
BEGIN
    RAISERROR('seed-progress: one or more expected systems are missing. Run scripts/seed-systems.sh first.', 16, 1);
    RETURN;
END

-- Helper inline: deterministic GUID from a seed key.
--   CAST(HASHBYTES('MD5', N'<key>') AS UNIQUEIDENTIFIER) → consistent UUID per key.

BEGIN TRANSACTION;

-- ─── 1.  RMF phase + OperationalStatus per system ───────────────────────────
UPDATE RegisteredSystems SET CurrentRmfStep = N'Prepare',     OperationalStatus = N'UnderDevelopment', RmfStepUpdatedAt = @now, ModifiedAt = @now WHERE Id = @pb AND CurrentRmfStep <> N'Prepare';
UPDATE RegisteredSystems SET CurrentRmfStep = N'Categorize',  OperationalStatus = N'UnderDevelopment', RmfStepUpdatedAt = @now, ModifiedAt = @now WHERE Id = @cw AND CurrentRmfStep <> N'Categorize';
UPDATE RegisteredSystems SET CurrentRmfStep = N'Implement',   OperationalStatus = N'UnderDevelopment', RmfStepUpdatedAt = @now, ModifiedAt = @now WHERE Id = @pf AND CurrentRmfStep <> N'Implement';
UPDATE RegisteredSystems SET CurrentRmfStep = N'Assess',      OperationalStatus = N'UnderDevelopment', RmfStepUpdatedAt = @now, ModifiedAt = @now WHERE Id = @en AND CurrentRmfStep <> N'Assess';
UPDATE RegisteredSystems SET CurrentRmfStep = N'Monitor',     OperationalStatus = N'Operational',      RmfStepUpdatedAt = @now, ModifiedAt = @now,
       OperationalDate = ISNULL(OperationalDate, DATEADD(MONTH, -8, @now))
 WHERE Id = @ee AND CurrentRmfStep <> N'Monitor';

-- ─── 2.  Security Categorizations (tiers 2..5) ──────────────────────────────
;WITH src(SystemId, NSS, Justification, CategorizedBy, CategorizedAt) AS (
    SELECT @cw, 0, N'FIPS 199 Low: maritime collaboration data, no PII.',                                      N'Sarah Mitchell', DATEADD(DAY,  -7, @now)
    UNION ALL SELECT @pf, 0, N'FIPS 199 Moderate: logistics PII (CUI), supply-chain integrity is essential.',   N'Sarah Mitchell', DATEADD(DAY, -45, @now)
    UNION ALL SELECT @en, 0, N'FIPS 199 Moderate: ISR analytics enclave, supports Eagle Eye operations.',       N'Sarah Mitchell', DATEADD(DAY, -90, @now)
    UNION ALL SELECT @ee, 1, N'FIPS 199 High / NSS: ISR data fusion with classified information types.',        N'Sarah Mitchell', DATEADD(DAY,-200, @now)
)
INSERT INTO SecurityCategorizations (Id, RegisteredSystemId, IsNationalSecuritySystem, Justification, CategorizedBy, CategorizedAt)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'cat:' + s.SystemId) AS UNIQUEIDENTIFIER)),
    s.SystemId, s.NSS, s.Justification, s.CategorizedBy, s.CategorizedAt
FROM src s
WHERE NOT EXISTS (SELECT 1 FROM SecurityCategorizations c WHERE c.RegisteredSystemId = s.SystemId);

-- ─── 3.  Information Types (a couple per categorized system) ────────────────
;WITH it(SystemId, Sp, Name, Cat, C, I, A) AS (
    -- Coastal Watch — Low
    SELECT @cw, N'D.14.1', N'General Information for Citizens',     N'Citizen Services', N'Low', N'Low', N'Low'
    UNION ALL SELECT @cw, N'C.3.5.1', N'Continuity of Operations',   N'Defense & National Security', N'Low', N'Low', N'Moderate'
    -- Phoenix Falcon — Moderate
    UNION ALL SELECT @pf, N'C.3.4.1', N'Logistics Management',                  N'Defense & National Security', N'Moderate', N'Moderate', N'Moderate'
    UNION ALL SELECT @pf, N'C.2.8.4', N'Procurement / Acquisition',             N'Defense & National Security', N'Moderate', N'Moderate', N'Low'
    -- Eagle Nest — Moderate
    UNION ALL SELECT @en, N'C.3.5.4', N'Intelligence Operations',               N'Defense & National Security', N'Moderate', N'Moderate', N'Moderate'
    UNION ALL SELECT @en, N'C.3.5.6', N'Operational Test & Evaluation',         N'Defense & National Security', N'Moderate', N'Moderate', N'Moderate'
    -- Eagle Eye — High / NSS
    UNION ALL SELECT @ee, N'C.3.5.4', N'Intelligence Operations',               N'Defense & National Security', N'High',     N'High',     N'High'
    UNION ALL SELECT @ee, N'C.3.5.5', N'Surveillance & Reconnaissance',         N'Defense & National Security', N'High',     N'Moderate', N'High'
    UNION ALL SELECT @ee, N'C.3.5.1', N'Continuity of Operations',              N'Defense & National Security', N'High',     N'High',     N'High'
)
INSERT INTO InformationTypes (Id, SecurityCategorizationId, Sp80060Id, Name, Category, ConfidentialityImpact, IntegrityImpact, AvailabilityImpact, UsesProvisionalImpactLevels)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'it:' + it.SystemId + N':' + it.Sp) AS UNIQUEIDENTIFIER)),
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'cat:' + it.SystemId)            AS UNIQUEIDENTIFIER)),
    it.Sp, it.Name, it.Cat, it.C, it.I, it.A, 1
FROM it
WHERE NOT EXISTS (
    SELECT 1 FROM InformationTypes existing
    WHERE existing.SecurityCategorizationId = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'cat:' + it.SystemId) AS UNIQUEIDENTIFIER))
      AND existing.Sp80060Id = it.Sp
);

-- ─── 4.  Control Baselines (tiers 2..5) ─────────────────────────────────────
;WITH bl(SystemId, Lvl, Overlay, Total, Customer, Inherited, Shared, TailoredOut, TailoredIn) AS (
    SELECT @cw, N'Low',      NULL,                       149, 105, 30, 14, 4,  3
    UNION ALL SELECT @pf, N'Moderate', N'CNSSI 1253 IL4',          287, 198, 60, 29, 7,  6
    UNION ALL SELECT @en, N'Moderate', N'CNSSI 1253 IL5',          287, 200, 58, 29, 5,  8
    UNION ALL SELECT @ee, N'High',     N'CNSSI 1253 IL5 + ICD 503',378, 250, 88, 40, 10, 12
)
INSERT INTO ControlBaselines (Id, RegisteredSystemId, BaselineLevel, OverlayApplied, TotalControls, CustomerControls, InheritedControls, SharedControls, TailoredOutControls, TailoredInControls, ControlIds, CreatedAt, CreatedBy)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'baseline:' + bl.SystemId) AS UNIQUEIDENTIFIER)),
    bl.SystemId, bl.Lvl, bl.Overlay, bl.Total, bl.Customer, bl.Inherited, bl.Shared, bl.TailoredOut, bl.TailoredIn,
    N'[]', -- Concrete control ID list will be populated by Select-phase tools.
    DATEADD(DAY, -30, @now), @actor
FROM bl
WHERE NOT EXISTS (SELECT 1 FROM ControlBaselines cb WHERE cb.RegisteredSystemId = bl.SystemId);

-- ─── 5.  RMF Role assignments (tiers 2..5) ──────────────────────────────────
;WITH role(SystemId, RmfRole, UserId, UserDisplayName) AS (
    SELECT v.SystemId, r.RmfRole, r.UserId, r.UserDisplayName
    FROM (VALUES (@cw),(@pf),(@en),(@ee)) AS v(SystemId)
    CROSS JOIN (VALUES
        (N'AuthorizingOfficial', N'col.harris@agency.gov',     N'Col. Robert Harris'),
        (N'Issm',                N'sarah.mitchell@agency.gov', N'Sarah Mitchell'),
        (N'Isso',                N'james.rodriguez@agency.gov',N'James Rodriguez'),
        (N'SystemOwner',         N'maj.chen@agency.gov',       N'Maj. Lisa Chen')
    ) AS r(RmfRole, UserId, UserDisplayName)
)
INSERT INTO RmfRoleAssignments (Id, RegisteredSystemId, RmfRole, UserId, UserDisplayName, AssignedAt, AssignedBy, IsActive)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'role:' + role.SystemId + N':' + role.RmfRole) AS UNIQUEIDENTIFIER)),
    role.SystemId, role.RmfRole, role.UserId, role.UserDisplayName,
    DATEADD(DAY, -60, @now), @actor, 1
FROM role
WHERE NOT EXISTS (
    SELECT 1 FROM RmfRoleAssignments ra
    WHERE ra.RegisteredSystemId = role.SystemId AND ra.RmfRole = role.RmfRole
);

-- ─── 6.  Primary authorization-boundary definitions (tiers 2..5) ────────────
-- BoundaryType is the BoundaryDefinitionType enum (Physical | Logical | Hybrid),
-- NOT an environment name. Environment goes in the boundary Name.
;WITH bdef(SystemId, Name, BoundaryType, Description) AS (
    SELECT @cw, N'Coastal Watch — Production',   N'Logical', N'Maritime collaboration enclave (single Azure Gov VA subscription).'
    UNION ALL SELECT @pf, N'Phoenix Falcon — Production', N'Logical', N'Logistics application boundary across two AzGov subscriptions.'
    UNION ALL SELECT @en, N'Eagle Nest — Production',     N'Logical', N'Analytics back-end supporting Eagle Eye.'
    UNION ALL SELECT @ee, N'Eagle Eye — Production',      N'Logical', N'ISR data fusion platform — primary authorization boundary.'
)
INSERT INTO AuthorizationBoundaryDefinitions (Id, RegisteredSystemId, Name, BoundaryType, Description, IsPrimary, CreatedAt, CreatedBy)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'boundary:' + bdef.SystemId) AS UNIQUEIDENTIFIER)),
    bdef.SystemId, bdef.Name, bdef.BoundaryType, bdef.Description, 1, DATEADD(DAY, -45, @now), @actor
FROM bdef
WHERE NOT EXISTS (
    SELECT 1 FROM AuthorizationBoundaryDefinitions abd
    WHERE abd.RegisteredSystemId = bdef.SystemId AND abd.IsPrimary = 1
);

-- ─── 7.  Latest assessments (tiers 3..5) ────────────────────────────────────
-- Status enum: 0=Pending,1=InProgress,2=Completed,3=Failed,4=Cancelled
;WITH asm(SystemId, Score, Total, Passed, Failed, NotAssessed, Baseline, AssessedAt) AS (
    SELECT @pf, 62.0, 287, 178, 84, 25, N'Moderate', DATEADD(DAY,  -3, @now)
    UNION ALL SELECT @en, 78.0, 287, 224, 47, 16, N'Moderate', DATEADD(DAY, -10, @now)
    UNION ALL SELECT @ee, 92.0, 378, 348, 22,  8, N'High',     DATEADD(DAY,  -5, @now)
)
INSERT INTO Assessments (
    Id, SubscriptionId, Framework, Baseline, ScanType, Status, InitiatedBy, AssessedAt, CompletedAt,
    ProgressMessage, ComplianceScore, TotalControls, PassedControls, FailedControls, NotAssessedControls,
    ControlFamilyResults, ExecutiveSummary, RiskProfile, EnvironmentName, SubscriptionIds, ScanPillarResults,
    RegisteredSystemId)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:' + asm.SystemId + N':v1') AS UNIQUEIDENTIFIER)),
    N'00000000-0000-0000-0000-000000000000', N'NIST 800-53 Rev 5', asm.Baseline, N'Comprehensive', 2, @actor,
    asm.AssessedAt, DATEADD(MINUTE, 12, asm.AssessedAt),
    N'Completed', asm.Score, asm.Total, asm.Passed, asm.Failed, asm.NotAssessed,
    N'[]', CONCAT(N'Initial baseline assessment — ', CAST(asm.Score AS NVARCHAR(10)), N'% compliance.'),
    -- RiskProfile is a JSON-serialized RiskProfile? object (NOT a level string). NULL is honored by the converter.
    NULL,
    -- ScanPillarResults is a JSON-serialized Dictionary<string,bool> — must be an OBJECT, not an array.
    N'AzureGovernment', N'[]', N'{}', asm.SystemId
FROM asm
WHERE NOT EXISTS (
    SELECT 1 FROM Assessments a
    WHERE a.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:' + asm.SystemId + N':v1') AS UNIQUEIDENTIFIER))
);

-- ─── 8.  Findings tied to those assessments (tiers 3..5) ────────────────────
-- FindingSeverity (int): 0=Critical,1=High,2=Medium,3=Low,4=Informational
-- FindingStatus  (int): 0=Open,1=InProgress,2=Remediated,3=Accepted,4=FalsePositive
-- ScanSourceType (int): 0=Resource,1=Policy,2=Defender,3=Combined,4=Cloud
-- RemediationType(int): 0=Unknown,1=ResourceConfiguration,2=PolicyAssignment,3=PolicyRemediation,4=Manual
-- RiskLevel      (int): 0=Standard,1=High
DECLARE @asPf NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:' + @pf + N':v1') AS UNIQUEIDENTIFIER));
DECLARE @asEn NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:' + @en + N':v1') AS UNIQUEIDENTIFIER));
DECLARE @asEe NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:' + @ee + N':v1') AS UNIQUEIDENTIFIER));

;WITH f(AssessmentId, Slug, ControlId, ControlFamily, Title, Severity, Status, CatSeverity, ResourceId, ResourceType) AS (
    -- Phoenix Falcon (Tier 3) — 3 CatI / 8 CatII / 14 CatIII = 25 total
    SELECT @asPf, N'pf-1',  N'AC-2',  N'AC', N'Privileged accounts lack lifecycle review',                  1, 0, N'CatI',   N'/sub/pf/ad', N'Microsoft.Authorization/roleAssignments'
    UNION ALL SELECT @asPf, N'pf-2',  N'IA-2', N'IA', N'MFA not enforced for break-glass account',          0, 0, N'CatI',   N'/sub/pf/ad', N'Microsoft.Authorization/roleAssignments'
    UNION ALL SELECT @asPf, N'pf-3',  N'SC-8', N'SC', N'TLS 1.0 still permitted on edge load balancer',     0, 0, N'CatI',   N'/sub/pf/lb', N'Microsoft.Network/loadBalancers'
    UNION ALL SELECT @asPf, N'pf-4',  N'AU-6', N'AU', N'Sentinel ingestion gap > 4h on Tuesday',            1, 0, N'CatII',  N'/sub/pf/sentinel', N'Microsoft.OperationalInsights/workspaces'
    UNION ALL SELECT @asPf, N'pf-5',  N'CM-6', N'CM', N'Configuration drift on web tier (3 VMs)',           2, 0, N'CatII',  N'/sub/pf/web', N'Microsoft.Compute/virtualMachines'
    UNION ALL SELECT @asPf, N'pf-6',  N'SI-2', N'SI', N'Critical patches > 30 days old (linux app tier)',   2, 0, N'CatII',  N'/sub/pf/app', N'Microsoft.Compute/virtualMachines'
    UNION ALL SELECT @asPf, N'pf-7',  N'SC-7', N'SC', N'NSG allows 0.0.0.0/0 inbound on port 22 in dev',    1, 0, N'CatII',  N'/sub/pf/dev/nsg', N'Microsoft.Network/networkSecurityGroups'
    UNION ALL SELECT @asPf, N'pf-8',  N'CP-9', N'CP', N'Backup retention < required 90 days',               2, 0, N'CatII',  N'/sub/pf/backup', N'Microsoft.RecoveryServices/vaults'
    UNION ALL SELECT @asPf, N'pf-9',  N'AC-6', N'AC', N'Owner role assigned to 12 users (target ≤ 6)',      2, 0, N'CatII',  N'/sub/pf', N'Microsoft.Authorization/roleAssignments'
    UNION ALL SELECT @asPf, N'pf-10', N'IA-5', N'IA', N'Service principal secrets > 90d unrotated (4)',     2, 0, N'CatII',  N'/sub/pf/sps', N'Microsoft.AzureActiveDirectory/applications'
    UNION ALL SELECT @asPf, N'pf-11', N'CA-7', N'CA', N'Defender continuous export not configured',         2, 0, N'CatII',  N'/sub/pf/defender', N'Microsoft.Security/defender'
    UNION ALL SELECT @asPf, N'pf-12', N'AC-7', N'AC', N'Account lockout threshold > 10 attempts',           3, 0, N'CatIII', N'/sub/pf/ad', N'Microsoft.AzureActiveDirectory/policies'
    UNION ALL SELECT @asPf, N'pf-13', N'AU-3', N'AU', N'Audit record fields missing (sourceIp on 2 logs)',  3, 0, N'CatIII', N'/sub/pf/sentinel', N'Microsoft.OperationalInsights/workspaces'
    UNION ALL SELECT @asPf, N'pf-14', N'AU-9', N'AU', N'Log workspace lacks immutability lock',             3, 0, N'CatIII', N'/sub/pf/sentinel', N'Microsoft.OperationalInsights/workspaces'
    UNION ALL SELECT @asPf, N'pf-15', N'CM-2', N'CM', N'Baseline config not version-pinned (3 ARM templates)',3,0,N'CatIII', N'/sub/pf/templates', N'Microsoft.Resources/templateSpecs'
    UNION ALL SELECT @asPf, N'pf-16', N'CM-7', N'CM', N'Unused inbound rule on app gateway WAF',            3, 0, N'CatIII', N'/sub/pf/agw', N'Microsoft.Network/applicationGateways'
    UNION ALL SELECT @asPf, N'pf-17', N'SC-12', N'SC', N'Key Vault key rotation policy missing on 2 keys',  3, 0, N'CatIII', N'/sub/pf/kv', N'Microsoft.KeyVault/vaults'
    UNION ALL SELECT @asPf, N'pf-18', N'SC-28', N'SC', N'Storage account using default Microsoft-managed keys',3,0,N'CatIII', N'/sub/pf/storage', N'Microsoft.Storage/storageAccounts'
    UNION ALL SELECT @asPf, N'pf-19', N'RA-5', N'RA', N'Vulnerability scan cadence > 7 days',               3, 0, N'CatIII', N'/sub/pf/defender', N'Microsoft.Security/assessments'
    UNION ALL SELECT @asPf, N'pf-20', N'PE-2', N'PE', N'Physical access list not reviewed in 365 days',     3, 0, N'CatIII', N'/sub/pf/admin', N'Microsoft.Authorization/policyAssignments'
    UNION ALL SELECT @asPf, N'pf-21', N'PL-2', N'PL', N'SSP control narrative missing for 4 controls',      3, 0, N'CatIII', N'/sub/pf/ssp', N'AtoCopilot/SspSection'
    UNION ALL SELECT @asPf, N'pf-22', N'IR-4', N'IR', N'Tabletop exercise not held this fiscal year',       3, 0, N'CatIII', N'/sub/pf/ir', N'AtoCopilot/IncidentResponse'
    UNION ALL SELECT @asPf, N'pf-23', N'AT-2', N'AT', N'Annual security awareness training overdue (2 users)',3,0,N'CatIII', N'/sub/pf/training', N'AtoCopilot/Training'
    UNION ALL SELECT @asPf, N'pf-24', N'CP-4', N'CP', N'Contingency plan testing > 12 months',              3, 0, N'CatIII', N'/sub/pf/cp', N'AtoCopilot/ContingencyPlan'
    UNION ALL SELECT @asPf, N'pf-25', N'MA-2', N'MA', N'Maintenance log gaps in Q1',                        3, 0, N'CatIII', N'/sub/pf/ops', N'AtoCopilot/MaintenanceLog'

    -- Eagle Nest (Tier 4) — 1 CatI / 5 CatII / 9 CatIII = 15 total
    UNION ALL SELECT @asEn, N'en-1',  N'AU-9',  N'AU', N'Sentinel workspace lacks delete-protection lock',  1, 0, N'CatI',   N'/sub/en/sentinel', N'Microsoft.OperationalInsights/workspaces'
    UNION ALL SELECT @asEn, N'en-2',  N'AC-6',  N'AC', N'JIT VM access not configured for analytics nodes', 2, 0, N'CatII',  N'/sub/en/vm', N'Microsoft.Compute/virtualMachines'
    UNION ALL SELECT @asEn, N'en-3',  N'CM-6',  N'CM', N'Drift on 2 analytics nodes (chrony, sshd)',        2, 0, N'CatII',  N'/sub/en/vm', N'Microsoft.Compute/virtualMachines'
    UNION ALL SELECT @asEn, N'en-4',  N'SI-4',  N'SI', N'EDR sensor offline > 24h on 1 node',               2, 0, N'CatII',  N'/sub/en/vm', N'Microsoft.Compute/virtualMachines'
    UNION ALL SELECT @asEn, N'en-5',  N'CP-9',  N'CP', N'Geo-redundant copy lag > 4h',                      2, 0, N'CatII',  N'/sub/en/backup', N'Microsoft.RecoveryServices/vaults'
    UNION ALL SELECT @asEn, N'en-6',  N'SC-7',  N'SC', N'East-west traffic not micro-segmented in 1 vnet',  2, 0, N'CatII',  N'/sub/en/network', N'Microsoft.Network/virtualNetworks'
    UNION ALL SELECT @asEn, N'en-7',  N'PL-2',  N'PL', N'SSP narrative outdated for control SC-13',         3, 0, N'CatIII', N'/sub/en/ssp', N'AtoCopilot/SspSection'
    UNION ALL SELECT @asEn, N'en-8',  N'AT-3',  N'AT', N'Role-based training delta for 1 ISSO contractor',  3, 0, N'CatIII', N'/sub/en/training', N'AtoCopilot/Training'
    UNION ALL SELECT @asEn, N'en-9',  N'CA-7',  N'CA', N'Continuous monitoring frequency drift (RA-5)',     3, 0, N'CatIII', N'/sub/en/conmon', N'AtoCopilot/ConMon'
    UNION ALL SELECT @asEn, N'en-10', N'IA-5',  N'IA', N'2 service principal secrets aged > 60 days',       3, 0, N'CatIII', N'/sub/en/sps', N'Microsoft.AzureActiveDirectory/applications'
    UNION ALL SELECT @asEn, N'en-11', N'AU-6',  N'AU', N'Audit review backlog of 2 weeks',                  3, 0, N'CatIII', N'/sub/en/sentinel', N'Microsoft.OperationalInsights/workspaces'
    UNION ALL SELECT @asEn, N'en-12', N'CM-7',  N'CM', N'1 unused legacy NSG rule',                         3, 0, N'CatIII', N'/sub/en/network', N'Microsoft.Network/networkSecurityGroups'
    UNION ALL SELECT @asEn, N'en-13', N'IR-5',  N'IR', N'Lessons-learned doc missing for FY-Q4 incident',   3, 0, N'CatIII', N'/sub/en/ir', N'AtoCopilot/IncidentResponse'
    UNION ALL SELECT @asEn, N'en-14', N'CP-4',  N'CP', N'Last DR table-top > 9 months ago',                 3, 0, N'CatIII', N'/sub/en/cp', N'AtoCopilot/ContingencyPlan'
    UNION ALL SELECT @asEn, N'en-15', N'PE-3',  N'PE', N'Datacenter access roster needs annual recert',     3, 0, N'CatIII', N'/sub/en/admin', N'AtoCopilot/Facility'

    -- Eagle Eye (Tier 5) — 0 CatI / 2 CatII / 5 CatIII = 7 total (steady state)
    UNION ALL SELECT @asEe, N'ee-1', N'CM-6',  N'CM', N'Configuration drift on 1 jumpbox',                  2, 0, N'CatII',  N'/sub/ee/vm', N'Microsoft.Compute/virtualMachines'
    UNION ALL SELECT @asEe, N'ee-2', N'AU-6',  N'AU', N'Audit review SLA missed for 1 day in last 30',      2, 0, N'CatII',  N'/sub/ee/sentinel', N'Microsoft.OperationalInsights/workspaces'
    UNION ALL SELECT @asEe, N'ee-3', N'AT-3',  N'AT', N'1 user training overdue by 3 days',                 3, 0, N'CatIII', N'/sub/ee/training', N'AtoCopilot/Training'
    UNION ALL SELECT @asEe, N'ee-4', N'IA-5',  N'IA', N'1 SP secret aged 75 days (rotation cadence drift)', 3, 0, N'CatIII', N'/sub/ee/sps', N'Microsoft.AzureActiveDirectory/applications'
    UNION ALL SELECT @asEe, N'ee-5', N'CA-7',  N'CA', N'ConMon dashboard lag of 2 hours noted',             3, 0, N'CatIII', N'/sub/ee/conmon', N'AtoCopilot/ConMon'
    UNION ALL SELECT @asEe, N'ee-6', N'CP-4',  N'CP', N'Annual DR exercise scheduled — not yet executed',   3, 0, N'CatIII', N'/sub/ee/cp', N'AtoCopilot/ContingencyPlan'
    UNION ALL SELECT @asEe, N'ee-7', N'PL-2',  N'PL', N'SSP appendix needs minor update post-Foundry roll', 3, 0, N'CatIII', N'/sub/ee/ssp', N'AtoCopilot/SspSection'
)
INSERT INTO Findings (
    Id, ControlId, ControlFamily, Title, Description, Severity, Status,
    ResourceId, ResourceType, RemediationGuidance, DiscoveredAt, AutoRemediable,
    Source, ScanSource, RemediationType, RiskLevel, AssessmentId,
    ControlTitle, ControlDescription, StigFinding, RemediationTrackingStatus, CatSeverity)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'finding:' + f.Slug) AS UNIQUEIDENTIFIER)),
    f.ControlId, f.ControlFamily, f.Title,
    CONCAT(N'Discovered during baseline assessment. ', f.Title, N'.'),
    f.Severity, f.Status,
    f.ResourceId, f.ResourceType,
    N'Refer to NIST 800-53 Rev 5 control guidance.',
    DATEADD(DAY, -CAST(ABS(CHECKSUM(NEWID())) % 60 AS INT), @now),
    0, N'AtoCopilot', 0,
    CASE WHEN f.Severity <= 1 THEN 1 ELSE 4 END,
    CASE WHEN f.ControlFamily IN (N'AC',N'IA',N'SC') THEN 1 ELSE 0 END,
    f.AssessmentId,
    f.ControlId, N'See NIST 800-53 Rev 5.', 0, 0, f.CatSeverity
FROM f
WHERE NOT EXISTS (
    SELECT 1 FROM Findings existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'finding:' + f.Slug) AS UNIQUEIDENTIFIER))
);

-- ─── 9.  POA&M items (tiers 3..5) ──────────────────────────────────────────-
;WITH p(SystemId, Slug, ControlId, CatSev, Status, Weakness, Source, ScheduledDays, Overdue) AS (
    -- Phoenix Falcon: 6 open POA&Ms (2 CatI, 2 CatII, 2 CatIII), 2 overdue
    SELECT @pf, N'pf-poam-1', N'AC-2', N'CatI',   N'Ongoing', N'Privileged accounts lack lifecycle review',         N'SCA Assessment',  60,  0
    UNION ALL SELECT @pf, N'pf-poam-2', N'IA-2',  N'CatI',   N'Ongoing', N'MFA not enforced for break-glass account',          N'SCA Assessment',  -7,  1
    UNION ALL SELECT @pf, N'pf-poam-3', N'SC-7',  N'CatII',  N'Ongoing', N'NSG allows 0.0.0.0/0 inbound on dev port 22',       N'SCA Assessment',  30,  0
    UNION ALL SELECT @pf, N'pf-poam-4', N'CP-9',  N'CatII',  N'Ongoing', N'Backup retention below 90-day requirement',         N'SCA Assessment',  90,  0
    UNION ALL SELECT @pf, N'pf-poam-5', N'AT-2',  N'CatIII', N'Ongoing', N'Annual security awareness training overdue (2 users)',N'Manual',         -3,  1
    UNION ALL SELECT @pf, N'pf-poam-6', N'CP-4',  N'CatIII', N'Ongoing', N'Contingency plan testing > 12 months',              N'Manual',          120, 0

    -- Eagle Nest: 4 open POA&Ms (1 CatI, 2 CatII, 1 CatIII), none overdue
    UNION ALL SELECT @en, N'en-poam-1', N'AU-9', N'CatI',   N'Ongoing', N'Sentinel workspace lacks delete-protection lock',   N'SCA Assessment', 14, 0
    UNION ALL SELECT @en, N'en-poam-2', N'CP-9', N'CatII',  N'Ongoing', N'Geo-redundant copy lag > 4h',                       N'SCA Assessment', 30, 0
    UNION ALL SELECT @en, N'en-poam-3', N'SI-4', N'CatII',  N'Ongoing', N'EDR sensor offline > 24h on 1 node',                N'SCA Assessment', 21, 0
    UNION ALL SELECT @en, N'en-poam-4', N'IA-5', N'CatIII', N'Ongoing', N'2 service principal secrets aged > 60 days',        N'Manual',         45, 0

    -- Eagle Eye: 2 open POA&Ms (steady state)
    UNION ALL SELECT @ee, N'ee-poam-1', N'IA-5', N'CatIII', N'Ongoing', N'1 SP secret aged 75 days',                          N'ConMon',          15, 0
    UNION ALL SELECT @ee, N'ee-poam-2', N'CP-4', N'CatIII', N'Ongoing', N'Annual DR exercise scheduled but not yet executed', N'ConMon',          30, 0
)
INSERT INTO PoamItems (
    Id, RegisteredSystemId, Weakness, WeaknessSource, SecurityControlNumber, CatSeverity,
    PointOfContact, PocEmail, ScheduledCompletionDate, Status, CreatedAt, CreatedBy, RowVersion)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'poam:' + p.Slug) AS UNIQUEIDENTIFIER)),
    p.SystemId, p.Weakness, p.Source, p.ControlId, p.CatSev,
    N'James Rodriguez', N'james.rodriguez@agency.gov',
    DATEADD(DAY, p.ScheduledDays, @now), p.Status,
    DATEADD(DAY, -45, @now), @actor, NEWID()
FROM p
WHERE NOT EXISTS (
    SELECT 1 FROM PoamItems existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'poam:' + p.Slug) AS UNIQUEIDENTIFIER))
);

-- ─── 10. Active ATO for Eagle Eye (tier 5) ──────────────────────────────────
INSERT INTO AuthorizationDecisions (
    Id, RegisteredSystemId, DecisionType, DecisionDate, ExpirationDate,
    TermsAndConditions, ResidualRiskLevel, ResidualRiskJustification,
    ComplianceScoreAtDecision, FindingsAtDecision, IssuedBy, IssuedByName, IsActive)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'authz:' + @ee + N':v1') AS UNIQUEIDENTIFIER)),
    @ee, N'Ato', DATEADD(DAY, -210, @now), DATEADD(DAY, 155, @now),
    N'ATO subject to: (1) annual reauthorization review, (2) ConMon report submitted monthly, (3) all CAT I findings remediated within 30 days of discovery.',
    N'Low', N'Residual risk evaluated as Low. Compensating controls in place for all open CAT II/III findings; no open CAT I findings.',
    92.0,
    N'{"catI":0,"catII":2,"catIII":5}',
    N'col.harris@agency.gov', N'Col. Robert Harris', 1
WHERE NOT EXISTS (
    SELECT 1 FROM AuthorizationDecisions ad WHERE ad.RegisteredSystemId = @ee AND ad.IsActive = 1
);

-- ─── 11. Daily ComplianceTrendSnapshots — last 90 days, per system ──────────
-- Numbers tail off / climb depending on tier; deterministic so reseeds match.
;WITH days AS (
    SELECT TOP (90) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) - 1 AS DayBack
    FROM sys.all_objects
)
INSERT INTO ComplianceTrendSnapshots (Id, RegisteredSystemId, CapturedAt, ComplianceScore, CatICount, CatIICount, CatIIICount, OpenPoamCount, OverduePoamCount, NarrativeCoverage, Source)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'trend:' + s.SystemId + N':' + CAST(d.DayBack AS NVARCHAR(10))) AS UNIQUEIDENTIFIER)),
    s.SystemId,
    DATEADD(DAY, -d.DayBack, @now),
    s.Score - (d.DayBack * s.Slope),
    CASE WHEN s.SystemId = @pf THEN 3 + (d.DayBack / 30)
         WHEN s.SystemId = @en THEN 1 + (d.DayBack / 45)
         WHEN s.SystemId = @ee THEN CASE WHEN d.DayBack < 30 THEN 0 ELSE 1 END
         ELSE 0 END,
    CASE WHEN s.SystemId = @pf THEN 8 + (d.DayBack / 15)
         WHEN s.SystemId = @en THEN 5 + (d.DayBack / 20)
         WHEN s.SystemId = @ee THEN 2 + (d.DayBack / 60)
         ELSE 0 END,
    CASE WHEN s.SystemId = @pf THEN 14 + (d.DayBack / 10)
         WHEN s.SystemId = @en THEN 9 + (d.DayBack / 18)
         WHEN s.SystemId = @ee THEN 5 + (d.DayBack / 30)
         ELSE 0 END,
    CASE WHEN s.SystemId = @pf THEN 6 + (d.DayBack / 30)
         WHEN s.SystemId = @en THEN 4 + (d.DayBack / 30)
         WHEN s.SystemId = @ee THEN 2 + (d.DayBack / 45)
         ELSE 0 END,
    CASE WHEN s.SystemId = @pf THEN CASE WHEN d.DayBack > 60 THEN 3 WHEN d.DayBack > 30 THEN 2 ELSE 2 END
         WHEN s.SystemId = @en THEN CASE WHEN d.DayBack > 60 THEN 1 ELSE 0 END
         WHEN s.SystemId = @ee THEN 0
         ELSE 0 END,
    s.NarrativeFloor + (CAST(89 - d.DayBack AS FLOAT) * s.NarrativeSlope),
    N'Scheduled'
FROM (VALUES
    (@pb, 0.0,  0.00, 0.0,  0.00),
    (@cw, 12.0, 0.05, 5.0,  0.10),
    (@pf, 62.0, 0.30, 35.0, 0.30),
    (@en, 78.0, 0.15, 60.0, 0.20),
    (@ee, 92.0, 0.05, 85.0, 0.10)
) AS s(SystemId, Score, Slope, NarrativeFloor, NarrativeSlope)
CROSS JOIN days d
WHERE NOT EXISTS (
    SELECT 1 FROM ComplianceTrendSnapshots existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'trend:' + s.SystemId + N':' + CAST(d.DayBack AS NVARCHAR(10))) AS UNIQUEIDENTIFIER))
);

-- ─── 12. Activity feed entries per system ───────────────────────────────────
;WITH a(SystemId, Slug, EventType, OffsetDays, Actor, Summary, RelType, RelId) AS (
    -- Polar Bear (Tier 1)
    SELECT @pb, N'pb-1', N'SystemRegistered',     -3,  N'dashboard-user',         N'System ''Polar Bear'' registered (PlatformIt, MissionCritical, IL6 air-gapped).',                              N'RegisteredSystem', NULL
    UNION ALL SELECT @pb, N'pb-2', N'WorkflowStarted',      -2,  N'dashboard-user',         N'Engineer started Prepare-phase intake wizard for Polar Bear.',                                                  N'Wizard',           NULL

    -- Coastal Watch (Tier 2)
    UNION ALL SELECT @cw, N'cw-1', N'SystemRegistered',     -10, N'dashboard-user',         N'System ''Coastal Watch'' registered (Enclave, MissionSupport).',                                                N'RegisteredSystem', NULL
    UNION ALL SELECT @cw, N'cw-2', N'CategorizationCompleted',-7,N'sarah.mitchell@agency.gov',N'FIPS 199 categorization completed: Low / Low / Moderate (high-water Low).',                                  N'SecurityCategorization', NULL
    UNION ALL SELECT @cw, N'cw-3', N'BaselineSelected',     -5,  N'sarah.mitchell@agency.gov',N'Selected NIST 800-53 Rev 5 Low baseline (149 controls).',                                                    N'ControlBaseline',  NULL
    UNION ALL SELECT @cw, N'cw-4', N'RmfPhaseAdvanced',     -5,  N'sarah.mitchell@agency.gov',N'RMF phase advanced from Prepare to Categorize.',                                                              N'RegisteredSystem', NULL

    -- Phoenix Falcon (Tier 3)
    UNION ALL SELECT @pf, N'pf-a-1', N'SystemRegistered',         -75, N'dashboard-user',           N'System ''Phoenix Falcon'' registered (MajorApplication, MissionEssential).',                            N'RegisteredSystem', NULL
    UNION ALL SELECT @pf, N'pf-a-2', N'CategorizationCompleted',  -55, N'sarah.mitchell@agency.gov', N'FIPS 199 categorization completed: Moderate baseline.',                                                 N'SecurityCategorization', NULL
    UNION ALL SELECT @pf, N'pf-a-3', N'BaselineSelected',         -45, N'sarah.mitchell@agency.gov', N'Selected NIST 800-53 Rev 5 Moderate + CNSSI 1253 IL4 (287 controls).',                                  N'ControlBaseline',  NULL
    UNION ALL SELECT @pf, N'pf-a-4', N'RmfPhaseAdvanced',         -40, N'sarah.mitchell@agency.gov', N'RMF phase advanced from Select to Implement.',                                                          N'RegisteredSystem', NULL
    UNION ALL SELECT @pf, N'pf-a-5', N'AssessmentCompleted',      -3,  N'james.rodriguez@agency.gov',N'Initial baseline assessment completed: 62.0% compliance, 25 findings.',                                N'ComplianceAssessment', NULL
    UNION ALL SELECT @pf, N'pf-a-6', N'PoamCreated',              -2,  N'james.rodriguez@agency.gov',N'Bulk-created 6 POA&Ms from assessment findings.',                                                       N'PoamItem',         NULL
    UNION ALL SELECT @pf, N'pf-a-7', N'NarrativeUpdated',         -1,  N'james.rodriguez@agency.gov',N'Updated SSP narratives for AC-2, IA-2, SC-8.',                                                          N'SspSection',       NULL

    -- Eagle Nest (Tier 4)
    UNION ALL SELECT @en, N'en-a-1', N'SystemRegistered',         -120,N'dashboard-user',           N'System ''Eagle Nest'' registered (Enclave, MissionEssential).',                                          N'RegisteredSystem', NULL
    UNION ALL SELECT @en, N'en-a-2', N'CategorizationCompleted',  -100,N'sarah.mitchell@agency.gov', N'FIPS 199 categorization completed: Moderate.',                                                          N'SecurityCategorization', NULL
    UNION ALL SELECT @en, N'en-a-3', N'BaselineSelected',         -90, N'sarah.mitchell@agency.gov', N'Selected NIST 800-53 Rev 5 Moderate + CNSSI 1253 IL5 (287 controls).',                                  N'ControlBaseline',  NULL
    UNION ALL SELECT @en, N'en-a-4', N'RmfPhaseAdvanced',         -65, N'sarah.mitchell@agency.gov', N'RMF phase advanced from Implement to Assess.',                                                          N'RegisteredSystem', NULL
    UNION ALL SELECT @en, N'en-a-5', N'AssessmentCompleted',      -10, N'james.rodriguez@agency.gov',N'Independent SCA assessment completed: 78.0% compliance, 15 findings.',                                 N'ComplianceAssessment', NULL
    UNION ALL SELECT @en, N'en-a-6', N'PoamCreated',              -9,  N'james.rodriguez@agency.gov',N'Bulk-created 4 POA&Ms from SCA findings.',                                                              N'PoamItem',         NULL
    UNION ALL SELECT @en, N'en-a-7', N'PackageSubmitted',         -2,  N'james.rodriguez@agency.gov',N'Authorization package submitted to AO for review.',                                                     N'AuthorizationPackage', NULL

    -- Eagle Eye (Tier 5)
    UNION ALL SELECT @ee, N'ee-a-1', N'SystemRegistered',         -300,N'dashboard-user',           N'System ''Eagle Eye'' registered (MajorApplication, MissionCritical).',                                  N'RegisteredSystem', NULL
    UNION ALL SELECT @ee, N'ee-a-2', N'AuthorizationGranted',     -210,N'col.harris@agency.gov',     N'AO Col. Robert Harris issued ATO valid for 365 days (residual risk: Low).',                            N'AuthorizationDecision', NULL
    UNION ALL SELECT @ee, N'ee-a-3', N'RmfPhaseAdvanced',         -210,N'col.harris@agency.gov',     N'RMF phase advanced from Authorize to Monitor.',                                                         N'RegisteredSystem', NULL
    UNION ALL SELECT @ee, N'ee-a-4', N'ConMonReportSubmitted',    -30, N'james.rodriguez@agency.gov',N'Monthly ConMon report submitted (April).',                                                              N'ConMonReport',     NULL
    UNION ALL SELECT @ee, N'ee-a-5', N'AssessmentCompleted',      -5,  N'james.rodriguez@agency.gov',N'Quarterly continuous-monitoring assessment: 92.0% compliance, 7 findings.',                            N'ComplianceAssessment', NULL
    UNION ALL SELECT @ee, N'ee-a-6', N'NarrativeUpdated',         -2,  N'james.rodriguez@agency.gov',N'Updated SSP narrative for IA-5 (rotation cadence drift).',                                              N'SspSection',       NULL
    UNION ALL SELECT @ee, N'ee-a-7', N'PoamUpdated',              -1,  N'james.rodriguez@agency.gov',N'Closed POA&M for SC-13 after key rotation policy applied.',                                             N'PoamItem',         NULL
)
INSERT INTO DashboardActivities (Id, RegisteredSystemId, EventType, Timestamp, Actor, Summary, RelatedEntityType, RelatedEntityId)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'activity:' + a.Slug) AS UNIQUEIDENTIFIER)),
    a.SystemId, a.EventType, DATEADD(DAY, a.OffsetDays, @now), a.Actor, a.Summary, a.RelType, NULL
FROM a
WHERE NOT EXISTS (
    SELECT 1 FROM DashboardActivities existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'activity:' + a.Slug) AS UNIQUEIDENTIFIER))
);

COMMIT TRANSACTION;

-- ─── Summary ────────────────────────────────────────────────────────────────
PRINT '─── Seed-progress summary ───';
SELECT
    rs.Name,
    rs.CurrentRmfStep                                                AS Phase,
    rs.OperationalStatus                                             AS OpStatus,
    cb.BaselineLevel                                                 AS Baseline,
    (SELECT COUNT(*) FROM RmfRoleAssignments r WHERE r.RegisteredSystemId = rs.Id AND r.IsActive = 1) AS Roles,
    (SELECT COUNT(*) FROM Assessments a WHERE a.RegisteredSystemId = rs.Id) AS Assessments,
    (SELECT COUNT(*) FROM Findings f JOIN Assessments a ON f.AssessmentId = a.Id WHERE a.RegisteredSystemId = rs.Id) AS Findings,
    (SELECT COUNT(*) FROM PoamItems p WHERE p.RegisteredSystemId = rs.Id AND p.Status = N'Ongoing') AS OpenPoams,
    (SELECT COUNT(*) FROM AuthorizationDecisions ad WHERE ad.RegisteredSystemId = rs.Id AND ad.IsActive = 1) AS ActiveAtos,
    (SELECT COUNT(*) FROM ComplianceTrendSnapshots t WHERE t.RegisteredSystemId = rs.Id) AS TrendDays,
    (SELECT COUNT(*) FROM DashboardActivities da WHERE da.RegisteredSystemId = rs.Id) AS Activities
FROM RegisteredSystems rs
LEFT JOIN ControlBaselines cb ON cb.RegisteredSystemId = rs.Id
WHERE rs.IsActive = 1
ORDER BY
    CASE rs.CurrentRmfStep
        WHEN N'Prepare'     THEN 1
        WHEN N'Categorize'  THEN 2
        WHEN N'Select'      THEN 3
        WHEN N'Implement'   THEN 4
        WHEN N'Assess'      THEN 5
        WHEN N'Authorize'   THEN 6
        WHEN N'Monitor'     THEN 7
    END;
