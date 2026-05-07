-- ============================================================================
--  ATO Copilot — Seed varied RMF / ATO progress against the AZURE SQL DB.
--  Target:  Azure SQL `AtoCopilot`
--  Idempotent: re-running is a no-op (deterministic UUIDs from HASHBYTES).
--
--  Tier mapping for the 4 systems registered in Azure SQL:
--    Test System    (80b26255-…)   Tier 1 — Prepare       (no progress)
--    Test System 2  (70c5c614-…)   Tier 3 — Implement     (mid-build, 2 assessments)
--    Eagle Nest     (e44f01af-…)   Tier 4 — Assess        (independent SCA, 3 assessments)
--    Eagle Eye      (95eec6d6-…)   Tier 5 — Monitor + ATO (active ATO, 4 assessments)
--
--  Enum reminders:
--    FindingSeverity (int): 0=Critical 1=High 2=Medium 3=Low 4=Informational
--    FindingStatus   (int): 0=Open 1=InProgress 2=Remediated 3=Accepted 4=FalsePositive
--    AssessmentStatus(int): 0=Pending 1=InProgress 2=Completed 3=Failed 4=Cancelled
--    Kanban TaskStatus(int): 0=Backlog 1=ToDo 2=InProgress 3=InReview 4=Blocked 5=Done
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @now      DATETIME2(7) = SYSUTCDATETIME();
DECLARE @actor    NVARCHAR(200) = N'seed-azure-progress';
DECLARE @scaUser  NVARCHAR(200) = N'james.rodriguez@agency.gov';
DECLARE @scaName  NVARCHAR(200) = N'James Rodriguez';
DECLARE @issmUser NVARCHAR(200) = N'sarah.mitchell@agency.gov';
DECLARE @issmName NVARCHAR(200) = N'Sarah Mitchell';
DECLARE @aoUser   NVARCHAR(200) = N'col.harris@agency.gov';
DECLARE @aoName   NVARCHAR(200) = N'Col. Robert Harris';
DECLARE @ownerUser NVARCHAR(200) = N'maj.chen@agency.gov';
DECLARE @ownerName NVARCHAR(200) = N'Maj. Lisa Chen';

-- ─── Resolve system IDs by canonical name ───────────────────────────────────
DECLARE @ts  NVARCHAR(36) = (SELECT Id FROM RegisteredSystems WHERE Name = N'Test System'   AND IsActive = 1);
DECLARE @ts2 NVARCHAR(36) = (SELECT Id FROM RegisteredSystems WHERE Name = N'Test System 2' AND IsActive = 1);
DECLARE @en  NVARCHAR(36) = (SELECT Id FROM RegisteredSystems WHERE Name = N'Eagle Nest'    AND IsActive = 1);
DECLARE @ee  NVARCHAR(36) = (SELECT Id FROM RegisteredSystems WHERE Name = N'Eagle Eye'     AND IsActive = 1);

IF @ts IS NULL OR @ts2 IS NULL OR @en IS NULL OR @ee IS NULL
BEGIN
    RAISERROR('seed-azure-progress: expected systems are missing from Azure SQL.', 16, 1);
    RETURN;
END

PRINT '─── Resolved system IDs ───';
PRINT N'  Test System   = ' + @ts;
PRINT N'  Test System 2 = ' + @ts2;
PRINT N'  Eagle Nest    = ' + @en;
PRINT N'  Eagle Eye     = ' + @ee;

BEGIN TRANSACTION;

-- ============================================================================
-- 1. RMF phase + OperationalStatus per system
--    Tier 2 — Select       — Test System
--    Tier 3 — Implement    — Test System 2
--    Tier 4 — Assess       — Eagle Nest
--    Tier 5 — Monitor+ATO  — Eagle Eye
-- ============================================================================
UPDATE RegisteredSystems SET CurrentRmfStep = N'Select',      OperationalStatus = N'UnderDevelopment',
       RmfStepUpdatedAt = @now, ModifiedAt = @now WHERE Id = @ts;
UPDATE RegisteredSystems SET CurrentRmfStep = N'Implement',   OperationalStatus = N'UnderDevelopment',
       RmfStepUpdatedAt = @now, ModifiedAt = @now WHERE Id = @ts2;
UPDATE RegisteredSystems SET CurrentRmfStep = N'Assess',      OperationalStatus = N'UnderDevelopment',
       RmfStepUpdatedAt = @now, ModifiedAt = @now WHERE Id = @en;
UPDATE RegisteredSystems SET CurrentRmfStep = N'Monitor',     OperationalStatus = N'Operational',
       RmfStepUpdatedAt = @now, ModifiedAt = @now,
       OperationalDate  = ISNULL(OperationalDate, DATEADD(MONTH, -8, @now))
 WHERE Id = @ee;

-- ============================================================================
-- 2. Security Categorizations (tiers 3..5; tier 1 stays empty)
-- ============================================================================
;WITH src(SystemId, NSS, Justification, CategorizedBy, CategorizedAt) AS (
    SELECT @ts2, 0, N'FIPS 199 Moderate: logistics PII (CUI), supply-chain integrity essential.',  @issmName, DATEADD(DAY, -45, @now)
    UNION ALL SELECT @en, 0, N'FIPS 199 Moderate: ISR analytics enclave supporting Eagle Eye.',     @issmName, DATEADD(DAY, -90, @now)
    UNION ALL SELECT @ee, 1, N'FIPS 199 High / NSS: ISR data fusion with classified info types.',   @issmName, DATEADD(DAY,-200, @now)
)
INSERT INTO SecurityCategorizations (Id, RegisteredSystemId, IsNationalSecuritySystem, Justification, CategorizedBy, CategorizedAt)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'cat:' + s.SystemId) AS UNIQUEIDENTIFIER)),
    s.SystemId, s.NSS, s.Justification, s.CategorizedBy, s.CategorizedAt
FROM src s
WHERE NOT EXISTS (SELECT 1 FROM SecurityCategorizations c WHERE c.RegisteredSystemId = s.SystemId);

-- ============================================================================
-- 3. Information Types (rich seed for systems with categorization)
-- ============================================================================
;WITH it(SystemId, Sp, Name, Cat, C, I, A) AS (
    -- Test System 2 — Moderate
    SELECT @ts2, N'C.3.4.1', N'Logistics Management',          N'Defense & National Security', N'Moderate', N'Moderate', N'Moderate'
    UNION ALL SELECT @ts2, N'C.2.8.4', N'Procurement / Acquisition',     N'Defense & National Security', N'Moderate', N'Moderate', N'Low'
    UNION ALL SELECT @ts2, N'C.3.5.1', N'Continuity of Operations',      N'Defense & National Security', N'Moderate', N'Moderate', N'Moderate'
    -- Eagle Nest — Moderate
    UNION ALL SELECT @en, N'C.3.5.4', N'Intelligence Operations',        N'Defense & National Security', N'Moderate', N'Moderate', N'Moderate'
    UNION ALL SELECT @en, N'C.3.5.6', N'Operational Test & Evaluation',  N'Defense & National Security', N'Moderate', N'Moderate', N'Moderate'
    UNION ALL SELECT @en, N'D.20.2', N'Network Security Management',     N'Information & Comms Mgmt',    N'Moderate', N'Moderate', N'Moderate'
    -- Eagle Eye — High / NSS
    UNION ALL SELECT @ee, N'C.3.5.4', N'Intelligence Operations',        N'Defense & National Security', N'High',     N'High',     N'High'
    UNION ALL SELECT @ee, N'C.3.5.5', N'Surveillance & Reconnaissance',  N'Defense & National Security', N'High',     N'Moderate', N'High'
    UNION ALL SELECT @ee, N'C.3.5.1', N'Continuity of Operations',       N'Defense & National Security', N'High',     N'High',     N'High'
    UNION ALL SELECT @ee, N'C.3.5.7', N'Defense Strategy & Planning',    N'Defense & National Security', N'High',     N'High',     N'Moderate'
)
INSERT INTO InformationTypes (Id, SecurityCategorizationId, Sp80060Id, Name, Category, ConfidentialityImpact, IntegrityImpact, AvailabilityImpact, UsesProvisionalImpactLevels)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'it:' + it.SystemId + N':' + it.Sp) AS UNIQUEIDENTIFIER)),
    (SELECT TOP 1 Id FROM SecurityCategorizations WHERE RegisteredSystemId = it.SystemId),
    it.Sp, it.Name, it.Cat, it.C, it.I, it.A, 1
FROM it
WHERE EXISTS (SELECT 1 FROM SecurityCategorizations sc WHERE sc.RegisteredSystemId = it.SystemId)
  AND NOT EXISTS (
    SELECT 1 FROM InformationTypes existing
    WHERE existing.SecurityCategorizationId = (SELECT TOP 1 Id FROM SecurityCategorizations WHERE RegisteredSystemId = it.SystemId)
      AND existing.Sp80060Id = it.Sp
);

-- ============================================================================
-- 4. Control Baselines (tiers 3..5) — Eagle Nest only (others already have)
-- ============================================================================
;WITH bl(SystemId, Lvl, Overlay, Total, Customer, Inherited, Shared, TailoredOut, TailoredIn) AS (
    SELECT @en, N'Moderate', N'CNSSI 1253 IL5',           287, 200, 58, 29, 5,  8
)
INSERT INTO ControlBaselines (Id, RegisteredSystemId, BaselineLevel, OverlayApplied, TotalControls, CustomerControls, InheritedControls, SharedControls, TailoredOutControls, TailoredInControls, ControlIds, CreatedAt, CreatedBy)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'baseline:' + bl.SystemId) AS UNIQUEIDENTIFIER)),
    bl.SystemId, bl.Lvl, bl.Overlay, bl.Total, bl.Customer, bl.Inherited, bl.Shared, bl.TailoredOut, bl.TailoredIn,
    N'[]', DATEADD(DAY, -85, @now), @actor
FROM bl
WHERE NOT EXISTS (SELECT 1 FROM ControlBaselines cb WHERE cb.RegisteredSystemId = bl.SystemId);

-- ============================================================================
-- 5. RMF Role assignments — fill any missing roles for tiers 3..5
-- ============================================================================
;WITH role(SystemId, RmfRole, UserId, UserDisplayName) AS (
    SELECT v.SystemId, r.RmfRole, r.UserId, r.UserDisplayName
    FROM (VALUES (@ts2),(@en),(@ee)) AS v(SystemId)
    CROSS JOIN (VALUES
        (N'AuthorizingOfficial', @aoUser,    @aoName),
        (N'Issm',                @issmUser,  @issmName),
        (N'Isso',                @scaUser,   @scaName),
        (N'SystemOwner',         @ownerUser, @ownerName),
        (N'Sca',                 @scaUser,   @scaName)
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
    WHERE ra.RegisteredSystemId = role.SystemId AND ra.RmfRole = role.RmfRole AND ra.IsActive = 1
);

-- ============================================================================
-- 6. Authorization-boundary primary flag + Eagle Nest boundary
-- ============================================================================
INSERT INTO AuthorizationBoundaryDefinitions (Id, RegisteredSystemId, Name, BoundaryType, Description, IsPrimary, CreatedAt, CreatedBy)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'boundary:' + @en) AS UNIQUEIDENTIFIER)),
    @en, N'Eagle Nest — Production', N'Logical',
    N'Analytics back-end supporting Eagle Eye ISR fusion.',
    1, DATEADD(DAY, -85, @now), @actor
WHERE NOT EXISTS (
    SELECT 1 FROM AuthorizationBoundaryDefinitions abd
    WHERE abd.RegisteredSystemId = @en AND abd.IsPrimary = 1
);

-- Ensure each pre-existing system has exactly one IsPrimary boundary.
UPDATE AuthorizationBoundaryDefinitions
   SET IsPrimary = 1
 WHERE RegisteredSystemId IN (@ts, @ts2, @ee)
   AND IsPrimary = 0
   AND Id = (
        SELECT TOP 1 Id FROM AuthorizationBoundaryDefinitions abd2
         WHERE abd2.RegisteredSystemId = AuthorizationBoundaryDefinitions.RegisteredSystemId
         ORDER BY abd2.CreatedAt
   );

-- ============================================================================
-- 7. Multiple Assessments per non-Prepare system
--    Test System 2: 2  | Eagle Nest: 3  | Eagle Eye: 4
-- ============================================================================
;WITH asm(SystemId, Slug, Score, Total, Passed, Failed, NotAssessed, Baseline, AssessedAt, ScanType, Summary) AS (
    -- Test System 2 — improving through Implement phase
    SELECT @ts2, N'ts2-a-1', 31.0, 287,  89, 162, 36, N'Moderate', DATEADD(DAY, -30, @now), N'Quick',         N'Initial baseline gap-scan run by SCA — early implementation.'
    UNION ALL SELECT @ts2, N'ts2-a-2', 47.0, 287, 135, 122, 30, N'Moderate', DATEADD(DAY,  -3, @now), N'Comprehensive', N'Mid-implementation re-scan: substantial progress on AC, IA, SC families.'

    -- Eagle Nest — initial → impl-progress → independent SCA
    UNION ALL SELECT @en,  N'en-a-1', 41.0, 287, 117, 145, 25, N'Moderate', DATEADD(DAY, -75, @now), N'Quick',         N'Initial gap-assessment ahead of impl phase.'
    UNION ALL SELECT @en,  N'en-a-2', 65.0, 287, 187,  74, 26, N'Moderate', DATEADD(DAY, -30, @now), N'Comprehensive', N'Implementation-phase scan: major remediation cycle complete.'
    UNION ALL SELECT @en,  N'en-a-3', 78.0, 287, 224,  47, 16, N'Moderate', DATEADD(DAY, -10, @now), N'Comprehensive', N'Independent SCA assessment: ready for AO review.'

    -- Eagle Eye — initial → SCA → ATO grant baseline → 2 quarterly ConMon
    UNION ALL SELECT @ee,  N'ee-a-1', 71.0, 378, 268, 87,  23, N'High',     DATEADD(DAY,-260, @now), N'Comprehensive', N'Pre-SCA gap analysis.'
    UNION ALL SELECT @ee,  N'ee-a-2', 88.0, 378, 333, 30,  15, N'High',     DATEADD(DAY,-220, @now), N'Comprehensive', N'Independent SCA — feeds AO authorization decision.'
    UNION ALL SELECT @ee,  N'ee-a-3', 90.5, 378, 342, 24,  12, N'High',     DATEADD(DAY,-130, @now), N'Comprehensive', N'Q1 continuous monitoring assessment.'
    UNION ALL SELECT @ee,  N'ee-a-4', 92.0, 378, 348, 22,   8, N'High',     DATEADD(DAY,  -5, @now), N'Comprehensive', N'Q2 continuous monitoring assessment.'
)
INSERT INTO Assessments (
    Id, SubscriptionId, Framework, Baseline, ScanType, Status, InitiatedBy,
    AssessedAt, CompletedAt, ProgressMessage, ComplianceScore, TotalControls,
    PassedControls, FailedControls, NotAssessedControls, ControlFamilyResults,
    ExecutiveSummary, RiskProfile, EnvironmentName, SubscriptionIds,
    ScanPillarResults, RegisteredSystemId)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:' + asm.Slug) AS UNIQUEIDENTIFIER)),
    N'00000000-0000-0000-0000-000000000000', N'NIST 800-53 Rev 5', asm.Baseline, asm.ScanType,
    2 /* Completed */, @actor,
    asm.AssessedAt, DATEADD(MINUTE, 14, asm.AssessedAt),
    N'Completed', asm.Score, asm.Total, asm.Passed, asm.Failed, asm.NotAssessed,
    N'[]', asm.Summary, NULL,
    N'AzureCommercial', N'[]', N'{}', asm.SystemId
FROM asm
WHERE NOT EXISTS (
    SELECT 1 FROM Assessments a WHERE a.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:' + asm.Slug) AS UNIQUEIDENTIFIER))
);

-- ============================================================================
-- 8. Findings tied to assessments (rich, varied, full-scope)
-- ============================================================================
DECLARE @as_ts2_1 NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:ts2-a-1') AS UNIQUEIDENTIFIER));
DECLARE @as_ts2_2 NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:ts2-a-2') AS UNIQUEIDENTIFIER));
DECLARE @as_en_1  NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:en-a-1')  AS UNIQUEIDENTIFIER));
DECLARE @as_en_2  NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:en-a-2')  AS UNIQUEIDENTIFIER));
DECLARE @as_en_3  NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:en-a-3')  AS UNIQUEIDENTIFIER));
DECLARE @as_ee_1  NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:ee-a-1')  AS UNIQUEIDENTIFIER));
DECLARE @as_ee_2  NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:ee-a-2')  AS UNIQUEIDENTIFIER));
DECLARE @as_ee_3  NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:ee-a-3')  AS UNIQUEIDENTIFIER));
DECLARE @as_ee_4  NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:ee-a-4')  AS UNIQUEIDENTIFIER));

;WITH f(AssessmentId, Slug, ControlId, ControlFamily, Title, Severity, Status, CatSeverity, ResourceId, ResourceType, RemediationGuidance, Source, AutoRemediable) AS (
    -- ───── Test System 2 / assessment 1 (initial baseline) — 8 findings ─────
    SELECT @as_ts2_1, N'ts2-1-a',  N'AC-2',  N'AC', N'Privileged role assignments lack lifecycle review',                  1, 0, N'CatI',   N'/sub/ts2/aad', N'Microsoft.Authorization/roleAssignments', N'Implement quarterly access review with ad-hoc removal.',  N'AzurePolicy',  0
    UNION ALL SELECT @as_ts2_1, N'ts2-1-b',  N'IA-2',  N'IA', N'MFA not enforced for cloud admins (3 users)',                       0, 0, N'CatI',   N'/sub/ts2/aad', N'Microsoft.Authorization/roleAssignments', N'Enable Conditional Access policy MFA-Admins.',             N'AzurePolicy',  1
    UNION ALL SELECT @as_ts2_1, N'ts2-1-c',  N'SC-7',  N'SC', N'NSG allows 0.0.0.0/0 inbound on 22 in dev',                          1, 0, N'CatI',   N'/sub/ts2/dev/nsg', N'Microsoft.Network/networkSecurityGroups', N'Restrict SSH ingress to bastion subnet.',              N'AzurePolicy',  1
    UNION ALL SELECT @as_ts2_1, N'ts2-1-d',  N'AU-6',  N'AU', N'Sentinel ingestion gap > 6h on Mondays',                            1, 0, N'CatII',  N'/sub/ts2/sentinel', N'Microsoft.OperationalInsights/workspaces', N'Investigate connector throttling.',                  N'AzurePolicy',  0
    UNION ALL SELECT @as_ts2_1, N'ts2-1-e',  N'CM-6',  N'CM', N'Configuration drift on web tier (4 VMs)',                           2, 0, N'CatII',  N'/sub/ts2/web', N'Microsoft.Compute/virtualMachines',     N'Apply Azure Policy DSC baseline.',                       N'AzurePolicy',  1
    UNION ALL SELECT @as_ts2_1, N'ts2-1-f',  N'CP-9',  N'CP', N'Backup retention < required 90 days',                              2, 0, N'CatII',  N'/sub/ts2/backup', N'Microsoft.RecoveryServices/vaults',  N'Set retention to 90 days.',                              N'AzurePolicy',  1
    UNION ALL SELECT @as_ts2_1, N'ts2-1-g',  N'AT-2',  N'AT', N'Annual security awareness training overdue (5 users)',             3, 0, N'CatIII', N'/sub/ts2/training', N'AtoCopilot/Training',             N'Send reminders / escalate to managers.',                  N'Manual',       0
    UNION ALL SELECT @as_ts2_1, N'ts2-1-h',  N'PL-2',  N'PL', N'SSP control narrative missing for 12 controls',                    3, 0, N'CatIII', N'/sub/ts2/ssp', N'AtoCopilot/SspSection',                N'Use AI-narrative agent to draft.',                        N'Manual',       0

    -- ───── Test System 2 / assessment 2 (mid-impl) — 10 findings (mix open/in-progress/remediated) ─────
    UNION ALL SELECT @as_ts2_2, N'ts2-2-a',  N'AC-2',  N'AC', N'Privileged role assignments lack lifecycle review',                  1, 1, N'CatI',   N'/sub/ts2/aad', N'Microsoft.Authorization/roleAssignments', N'Quarterly review automated; first cycle in flight.',  N'AzurePolicy',  0
    UNION ALL SELECT @as_ts2_2, N'ts2-2-b',  N'IA-2',  N'IA', N'MFA not enforced for break-glass account',                          0, 0, N'CatI',   N'/sub/ts2/aad', N'Microsoft.Authorization/roleAssignments', N'Enable PIM activation for break-glass.',              N'AzurePolicy',  1
    UNION ALL SELECT @as_ts2_2, N'ts2-2-c',  N'SC-7',  N'SC', N'NSG allows 0.0.0.0/0 inbound on 22 in dev',                          1, 2, N'CatI',   N'/sub/ts2/dev/nsg', N'Microsoft.Network/networkSecurityGroups', N'Remediated by NSG-baseline policy.',                N'AzurePolicy',  1
    UNION ALL SELECT @as_ts2_2, N'ts2-2-d',  N'AU-6',  N'AU', N'Sentinel ingestion gap > 6h on Mondays',                            1, 2, N'CatII',  N'/sub/ts2/sentinel', N'Microsoft.OperationalInsights/workspaces', N'Connector replaced; gap closed.',                    N'AzurePolicy',  0
    UNION ALL SELECT @as_ts2_2, N'ts2-2-e',  N'CM-6',  N'CM', N'Configuration drift on web tier (2 VMs remaining)',                 2, 1, N'CatII',  N'/sub/ts2/web', N'Microsoft.Compute/virtualMachines',     N'Two VMs pending reboot window.',                         N'AzurePolicy',  1
    UNION ALL SELECT @as_ts2_2, N'ts2-2-f',  N'CP-9',  N'CP', N'Backup retention < required 90 days',                              2, 0, N'CatII',  N'/sub/ts2/backup', N'Microsoft.RecoveryServices/vaults',  N'Policy assigned but not yet applied.',                   N'AzurePolicy',  1
    UNION ALL SELECT @as_ts2_2, N'ts2-2-g',  N'SI-2',  N'SI', N'Critical patches > 30 days old on 5 VMs',                          1, 0, N'CatII',  N'/sub/ts2/app', N'Microsoft.Compute/virtualMachines',     N'Patch via Update Manager urgently.',                     N'Defender',     1
    UNION ALL SELECT @as_ts2_2, N'ts2-2-h',  N'AC-6',  N'AC', N'Owner role assigned to 14 users (target ≤ 6)',                     2, 1, N'CatII',  N'/sub/ts2',     N'Microsoft.Authorization/roleAssignments',N'Right-size cycle in progress.',                          N'AzurePolicy',  0
    UNION ALL SELECT @as_ts2_2, N'ts2-2-i',  N'AT-2',  N'AT', N'Annual training overdue (1 user remaining)',                       3, 1, N'CatIII', N'/sub/ts2/training', N'AtoCopilot/Training',             N'Final reminder sent.',                                    N'Manual',       0
    UNION ALL SELECT @as_ts2_2, N'ts2-2-j',  N'PL-2',  N'PL', N'SSP narratives missing for 4 controls',                            3, 1, N'CatIII', N'/sub/ts2/ssp', N'AtoCopilot/SspSection',                N'AI agent drafted; awaiting ISSM review.',                 N'Manual',       0

    -- ───── Eagle Nest / assessment 1 (initial gap) — 9 findings, all Open ─────
    UNION ALL SELECT @as_en_1,  N'en-1-a', N'AC-2',  N'AC', N'Service account credentials hard-coded in 2 ARM templates',         0, 0, N'CatI',   N'/sub/en/iac',         N'Microsoft.Resources/templateSpecs',     N'Move to Key Vault references.',                            N'Manual',       0
    UNION ALL SELECT @as_en_1,  N'en-1-b', N'AU-9',  N'AU', N'Sentinel workspace lacks delete-protection lock',                   1, 0, N'CatI',   N'/sub/en/sentinel',    N'Microsoft.OperationalInsights/workspaces', N'Apply CanNotDelete lock.',                              N'AzurePolicy',  1
    UNION ALL SELECT @as_en_1,  N'en-1-c', N'AC-6',  N'AC', N'JIT VM access not configured for analytics nodes',                  2, 0, N'CatII',  N'/sub/en/vm',          N'Microsoft.Compute/virtualMachines',     N'Enable Defender JIT VM.',                                 N'Defender',     1
    UNION ALL SELECT @as_en_1,  N'en-1-d', N'CM-6',  N'CM', N'Drift on 3 analytics nodes (chrony, sshd, audit.rules)',           2, 0, N'CatII',  N'/sub/en/vm',          N'Microsoft.Compute/virtualMachines',     N'DSC re-baseline.',                                        N'AzurePolicy',  1
    UNION ALL SELECT @as_en_1,  N'en-1-e', N'SI-4',  N'SI', N'EDR sensor offline > 24h on 1 node',                                2, 0, N'CatII',  N'/sub/en/vm',          N'Microsoft.Compute/virtualMachines',     N'Reinstall agent.',                                        N'Defender',     1
    UNION ALL SELECT @as_en_1,  N'en-1-f', N'CP-9',  N'CP', N'Geo-redundant copy lag > 4h',                                       2, 0, N'CatII',  N'/sub/en/backup',      N'Microsoft.RecoveryServices/vaults',     N'Switch to RA-GRS.',                                       N'AzurePolicy',  0
    UNION ALL SELECT @as_en_1,  N'en-1-g', N'SC-7',  N'SC', N'East-west traffic not micro-segmented in 1 vnet',                   2, 0, N'CatII',  N'/sub/en/network',     N'Microsoft.Network/virtualNetworks',     N'Apply NSG rules per-subnet.',                             N'AzurePolicy',  1
    UNION ALL SELECT @as_en_1,  N'en-1-h', N'PL-2',  N'PL', N'SSP narrative outdated for SC-13',                                  3, 0, N'CatIII', N'/sub/en/ssp',         N'AtoCopilot/SspSection',                 N'Refresh narrative for SC-13.',                            N'Manual',       0
    UNION ALL SELECT @as_en_1,  N'en-1-i', N'IA-5',  N'IA', N'4 service principal secrets aged > 90 days',                        2, 0, N'CatII',  N'/sub/en/sps',         N'Microsoft.AzureActiveDirectory/applications', N'Rotate secrets, configure expiry alerts.',           N'AzurePolicy',  1

    -- ───── Eagle Nest / assessment 2 (mid-impl) — 7 findings, mostly InProgress/Remediated ─────
    UNION ALL SELECT @as_en_2,  N'en-2-a', N'AC-2',  N'AC', N'Service account credentials hard-coded',                            0, 2, N'CatI',   N'/sub/en/iac',         N'Microsoft.Resources/templateSpecs',     N'Migrated to Key Vault references.',                       N'Manual',       0
    UNION ALL SELECT @as_en_2,  N'en-2-b', N'AU-9',  N'AU', N'Sentinel workspace lacks delete-protection lock',                   1, 2, N'CatI',   N'/sub/en/sentinel',    N'Microsoft.OperationalInsights/workspaces', N'CanNotDelete lock applied.',                            N'AzurePolicy',  1
    UNION ALL SELECT @as_en_2,  N'en-2-c', N'AC-6',  N'AC', N'JIT VM access not configured for analytics nodes',                  2, 2, N'CatII',  N'/sub/en/vm',          N'Microsoft.Compute/virtualMachines',     N'JIT enabled cluster-wide.',                               N'Defender',     1
    UNION ALL SELECT @as_en_2,  N'en-2-d', N'CM-6',  N'CM', N'Drift on 1 analytics node (sshd)',                                  2, 1, N'CatII',  N'/sub/en/vm',          N'Microsoft.Compute/virtualMachines',     N'DSC re-baseline pending reboot window.',                  N'AzurePolicy',  1
    UNION ALL SELECT @as_en_2,  N'en-2-e', N'IA-5',  N'IA', N'2 service principal secrets aged > 60 days',                        2, 1, N'CatII',  N'/sub/en/sps',         N'Microsoft.AzureActiveDirectory/applications', N'Rotation in flight.',                                 N'AzurePolicy',  1
    UNION ALL SELECT @as_en_2,  N'en-2-f', N'CP-9',  N'CP', N'Geo-redundant copy lag > 2h',                                       2, 1, N'CatII',  N'/sub/en/backup',      N'Microsoft.RecoveryServices/vaults',     N'RA-GRS migration scheduled.',                             N'AzurePolicy',  0
    UNION ALL SELECT @as_en_2,  N'en-2-g', N'PL-2',  N'PL', N'SSP narrative outdated for SC-13',                                  3, 1, N'CatIII', N'/sub/en/ssp',         N'AtoCopilot/SspSection',                 N'AI agent drafted; ISSM review.',                          N'Manual',       0

    -- ───── Eagle Nest / assessment 3 (independent SCA) — 8 findings (current state) ─────
    UNION ALL SELECT @as_en_3,  N'en-3-a', N'AU-9',  N'AU', N'Workspace lock validated; one secondary workspace missing',         1, 0, N'CatI',   N'/sub/en/sentinel-2',  N'Microsoft.OperationalInsights/workspaces', N'Apply lock to secondary.',                              N'AzurePolicy',  1
    UNION ALL SELECT @as_en_3,  N'en-3-b', N'AC-6',  N'AC', N'JIT enabled but session length > 8h on 2 VMs',                      2, 0, N'CatII',  N'/sub/en/vm',          N'Microsoft.Compute/virtualMachines',     N'Cap JIT session at 4h.',                                  N'Defender',     1
    UNION ALL SELECT @as_en_3,  N'en-3-c', N'CM-6',  N'CM', N'Drift on 2 analytics nodes (chrony, sshd)',                         2, 0, N'CatII',  N'/sub/en/vm',          N'Microsoft.Compute/virtualMachines',     N'DSC re-baseline.',                                        N'AzurePolicy',  1
    UNION ALL SELECT @as_en_3,  N'en-3-d', N'SI-4',  N'SI', N'EDR sensor offline > 24h on 1 node',                                2, 0, N'CatII',  N'/sub/en/vm',          N'Microsoft.Compute/virtualMachines',     N'Reinstall agent + alert on offline > 4h.',                N'Defender',     1
    UNION ALL SELECT @as_en_3,  N'en-3-e', N'CP-9',  N'CP', N'Geo-redundant copy lag > 4h',                                       2, 0, N'CatII',  N'/sub/en/backup',      N'Microsoft.RecoveryServices/vaults',     N'RA-GRS migration in flight.',                             N'AzurePolicy',  0
    UNION ALL SELECT @as_en_3,  N'en-3-f', N'PL-2',  N'PL', N'SSP narrative outdated for SC-13',                                  3, 0, N'CatIII', N'/sub/en/ssp',         N'AtoCopilot/SspSection',                 N'Refresh narrative.',                                      N'Manual',       0
    UNION ALL SELECT @as_en_3,  N'en-3-g', N'AT-3',  N'AT', N'Role-based training delta for 1 ISSO contractor',                   3, 0, N'CatIII', N'/sub/en/training',    N'AtoCopilot/Training',                   N'Schedule contractor training.',                           N'Manual',       0
    UNION ALL SELECT @as_en_3,  N'en-3-h', N'CA-7',  N'CA', N'ConMon frequency drift (RA-5 monthly→quarterly)',                   3, 0, N'CatIII', N'/sub/en/conmon',      N'AtoCopilot/ConMon',                     N'Restore monthly cadence.',                                N'Manual',       0

    -- ───── Eagle Eye / assessment 1 (pre-SCA gap) — 12 findings ─────
    UNION ALL SELECT @as_ee_1,  N'ee-1-a', N'AC-2',  N'AC', N'Privileged accounts lack lifecycle review',                          1, 0, N'CatI',   N'/sub/ee/aad',         N'Microsoft.Authorization/roleAssignments', N'Implement PAM lifecycle.',                              N'AzurePolicy',  0
    UNION ALL SELECT @as_ee_1,  N'ee-1-b', N'IA-2',  N'IA', N'MFA bypass detected for 1 admin (CA exclusion)',                    0, 0, N'CatI',   N'/sub/ee/aad',         N'Microsoft.Authorization/roleAssignments', N'Remove CA exclusion.',                                  N'AzurePolicy',  1
    UNION ALL SELECT @as_ee_1,  N'ee-1-c', N'SC-12', N'SC', N'Key Vault key rotation policy missing on 4 keys',                    1, 0, N'CatI',   N'/sub/ee/kv',          N'Microsoft.KeyVault/vaults',             N'Apply rotation policy.',                                  N'AzurePolicy',  1
    UNION ALL SELECT @as_ee_1,  N'ee-1-d', N'AU-6',  N'AU', N'Sentinel automation rules not yet active for high-sev alerts',      1, 0, N'CatII',  N'/sub/ee/sentinel',    N'Microsoft.OperationalInsights/workspaces', N'Enable automation rules.',                              N'AzurePolicy',  1
    UNION ALL SELECT @as_ee_1,  N'ee-1-e', N'CM-2',  N'CM', N'Baseline config not version-pinned in 6 IaC modules',                2, 0, N'CatII',  N'/sub/ee/iac',         N'Microsoft.Resources/templateSpecs',     N'Pin module versions.',                                    N'Manual',       0
    UNION ALL SELECT @as_ee_1,  N'ee-1-f', N'CP-4',  N'CP', N'Last DR table-top > 9 months ago',                                  2, 0, N'CatII',  N'/sub/ee/cp',          N'AtoCopilot/ContingencyPlan',            N'Schedule table-top.',                                     N'Manual',       0
    UNION ALL SELECT @as_ee_1,  N'ee-1-g', N'IR-4',  N'IR', N'Tabletop exercise overdue',                                          2, 0, N'CatII',  N'/sub/ee/ir',          N'AtoCopilot/IncidentResponse',           N'Schedule + capture lessons learned.',                     N'Manual',       0
    UNION ALL SELECT @as_ee_1,  N'ee-1-h', N'AC-6',  N'AC', N'Owner role assigned to 8 users (target ≤ 4)',                       2, 0, N'CatII',  N'/sub/ee',             N'Microsoft.Authorization/roleAssignments', N'Right-size to 4.',                                      N'AzurePolicy',  0
    UNION ALL SELECT @as_ee_1,  N'ee-1-i', N'PE-2',  N'PE', N'Physical access list not reviewed in 365 days',                     3, 0, N'CatIII', N'/sub/ee/admin',       N'Microsoft.Authorization/policyAssignments', N'Schedule review.',                                       N'Manual',       0
    UNION ALL SELECT @as_ee_1,  N'ee-1-j', N'AT-3',  N'AT', N'Role-based training overdue for 3 ISSOs',                            3, 0, N'CatIII', N'/sub/ee/training',    N'AtoCopilot/Training',                   N'Send reminders.',                                         N'Manual',       0
    UNION ALL SELECT @as_ee_1,  N'ee-1-k', N'PL-2',  N'PL', N'SSP narrative coverage at 78% (target 95%)',                        3, 0, N'CatIII', N'/sub/ee/ssp',         N'AtoCopilot/SspSection',                 N'AI agent draft sweep.',                                   N'Manual',       0
    UNION ALL SELECT @as_ee_1,  N'ee-1-l', N'MA-2',  N'MA', N'Maintenance log gaps in Q3',                                         3, 0, N'CatIII', N'/sub/ee/ops',         N'AtoCopilot/MaintenanceLog',             N'Backfill log entries.',                                   N'Manual',       0

    -- ───── Eagle Eye / assessment 2 (SCA — feeds ATO) — 9 findings, mostly remediated ─────
    UNION ALL SELECT @as_ee_2,  N'ee-2-a', N'AC-2',  N'AC', N'Privileged accounts lifecycle program operational',                  1, 2, N'CatI',   N'/sub/ee/aad',         N'Microsoft.Authorization/roleAssignments', N'PAM in production.',                                    N'AzurePolicy',  0
    UNION ALL SELECT @as_ee_2,  N'ee-2-b', N'IA-2',  N'IA', N'MFA enforced (CA exclusion removed)',                                0, 2, N'CatI',   N'/sub/ee/aad',         N'Microsoft.Authorization/roleAssignments', N'No exclusions.',                                        N'AzurePolicy',  1
    UNION ALL SELECT @as_ee_2,  N'ee-2-c', N'SC-12', N'SC', N'Key rotation policy applied on all keys',                            1, 2, N'CatI',   N'/sub/ee/kv',          N'Microsoft.KeyVault/vaults',             N'Auto-rotate at 90d.',                                     N'AzurePolicy',  1
    UNION ALL SELECT @as_ee_2,  N'ee-2-d', N'AU-6',  N'AU', N'Sentinel automation rules enabled for high-sev',                     1, 2, N'CatII',  N'/sub/ee/sentinel',    N'Microsoft.OperationalInsights/workspaces', N'Active.',                                              N'AzurePolicy',  1
    UNION ALL SELECT @as_ee_2,  N'ee-2-e', N'CM-2',  N'CM', N'IaC modules pinned (3 still floating)',                              2, 1, N'CatII',  N'/sub/ee/iac',         N'Microsoft.Resources/templateSpecs',     N'Pin remaining.',                                          N'Manual',       0
    UNION ALL SELECT @as_ee_2,  N'ee-2-f', N'AC-6',  N'AC', N'Owner role assigned to 5 users (target ≤ 4)',                        2, 1, N'CatII',  N'/sub/ee',             N'Microsoft.Authorization/roleAssignments', N'Trim to 4.',                                            N'AzurePolicy',  0
    UNION ALL SELECT @as_ee_2,  N'ee-2-g', N'CP-4',  N'CP', N'DR tabletop scheduled, not yet executed',                            2, 1, N'CatII',  N'/sub/ee/cp',          N'AtoCopilot/ContingencyPlan',            N'Execute on calendar.',                                    N'Manual',       0
    UNION ALL SELECT @as_ee_2,  N'ee-2-h', N'PL-2',  N'PL', N'SSP narrative coverage 92%',                                         3, 1, N'CatIII', N'/sub/ee/ssp',         N'AtoCopilot/SspSection',                 N'Final narrative pass.',                                   N'Manual',       0
    UNION ALL SELECT @as_ee_2,  N'ee-2-i', N'AT-3',  N'AT', N'Role-based training overdue for 1 ISSO',                             3, 1, N'CatIII', N'/sub/ee/training',    N'AtoCopilot/Training',                   N'Final reminder.',                                         N'Manual',       0

    -- ───── Eagle Eye / assessment 3 (Q1 ConMon) — 6 findings ─────
    UNION ALL SELECT @as_ee_3,  N'ee-3-a', N'CM-6',  N'CM', N'Drift on 1 jumpbox (Q1)',                                            2, 2, N'CatII',  N'/sub/ee/vm',          N'Microsoft.Compute/virtualMachines',     N'Re-baselined.',                                           N'AzurePolicy',  1
    UNION ALL SELECT @as_ee_3,  N'ee-3-b', N'AU-6',  N'AU', N'Audit review SLA missed for 2 days (Q1)',                            2, 2, N'CatII',  N'/sub/ee/sentinel',    N'Microsoft.OperationalInsights/workspaces', N'Process improvement applied.',                          N'AzurePolicy',  0
    UNION ALL SELECT @as_ee_3,  N'ee-3-c', N'IA-5',  N'IA', N'1 SP secret aged > 75d',                                             3, 2, N'CatIII', N'/sub/ee/sps',         N'Microsoft.AzureActiveDirectory/applications', N'Rotated.',                                              N'AzurePolicy',  1
    UNION ALL SELECT @as_ee_3,  N'ee-3-d', N'CA-7',  N'CA', N'ConMon dashboard lag of 4h (Q1)',                                    3, 2, N'CatIII', N'/sub/ee/conmon',      N'AtoCopilot/ConMon',                     N'Connector tuning.',                                       N'Manual',       0
    UNION ALL SELECT @as_ee_3,  N'ee-3-e', N'PL-2',  N'PL', N'SSP appendix needs minor update post-Foundry roll',                  3, 2, N'CatIII', N'/sub/ee/ssp',         N'AtoCopilot/SspSection',                 N'Done.',                                                   N'Manual',       0
    UNION ALL SELECT @as_ee_3,  N'ee-3-f', N'IR-5',  N'IR', N'Lessons-learned doc missing for FY-Q4 incident',                     3, 2, N'CatIII', N'/sub/ee/ir',          N'AtoCopilot/IncidentResponse',           N'Written.',                                                N'Manual',       0

    -- ───── Eagle Eye / assessment 4 (Q2 ConMon — current) — 7 findings ─────
    UNION ALL SELECT @as_ee_4,  N'ee-4-a', N'CM-6',  N'CM', N'Configuration drift on 1 jumpbox',                                   2, 0, N'CatII',  N'/sub/ee/vm',          N'Microsoft.Compute/virtualMachines',     N'Re-baseline scheduled.',                                  N'AzurePolicy',  1
    UNION ALL SELECT @as_ee_4,  N'ee-4-b', N'AU-6',  N'AU', N'Audit review SLA missed for 1 day in last 30',                       2, 0, N'CatII',  N'/sub/ee/sentinel',    N'Microsoft.OperationalInsights/workspaces', N'Cross-train backups.',                                  N'AzurePolicy',  0
    UNION ALL SELECT @as_ee_4,  N'ee-4-c', N'AT-3',  N'AT', N'1 user training overdue by 3 days',                                  3, 0, N'CatIII', N'/sub/ee/training',    N'AtoCopilot/Training',                   N'Reminder sent.',                                          N'Manual',       0
    UNION ALL SELECT @as_ee_4,  N'ee-4-d', N'IA-5',  N'IA', N'1 SP secret aged 75 days (rotation cadence drift)',                  3, 0, N'CatIII', N'/sub/ee/sps',         N'Microsoft.AzureActiveDirectory/applications', N'Schedule rotation.',                                    N'AzurePolicy',  1
    UNION ALL SELECT @as_ee_4,  N'ee-4-e', N'CA-7',  N'CA', N'ConMon dashboard lag of 2h noted',                                   3, 0, N'CatIII', N'/sub/ee/conmon',      N'AtoCopilot/ConMon',                     N'Tune connector.',                                         N'Manual',       0
    UNION ALL SELECT @as_ee_4,  N'ee-4-f', N'CP-4',  N'CP', N'Annual DR exercise scheduled — not yet executed',                    3, 0, N'CatIII', N'/sub/ee/cp',          N'AtoCopilot/ContingencyPlan',            N'Calendar event in 2 weeks.',                              N'Manual',       0
    UNION ALL SELECT @as_ee_4,  N'ee-4-g', N'PL-2',  N'PL', N'SSP appendix needs minor update post-Foundry roll',                  3, 0, N'CatIII', N'/sub/ee/ssp',         N'AtoCopilot/SspSection',                 N'Patch in next narrative cycle.',                          N'Manual',       0
)
INSERT INTO Findings (
    Id, ControlId, ControlFamily, Title, Description, Severity, Status,
    ResourceId, ResourceType, RemediationGuidance, DiscoveredAt, AutoRemediable,
    Source, ScanSource, RemediationType, RiskLevel, AssessmentId,
    ControlTitle, ControlDescription, StigFinding, RemediationTrackingStatus, CatSeverity)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'finding:' + f.Slug) AS UNIQUEIDENTIFIER)),
    f.ControlId, f.ControlFamily, f.Title,
    CONCAT(N'Discovered during assessment. ', f.Title, N'.'),
    f.Severity, f.Status,
    f.ResourceId, f.ResourceType,
    f.RemediationGuidance,
    DATEADD(DAY, -CAST(ABS(CHECKSUM(HASHBYTES('MD5', f.Slug))) % 60 AS INT), @now),
    f.AutoRemediable, f.Source,
    CASE f.Source WHEN N'AzurePolicy' THEN 1 WHEN N'Defender' THEN 2 WHEN N'Manual' THEN 0 ELSE 0 END,
    CASE f.Source WHEN N'AzurePolicy' THEN 2 WHEN N'Defender' THEN 1 ELSE 4 END,
    CASE WHEN f.Severity <= 1 THEN 1 ELSE 0 END,
    f.AssessmentId,
    f.ControlId, N'See NIST 800-53 Rev 5.', 0, f.Status, f.CatSeverity
FROM f
WHERE NOT EXISTS (
    SELECT 1 FROM Findings existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'finding:' + f.Slug) AS UNIQUEIDENTIFIER))
);

-- ============================================================================
-- 9. POA&M items (open / risk-accepted / completed mix)
-- ============================================================================
;WITH p(SystemId, Slug, ControlId, CatSev, Status, Weakness, Source, ScheduledDays, Comments) AS (
    -- Test System 2: 5 (open + 1 completed + 1 risk-accepted)
    SELECT @ts2, N'ts2-poam-1', N'AC-2', N'CatI',   N'Ongoing',       N'Privileged role assignments lack lifecycle review (PAM rollout in flight).', N'SCA Assessment', 60,  N'PAM cycle 1 active.'
    UNION ALL SELECT @ts2, N'ts2-poam-2', N'IA-2', N'CatI',   N'Ongoing',       N'MFA not enforced for break-glass account.',                                                  N'SCA Assessment', -7,  N'Awaiting CR approval — overdue.'
    UNION ALL SELECT @ts2, N'ts2-poam-3', N'CP-9', N'CatII',  N'Ongoing',       N'Backup retention below 90-day requirement.',                                                  N'SCA Assessment', 90,  N'Policy assigned.'
    UNION ALL SELECT @ts2, N'ts2-poam-4', N'AT-2', N'CatIII', N'Completed',     N'Annual security awareness training overdue (training delivered).',                            N'Manual',         -10, N'Closed after training cycle complete.'
    UNION ALL SELECT @ts2, N'ts2-poam-5', N'CM-2', N'CatII',  N'RiskAccepted',  N'Legacy module versioning — accepted for 6 months.',                                          N'Manual',         180, N'AO-approved 6-month risk acceptance.'

    -- Eagle Nest: 7 (mix of open + completed; some overdue)
    UNION ALL SELECT @en, N'en-poam-1', N'AU-9', N'CatI',   N'Ongoing',   N'Sentinel workspace lacks delete-protection lock on secondary.',                  N'SCA Assessment', 14,  N'Pending policy assignment.'
    UNION ALL SELECT @en, N'en-poam-2', N'AC-6', N'CatII',  N'Ongoing',   N'JIT session length > 8h on 2 VMs.',                                              N'SCA Assessment', 30,  N'Updating template.'
    UNION ALL SELECT @en, N'en-poam-3', N'CM-6', N'CatII',  N'Ongoing',   N'Drift on 2 analytics nodes.',                                                    N'SCA Assessment', 21,  N'Re-baseline scheduled.'
    UNION ALL SELECT @en, N'en-poam-4', N'SI-4', N'CatII',  N'Ongoing',   N'EDR sensor offline > 24h on 1 node.',                                            N'SCA Assessment', -5,  N'Agent reinstall required — overdue.'
    UNION ALL SELECT @en, N'en-poam-5', N'IA-5', N'CatII',  N'Ongoing',   N'2 service principal secrets aged > 60 days.',                                    N'Manual',         45,  N'Rotation sprint planned.'
    UNION ALL SELECT @en, N'en-poam-6', N'PL-2', N'CatIII', N'Ongoing',   N'SSP narrative outdated for SC-13.',                                              N'Manual',         60,  N'AI draft pending review.'
    UNION ALL SELECT @en, N'en-poam-7', N'AC-2', N'CatI',   N'Completed', N'Service account credentials hard-coded — migrated to Key Vault.',                N'SCA Assessment', -25, N'Closed after Key Vault migration.'

    -- Eagle Eye: 4 (steady state)
    UNION ALL SELECT @ee, N'ee-poam-1', N'IA-5', N'CatIII', N'Ongoing',       N'1 SP secret aged 75 days.',                            N'ConMon',  15,  N'Rotation queued.'
    UNION ALL SELECT @ee, N'ee-poam-2', N'CP-4', N'CatIII', N'Ongoing',       N'Annual DR exercise scheduled but not yet executed.',   N'ConMon',  30,  N'On the calendar.'
    UNION ALL SELECT @ee, N'ee-poam-3', N'CM-6', N'CatII',  N'Ongoing',       N'Configuration drift on 1 jumpbox.',                    N'ConMon',  20,  N'DSC redeploy.'
    UNION ALL SELECT @ee, N'ee-poam-4', N'PL-2', N'CatIII', N'RiskAccepted',  N'Minor SSP appendix update — accepted until next cycle.',N'Manual', 90,  N'AO accepted minor delta.'
)
INSERT INTO PoamItems (
    Id, RegisteredSystemId, Weakness, WeaknessSource, SecurityControlNumber, CatSeverity,
    PointOfContact, PocEmail, ScheduledCompletionDate, Status, Comments,
    CreatedAt, CreatedBy, RowVersion)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'poam:' + p.Slug) AS UNIQUEIDENTIFIER)),
    p.SystemId, p.Weakness, p.Source, p.ControlId, p.CatSev,
    @scaName, @scaUser,
    DATEADD(DAY, p.ScheduledDays, @now), p.Status, p.Comments,
    DATEADD(DAY, -45, @now), @actor, NEWID()
FROM p
WHERE NOT EXISTS (
    SELECT 1 FROM PoamItems existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'poam:' + p.Slug) AS UNIQUEIDENTIFIER))
);

-- Set ActualCompletionDate on completed POA&Ms
UPDATE PoamItems
   SET ActualCompletionDate = DATEADD(DAY, -10, @now), ModifiedAt = @now, ModifiedBy = @actor
 WHERE Status = N'Completed' AND ActualCompletionDate IS NULL
   AND Id IN (
        CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'poam:ts2-poam-4') AS UNIQUEIDENTIFIER)),
        CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'poam:en-poam-7')  AS UNIQUEIDENTIFIER))
   );

-- ============================================================================
-- 10. Remediation Boards (one per non-Prepare system)
-- ============================================================================
;WITH b(SystemId, Slug, BoardName, Owner) AS (
    SELECT @ts2, N'ts2-board', N'Test System 2 — Implementation Board', @ownerName
    UNION ALL SELECT @en,  N'en-board',  N'Eagle Nest — Assessment Board',         @ownerName
    UNION ALL SELECT @ee,  N'ee-board',  N'Eagle Eye — ConMon Board',              @ownerName
)
INSERT INTO RemediationBoards (Id, Name, SubscriptionId, AssessmentId, Owner, CreatedAt, UpdatedAt, IsArchived, NextTaskNumber, RowVersion)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:' + b.Slug) AS UNIQUEIDENTIFIER)),
    b.BoardName, b.SystemId, NULL, b.Owner,
    DATEADD(DAY, -60, @now), @now, 0, 100, NEWID()
FROM b
WHERE NOT EXISTS (SELECT 1 FROM RemediationBoards rb WHERE rb.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:' + b.Slug) AS UNIQUEIDENTIFIER)));

-- Fix any prior runs where SubscriptionId was seeded as a placeholder (the API filters tasks by Board.SubscriptionId == systemId)
UPDATE rb
SET    rb.SubscriptionId = b.SystemId
FROM   RemediationBoards rb
JOIN   (
    SELECT @ts2 AS SystemId, N'ts2-board' AS Slug
    UNION ALL SELECT @en,  N'en-board'
    UNION ALL SELECT @ee,  N'ee-board'
) b ON rb.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:' + b.Slug) AS UNIQUEIDENTIFIER))
WHERE  rb.SubscriptionId <> b.SystemId;

-- ============================================================================
-- 11. Remediation Tasks linked to findings (mixed status across kanban columns)
-- ============================================================================
DECLARE @bd_ts2 NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ts2-board') AS UNIQUEIDENTIFIER));
DECLARE @bd_en  NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:en-board')  AS UNIQUEIDENTIFIER));
DECLARE @bd_ee  NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ee-board')  AS UNIQUEIDENTIFIER));

;WITH t(BoardId, FindingSlug, TaskNumber, Title, Severity, Status, Assignee, AssigneeName, DueOffsetDays) AS (
    -- Test System 2 board (mix across columns)
    SELECT @bd_ts2, N'ts2-2-a', N'TS2-001', N'Quarterly access review for privileged accounts',  1, 2 /* InProgress */, @scaUser, @scaName, 30
    UNION ALL SELECT @bd_ts2, N'ts2-2-b', N'TS2-002', N'Enable PIM activation for break-glass account',     0, 1 /* ToDo */,       @scaUser, @scaName, 14
    UNION ALL SELECT @bd_ts2, N'ts2-2-e', N'TS2-003', N'Reboot remaining 2 web VMs to apply DSC baseline',   2, 4 /* Blocked */,    @scaUser, @scaName, 21
    UNION ALL SELECT @bd_ts2, N'ts2-2-f', N'TS2-004', N'Enforce 90-day backup retention',                    2, 1 /* ToDo */,       @scaUser, @scaName, 30
    UNION ALL SELECT @bd_ts2, N'ts2-2-g', N'TS2-005', N'Patch 5 VMs with critical updates > 30d old',        1, 2 /* InProgress */, @scaUser, @scaName, 7
    UNION ALL SELECT @bd_ts2, N'ts2-2-h', N'TS2-006', N'Right-size Owner role assignments',                  2, 3 /* InReview */,   @issmUser,@issmName,15
    UNION ALL SELECT @bd_ts2, N'ts2-2-c', N'TS2-007', N'Validate NSG baseline policy fix',                   1, 5 /* Done */,       @scaUser, @scaName, -3
    UNION ALL SELECT @bd_ts2, N'ts2-2-d', N'TS2-008', N'Sentinel ingestion gap fix verified',                1, 5 /* Done */,       @scaUser, @scaName, -5

    -- Eagle Nest board
    UNION ALL SELECT @bd_en,  N'en-3-a', N'EN-001',  N'Apply CanNotDelete lock to secondary Sentinel workspace', 1, 1, @scaUser, @scaName, 14
    UNION ALL SELECT @bd_en,  N'en-3-b', N'EN-002',  N'Cap JIT VM session length to 4h',                    2, 2, @scaUser, @scaName, 21
    UNION ALL SELECT @bd_en,  N'en-3-c', N'EN-003',  N'DSC re-baseline 2 analytics nodes',                  2, 0 /* Backlog */,    @scaUser, @scaName, 30
    UNION ALL SELECT @bd_en,  N'en-3-d', N'EN-004',  N'Re-install EDR agent + alert on offline > 4h',       2, 4 /* Blocked */,    @scaUser, @scaName, -2
    UNION ALL SELECT @bd_en,  N'en-3-e', N'EN-005',  N'Migrate backup vault to RA-GRS',                     2, 2, @scaUser, @scaName, 30
    UNION ALL SELECT @bd_en,  N'en-3-f', N'EN-006',  N'Refresh SSP narrative for SC-13',                    3, 3 /* InReview */,   @issmUser,@issmName,21
    UNION ALL SELECT @bd_en,  N'en-3-g', N'EN-007',  N'Schedule contractor role-based training',            3, 1, @issmUser,@issmName,14
    UNION ALL SELECT @bd_en,  N'en-3-h', N'EN-008',  N'Restore monthly RA-5 cadence',                       3, 0, @scaUser, @scaName, 30
    UNION ALL SELECT @bd_en,  N'en-2-a', N'EN-009',  N'Validated: Key Vault migration for service creds',   1, 5, @scaUser, @scaName, -10
    UNION ALL SELECT @bd_en,  N'en-2-c', N'EN-010',  N'Validated: JIT enabled cluster-wide',                2, 5, @scaUser, @scaName, -5

    -- Eagle Eye board (ConMon-style — small steady-state queue)
    UNION ALL SELECT @bd_ee,  N'ee-4-a', N'EE-001',  N'Re-baseline jumpbox config drift',                   2, 2, @scaUser, @scaName, 14
    UNION ALL SELECT @bd_ee,  N'ee-4-b', N'EE-002',  N'Cross-train audit reviewers (avoid SLA miss)',       2, 1, @issmUser,@issmName,30
    UNION ALL SELECT @bd_ee,  N'ee-4-d', N'EE-003',  N'Rotate 1 service principal secret (75d)',            3, 1, @scaUser, @scaName, 7
    UNION ALL SELECT @bd_ee,  N'ee-4-f', N'EE-004',  N'Execute annual DR table-top',                        3, 1, @ownerUser,@ownerName,21
    UNION ALL SELECT @bd_ee,  N'ee-3-a', N'EE-005',  N'Validated: jumpbox re-baseline (Q1)',                2, 5, @scaUser, @scaName, -30
    UNION ALL SELECT @bd_ee,  N'ee-3-b', N'EE-006',  N'Validated: audit review SLA process improvement',    2, 5, @issmUser,@issmName,-30
    UNION ALL SELECT @bd_ee,  N'ee-3-d', N'EE-007',  N'Validated: ConMon connector tuning (Q1)',            3, 5, @scaUser, @scaName, -30
)
INSERT INTO RemediationTasks (
    Id, TaskNumber, BoardId, Title, Description, ControlId, ControlFamily,
    Severity, Status, AssigneeId, AssigneeName, DueDate, CreatedAt, UpdatedAt,
    AffectedResources, FindingId, CreatedBy, RowVersion)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'task:' + t.TaskNumber) AS UNIQUEIDENTIFIER)),
    t.TaskNumber, t.BoardId, t.Title,
    CONCAT(N'Auto-generated remediation task linked to finding ', t.FindingSlug, N'.'),
    f.ControlId, f.ControlFamily, t.Severity, t.Status,
    t.Assignee, t.AssigneeName,
    DATEADD(DAY, t.DueOffsetDays, @now),
    DATEADD(DAY, -30, @now), @now,
    CONCAT(N'["', f.ResourceId, N'"]'),
    f.Id, @actor, NEWID()
FROM t
JOIN Findings f ON f.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'finding:' + t.FindingSlug) AS UNIQUEIDENTIFIER))
WHERE NOT EXISTS (
    SELECT 1 FROM RemediationTasks rt WHERE rt.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'task:' + t.TaskNumber) AS UNIQUEIDENTIFIER))
);

-- ============================================================================
-- 12. Active ATO for Eagle Eye (tier 5)
-- ============================================================================
INSERT INTO AuthorizationDecisions (
    Id, RegisteredSystemId, DecisionType, DecisionDate, ExpirationDate,
    TermsAndConditions, ResidualRiskLevel, ResidualRiskJustification,
    ComplianceScoreAtDecision, FindingsAtDecision, IssuedBy, IssuedByName, IsActive)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'authz:' + @ee + N':v1') AS UNIQUEIDENTIFIER)),
    @ee, N'Ato', DATEADD(DAY, -210, @now), DATEADD(DAY, 155, @now),
    N'ATO subject to: (1) annual reauthorization review, (2) ConMon report submitted monthly, (3) all CAT I findings remediated within 30 days of discovery.',
    N'Low', N'Residual risk evaluated as Low. Compensating controls in place for all open CAT II/III findings; no open CAT I findings.',
    88.0,
    N'{"catI":0,"catII":3,"catIII":6}',
    @aoUser, @aoName, 1
WHERE NOT EXISTS (
    SELECT 1 FROM AuthorizationDecisions ad WHERE ad.RegisteredSystemId = @ee AND ad.IsActive = 1
);

-- ============================================================================
-- 13. ComplianceTrendSnapshots — last 90 days (per system, deterministic IDs)
-- ============================================================================
;WITH days AS (
    SELECT TOP (90) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) - 1 AS DayBack
    FROM sys.all_objects
)
INSERT INTO ComplianceTrendSnapshots (Id, RegisteredSystemId, CapturedAt, ComplianceScore, CatICount, CatIICount, CatIIICount, OpenPoamCount, OverduePoamCount, NarrativeCoverage, Source)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'trend:' + s.SystemId + N':' + CAST(d.DayBack AS NVARCHAR(10))) AS UNIQUEIDENTIFIER)),
    s.SystemId,
    DATEADD(DAY, -d.DayBack, @now),
    -- Score: nominal target minus a per-day slope (older days = lower scores → progress curve).
    -- Clamp at 0 via CASE so we never produce negative compliance scores.
    CASE
       WHEN s.SystemId = @ts  THEN 0.0
       WHEN s.SystemId = @ts2 THEN CASE WHEN 47.0 - (CAST(d.DayBack AS FLOAT) * 0.30) < 0 THEN 0.0 ELSE 47.0 - (CAST(d.DayBack AS FLOAT) * 0.30) END
       WHEN s.SystemId = @en  THEN CASE WHEN 78.0 - (CAST(d.DayBack AS FLOAT) * 0.20) < 0 THEN 0.0 ELSE 78.0 - (CAST(d.DayBack AS FLOAT) * 0.20) END
       WHEN s.SystemId = @ee  THEN CASE WHEN 92.0 - (CAST(d.DayBack AS FLOAT) * 0.04) < 0 THEN 0.0 ELSE 92.0 - (CAST(d.DayBack AS FLOAT) * 0.04) END
    END,
    -- CatI counts (older = more)
    CASE
       WHEN s.SystemId = @ts2 THEN CASE WHEN d.DayBack < 5 THEN 1 WHEN d.DayBack < 15 THEN 2 ELSE 3 END
       WHEN s.SystemId = @en  THEN CASE WHEN d.DayBack < 12 THEN 1 WHEN d.DayBack < 40 THEN 1 ELSE 2 END
       WHEN s.SystemId = @ee  THEN 0
       ELSE 0
    END,
    -- CatII counts
    CASE
       WHEN s.SystemId = @ts2 THEN 4 + (d.DayBack / 12)
       WHEN s.SystemId = @en  THEN 4 + (d.DayBack / 18)
       WHEN s.SystemId = @ee  THEN 2 + (d.DayBack / 60)
       ELSE 0
    END,
    -- CatIII counts
    CASE
       WHEN s.SystemId = @ts2 THEN 4 + (d.DayBack / 10)
       WHEN s.SystemId = @en  THEN 3 + (d.DayBack / 18)
       WHEN s.SystemId = @ee  THEN 4 + (d.DayBack / 30)
       ELSE 0
    END,
    -- OpenPoam
    CASE
       WHEN s.SystemId = @ts2 THEN 3 + (d.DayBack / 30)
       WHEN s.SystemId = @en  THEN 5 + (d.DayBack / 30)
       WHEN s.SystemId = @ee  THEN 3 + (d.DayBack / 45)
       ELSE 0
    END,
    -- OverduePoam
    CASE
       WHEN s.SystemId = @ts2 THEN CASE WHEN d.DayBack > 60 THEN 3 WHEN d.DayBack > 30 THEN 2 ELSE 1 END
       WHEN s.SystemId = @en  THEN CASE WHEN d.DayBack > 60 THEN 2 WHEN d.DayBack > 30 THEN 1 ELSE 1 END
       WHEN s.SystemId = @ee  THEN 0
       ELSE 0
    END,
    -- Narrative coverage % (improving over time)
    CASE
       WHEN s.SystemId = @ts2 THEN 30.0 + (CAST(89 - d.DayBack AS FLOAT) * 0.45)
       WHEN s.SystemId = @en  THEN 60.0 + (CAST(89 - d.DayBack AS FLOAT) * 0.30)
       WHEN s.SystemId = @ee  THEN 88.0 + (CAST(89 - d.DayBack AS FLOAT) * 0.08)
       ELSE 0
    END,
    N'Scheduled'
FROM (VALUES (@ts2),(@en),(@ee)) AS s(SystemId)
CROSS JOIN days d
WHERE NOT EXISTS (
    SELECT 1 FROM ComplianceTrendSnapshots existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'trend:' + s.SystemId + N':' + CAST(d.DayBack AS NVARCHAR(10))) AS UNIQUEIDENTIFIER))
);

-- ============================================================================
-- 14. DashboardActivities — rich timeline per system
-- ============================================================================
;WITH a(SystemId, Slug, EventType, OffsetDays, Actor, Summary, RelType) AS (
    -- Test System (Tier 1 — minimal)
    SELECT @ts, N'ts-a-1', N'WorkflowStarted', -3, @ownerUser, N'Engineer started Prepare-phase intake wizard for Test System.', N'Wizard'

    -- Test System 2 (Tier 3)
    UNION ALL SELECT @ts2, N'ts2-a-1', N'CategorizationCompleted', -45, @issmUser, N'FIPS 199 categorization completed: Moderate baseline.', N'SecurityCategorization'
    UNION ALL SELECT @ts2, N'ts2-a-2', N'BaselineSelected',         -38, @issmUser, N'Selected NIST 800-53 Rev 5 Moderate baseline (287 controls).', N'ControlBaseline'
    UNION ALL SELECT @ts2, N'ts2-a-3', N'RmfPhaseAdvanced',         -35, @issmUser, N'RMF phase advanced from Select to Implement.', N'RegisteredSystem'
    UNION ALL SELECT @ts2, N'ts2-a-4', N'AssessmentCompleted',      -30, @scaUser,  N'Initial gap-scan completed: 31.0% compliance, 8 findings.', N'ComplianceAssessment'
    UNION ALL SELECT @ts2, N'ts2-a-5', N'NarrativeUpdated',         -25, @issmUser, N'AI agent drafted narratives for AC, IA, SC families.', N'SspSection'
    UNION ALL SELECT @ts2, N'ts2-a-6', N'PoamCreated',              -20, @scaUser,  N'Bulk-created 5 POA&Ms from initial findings.', N'PoamItem'
    UNION ALL SELECT @ts2, N'ts2-a-7', N'AssessmentCompleted',       -3, @scaUser,  N'Mid-implementation re-scan: 47.0% compliance, 10 findings (3 remediated since baseline).', N'ComplianceAssessment'
    UNION ALL SELECT @ts2, N'ts2-a-8', N'PoamUpdated',               -2, @scaUser,  N'Closed POA&M for AT-2 after training cycle.', N'PoamItem'
    UNION ALL SELECT @ts2, N'ts2-a-9', N'NarrativeUpdated',          -1, @issmUser, N'Updated SSP narratives for AC-2, IA-2, SC-7.', N'SspSection'

    -- Eagle Nest (Tier 4)
    UNION ALL SELECT @en, N'en-a-1', N'CategorizationCompleted', -90, @issmUser, N'FIPS 199 categorization completed: Moderate.', N'SecurityCategorization'
    UNION ALL SELECT @en, N'en-a-2', N'BaselineSelected',         -85, @issmUser, N'Selected NIST 800-53 Rev 5 Moderate + CNSSI 1253 IL5 (287 controls).', N'ControlBaseline'
    UNION ALL SELECT @en, N'en-a-3', N'AssessmentCompleted',      -75, @scaUser,  N'Initial gap-assessment: 41.0% compliance, 9 findings.', N'ComplianceAssessment'
    UNION ALL SELECT @en, N'en-a-4', N'PoamCreated',              -70, @scaUser,  N'Bulk-created 7 POA&Ms.', N'PoamItem'
    UNION ALL SELECT @en, N'en-a-5', N'RmfPhaseAdvanced',         -65, @issmUser, N'RMF phase advanced from Implement to Assess.', N'RegisteredSystem'
    UNION ALL SELECT @en, N'en-a-6', N'AssessmentCompleted',      -30, @scaUser,  N'Implementation-phase scan: 65.0% compliance, 7 findings.', N'ComplianceAssessment'
    UNION ALL SELECT @en, N'en-a-7', N'PoamUpdated',              -25, @scaUser,  N'Closed POA&M for AC-2 (Key Vault migration).', N'PoamItem'
    UNION ALL SELECT @en, N'en-a-8', N'AssessmentCompleted',      -10, @scaUser,  N'Independent SCA assessment: 78.0% compliance, 8 findings.', N'ComplianceAssessment'
    UNION ALL SELECT @en, N'en-a-9', N'PackageSubmitted',          -2, @issmUser, N'Authorization package submitted to AO for review.', N'AuthorizationPackage'

    -- Eagle Eye (Tier 5)
    UNION ALL SELECT @ee, N'ee-a-1', N'CategorizationCompleted', -260, @issmUser, N'FIPS 199 categorization completed: High / NSS.', N'SecurityCategorization'
    UNION ALL SELECT @ee, N'ee-a-2', N'BaselineSelected',         -255, @issmUser, N'Selected NIST 800-53 Rev 5 High + CNSSI 1253 IL5 + ICD 503 (378 controls).', N'ControlBaseline'
    UNION ALL SELECT @ee, N'ee-a-3', N'AssessmentCompleted',      -260, @scaUser,  N'Pre-SCA gap analysis: 71.0% compliance, 12 findings.', N'ComplianceAssessment'
    UNION ALL SELECT @ee, N'ee-a-4', N'AssessmentCompleted',      -220, @scaUser,  N'Independent SCA assessment: 88.0% compliance, 9 findings.', N'ComplianceAssessment'
    UNION ALL SELECT @ee, N'ee-a-5', N'PoamCreated',              -215, @scaUser,  N'Bulk-created 4 ConMon POA&Ms.', N'PoamItem'
    UNION ALL SELECT @ee, N'ee-a-6', N'AuthorizationGranted',     -210, @aoUser,   N'AO Col. Robert Harris issued ATO valid for 365 days (residual risk: Low).', N'AuthorizationDecision'
    UNION ALL SELECT @ee, N'ee-a-7', N'RmfPhaseAdvanced',         -210, @aoUser,   N'RMF phase advanced from Authorize to Monitor.', N'RegisteredSystem'
    UNION ALL SELECT @ee, N'ee-a-8', N'ConMonReportSubmitted',    -180, @scaUser,  N'Monthly ConMon report submitted (M1).', N'ConMonReport'
    UNION ALL SELECT @ee, N'ee-a-9', N'ConMonReportSubmitted',    -150, @scaUser,  N'Monthly ConMon report submitted (M2).', N'ConMonReport'
    UNION ALL SELECT @ee, N'ee-a-10', N'AssessmentCompleted',     -130, @scaUser,  N'Q1 continuous monitoring assessment: 90.5% compliance, 6 findings.', N'ComplianceAssessment'
    UNION ALL SELECT @ee, N'ee-a-11', N'ConMonReportSubmitted',    -90, @scaUser,  N'Monthly ConMon report submitted (M3).', N'ConMonReport'
    UNION ALL SELECT @ee, N'ee-a-12', N'ConMonReportSubmitted',    -60, @scaUser,  N'Monthly ConMon report submitted (M4).', N'ConMonReport'
    UNION ALL SELECT @ee, N'ee-a-13', N'AssessmentCompleted',      -5,  @scaUser,  N'Q2 continuous monitoring assessment: 92.0% compliance, 7 findings.', N'ComplianceAssessment'
    UNION ALL SELECT @ee, N'ee-a-14', N'NarrativeUpdated',          -2,  @issmUser, N'Updated SSP narrative for IA-5 (rotation cadence drift).', N'SspSection'
    UNION ALL SELECT @ee, N'ee-a-15', N'PoamUpdated',               -1,  @scaUser,  N'Updated POA&M for SC-13 after key rotation policy applied.', N'PoamItem'
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

-- ============================================================================
-- 15. Test System (Tier 2 — Select) — categorize → select → initial baseline
--     gap scan + onboarding kanban. Idempotent via 'ts-*' slugs.
-- ============================================================================

-- 15.1 Security Categorization (Low) + Information Types
INSERT INTO SecurityCategorizations (Id, RegisteredSystemId, IsNationalSecuritySystem, Justification, CategorizedBy, CategorizedAt)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'cat:' + @ts) AS UNIQUEIDENTIFIER)),
    @ts, 0,
    N'FIPS 199 Low: development sandbox supporting non-CUI engineering workloads.',
    @issmName, DATEADD(DAY, -28, @now)
WHERE NOT EXISTS (SELECT 1 FROM SecurityCategorizations c WHERE c.RegisteredSystemId = @ts);

;WITH it_ts(Sp, Name, Cat, C, I, A) AS (
    SELECT N'C.2.1.1', N'IT Infrastructure Maintenance',  N'Information & Comms Mgmt',    N'Low', N'Low', N'Low'
    UNION ALL SELECT N'C.2.4.1', N'Software Development', N'Information & Comms Mgmt',    N'Low', N'Low', N'Low'
)
INSERT INTO InformationTypes (Id, SecurityCategorizationId, Sp80060Id, Name, Category, ConfidentialityImpact, IntegrityImpact, AvailabilityImpact, UsesProvisionalImpactLevels)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'it:' + @ts + N':' + it_ts.Sp) AS UNIQUEIDENTIFIER)),
    (SELECT TOP 1 Id FROM SecurityCategorizations WHERE RegisteredSystemId = @ts),
    it_ts.Sp, it_ts.Name, it_ts.Cat, it_ts.C, it_ts.I, it_ts.A, 1
FROM it_ts
WHERE EXISTS (SELECT 1 FROM SecurityCategorizations sc WHERE sc.RegisteredSystemId = @ts)
  AND NOT EXISTS (
    SELECT 1 FROM InformationTypes existing
    WHERE existing.SecurityCategorizationId = (SELECT TOP 1 Id FROM SecurityCategorizations WHERE RegisteredSystemId = @ts)
      AND existing.Sp80060Id = it_ts.Sp
);

-- 15.2 Control Baseline (Low — 149 controls per NIST 800-53B)
INSERT INTO ControlBaselines (Id, RegisteredSystemId, BaselineLevel, OverlayApplied, TotalControls, CustomerControls, InheritedControls, SharedControls, TailoredOutControls, TailoredInControls, ControlIds, CreatedAt, CreatedBy)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'baseline:' + @ts) AS UNIQUEIDENTIFIER)),
    @ts, N'Low', NULL, 149, 100, 35, 14, 0, 0,
    N'[]', DATEADD(DAY, -27, @now), @actor
WHERE NOT EXISTS (SELECT 1 FROM ControlBaselines cb WHERE cb.RegisteredSystemId = @ts);

-- 15.3 RMF Role Assignments (5 roles)
;WITH role_ts(RmfRole, UserId, UserDisplayName) AS (
    SELECT N'AuthorizingOfficial', @aoUser,    @aoName
    UNION ALL SELECT N'Issm',      @issmUser,  @issmName
    UNION ALL SELECT N'Isso',      @scaUser,   @scaName
    UNION ALL SELECT N'SystemOwner', @ownerUser, @ownerName
    UNION ALL SELECT N'Sca',       @scaUser,   @scaName
)
INSERT INTO RmfRoleAssignments (Id, RegisteredSystemId, RmfRole, UserId, UserDisplayName, AssignedAt, AssignedBy, IsActive)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'role:' + @ts + N':' + role_ts.RmfRole) AS UNIQUEIDENTIFIER)),
    @ts, role_ts.RmfRole, role_ts.UserId, role_ts.UserDisplayName,
    DATEADD(DAY, -30, @now), @actor, 1
FROM role_ts
WHERE NOT EXISTS (
    SELECT 1 FROM RmfRoleAssignments ra
    WHERE ra.RegisteredSystemId = @ts AND ra.RmfRole = role_ts.RmfRole AND ra.IsActive = 1
);

-- 15.4 Authorization Boundary
INSERT INTO AuthorizationBoundaryDefinitions (Id, RegisteredSystemId, Name, BoundaryType, Description, IsPrimary, CreatedAt, CreatedBy)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'boundary:' + @ts) AS UNIQUEIDENTIFIER)),
    @ts, N'Test System — Development', N'Logical',
    N'Engineering sandbox; no CUI / PII. Supports developer enablement workflows.',
    1, DATEADD(DAY, -27, @now), @actor
WHERE NOT EXISTS (
    SELECT 1 FROM AuthorizationBoundaryDefinitions abd
    WHERE abd.RegisteredSystemId = @ts AND abd.IsPrimary = 1
);

-- 15.5 Initial baseline-gap Assessment (Completed)
INSERT INTO Assessments (
    Id, SubscriptionId, Framework, Baseline, ScanType, Status, InitiatedBy,
    AssessedAt, CompletedAt, ProgressMessage, ComplianceScore, TotalControls,
    PassedControls, FailedControls, NotAssessedControls, ControlFamilyResults,
    ExecutiveSummary, RiskProfile, EnvironmentName, SubscriptionIds,
    ScanPillarResults, RegisteredSystemId)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:ts-a-1') AS UNIQUEIDENTIFIER)),
    N'00000000-0000-0000-0000-000000000000', N'NIST 800-53 Rev 5', N'Low', N'Comprehensive',
    2 /* Completed */, @actor,
    DATEADD(DAY, -25, @now), DATEADD(DAY, -25, @now),
    N'Completed', 22.0, 149, 33, 105, 11,
    N'[]',
    N'Initial Select-phase gap scan against NIST 800-53 Rev 5 Low baseline.',
    NULL,
    N'AzureCommercial', N'[]', N'{}', @ts
WHERE NOT EXISTS (
    SELECT 1 FROM Assessments a
    WHERE a.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:ts-a-1') AS UNIQUEIDENTIFIER))
);

-- 15.6 Findings (6 — initial baseline gaps, all Open)
DECLARE @as_ts_1 NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'assessment:ts-a-1') AS UNIQUEIDENTIFIER));
;WITH f_ts(Slug, ControlId, ControlFamily, Title, Severity, CatSeverity, ResourceId, ResourceType, RemediationGuidance, Source, AutoRemediable) AS (
    SELECT N'ts-1-a', N'AC-2',  N'AC', N'Local admin accounts still active on legacy server',                        0, N'CatI',   N'/sub/ts/legacy',     N'Microsoft.Compute/virtualMachines',     N'Decommission legacy server; remove local admins.', N'AzurePolicy', 0
    UNION ALL SELECT N'ts-1-b', N'IA-2',  N'IA', N'MFA not yet enforced for tenant admins',                                  1, N'CatI',   N'/sub/ts/aad',        N'Microsoft.Authorization/roleAssignments', N'Enable Conditional Access MFA for admin roles.', N'AzurePolicy', 1
    UNION ALL SELECT N'ts-1-c', N'SC-7',  N'SC', N'Default NSG allows broad inbound from VNet',                              2, N'CatII',  N'/sub/ts/network',    N'Microsoft.Network/networkSecurityGroups', N'Apply micro-segmentation NSG ruleset.', N'AzurePolicy', 1
    UNION ALL SELECT N'ts-1-d', N'AU-2',  N'AU', N'Audit logging not yet centralized in Sentinel workspace',                 2, N'CatII',  N'/sub/ts/diag',       N'Microsoft.OperationalInsights/workspaces', N'Configure diagnostic settings → Sentinel workspace.', N'AzurePolicy', 1
    UNION ALL SELECT N'ts-1-e', N'CM-6',  N'CM', N'Configuration baseline not yet defined for VM gold image',                3, N'CatIII', N'/sub/ts/images',     N'Microsoft.Compute/galleries',           N'Author DSC baseline + bake into shared image gallery.', N'Manual',     0
    UNION ALL SELECT N'ts-1-f', N'PL-2',  N'PL', N'SSP narrative not yet drafted (system in Select phase)',                  3, N'CatIII', N'/sub/ts/ssp',        N'AtoCopilot/SspSection',                 N'Use AI narrative agent to produce initial SSP draft.', N'Manual',     0
)
INSERT INTO Findings (
    Id, ControlId, ControlFamily, Title, Description, Severity, Status,
    ResourceId, ResourceType, RemediationGuidance, DiscoveredAt, AutoRemediable,
    Source, ScanSource, RemediationType, RiskLevel, AssessmentId,
    ControlTitle, ControlDescription, StigFinding, RemediationTrackingStatus, CatSeverity)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'finding:' + f_ts.Slug) AS UNIQUEIDENTIFIER)),
    f_ts.ControlId, f_ts.ControlFamily, f_ts.Title,
    CONCAT(N'Discovered during initial baseline gap-scan. ', f_ts.Title, N'.'),
    f_ts.Severity, 0 /* Open */,
    f_ts.ResourceId, f_ts.ResourceType, f_ts.RemediationGuidance,
    DATEADD(DAY, -25, @now), f_ts.AutoRemediable, f_ts.Source,
    CASE f_ts.Source WHEN N'AzurePolicy' THEN 1 WHEN N'Defender' THEN 2 ELSE 0 END,
    CASE f_ts.Source WHEN N'AzurePolicy' THEN 2 WHEN N'Defender' THEN 1 ELSE 4 END,
    CASE WHEN f_ts.Severity <= 1 THEN 1 ELSE 0 END,
    @as_ts_1,
    f_ts.ControlId, N'See NIST 800-53 Rev 5.', 0, 0, f_ts.CatSeverity
FROM f_ts
WHERE NOT EXISTS (
    SELECT 1 FROM Findings existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'finding:' + f_ts.Slug) AS UNIQUEIDENTIFIER))
);

-- 15.7 POA&Ms (3 — Ongoing, top weaknesses)
;WITH p_ts(Slug, ControlId, CatSev, Status, Weakness, Source, ScheduledDays, Comments) AS (
    SELECT N'ts-poam-1', N'AC-2', N'CatI',  N'Ongoing', N'Local admin accounts active on legacy server.',                  N'Initial Assessment', 60, N'Decommission roadmap drafted; awaiting CR.'
    UNION ALL SELECT N'ts-poam-2', N'IA-2', N'CatI',  N'Ongoing', N'Tenant admin MFA enforcement not yet rolled out.',     N'Initial Assessment', 30, N'CA policy authored; staging review pending.'
    UNION ALL SELECT N'ts-poam-3', N'SC-7', N'CatII', N'Ongoing', N'Default NSG allows broad inbound — segmentation TBD.', N'Initial Assessment', 45, N'Network design under review.'
)
INSERT INTO PoamItems (
    Id, RegisteredSystemId, Weakness, WeaknessSource, SecurityControlNumber, CatSeverity,
    PointOfContact, PocEmail, ScheduledCompletionDate, Status, Comments,
    CreatedAt, CreatedBy, RowVersion)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'poam:' + p_ts.Slug) AS UNIQUEIDENTIFIER)),
    @ts, p_ts.Weakness, p_ts.Source, p_ts.ControlId, p_ts.CatSev,
    @scaName, @scaUser,
    DATEADD(DAY, p_ts.ScheduledDays, @now), p_ts.Status, p_ts.Comments,
    DATEADD(DAY, -23, @now), @actor, NEWID()
FROM p_ts
WHERE NOT EXISTS (
    SELECT 1 FROM PoamItems existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'poam:' + p_ts.Slug) AS UNIQUEIDENTIFIER))
);

-- 15.8 Remediation Board
INSERT INTO RemediationBoards (Id, Name, SubscriptionId, AssessmentId, Owner, CreatedAt, UpdatedAt, IsArchived, NextTaskNumber, RowVersion)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ts-board') AS UNIQUEIDENTIFIER)),
    N'Test System — Onboarding Board', @ts, NULL, @ownerName,
    DATEADD(DAY, -25, @now), @now, 0, 100, NEWID()
WHERE NOT EXISTS (
    SELECT 1 FROM RemediationBoards rb
    WHERE rb.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ts-board') AS UNIQUEIDENTIFIER))
);

-- Heal SubscriptionId in case prior runs left a placeholder
UPDATE rb
SET    rb.SubscriptionId = @ts
FROM   RemediationBoards rb
WHERE  rb.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ts-board') AS UNIQUEIDENTIFIER))
  AND  rb.SubscriptionId <> @ts;

-- 15.9 Remediation Tasks (4 — Backlog/ToDo/InProgress)
DECLARE @bd_ts NVARCHAR(36) = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ts-board') AS UNIQUEIDENTIFIER));
;WITH t_ts(FindingSlug, TaskNumber, Title, Severity, Status, Assignee, AssigneeName, DueOffsetDays) AS (
    SELECT N'ts-1-a', N'TS-001', N'Decommission legacy server; remove local admin accounts',  0, 1 /* ToDo */,    @ownerUser, @ownerName, 30
    UNION ALL SELECT N'ts-1-b', N'TS-002', N'Roll out CA policy: MFA for all tenant admins',          1, 2 /* InProgress */, @scaUser,   @scaName,   14
    UNION ALL SELECT N'ts-1-c', N'TS-003', N'Apply baseline NSG rules to default vnets',              2, 0 /* Backlog */, @scaUser,   @scaName,   30
    UNION ALL SELECT N'ts-1-d', N'TS-004', N'Configure diagnostic settings → Sentinel workspace',     2, 1 /* ToDo */,    @scaUser,   @scaName,   21
    UNION ALL SELECT N'ts-1-e', N'TS-005', N'Define VM gold-image DSC baseline',                      2, 0 /* Backlog */, @ownerUser, @ownerName, 45
)
INSERT INTO RemediationTasks (
    Id, TaskNumber, BoardId, Title, Description, ControlId, ControlFamily,
    Severity, Status, AssigneeId, AssigneeName, DueDate, CreatedAt, UpdatedAt,
    AffectedResources, FindingId, CreatedBy, RowVersion)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'task:' + t_ts.TaskNumber) AS UNIQUEIDENTIFIER)),
    t_ts.TaskNumber, @bd_ts, t_ts.Title,
    CONCAT(N'Auto-generated remediation task linked to finding ', t_ts.FindingSlug, N'.'),
    f.ControlId, f.ControlFamily, t_ts.Severity, t_ts.Status,
    t_ts.Assignee, t_ts.AssigneeName,
    DATEADD(DAY, t_ts.DueOffsetDays, @now),
    DATEADD(DAY, -23, @now), @now,
    CONCAT(N'["', f.ResourceId, N'"]'),
    f.Id, @actor, NEWID()
FROM t_ts
JOIN Findings f ON f.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'finding:' + t_ts.FindingSlug) AS UNIQUEIDENTIFIER))
WHERE NOT EXISTS (
    SELECT 1 FROM RemediationTasks rt
    WHERE rt.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'task:' + t_ts.TaskNumber) AS UNIQUEIDENTIFIER))
);

-- 15.10 Compliance Trend Snapshots — 30 days, ramping 8% → 22%
;WITH days_ts AS (
    SELECT TOP (30) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) - 1 AS DayBack
    FROM sys.all_objects
)
INSERT INTO ComplianceTrendSnapshots (Id, RegisteredSystemId, CapturedAt, ComplianceScore, CatICount, CatIICount, CatIIICount, OpenPoamCount, OverduePoamCount, NarrativeCoverage, Source)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'trend:' + @ts + N':' + CAST(d.DayBack AS NVARCHAR(10))) AS UNIQUEIDENTIFIER)),
    @ts, DATEADD(DAY, -d.DayBack, @now),
    -- Score ramp: 22.0 today → 8.0 thirty days ago
    CASE WHEN 22.0 - (CAST(d.DayBack AS FLOAT) * 0.47) < 0 THEN 0.0 ELSE 22.0 - (CAST(d.DayBack AS FLOAT) * 0.47) END,
    2,                                          -- CatI count steady at 2
    CASE WHEN d.DayBack < 5 THEN 2 ELSE 1 END,  -- CatII grew during scan
    2,                                          -- CatIII steady
    CASE WHEN d.DayBack < 23 THEN 3 ELSE 0 END, -- POA&Ms created day -23
    0,                                          -- not overdue yet (new system)
    CASE WHEN 5.0 + (CAST(29 - d.DayBack AS FLOAT) * 0.4) > 100 THEN 100.0 ELSE 5.0 + (CAST(29 - d.DayBack AS FLOAT) * 0.4) END,
    N'Scheduled'
FROM days_ts d
WHERE NOT EXISTS (
    SELECT 1 FROM ComplianceTrendSnapshots existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'trend:' + @ts + N':' + CAST(d.DayBack AS NVARCHAR(10))) AS UNIQUEIDENTIFIER))
);

-- 15.11 Dashboard Activities — onboarding timeline (6)
;WITH a_ts(Slug, EventType, OffsetDays, Actor, Summary, RelType) AS (
    SELECT N'ts-a-2', N'CategorizationCompleted', -28, @issmUser, N'FIPS 199 categorization completed: Low impact.',                                          N'SecurityCategorization'
    UNION ALL SELECT N'ts-a-3', N'BaselineSelected',         -27, @issmUser, N'Selected NIST 800-53 Rev 5 Low baseline (149 controls).',                       N'ControlBaseline'
    UNION ALL SELECT N'ts-a-4', N'RmfPhaseAdvanced',         -27, @issmUser, N'RMF phase advanced from Prepare to Select.',                                    N'RegisteredSystem'
    UNION ALL SELECT N'ts-a-5', N'AssessmentCompleted',      -25, @scaUser,  N'Initial baseline gap-scan completed: 22.0% compliance, 6 findings.',            N'ComplianceAssessment'
    UNION ALL SELECT N'ts-a-6', N'PoamCreated',              -23, @scaUser,  N'Bulk-created 3 POA&Ms from initial findings.',                                   N'PoamItem'
    UNION ALL SELECT N'ts-a-7', N'NarrativeUpdated',         -10, @issmUser, N'AI narrative agent drafted SSP sections for AC and IA families.',                N'SspSection'
)
INSERT INTO DashboardActivities (Id, RegisteredSystemId, EventType, Timestamp, Actor, Summary, RelatedEntityType, RelatedEntityId)
SELECT
    CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'activity:' + a_ts.Slug) AS UNIQUEIDENTIFIER)),
    @ts, a_ts.EventType, DATEADD(DAY, a_ts.OffsetDays, @now), a_ts.Actor, a_ts.Summary, a_ts.RelType, NULL
FROM a_ts
WHERE NOT EXISTS (
    SELECT 1 FROM DashboardActivities existing
    WHERE existing.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'activity:' + a_ts.Slug) AS UNIQUEIDENTIFIER))
);

COMMIT TRANSACTION;

-- ============================================================================
-- Summary
-- ============================================================================
PRINT '─── Seed-azure-progress summary ───';
SELECT
    rs.Name,
    rs.CurrentRmfStep                                                AS Phase,
    rs.OperationalStatus                                             AS OpStatus,
    cb.BaselineLevel                                                 AS Baseline,
    (SELECT COUNT(*) FROM RmfRoleAssignments r WHERE r.RegisteredSystemId = rs.Id AND r.IsActive = 1) AS Roles,
    (SELECT COUNT(*) FROM Assessments a WHERE a.RegisteredSystemId = rs.Id) AS Assessments,
    (SELECT COUNT(*) FROM Findings f JOIN Assessments a ON f.AssessmentId = a.Id WHERE a.RegisteredSystemId = rs.Id) AS Findings,
    (SELECT COUNT(*) FROM PoamItems p WHERE p.RegisteredSystemId = rs.Id AND p.Status = N'Ongoing') AS OpenPoams,
    (SELECT COUNT(*) FROM RemediationTasks rt
       JOIN RemediationBoards rb ON rb.Id = rt.BoardId
      WHERE rb.Id IN (
            CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ts-board')  AS UNIQUEIDENTIFIER)),
            CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ts2-board') AS UNIQUEIDENTIFIER)),
            CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:en-board')  AS UNIQUEIDENTIFIER)),
            CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ee-board')  AS UNIQUEIDENTIFIER))
        )
        AND ((rs.Id = @ts  AND rb.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ts-board')  AS UNIQUEIDENTIFIER)))
          OR (rs.Id = @ts2 AND rb.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ts2-board') AS UNIQUEIDENTIFIER)))
          OR (rs.Id = @en  AND rb.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:en-board')  AS UNIQUEIDENTIFIER)))
          OR (rs.Id = @ee  AND rb.Id = CONVERT(NVARCHAR(36), CAST(HASHBYTES('MD5', N'board:ee-board')  AS UNIQUEIDENTIFIER))))
    ) AS RemTasks,
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
