-- ============================================================================
--  ATO Copilot — Flankspeed CSP-portfolio demo seed.
--  Target:  AtoCopilot (SQL Server in docker compose, container ato-copilot-sql)
--  Idempotent: DELETE-by-prefix then INSERT, so running this is the
--              authoritative source of truth for the Flankspeed demo rows.
--
--  Per-system coherent state (RMF phase ↔ compliance score ↔ ATO):
--
--    Coastal Watch (PEO-790 / Monitor)
--      • Mature operational baseline, full ATO valid 3 years.
--      • Assessment 92.5% — 0 findings, 0 POA&Ms, 0 deviations.
--
--    Eagle Eye (PEO-790 / Prepare)
--      • Brand-new system, just registered. NO Assessment, NO ATO,
--        NO findings, NO POA&Ms. Compliance score column reads as "—".
--
--    Eagle Nest (PMA 290 / Monitor)
--      • ATOwC valid 1 year — 2 conditions tracked as POA&Ms.
--      • Assessment 78% — 0 findings (conditions are closed via POA&Ms).
--
--    Phoenix Falcon (PMA 290 / Assess)
--      • IATT valid 90 days for SCA testing.
--      • Assessment 71% — 1 High finding, 1 POA&M tracking it.
--
--    Polar Bear (PMS 408 / Authorize)
--      • DATO — AO denied; system must not operate.
--      • Assessment 42% — 1 Critical + 1 High + 1 Medium finding,
--        2 POA&Ms, 1 Pending deviation.
--
--  Pre-requisite: scripts/reassign-flankspeed-systems.sql has moved the
--  5 demo RegisteredSystems onto the 3 mission-owner tenants. The
--  scripts/seed-flankspeed.sh wrapper runs both in the right order.
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
USE AtoCopilot;

DECLARE @PEO790 UNIQUEIDENTIFIER = '4A5E1C76-4743-48E8-9C19-DDAF27CE376F';
DECLARE @PMA290 UNIQUEIDENTIFIER = '62373322-306C-462C-A29D-41491287AD49';
DECLARE @PMS408 UNIQUEIDENTIFIER = '4A87B9F7-027C-445F-B3EE-6F3DC3D87F60';

DECLARE @Sys_Coastal   NVARCHAR(72) = '44a2f788-4808-4b88-bcf4-fea1d63779ab';
DECLARE @Sys_EagleEye  NVARCHAR(72) = 'c267a009-dd48-4555-a194-7edecbbb45b4';
DECLARE @Sys_EagleNest NVARCHAR(72) = '9b6bd346-d188-4d51-a1ae-7371f8b93607';
DECLARE @Sys_Phoenix   NVARCHAR(72) = '32ef294f-e725-4430-94cc-785a7b47398c';
DECLARE @Sys_Polar     NVARCHAR(72) = '939b29a5-3010-4dd1-8c2c-420e4d74eca3';

DECLARE @Now    DATETIME2 = SYSUTCDATETIME();
DECLARE @90d    DATETIME2 = DATEADD(DAY, 90, @Now);
DECLARE @180d   DATETIME2 = DATEADD(DAY, 180, @Now);
DECLARE @1y     DATETIME2 = DATEADD(YEAR, 1, @Now);
DECLARE @3y     DATETIME2 = DATEADD(YEAR, 3, @Now);
DECLARE @120dAgo DATETIME2 = DATEADD(DAY, -120, @Now);
DECLARE @60dAgo  DATETIME2 = DATEADD(DAY, -60, @Now);
DECLARE @45dAgo  DATETIME2 = DATEADD(DAY, -45, @Now);

-- Deterministic IDs (PKs) so re-runs land on the same rows ------------
DECLARE @Assess_Coastal   NVARCHAR(200) = 'flankspeed-seed-assess-coastal';
DECLARE @Assess_EagleNest NVARCHAR(200) = 'flankspeed-seed-assess-eagle-nest';
DECLARE @Assess_Phoenix   NVARCHAR(200) = 'flankspeed-seed-assess-phoenix';
DECLARE @Assess_Polar     NVARCHAR(200) = 'flankspeed-seed-assess-polar';

DECLARE @AD_Coastal    NVARCHAR(72) = 'fs-seed-ad-coastal';
DECLARE @AD_EagleNest  NVARCHAR(72) = 'fs-seed-ad-eagle-nest';
DECLARE @AD_Phoenix    NVARCHAR(72) = 'fs-seed-ad-phoenix';
DECLARE @AD_Polar      NVARCHAR(72) = 'fs-seed-ad-polar';

DECLARE @F_PHX_SC7 NVARCHAR(200) = 'flankspeed-seed-f-phx-sc7';
DECLARE @F_PB_IA2  NVARCHAR(200) = 'flankspeed-seed-f-pb-ia2';
DECLARE @F_PB_AU12 NVARCHAR(200) = 'flankspeed-seed-f-pb-au12';
DECLARE @F_PB_CM7  NVARCHAR(200) = 'flankspeed-seed-f-pb-cm7';

DECLARE @P_EN_AC2  NVARCHAR(72) = 'fs-seed-p-en-ac2';
DECLARE @P_EN_SI4  NVARCHAR(72) = 'fs-seed-p-en-si4';
DECLARE @P_PHX_SC7 NVARCHAR(72) = 'fs-seed-p-phx-sc7';
DECLARE @P_PB_IA2  NVARCHAR(72) = 'fs-seed-p-pb-ia2';
DECLARE @P_PB_CM7  NVARCHAR(72) = 'fs-seed-p-pb-cm7';
-- Coastal Watch closed/historical POA&Ms (remediated during the ATO).
DECLARE @P_CW_AC2  NVARCHAR(72) = 'fs-seed-p-cw-ac2';
DECLARE @P_CW_SC8  NVARCHAR(72) = 'fs-seed-p-cw-sc8';

DECLARE @D_PB_CM7  NVARCHAR(72) = 'fs-seed-d-pb-cm7';

BEGIN TRY
  BEGIN TRAN;

  -- ─── Cleanup (delete-then-insert pattern) ────────────────────────
  -- Wipe any prior Flankspeed seed rows (FK-safe order: children first).
  -- Anything tagged with our deterministic 'flankspeed-seed-' /
  -- 'fs-seed-' prefix is owned by this script; nothing else is touched.
  DELETE FROM dbo.PoamMilestones          WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.Deviations              WHERE Id LIKE 'fs-seed-%';
  -- RemediationTasks FK-reference PoamItems, so they must go first.
  DELETE FROM dbo.RemediationTasks        WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.RemediationBoards       WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.PoamItems               WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.Findings                WHERE Id LIKE 'flankspeed-seed-%';
  DELETE FROM dbo.AuthorizationDecisions  WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.Assessments             WHERE Id LIKE 'flankspeed-seed-%';

  -- ─── Step 8 prerequisite-artifact cleanup (children → parents) ───
  -- Every Step 8 row carries a deterministic 'fs-seed-' Id prefix, so
  -- prefix-scoped deletes are sufficient and never touch real tenant data.
  -- InformationTypes / ControlInheritances are deleted here (before the
  -- demo-system block wipes SecurityCategorizations / ControlBaselines) so
  -- the FK children are gone before their parents.
  -- ControlImplementations.ApprovedVersionId FK-references NarrativeVersions
  -- (circular). Break the link before deleting the narratives.
  UPDATE dbo.ControlImplementations SET ApprovedVersionId = NULL WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.NarrativeVersions        WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.ControlImplementations   WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.CapabilityControlMappings WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.SystemCapabilityLinks    WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.SecurityCapabilities     WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.ControlInheritances      WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.Evidence                 WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.Documents                WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.ConMonReports            WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.ConMonPlans              WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.InformationTypes         WHERE Id LIKE 'fs-seed-%';
  -- Prerequisite-data tables seeded in Step 7+ are scoped strictly to
  -- the 5 demo systems, so cleanup uses RegisteredSystemId — this also
  -- catches legacy rows left over from earlier seed iterations or from
  -- the reassign-systems cascade (which moved rows from the system
  -- tenant by FK but left orphan content like SecurityCategorizations
  -- with no seed prefix).
  DECLARE @DemoSystemIds TABLE (Id NVARCHAR(72) PRIMARY KEY);
  INSERT @DemoSystemIds VALUES
    (@Sys_Coastal), (@Sys_EagleEye), (@Sys_EagleNest), (@Sys_Phoenix), (@Sys_Polar);

  -- Profile-section child entities (UserCategories, DataTypeEntries, PpsEntries,
  -- LeveragedAuthorizations) FK-reference SystemProfileSections, so they must be
  -- deleted before their parent sections.
  DELETE FROM dbo.UserCategories          WHERE SystemProfileSectionId IN (SELECT Id FROM dbo.SystemProfileSections WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds));
  DELETE FROM dbo.DataTypeEntries         WHERE SystemProfileSectionId IN (SELECT Id FROM dbo.SystemProfileSections WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds));
  DELETE FROM dbo.PpsEntries              WHERE SystemProfileSectionId IN (SELECT Id FROM dbo.SystemProfileSections WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds));
  DELETE FROM dbo.LeveragedAuthorizations WHERE SystemProfileSectionId IN (SELECT Id FROM dbo.SystemProfileSections WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds));
  DELETE FROM dbo.SystemProfileSections      WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds);
  DELETE FROM dbo.ControlBaselines           WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds);
  DELETE FROM dbo.PrivacyThresholdAnalyses   WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds);
  -- Stale "you skipped this gate" reminders left over from UI force-advances.
  -- This seed reinstates each system's full prerequisite data set, so any
  -- prior deferred-prerequisite reminder is by definition no longer valid;
  -- clearing them keeps the To-do panel coherent with the seeded RMF phase.
  DELETE FROM dbo.DeferredPrerequisites      WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds);
  DELETE FROM dbo.SecurityCategorizations    WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds);
  DELETE FROM dbo.RmfRoleAssignments         WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds);
  DELETE FROM dbo.ComponentSystemAssignments WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds);
  DELETE FROM dbo.SystemComponents           WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds);
  DELETE FROM dbo.AuthorizationBoundaryDefinitions WHERE RegisteredSystemId IN (SELECT Id FROM @DemoSystemIds);

  -- ─── Step 1: align RegisteredSystems.CurrentRmfStep ──────────────
  -- RmfPhase enum: Prepare=0, Categorize=1, Select=2, Implement=3,
  --                Assess=4, Authorize=5, Monitor=6
  -- EF stores enums as string by configuration here, so we use names.
  UPDATE dbo.RegisteredSystems SET CurrentRmfStep = 'Monitor'   WHERE Id = @Sys_Coastal;
  UPDATE dbo.RegisteredSystems SET CurrentRmfStep = 'Prepare'   WHERE Id = @Sys_EagleEye;
  UPDATE dbo.RegisteredSystems SET CurrentRmfStep = 'Monitor'   WHERE Id = @Sys_EagleNest;
  UPDATE dbo.RegisteredSystems SET CurrentRmfStep = 'Assess'    WHERE Id = @Sys_Phoenix;
  UPDATE dbo.RegisteredSystems SET CurrentRmfStep = 'Authorize' WHERE Id = @Sys_Polar;

  -- Eagle Eye is brand-new (Prepare) — interconnection state stays NULL/0.
  -- All other systems are past Categorize; certify them as having no
  -- external interconnections so the prerequisite checklist is satisfied
  -- without needing SystemInterconnections rows.
  UPDATE dbo.RegisteredSystems
     SET HasNoExternalInterconnections = 1
   WHERE Id IN (@Sys_Coastal, @Sys_EagleNest, @Sys_Phoenix, @Sys_Polar);
  UPDATE dbo.RegisteredSystems
     SET HasNoExternalInterconnections = 0
   WHERE Id = @Sys_EagleEye;

  -- ─── Step 2: Assessments (one per system EXCEPT Eagle Eye) ───────
  -- Status int: 0=Pending, 1=InProgress, 2=Completed
  INSERT dbo.Assessments
    (Id, TenantId, SubscriptionId, Framework, Baseline, ScanType, Status,
     InitiatedBy, AssessedAt, CompletedAt, ProgressMessage, ComplianceScore,
     TotalControls, PassedControls, FailedControls, NotAssessedControls,
     ControlFamilyResults, ExecutiveSummary, SubscriptionIds, ScanPillarResults,
     RegisteredSystemId)
  VALUES
    -- Coastal Watch — Monitor, 92.5% mature
    (@Assess_Coastal, @PEO790, '00000000-0000-0000-0000-000000000aaa',
     'NIST_SP_800_53', 'Moderate', 'Comprehensive', 2,
     'system@spin-agent.local', @Now, @Now,
     'Continuous-monitoring assessment — baseline holds.', 92.5,
     300, 278, 2, 20, '[]',
     'Coastal Watch ATO renewed; residual risk LOW.', '[]', '{}', @Sys_Coastal),

    -- Eagle Nest — Monitor, 78% with conditions
    (@Assess_EagleNest, @PMA290, '00000000-0000-0000-0000-000000000bbb',
     'NIST_SP_800_53', 'Moderate', 'Comprehensive', 2,
     'system@spin-agent.local', @Now, @Now,
     'Post-AtoWithConditions assessment.', 78.0,
     300, 234, 0, 66, '[]',
     'Two conditions tracked as POA&Ms; residual risk MEDIUM.', '[]', '{}', @Sys_EagleNest),

    -- Phoenix Falcon — Assess phase, 71% IATT
    (@Assess_Phoenix, @PMA290, '00000000-0000-0000-0000-000000000ccc',
     'NIST_SP_800_53', 'Moderate', 'Comprehensive', 1,
     'sca.pma290@navy.mil', @Now, NULL,
     'SCA assessment in progress for IATT extension.', 71.0,
     300, 213, 1, 86, '[]',
     'IATT covers limited connectivity; one CAT II finding under remediation.',
     '[]', '{}', @Sys_Phoenix),

    -- Polar Bear — Authorize phase, 42% DATO
    (@Assess_Polar, @PMS408, '00000000-0000-0000-0000-000000000ddd',
     'NIST_SP_800_53', 'High', 'Comprehensive', 2,
     'sca.pms408@navy.mil', @Now, @Now,
     'Final assessment supporting authorization decision.', 42.0,
     320, 134, 3, 183, '[]',
     'DATO recommended — Critical IA and significant SC/AU/CM findings.',
     '[]', '{}', @Sys_Polar);

  PRINT 'Assessments inserted: 4';

  -- ─── Step 3: AuthorizationDecisions ──────────────────────────────
  INSERT dbo.AuthorizationDecisions
    (Id, TenantId, RegisteredSystemId, DecisionType, DecisionDate, ExpirationDate,
     ResidualRiskLevel, ResidualRiskJustification, ComplianceScoreAtDecision,
     FindingsAtDecision, IssuedBy, IssuedByName, IsActive)
  VALUES
    -- Coastal Watch — ATO valid 3 years
    (@AD_Coastal, @PEO790, @Sys_Coastal, 'Ato', @Now, @3y, 'Low',
     'Operational baseline meets all 800-53 moderate controls.', 92.5,
     '[]', 'ao.peo790@navy.mil', 'CAPT R. Halsey, AO PEO-790', 1),

    -- Eagle Nest — ATO with Conditions, valid 1 year
    (@AD_EagleNest, @PMA290, @Sys_EagleNest, 'AtoWithConditions', @Now, @1y, 'Medium',
     'Two CAT II conditions tracked as POA&Ms; quarterly review.', 78.0,
     '[]', 'ao.pma290@navy.mil', 'COL S. Nimitz, AO PMA 290', 1),

    -- Phoenix Falcon — IATT, valid 90 days for testing
    (@AD_Phoenix, @PMA290, @Sys_Phoenix, 'Iatt', @Now, @90d, 'Medium',
     'Testing phase only; limited connectivity, no production data.', 71.0,
     '[{"id":"f-phx-sc7","sev":"High"}]',
     'ao.pma290@navy.mil', 'COL S. Nimitz, AO PMA 290', 1),

    -- Polar Bear — DATO (denied)
    (@AD_Polar, @PMS408, @Sys_Polar, 'Dato', @Now, NULL, 'Critical',
     'Critical IA and SC findings; system must not operate until remediated.', 42.0,
     '[{"id":"f-pb-ia2","sev":"Critical"},{"id":"f-pb-au12","sev":"High"}]',
     'ao.pms408@navy.mil', 'RDML K. Mitscher, AO PMS 408', 1);

  PRINT 'AuthorizationDecisions inserted: 4';

  -- ─── Step 4: Findings ────────────────────────────────────────────
  -- Severity int: Critical=0 | High=1 | Medium=2 | Low=3 | Informational=4
  -- Status   int: Open=0 | InProgress=1
  -- ScanSource int: Resource=0 | Policy=1 | Defender=2
  INSERT dbo.Findings
    (Id, TenantId, ControlId, ControlFamily, Title, Description, Severity, Status,
     ResourceId, ResourceType, RemediationGuidance, DiscoveredAt,
     AutoRemediable, Source, ScanSource, RemediationType, RiskLevel,
     AssessmentId, ControlTitle, ControlDescription, StigFinding,
     RemediationTrackingStatus, CatSeverity)
  VALUES
    -- Phoenix Falcon: 1 High
    (@F_PHX_SC7, @PMA290, 'SC-7', 'SC', 'NSG allows inbound 3389 from 0.0.0.0/0',
     'Network security group attached to a test VM exposes RDP to the internet.',
     1, 0, '/subs/x/rg/phoenix/nsg-test-01', 'Microsoft.Network/networkSecurityGroups',
     'Restrict 3389 source to engineering bastion CIDR per SC-7.', @Now,
     1, 'Defender', 0, 0, 1, @Assess_Phoenix,
     'Boundary Protection', 'Monitor and control communications...', 0, 0, 'CatII'),

    -- Polar Bear: 1 Critical + 1 High + 1 Medium
    (@F_PB_IA2, @PMS408, 'IA-2', 'IA', 'MFA not enforced for global administrators',
     'Two global administrators have MFA registered but not enforced via Conditional Access.',
     0, 0, '/tenants/pms408/conditional-access/0', 'Microsoft.Graph/conditionalAccessPolicies',
     'Enforce MFA via CA policy targeting Directory Roles per IA-2(1).', @Now,
     0, 'Defender', 2, 0, 1, @Assess_Polar,
     'Identification and Authentication', 'Identify users and authenticate...', 0, 0, 'CatI'),

    (@F_PB_AU12, @PMS408, 'AU-12', 'AU', 'Diagnostic settings missing on Key Vault',
     'Critical Key Vault has no diagnostic settings; audit events not captured.',
     1, 0, '/subs/x/rg/polar/kv-prod-01', 'Microsoft.KeyVault/vaults',
     'Configure diagnostic settings to Log Analytics per AU-12.', @Now,
     1, 'Policy', 1, 0, 0, @Assess_Polar,
     'Audit Record Generation', 'Generate audit records for events...', 0, 0, 'CatII'),

    (@F_PB_CM7, @PMS408, 'CM-7', 'CM', 'Function App allows all CORS origins',
     'Function App CORS configured with wildcard (*), exceeding least functionality.',
     2, 0, '/subs/x/rg/polar/func-api-01', 'Microsoft.Web/sites',
     'Restrict CORS to known FQDNs per CM-7.', @Now,
     1, 'Policy', 1, 0, 0, @Assess_Polar,
     'Least Functionality', 'Configure information system...', 0, 0, 'CatIII');

  PRINT 'Findings inserted: 4';

  -- ─── Step 5: PoamItems (all Status='Ongoing' → count as open) ────
  INSERT dbo.PoamItems
    (Id, TenantId, RegisteredSystemId, Weakness, WeaknessSource,
     SecurityControlNumber, CatSeverity, PointOfContact, PocEmail,
     ResourcesRequired, CostEstimate, ScheduledCompletionDate, Status,
     Comments, CreatedAt, RowVersion)
  VALUES
    -- Eagle Nest: 2 conditions tracked as POA&Ms (closing the AtoWithConditions)
    (@P_EN_AC2, @PMA290, @Sys_EagleNest,
     'Account inactivity disablement not automated',
     'AO Conditions Memo', 'AC-2', 'CatII',
     'ISSM PMA 290', 'issm.pma290@navy.mil',
     '40 engineer hours', 12000.00, @90d, 'Ongoing',
     'Closing AtoWithConditions item 1 of 2.', @Now, NEWID()),

    (@P_EN_SI4, @PMA290, @Sys_EagleNest,
     'Storage public network access enabled on logging account',
     'AO Conditions Memo', 'SI-4', 'CatII',
     'ISSM PMA 290', 'issm.pma290@navy.mil',
     '20 engineer hours', 6000.00, @180d, 'Ongoing',
     'Closing AtoWithConditions item 2 of 2.', @Now, NEWID()),

    -- Phoenix Falcon: 1 POA&M tracking the SC-7 finding
    (@P_PHX_SC7, @PMA290, @Sys_Phoenix,
     'NSG allows inbound 3389 from 0.0.0.0/0',
     'SCA Assessment (in-progress)', 'SC-7', 'CatII',
     'ISSO PMA 290', 'isso.pma290@navy.mil',
     '8 engineer hours', 2500.00, @90d, 'Ongoing',
     'Tracking the IATT residual finding; remediation underway.', @Now, NEWID()),

    -- Polar Bear: 2 POA&Ms tracking the Critical + Medium findings
    -- (the AU-12 High maps to a separate operations runbook, not a POA&M)
    (@P_PB_IA2, @PMS408, @Sys_Polar,
     'MFA not enforced for global administrators',
     'Defender for Cloud', 'IA-2', 'CatI',
     'ISSO PMS 408', 'isso.pms408@navy.mil',
     '8 engineer hours + identity governance license', 4500.00, @90d, 'Ongoing',
     'CA policy drafted, pending change advisory board approval.', @Now, NEWID()),

    (@P_PB_CM7, @PMS408, @Sys_Polar,
     'Function App allows all CORS origins',
     'Azure Policy', 'CM-7', 'CatIII',
     'ISSO PMS 408', 'isso.pms408@navy.mil',
     '4 engineer hours', 1500.00, @180d, 'Ongoing',
     'Coordinating with downstream consumers on allow-list.', @Now, NEWID());

  PRINT 'PoamItems inserted: 5';

  -- Coastal Watch (Monitor / full ATO): two historical POA&Ms remediated
  -- and closed during the assessment — proof of a working ConMon lifecycle.
  INSERT dbo.PoamItems
    (Id, TenantId, RegisteredSystemId, Weakness, WeaknessSource,
     SecurityControlNumber, CatSeverity, PointOfContact, PocEmail,
     ResourcesRequired, CostEstimate, ScheduledCompletionDate, Status,
     ActualCompletionDate, Comments, CreatedAt, RowVersion)
  VALUES
    (@P_CW_AC2, @PEO790, @Sys_Coastal,
     'Privileged accounts lacked quarterly recertification',
     'Initial SCA Assessment', 'AC-2', 'CatII',
     'ISSO PEO 790', 'isso.coastal@navy.mil',
     '16 engineer hours', 4000.00, @60dAgo, 'Completed',
     @45dAgo, 'Closed: automated quarterly access reviews deployed; verified at 0 stale accounts.', @120dAgo, NEWID()),

    (@P_CW_SC8, @PEO790, @Sys_Coastal,
     'Transmission confidentiality not enforced end-to-end',
     'Initial SCA Assessment', 'SC-8', 'CatII',
     'ISSO PEO 790', 'isso.coastal@navy.mil',
     '24 engineer hours', 6000.00, @60dAgo, 'Completed',
     @60dAgo, 'Closed: TLS 1.2+ enforced on all endpoints; HSTS enabled; retest passed.', @120dAgo, NEWID());

  PRINT 'PoamItems (Coastal closed) inserted: 2';

  -- ─── Step 6: Deviations ──────────────────────────────────────────
  INSERT dbo.Deviations
    (Id, TenantId, RegisteredSystemId, DeviationType, Status, ControlId, CatSeverity,
     Justification, CompensatingControls, EvidenceReferences, ExpirationDate,
     ReviewCycle, RequestedBy, RequestedAt, CreatedAt)
  VALUES
    (@D_PB_CM7, @PMS408, @Sys_Polar,
     'RiskAcceptance', 'Pending', 'CM-7', 'CatIII',
     'Wildcard CORS retained on read-only public preview endpoint pending API gateway rollout.',
     'WAF rules restrict request methods; endpoint serves cached static content only.',
     '[]',
     @180d, '180d', 'isso.pms408@navy.mil', @Now, @Now);

  PRINT 'Deviations inserted: 1';

  -- ─── Step 7: Prerequisite data for systems past Prepare ──────────
  -- Eagle Eye stays empty — it's intentionally a brand-new system.
  -- Coastal / Eagle Nest / Phoenix Falcon / Polar Bear each get the
  -- minimum prerequisite data the dashboard's Phase Readiness panel
  -- checks for (RMF roles, boundary + components, FIPS 199, PTA,
  -- system profile sections, control baseline) so the dashboard story
  -- matches what an ATO'd system would actually look like.

  -- ─── Step 7a: RmfRoleAssignments ─────────────────────────────────
  -- Each mature system gets AO + ISSM + ISSO + SCA. Polar Bear adds
  -- a SystemOwner since it's stuck in Authorize. Roles are stored as
  -- string-serialized enum names per RmfRole enum.
  INSERT dbo.RmfRoleAssignments
    (Id, TenantId, RegisteredSystemId, RmfRole, UserId, UserDisplayName,
     AssignedAt, AssignedBy, IsActive)
  VALUES
    -- Coastal Watch (PEO-790)
    ('fs-seed-rra-coastal-ao',   @PEO790, @Sys_Coastal, 'AuthorizingOfficial', 'ao.peo790@navy.mil',     'CAPT R. Halsey, AO PEO-790',     @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-coastal-issm', @PEO790, @Sys_Coastal, 'Issm',                'issm.peo790@navy.mil',   'LCDR M. Chen, ISSM PEO-790',     @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-coastal-isso', @PEO790, @Sys_Coastal, 'Isso',                'isso.coastal@navy.mil',  'LT J. Park, ISSO Coastal Watch', @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-coastal-sca',  @PEO790, @Sys_Coastal, 'Sca',                 'sca.peo790@navy.mil',    'Mr. D. Rivera, SCA PEO-790',     @Now, 'system@spin-agent.local', 1),

    -- Eagle Nest (PMA 290)
    ('fs-seed-rra-en-ao',   @PMA290, @Sys_EagleNest, 'AuthorizingOfficial', 'ao.pma290@navy.mil',     'COL S. Nimitz, AO PMA 290',         @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-en-issm', @PMA290, @Sys_EagleNest, 'Issm',                'issm.pma290@navy.mil',   'LCDR K. Patel, ISSM PMA 290',       @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-en-isso', @PMA290, @Sys_EagleNest, 'Isso',                'isso.eaglenest@navy.mil','LT A. Singh, ISSO Eagle Nest',      @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-en-sca',  @PMA290, @Sys_EagleNest, 'Sca',                 'sca.pma290@navy.mil',    'Ms. R. Goldberg, SCA PMA 290',      @Now, 'system@spin-agent.local', 1),

    -- Phoenix Falcon (PMA 290)
    ('fs-seed-rra-phx-ao',   @PMA290, @Sys_Phoenix, 'AuthorizingOfficial', 'ao.pma290@navy.mil',      'COL S. Nimitz, AO PMA 290',          @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-phx-issm', @PMA290, @Sys_Phoenix, 'Issm',                'issm.pma290@navy.mil',    'LCDR K. Patel, ISSM PMA 290',        @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-phx-isso', @PMA290, @Sys_Phoenix, 'Isso',                'isso.phoenix@navy.mil',   'LT B. Torres, ISSO Phoenix Falcon',  @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-phx-sca',  @PMA290, @Sys_Phoenix, 'Sca',                 'sca.pma290@navy.mil',     'Ms. R. Goldberg, SCA PMA 290',       @Now, 'system@spin-agent.local', 1),

    -- Polar Bear (PMS 408)
    ('fs-seed-rra-pb-ao',   @PMS408, @Sys_Polar, 'AuthorizingOfficial', 'ao.pms408@navy.mil',     'RDML K. Mitscher, AO PMS 408',           @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-pb-issm', @PMS408, @Sys_Polar, 'Issm',                'issm.pms408@navy.mil',   'CDR L. Vasquez, ISSM PMS 408',           @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-pb-isso', @PMS408, @Sys_Polar, 'Isso',                'isso.polar@navy.mil',    'LT C. Owens, ISSO Polar Bear',           @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-pb-sca',  @PMS408, @Sys_Polar, 'Sca',                 'sca.pms408@navy.mil',    'Mr. T. Bradley, SCA PMS 408',            @Now, 'system@spin-agent.local', 1),
    ('fs-seed-rra-pb-so',   @PMS408, @Sys_Polar, 'SystemOwner',         'so.polar@navy.mil',      'Mr. H. Adams, System Owner Polar Bear',  @Now, 'system@spin-agent.local', 1);

  PRINT 'RmfRoleAssignments inserted: 17';

  -- ─── Step 7b: AuthorizationBoundaryDefinitions ───────────────────
  -- One primary boundary per mature system. BoundaryType: 'Logical' | 'Physical' | 'Hybrid'.
  INSERT dbo.AuthorizationBoundaryDefinitions
    (Id, TenantId, RegisteredSystemId, Name, BoundaryType, Description, IsPrimary, CreatedAt, CreatedBy)
  VALUES
    ('fs-seed-abd-coastal',   @PEO790, @Sys_Coastal,   'Coastal Watch Enclave',           'Logical', 'Maritime domain awareness collaboration enclave boundary.', 1, @Now, 'system@spin-agent.local'),
    ('fs-seed-abd-eaglenest', @PMA290, @Sys_EagleNest, 'Eagle Nest Analytics Enclave',    'Logical', 'Eagle Nest back-end analytics enclave boundary.',           1, @Now, 'system@spin-agent.local'),
    ('fs-seed-abd-phoenix',   @PMA290, @Sys_Phoenix,   'Phoenix Falcon Application',      'Logical', 'Phoenix Falcon supply-chain decision-support boundary.',    1, @Now, 'system@spin-agent.local'),
    ('fs-seed-abd-polar',     @PMS408, @Sys_Polar,     'Polar Bear Platform IT (IL6)',    'Hybrid',  'Polar Bear air-gapped IL6 directory + DNS boundary.',       1, @Now, 'system@spin-agent.local');

  PRINT 'AuthorizationBoundaryDefinitions inserted: 4';

  -- ─── Step 7c: SystemComponents + ComponentSystemAssignments ──────
  -- ComponentType: 'Person' | 'Place' | 'Thing' | 'Policy'.
  -- Each system (except Eagle Eye, intentionally empty) gets a realistic
  -- multi-component authorization boundary: compute/web, data, identity,
  -- network, plus a key Person (ISSO/ISSM) and a governing Policy.
  INSERT dbo.SystemComponents
    (Id, TenantId, RegisteredSystemId, Name, ComponentType, SubType, Description,
     Owner, PersonName, Email, RmfRoleName, Status, CreatedAt, CreatedBy,
     AzureResourceId, AzureResourceType, AzureResourceGroup, AzureLocation,
     AuthorizationBoundaryDefinitionId)
  VALUES
    -- ── Coastal Watch (PEO-790, Monitor / full ATO) ──
    ('fs-seed-sc-coastal-aks',   @PEO790, @Sys_Coastal, 'AKS Collaboration Cluster',  'Thing',  'Container Platform', 'Azure Kubernetes Service hosting the maritime collaboration workloads.', 'platform.peo790@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-peo790/resourceGroups/rg-coastal/providers/Microsoft.ContainerService/managedClusters/aks-coastal', 'Microsoft.ContainerService/managedClusters', 'rg-coastal', 'usgovvirginia', 'fs-seed-abd-coastal'),
    ('fs-seed-sc-coastal-sql',   @PEO790, @Sys_Coastal, 'Coastal SQL Database',       'Thing',  'Database',           'Azure SQL Database storing situational-awareness records (TDE enabled).',  'dba.peo790@navy.mil',      NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-peo790/resourceGroups/rg-coastal/providers/Microsoft.Sql/servers/sql-coastal/databases/coastaldb', 'Microsoft.Sql/servers/databases', 'rg-coastal', 'usgovvirginia', 'fs-seed-abd-coastal'),
    ('fs-seed-sc-coastal-id',    @PEO790, @Sys_Coastal, 'Entra ID Tenant (PEO-790)',  'Thing',  'Identity Provider',  'Entra ID tenant providing SSO, Conditional Access and PIM for the boundary.', 'identity.peo790@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', NULL, 'Microsoft.AAD/tenants', NULL, 'usgovvirginia', 'fs-seed-abd-coastal'),
    ('fs-seed-sc-coastal-fw',    @PEO790, @Sys_Coastal, 'Azure Firewall (Coastal)',   'Thing',  'Network Boundary',   'Premium Azure Firewall enforcing deny-by-default egress at the VNet edge.', 'network.peo790@navy.mil',  NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-peo790/resourceGroups/rg-coastal/providers/Microsoft.Network/azureFirewalls/afw-coastal', 'Microsoft.Network/azureFirewalls', 'rg-coastal', 'usgovvirginia', 'fs-seed-abd-coastal'),
    ('fs-seed-sc-coastal-isso',  @PEO790, @Sys_Coastal, 'Coastal Watch ISSO',         'Person', 'Security Officer',   'Information System Security Officer accountable for the Coastal Watch boundary.', NULL, 'CDR Dana Reyes', 'isso.coastal@navy.mil', 'ISSO', 'Active', @Now, 'system@spin-agent.local', NULL, NULL, NULL, NULL, 'fs-seed-abd-coastal'),
    ('fs-seed-sc-coastal-pol',   @PEO790, @Sys_Coastal, 'Access Control Policy',      'Policy', 'Governance',         'Approved AC-family access-control policy governing least privilege and recertification.', 'iso.peo790@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', NULL, NULL, NULL, NULL, 'fs-seed-abd-coastal'),

    -- ── Eagle Nest (PMA-290, Monitor / ATOwC) ──
    ('fs-seed-sc-eaglenest-sql', @PMA290, @Sys_EagleNest, 'Eagle Nest SQL Managed Inst', 'Thing',  'Database',          'SQL Managed Instance storing analytics back-end data.',                 'dba.pma290@navy.mil',      NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-pma290/resourceGroups/rg-eaglenest/providers/Microsoft.Sql/managedInstances/mi-eaglenest', 'Microsoft.Sql/managedInstances', 'rg-eaglenest', 'usgovvirginia', 'fs-seed-abd-eaglenest'),
    ('fs-seed-sc-eaglenest-vm',  @PMA290, @Sys_EagleNest, 'Analytics Compute (VMSS)',    'Thing',  'Compute',           'VM Scale Set running the ISR analytics processing tier.',               'platform.pma290@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-pma290/resourceGroups/rg-eaglenest/providers/Microsoft.Compute/virtualMachineScaleSets/vmss-analytics', 'Microsoft.Compute/virtualMachineScaleSets', 'rg-eaglenest', 'usgovvirginia', 'fs-seed-abd-eaglenest'),
    ('fs-seed-sc-eaglenest-kv',  @PMA290, @Sys_EagleNest, 'Eagle Nest Key Vault',        'Thing',  'Secrets Store',     'Key Vault holding service credentials and data-encryption keys.',       'security.pma290@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-pma290/resourceGroups/rg-eaglenest/providers/Microsoft.KeyVault/vaults/kv-eaglenest', 'Microsoft.KeyVault/vaults', 'rg-eaglenest', 'usgovvirginia', 'fs-seed-abd-eaglenest'),
    ('fs-seed-sc-eaglenest-def', @PMA290, @Sys_EagleNest, 'Defender for Cloud (Plan)',   'Thing',  'Monitoring',        'Defender for Cloud workload-protection plan monitoring the analytics enclave.', 'soc.pma290@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', NULL, 'Microsoft.Security/pricings', 'rg-eaglenest', 'usgovvirginia', 'fs-seed-abd-eaglenest'),
    ('fs-seed-sc-eaglenest-issm',@PMA290, @Sys_EagleNest, 'Eagle Nest ISSM',             'Person', 'Security Manager',  'Information System Security Manager owning the ATOwC condition closure.', NULL, 'LCDR Priya Anand', 'issm.pma290@navy.mil', 'ISSM', 'Active', @Now, 'system@spin-agent.local', NULL, NULL, NULL, NULL, 'fs-seed-abd-eaglenest'),
    ('fs-seed-sc-eaglenest-pol', @PMA290, @Sys_EagleNest, 'Continuous Monitoring Policy','Policy', 'Governance',        'Approved CA/SI continuous-monitoring policy governing ongoing assessment.', 'iso.pma290@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', NULL, NULL, NULL, NULL, 'fs-seed-abd-eaglenest'),

    -- ── Phoenix Falcon (PMA-290, Assess / IATT) ──
    ('fs-seed-sc-phoenix-app',   @PMA290, @Sys_Phoenix, 'Phoenix Falcon App Service',  'Thing',  'Web Application',    'App Service hosting the Phoenix Falcon decision-support UI.',           'platform.pma290@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-pma290/resourceGroups/rg-phoenix/providers/Microsoft.Web/sites/app-phoenix', 'Microsoft.Web/sites', 'rg-phoenix', 'usgovvirginia', 'fs-seed-abd-phoenix'),
    ('fs-seed-sc-phoenix-cosmos',@PMA290, @Sys_Phoenix, 'Phoenix Cosmos DB',           'Thing',  'Database',           'Cosmos DB storing supply-chain logistics state for the decision engine.', 'dba.pma290@navy.mil',     NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-pma290/resourceGroups/rg-phoenix/providers/Microsoft.DocumentDB/databaseAccounts/cosmos-phoenix', 'Microsoft.DocumentDB/databaseAccounts', 'rg-phoenix', 'usgovvirginia', 'fs-seed-abd-phoenix'),
    ('fs-seed-sc-phoenix-nsg',   @PMA290, @Sys_Phoenix, 'Supplier Portal NSG',         'Thing',  'Network Boundary',   'Network security group on the supplier-portal subnet under SCA testing (SC-7 finding).', 'network.pma290@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-pma290/resourceGroups/rg-phoenix/providers/Microsoft.Network/networkSecurityGroups/nsg-supplier-portal', 'Microsoft.Network/networkSecurityGroups', 'rg-phoenix', 'usgovvirginia', 'fs-seed-abd-phoenix'),
    ('fs-seed-sc-phoenix-isso',  @PMA290, @Sys_Phoenix, 'Phoenix Falcon ISSO',         'Person', 'Security Officer',   'ISSO supporting the in-progress security control assessment.',          NULL, 'Mr. Sam Okoro', 'isso.phoenix@navy.mil', 'ISSO', 'Active', @Now, 'system@spin-agent.local', NULL, NULL, NULL, NULL, 'fs-seed-abd-phoenix'),

    -- ── Polar Bear (PMS-408, Authorize / DATO, IL6) ──
    ('fs-seed-sc-polar-ad',      @PMS408, @Sys_Polar, 'Polar Bear AD Forest (IL6)',  'Thing',  'Directory',          'Air-gapped Active Directory forest for IL6 classified workloads.',      'identity.pms408@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', NULL, 'Microsoft.AAD/domainServices', 'rg-polar', 'usgovvirginia', 'fs-seed-abd-polar'),
    ('fs-seed-sc-polar-dc',      @PMS408, @Sys_Polar, 'IL6 Domain Controllers',      'Thing',  'Compute',            'Pair of domain controllers (AU-12 audit-coverage gap tracked in remediation).', 'platform.pms408@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-pms408/resourceGroups/rg-polar/providers/Microsoft.Compute/virtualMachines/dc-il6-01', 'Microsoft.Compute/virtualMachines', 'rg-polar', 'usgovvirginia', 'fs-seed-abd-polar'),
    ('fs-seed-sc-polar-kv',      @PMS408, @Sys_Polar, 'Polar Bear Key Vault',        'Thing',  'Secrets Store',      'IL6 Key Vault (AU-12 diagnostic-settings finding under remediation).',  'security.pms408@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-pms408/resourceGroups/rg-polar/providers/Microsoft.KeyVault/vaults/kv-prod-01', 'Microsoft.KeyVault/vaults', 'rg-polar', 'usgovvirginia', 'fs-seed-abd-polar'),
    ('fs-seed-sc-polar-func',    @PMS408, @Sys_Polar, 'Polar Bear Function App',     'Thing',  'Serverless',         'Function App exposing the IL6 read API (CM-7 wildcard-CORS finding under deviation).', 'platform.pms408@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', '/subscriptions/fs-sub-pms408/resourceGroups/rg-polar/providers/Microsoft.Web/sites/func-api-01', 'Microsoft.Web/sites', 'rg-polar', 'usgovvirginia', 'fs-seed-abd-polar'),
    ('fs-seed-sc-polar-isso',    @PMS408, @Sys_Polar, 'Polar Bear ISSO',             'Person', 'Security Officer',   'ISSO driving DATO remediation across IA-2, AU-12 and CM-7.',            NULL, 'Ms. Tara Whitfield', 'isso.pms408@navy.mil', 'ISSO', 'Active', @Now, 'system@spin-agent.local', NULL, NULL, NULL, NULL, 'fs-seed-abd-polar'),
    ('fs-seed-sc-polar-pol',     @PMS408, @Sys_Polar, 'IL6 Hardening Policy',        'Policy', 'Governance',         'CNSSI 1253 IL6 baseline hardening policy governing least functionality (CM-7).', 'iso.pms408@navy.mil', NULL, NULL, NULL, 'Active', @Now, 'system@spin-agent.local', NULL, NULL, NULL, NULL, 'fs-seed-abd-polar');

  INSERT dbo.ComponentSystemAssignments
    (Id, TenantId, SystemComponentId, RegisteredSystemId, AuthorizationBoundaryDefinitionId, CreatedAt, CreatedBy)
  VALUES
    ('fs-seed-csa-coastal-aks',   @PEO790, 'fs-seed-sc-coastal-aks',   @Sys_Coastal,   'fs-seed-abd-coastal',   @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-coastal-sql',   @PEO790, 'fs-seed-sc-coastal-sql',   @Sys_Coastal,   'fs-seed-abd-coastal',   @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-coastal-id',    @PEO790, 'fs-seed-sc-coastal-id',    @Sys_Coastal,   'fs-seed-abd-coastal',   @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-coastal-fw',    @PEO790, 'fs-seed-sc-coastal-fw',    @Sys_Coastal,   'fs-seed-abd-coastal',   @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-coastal-isso',  @PEO790, 'fs-seed-sc-coastal-isso',  @Sys_Coastal,   'fs-seed-abd-coastal',   @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-coastal-pol',   @PEO790, 'fs-seed-sc-coastal-pol',   @Sys_Coastal,   'fs-seed-abd-coastal',   @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-en-sql',        @PMA290, 'fs-seed-sc-eaglenest-sql', @Sys_EagleNest, 'fs-seed-abd-eaglenest', @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-en-vm',         @PMA290, 'fs-seed-sc-eaglenest-vm',  @Sys_EagleNest, 'fs-seed-abd-eaglenest', @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-en-kv',         @PMA290, 'fs-seed-sc-eaglenest-kv',  @Sys_EagleNest, 'fs-seed-abd-eaglenest', @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-en-def',        @PMA290, 'fs-seed-sc-eaglenest-def', @Sys_EagleNest, 'fs-seed-abd-eaglenest', @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-en-issm',       @PMA290, 'fs-seed-sc-eaglenest-issm',@Sys_EagleNest, 'fs-seed-abd-eaglenest', @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-en-pol',        @PMA290, 'fs-seed-sc-eaglenest-pol', @Sys_EagleNest, 'fs-seed-abd-eaglenest', @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-phx-app',       @PMA290, 'fs-seed-sc-phoenix-app',   @Sys_Phoenix,   'fs-seed-abd-phoenix',   @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-phx-cosmos',    @PMA290, 'fs-seed-sc-phoenix-cosmos',@Sys_Phoenix,   'fs-seed-abd-phoenix',   @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-phx-nsg',       @PMA290, 'fs-seed-sc-phoenix-nsg',   @Sys_Phoenix,   'fs-seed-abd-phoenix',   @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-phx-isso',      @PMA290, 'fs-seed-sc-phoenix-isso',  @Sys_Phoenix,   'fs-seed-abd-phoenix',   @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-polar-ad',      @PMS408, 'fs-seed-sc-polar-ad',      @Sys_Polar,     'fs-seed-abd-polar',     @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-polar-dc',      @PMS408, 'fs-seed-sc-polar-dc',      @Sys_Polar,     'fs-seed-abd-polar',     @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-polar-kv',      @PMS408, 'fs-seed-sc-polar-kv',      @Sys_Polar,     'fs-seed-abd-polar',     @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-polar-func',    @PMS408, 'fs-seed-sc-polar-func',    @Sys_Polar,     'fs-seed-abd-polar',     @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-polar-isso',    @PMS408, 'fs-seed-sc-polar-isso',    @Sys_Polar,     'fs-seed-abd-polar',     @Now, 'system@spin-agent.local'),
    ('fs-seed-csa-polar-pol',     @PMS408, 'fs-seed-sc-polar-pol',     @Sys_Polar,     'fs-seed-abd-polar',     @Now, 'system@spin-agent.local');

  PRINT 'SystemComponents + ComponentSystemAssignments inserted: 22 + 22';

  -- ─── Step 7d: SecurityCategorizations (FIPS 199) ─────────────────
  INSERT dbo.SecurityCategorizations
    (Id, TenantId, RegisteredSystemId, IsNationalSecuritySystem, Justification, CategorizedBy, CategorizedAt)
  VALUES
    ('fs-seed-sca-coastal',   @PEO790, @Sys_Coastal,   0, 'Categorized Low per FIPS 199 — no PII, no mission-critical impact.',                'sca.peo790@navy.mil', @Now),
    ('fs-seed-sca-eaglenest', @PMA290, @Sys_EagleNest, 0, 'Categorized Moderate per FIPS 199 — analytics back-end serving ISR mission data.',  'sca.pma290@navy.mil', @Now),
    ('fs-seed-sca-phoenix',   @PMA290, @Sys_Phoenix,   0, 'Categorized Moderate per FIPS 199 with FedRAMP High overlay — supply-chain data.',   'sca.pma290@navy.mil', @Now),
    ('fs-seed-sca-polar',     @PMS408, @Sys_Polar,     1, 'NSS per CNSSI 1253 — IL6 classified directory; High Confidentiality + Integrity.',  'sca.pms408@navy.mil', @Now);

  PRINT 'SecurityCategorizations inserted: 4';

  -- ─── Step 7e: PrivacyThresholdAnalyses ───────────────────────────
  -- Determination: 'PiaRequired' | 'PiaNotRequired' | 'Exempt' | 'PendingConfirmation'.
  INSERT dbo.PrivacyThresholdAnalyses
    (Id, TenantId, RegisteredSystemId, Determination, CollectsPii, MaintainsPii, DisseminatesPii,
     PiiCategories, PiiSourceInfoTypes, Rationale, AnalyzedBy, AnalyzedAt)
  VALUES
    ('fs-seed-pta-coastal',   @PEO790, @Sys_Coastal,   'PiaNotRequired', 0, 0, 0, '[]', '[]', 'No PII collected; collaboration content limited to operational data.',                'issm.peo790@navy.mil', @Now),
    ('fs-seed-pta-eaglenest', @PMA290, @Sys_EagleNest, 'PiaNotRequired', 0, 0, 0, '[]', '[]', 'Analytics back-end processes mission telemetry; no PII.',                              'issm.pma290@navy.mil', @Now),
    ('fs-seed-pta-phoenix',   @PMA290, @Sys_Phoenix,   'PiaNotRequired', 0, 0, 0, '[]', '[]', 'Supply-chain decision support; processes vendor & part data, no PII.',                  'issm.pma290@navy.mil', @Now),
    ('fs-seed-pta-polar',     @PMS408, @Sys_Polar,     'Exempt',         1, 1, 0, '["Name","Title","WorkEmail"]', '[]', 'PII limited to directory entries for cleared personnel; exempt under E.O. 13526.', 'issm.pms408@navy.mil', @Now);

  PRINT 'PrivacyThresholdAnalyses inserted: 4';

  -- ─── Step 7f: SystemProfileSections (6 sections × 4 systems) ─────
  -- All Approved with CompletionPercentage=100 so the System Profile
  -- prerequisite shows 6/6 complete on the Phase Readiness panel.
  -- (Eagle Eye intentionally has none.)
  INSERT dbo.SystemProfileSections
    (Id, TenantId, RegisteredSystemId, SectionType, GovernanceStatus,
     DraftContent, ApprovedContent, CompletionPercentage,
     LastEditedBy, LastEditedAt, SubmittedBy, SubmittedAt, ReviewedBy, ReviewedAt,
     CreatedAt)
  -- ApprovedContent holds rich, schema-correct JSON keyed to the dashboard
  -- ProfileSectionForm field keys (missionStatement, businessPurpose, hostingModel,
  -- etc.). Multiselect values are stored as JSON-stringified arrays (e.g.
  -- "[\"Azure Government\"]") exactly as the form serializes them. DraftContent is
  -- synced to ApprovedContent by the UPDATE below (the form reads draftContent).
  VALUES
    -- Coastal Watch
    ('fs-seed-sps-coastal-mp', @PEO790, @Sys_Coastal, 'MissionAndPurpose',         'Approved', '{}', '{"missionStatement":"Coastal Watch provides persistent maritime domain awareness for the Fifth Fleet area of responsibility, fusing radar, AIS, and overhead ISR feeds into a single operational picture for watch standers.","businessPurpose":"Enables early detection of surface threats and anomalous vessel behavior in contested littoral waters, supporting force-protection and freedom-of-navigation operations.","operationalJustification":"No existing fleet system correlates multi-source maritime tracks at the required refresh rate; Coastal Watch closes that gap for the maritime operations center.","businessFunctions":"Track correlation; anomaly alerting; watch-officer dashboards; tip-and-cue to ISR assets."}', 100, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, @Now),
    ('fs-seed-sps-coastal-ua', @PEO790, @Sys_Coastal, 'UsersAndAccess',            'Approved', '{}', '{"accessOverview":"Access is limited to credentialed watch-floor personnel and a small administrator cadre; all sessions require CAC/PIV on the NIPR enclave with role-based authorization.","authenticationMethod":"[\"CAC/PIV\",\"MFA\",\"Active Directory\"]"}', 100, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, @Now),
    ('fs-seed-sps-coastal-ed', @PEO790, @Sys_Coastal, 'EnvironmentAndDeployment',  'Approved', '{}', '{"hostingModel":"Government Cloud (GovCloud)","cloudProvider":"[\"Azure Government\"]","networkZones":"[\"DMZ\",\"Application Tier\",\"Database Tier\"]","geographicLocations":"[\"CONUS (Continental US)\",\"US East\"]","availabilityTier":"99.9% (Three 9s)","disasterRecoveryPosture":"Warm Standby (Active-Passive)","rtoRpo":"RTO < 4hr / RPO < 1hr","maintenanceWindows":"Sundays 0000-0600 ET","operatingSystem":"[\"RHEL 9\",\"Container-Based (No Host OS)\"]","additionalDetails":"Deployed to Azure Government IL4; containerized microservices on AKS behind an internal load balancer."}', 100, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, @Now),
    ('fs-seed-sps-coastal-dt', @PEO790, @Sys_Coastal, 'DataTypes',                 'Approved', '{}', '{"dataOverview":"Coastal Watch processes unclassified-but-sensitive maritime track data, vessel registries, and watch-floor annotations; no PII beyond operator identities.","highestSensitivityLevel":"CUI"}', 100, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, @Now),
    ('fs-seed-sps-coastal-pp', @PEO790, @Sys_Coastal, 'PortsProtocolsAndServices', 'Approved', '{}', '{"ppsOverview":"All external traffic is HTTPS/443 inbound to the web tier; backend services communicate over mutual-TLS on internal ports only."}', 100, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, @Now),
    ('fs-seed-sps-coastal-la', @PEO790, @Sys_Coastal, 'LeveragedAuthorizations',   'Approved', '{}', '{"leveragedAuthOverview":"Inherits infrastructure and physical controls from the Azure Government FedRAMP High provisional authorization."}', 100, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, 'issm.peo790@navy.mil', @Now, @Now),

    -- Eagle Nest
    ('fs-seed-sps-en-mp', @PMA290, @Sys_EagleNest, 'MissionAndPurpose',         'Approved', '{}', '{"missionStatement":"Eagle Nest is the back-end analytics platform that ingests airborne ISR sensor feeds and produces processed intelligence products for P-8 and MQ-4C mission crews.","businessPurpose":"Reduces sensor-to-decision timelines by automating exploitation of full-motion video and signals metadata at the edge of the maritime patrol enterprise.","operationalJustification":"Manual exploitation cannot keep pace with multi-platform collection volume; Eagle Nest automates triage and tipping.","businessFunctions":"FMV exploitation; SIGINT metadata correlation; product dissemination; model retraining pipeline."}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),
    ('fs-seed-sps-en-ua', @PMA290, @Sys_EagleNest, 'UsersAndAccess',            'Approved', '{}', '{"accessOverview":"Analysts and data engineers authenticate via CAC/PIV; service-to-service calls use managed-identity tokens. Privileged access is brokered through PIM.","authenticationMethod":"[\"CAC/PIV\",\"MFA\",\"OAuth 2.0\",\"Certificate-Based\"]"}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),
    ('fs-seed-sps-en-ed', @PMA290, @Sys_EagleNest, 'EnvironmentAndDeployment',  'Approved', '{}', '{"hostingModel":"Government Cloud (GovCloud)","cloudProvider":"[\"Azure Government\"]","networkZones":"[\"Application Tier\",\"Database Tier\",\"Management\"]","geographicLocations":"[\"CONUS (Continental US)\",\"US West\"]","availabilityTier":"99.99% (Four 9s)","disasterRecoveryPosture":"Hot Standby (Active-Active)","rtoRpo":"RTO < 1hr / RPO < 15min","maintenanceWindows":"24/7 Rolling Updates","operatingSystem":"[\"Ubuntu 22.04 LTS\",\"Container-Based (No Host OS)\"]","additionalDetails":"GPU-backed inference nodes on Azure Government IL5; data lake on ADLS Gen2."}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),
    ('fs-seed-sps-en-dt', @PMA290, @Sys_EagleNest, 'DataTypes',                 'Approved', '{}', '{"dataOverview":"Eagle Nest handles mission telemetry, processed ISR products, and model artifacts. Aggregated products may reach the CUI//SP-ISR boundary.","highestSensitivityLevel":"CUI"}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),
    ('fs-seed-sps-en-pp', @PMA290, @Sys_EagleNest, 'PortsProtocolsAndServices', 'Approved', '{}', '{"ppsOverview":"HTTPS/443 for the analyst portal; 1433 to the SQL backend within the management subnet; all inter-service traffic is TLS-encrypted."}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),
    ('fs-seed-sps-en-la', @PMA290, @Sys_EagleNest, 'LeveragedAuthorizations',   'Approved', '{}', '{"leveragedAuthOverview":"Leverages Azure Government FedRAMP High and the Navy enterprise IL5 platform authorization for shared platform services."}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),

    -- Phoenix Falcon
    ('fs-seed-sps-phx-mp', @PMA290, @Sys_Phoenix, 'MissionAndPurpose',         'Approved', '{}', '{"missionStatement":"Phoenix Falcon delivers predictive supply-chain decision support for naval aviation depot maintenance, forecasting part demand and identifying diminishing-manufacturing-sources risk.","businessPurpose":"Improves aircraft readiness by getting the right parts to the right depot before backorders ground airframes.","operationalJustification":"Legacy spreadsheets cannot model multi-echelon demand; Phoenix Falcon provides the analytic backbone for sustainment planners.","businessFunctions":"Demand forecasting; DMSMS risk scoring; vendor performance analytics; reorder recommendations."}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),
    ('fs-seed-sps-phx-ua', @PMA290, @Sys_Phoenix, 'UsersAndAccess',            'Approved', '{}', '{"accessOverview":"Sustainment planners and vendors access role-scoped views; external vendor partners are federated through a brokered SSO with MFA.","authenticationMethod":"[\"CAC/PIV\",\"MFA\",\"SAML\",\"SSO\"]"}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),
    ('fs-seed-sps-phx-ed', @PMA290, @Sys_Phoenix, 'EnvironmentAndDeployment',  'Approved', '{}', '{"hostingModel":"Government Cloud (GovCloud)","cloudProvider":"[\"Azure Government\"]","networkZones":"[\"DMZ\",\"Application Tier\",\"Database Tier\"]","geographicLocations":"[\"CONUS (Continental US)\",\"Multiple Regions\"]","availabilityTier":"99.9% (Three 9s)","disasterRecoveryPosture":"Warm Standby (Active-Passive)","rtoRpo":"RTO < 4hr / RPO < 1hr","maintenanceWindows":"Weekends 0200-0600 ET","operatingSystem":"[\"Windows Server 2022\",\"Container-Based (No Host OS)\"]","additionalDetails":"Cosmos DB for catalog data; App Service for the planner portal; FedRAMP High overlay applied."}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),
    ('fs-seed-sps-phx-dt', @PMA290, @Sys_Phoenix, 'DataTypes',                 'Approved', '{}', '{"dataOverview":"Phoenix Falcon processes vendor records, part catalogs, and maintenance histories. Vendor PII is limited to point-of-contact details.","highestSensitivityLevel":"CUI"}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),
    ('fs-seed-sps-phx-pp', @PMA290, @Sys_Phoenix, 'PortsProtocolsAndServices', 'Approved', '{}', '{"ppsOverview":"HTTPS/443 for the planner portal and vendor API; 443 outbound to integrated logistics feeds; no inbound database exposure."}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),
    ('fs-seed-sps-phx-la', @PMA290, @Sys_Phoenix, 'LeveragedAuthorizations',   'Approved', '{}', '{"leveragedAuthOverview":"Leverages Azure Government FedRAMP High; integrates with the enterprise logistics data platform under its existing ATO."}', 100, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, 'issm.pma290@navy.mil', @Now, @Now),

    -- Polar Bear
    ('fs-seed-sps-pb-mp', @PMS408, @Sys_Polar, 'MissionAndPurpose',         'Approved', '{}', '{"missionStatement":"Polar Bear provides the classified directory, authentication, and DNS foundation for the SIPR-side mission enclave supporting undersea warfare command and control.","businessPurpose":"Delivers the identity and name-resolution backbone every classified application in the enclave depends on.","operationalJustification":"The enclave requires an air-gapped, IL6-authorized directory service; no shared service meets the isolation requirement.","businessFunctions":"Directory services; Kerberos authentication; classified DNS; certificate issuance."}', 100, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, @Now),
    ('fs-seed-sps-pb-ua', @PMS408, @Sys_Polar, 'UsersAndAccess',            'Approved', '{}', '{"accessOverview":"Access is restricted to enclave-cleared administrators using CAC/PIV-D on the SIPR network; all privileged actions require dual authorization.","authenticationMethod":"[\"CAC/PIV\",\"MFA\",\"Kerberos\",\"Active Directory\",\"Smart Card\"]"}', 100, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, @Now),
    ('fs-seed-sps-pb-ed', @PMS408, @Sys_Polar, 'EnvironmentAndDeployment',  'Approved', '{}', '{"hostingModel":"Government Cloud (GovCloud)","cloudProvider":"[\"Azure Government\"]","networkZones":"[\"Management\",\"Internal / Trusted\",\"Enclave\"]","geographicLocations":"[\"CONUS (Continental US)\",\"Classified Location\"]","availabilityTier":"99.999% (Five 9s)","disasterRecoveryPosture":"Multi-Region Failover","rtoRpo":"RTO < 1hr / RPO < 15min","maintenanceWindows":"Quarterly Scheduled","operatingSystem":"[\"Windows Server 2022\",\"RHEL 9\"]","additionalDetails":"Air-gapped IL6 enclave; CNSSI 1253 overlay; hardware security modules for the certificate root."}', 100, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, @Now),
    ('fs-seed-sps-pb-dt', @PMS408, @Sys_Polar, 'DataTypes',                 'Approved', '{}', '{"dataOverview":"Polar Bear stores classified directory objects, credentials, and DNS zone data at the SECRET level within the IL6 boundary.","highestSensitivityLevel":"Classified"}', 100, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, @Now),
    ('fs-seed-sps-pb-pp', @PMS408, @Sys_Polar, 'PortsProtocolsAndServices', 'Approved', '{}', '{"ppsOverview":"LDAPS/636 and DNS/53 served to enclave members only; Kerberos/88 for authentication; no traffic crosses the enclave boundary."}', 100, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, @Now),
    ('fs-seed-sps-pb-la', @PMS408, @Sys_Polar, 'LeveragedAuthorizations',   'Approved', '{}', '{"leveragedAuthOverview":"Leverages the IL6 platform provisional authorization and CNSSI 1253 enclave controls; physical security inherited from the SCIF."}', 100, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, 'issm.pms408@navy.mil', @Now, @Now);

  -- The dashboard ProfileSectionForm reads DraftContent; mirror the approved
  -- content into the draft so the seeded systems render fully populated.
  UPDATE dbo.SystemProfileSections
     SET DraftContent = ApprovedContent
   WHERE Id LIKE 'fs-seed-sps-%';

  PRINT 'SystemProfileSections inserted: 24';

  -- ─── Step 7f-2: Profile-section child entities ───────────────────
  -- Populate the CRUD tables the dashboard renders under each profile
  -- section. Select-column values use the exact ProfileSectionForm option
  -- strings so they display as selected. (Eagle Eye intentionally has none.)
  INSERT dbo.UserCategories
    (Id, TenantId, SystemProfileSectionId, CategoryName, Description, ApproximateCount, AccessMethod, DataSensitivityLevel, SortOrder)
  VALUES
    ('fs-seed-uc-coastal-1', @PEO790, 'fs-seed-sps-coastal-ua', 'Application Users',         'Maritime watch officers operating the common operational picture.', 25,   'Web Portal (SSO)', 'CUI', 0),
    ('fs-seed-uc-coastal-2', @PEO790, 'fs-seed-sps-coastal-ua', 'System Administrators',     'Platform administrators maintaining the AKS cluster and data feeds.', 5,    'VPN + MFA',        'CUI', 1),
    ('fs-seed-uc-en-1',      @PMA290, 'fs-seed-sps-en-ua',      'Application Users',         'ISR analysts exploiting full-motion video and signals metadata.',     40,   'CAC/PIV',          'CUI', 0),
    ('fs-seed-uc-en-2',      @PMA290, 'fs-seed-sps-en-ua',      'Privileged Administrators', 'Data engineers managing the inference pipeline via PIM.',             6,    'VPN + MFA',        'CUI', 1),
    ('fs-seed-uc-phx-1',     @PMA290, 'fs-seed-sps-phx-ua',     'Application Users',         'Sustainment planners running demand forecasts.',                      120,  'Web Portal (SSO)', 'CUI', 0),
    ('fs-seed-uc-phx-2',     @PMA290, 'fs-seed-sps-phx-ua',     'External Partners',         'Federated vendor users submitting catalog and lead-time data.',       180,  'Web Portal (SSO)', 'CUI', 1),
    ('fs-seed-uc-pb-1',      @PMS408, 'fs-seed-sps-pb-ua',      'Privileged Administrators', 'Enclave-cleared directory administrators on SIPR.',                   12,   'CAC/PIV',          'Classified', 0),
    ('fs-seed-uc-pb-2',      @PMS408, 'fs-seed-sps-pb-ua',      'Read-Only Users',           'Auditors reviewing directory and DNS change logs.',                   8,    'Direct Console',   'Classified', 1);

  INSERT dbo.DataTypeEntries
    (Id, TenantId, SystemProfileSectionId, DataTypeName, Description, SensitivityClassification, Source, Destination, ApplicableRegulations, SortOrder)
  VALUES
    ('fs-seed-dte-coastal-1', @PEO790, 'fs-seed-sps-coastal-dt', 'Operational Data', 'Fused maritime surface tracks and vessel registries.',          'CUI', 'Sensor / IoT',     'Internal Database', 'FISMA',       0),
    ('fs-seed-dte-coastal-2', @PEO790, 'fs-seed-sps-coastal-dt', 'Audit Logs',       'Watch-floor action and authentication audit records.',          'CUI', 'Internal System',  'Analytics / SIEM',  'FISMA',       1),
    ('fs-seed-dte-en-1',      @PMA290, 'fs-seed-sps-en-dt',      'Operational Data', 'Processed ISR products and mission telemetry.',                 'CUI', 'Sensor / IoT',     'Internal Database', 'FISMA',       0),
    ('fs-seed-dte-en-2',      @PMA290, 'fs-seed-sps-en-dt',      'System Configuration', 'Model artifacts and pipeline configuration baselines.',     'CUI', 'Internal System',  'Backup Storage',    'FISMA',       1),
    ('fs-seed-dte-phx-1',     @PMA290, 'fs-seed-sps-phx-dt',     'Operational Data', 'Part catalogs, demand signals, and maintenance histories.',     'CUI', 'Database',         'Internal Database', 'FISMA',       0),
    ('fs-seed-dte-phx-2',     @PMA290, 'fs-seed-sps-phx-dt',     'PII — Phone/Email','Vendor point-of-contact details.',                              'PII', 'External API',     'Internal Database', 'Privacy Act', 1),
    ('fs-seed-dte-pb-1',      @PMS408, 'fs-seed-sps-pb-dt',      'Authentication Credentials', 'Classified directory accounts and credential material.', 'Classified', 'Internal System', 'Internal Database', 'FISMA', 0),
    ('fs-seed-dte-pb-2',      @PMS408, 'fs-seed-sps-pb-dt',      'System Configuration', 'Classified DNS zone data and certificate trust stores.',    'Classified', 'Internal System', 'Internal Database', 'FISMA', 1);

  INSERT dbo.PpsEntries
    (Id, TenantId, SystemProfileSectionId, PortOrRange, Protocol, ServiceName, Direction, Justification, SortOrder)
  VALUES
    ('fs-seed-pps-coastal-1', @PEO790, 'fs-seed-sps-coastal-pp', '443 (HTTPS)',  'TCP', 'Web Application', 'Inbound', 'Watch-floor common operational picture and API access.',  0),
    ('fs-seed-pps-coastal-2', @PEO790, 'fs-seed-sps-coastal-pp', '1433 (MSSQL)', 'TCP', 'Database',        'Both',    'Track datastore — internal subnet only.',                 1),
    ('fs-seed-pps-en-1',      @PMA290, 'fs-seed-sps-en-pp',      '443 (HTTPS)',  'TCP', 'Web Application', 'Inbound', 'Analyst exploitation portal.',                            0),
    ('fs-seed-pps-en-2',      @PMA290, 'fs-seed-sps-en-pp',      '1433 (MSSQL)', 'TCP', 'Database',        'Both',    'ISR product datastore within the management subnet.',     1),
    ('fs-seed-pps-phx-1',     @PMA290, 'fs-seed-sps-phx-pp',     '443 (HTTPS)',  'TCP', 'REST API',        'Inbound', 'Planner portal and vendor catalog API.',                  0),
    ('fs-seed-pps-phx-2',     @PMA290, 'fs-seed-sps-phx-pp',     '443 (HTTPS)',  'TLS', 'REST API',        'Outbound','Integrated logistics data-feed consumption.',             1),
    ('fs-seed-pps-pb-1',      @PMS408, 'fs-seed-sps-pb-pp',      '636 (LDAPS)',  'TCP', 'LDAP / AD',       'Both',    'Classified directory queries within the enclave.',        0),
    ('fs-seed-pps-pb-2',      @PMS408, 'fs-seed-sps-pb-pp',      '53 (DNS)',     'TCP and UDP', 'DNS',     'Both',    'Classified name resolution for enclave members.',         1);

  INSERT dbo.LeveragedAuthorizations
    (Id, TenantId, SystemProfileSectionId, ProviderName, AuthorizationType, AuthorizationDate, CoveredControlFamilies, SortOrder)
  VALUES
    ('fs-seed-la-coastal-1', @PEO790, 'fs-seed-sps-coastal-la', 'Azure Government', 'FedRAMP High', @120dAgo, 'AC — Access Control', 0),
    ('fs-seed-la-en-1',      @PMA290, 'fs-seed-sps-en-la',      'Azure Government', 'DoD PA (IL5)', @120dAgo, 'Multiple / All',      0),
    ('fs-seed-la-phx-1',     @PMA290, 'fs-seed-sps-phx-la',     'Azure Government', 'FedRAMP High', @120dAgo, 'Multiple / All',      0),
    ('fs-seed-la-pb-1',      @PMS408, 'fs-seed-sps-pb-la',      'Azure Government', 'DoD PA (IL6)', @120dAgo, 'Multiple / All',      0);

  PRINT 'Profile-section child entities inserted: UserCategories 8, DataTypeEntries 8, PpsEntries 8, LeveragedAuthorizations 4';

  -- ─── Step 7g: ControlBaselines ───────────────────────────────────
  -- BaselineLevel: 'Low' | 'Moderate' | 'High'.
  INSERT dbo.ControlBaselines
    (Id, TenantId, RegisteredSystemId, BaselineLevel, OverlayApplied,
     TotalControls, CustomerControls, InheritedControls, SharedControls,
     TailoredOutControls, TailoredInControls, ControlIds, CreatedAt, CreatedBy)
  VALUES
    ('fs-seed-cb-coastal',   @PEO790, @Sys_Coastal,   'Low',      NULL,                149, 119,  20, 10, 0, 0, '["AC-2","AC-3","AU-2","SC-7","SI-4"]', @Now, 'sca.peo790@navy.mil'),
    ('fs-seed-cb-eaglenest', @PMA290, @Sys_EagleNest, 'Moderate', NULL,                287, 217,  40, 30, 0, 0, '["AC-2","AC-3","AU-2","SC-7","SI-4","CM-7"]', @Now, 'sca.pma290@navy.mil'),
    ('fs-seed-cb-phoenix',   @PMA290, @Sys_Phoenix,   'Moderate', 'FedRAMP High',      287, 207,  50, 30, 0, 0, '["AC-2","AC-3","AU-2","SC-7","SI-4","CM-7"]', @Now, 'sca.pma290@navy.mil'),
    ('fs-seed-cb-polar',     @PMS408, @Sys_Polar,     'High',     'CNSSI 1253 IL6',    370, 300,  50, 20, 0, 0, '["AC-2","AC-3","AU-2","SC-7","SI-4","CM-7","IA-2","AU-12"]', @Now, 'sca.pms408@navy.mil');

  PRINT 'ControlBaselines inserted: 4';

  -- ════════════════════════════════════════════════════════════════
  --  STEP 8 — Phase-coherent prerequisite artifacts
  --  Eagle Eye (Prepare) intentionally receives NONE of the below;
  --  it stays a clean, just-registered system. Every other system is
  --  populated only up to the artifacts its RMF phase has produced:
  --    Categorize → InformationTypes
  --    Select     → ControlInheritances
  --    Implement  → SecurityCapabilities, ControlImplementations, Narratives
  --    Assess     → Evidence, RemediationBoards/Tasks
  --    Authorize  → Documents (SSP/SAR)
  --    Monitor    → ConMonPlans + ConMonReports
  -- ════════════════════════════════════════════════════════════════

  -- ─── Step 8a: InformationTypes (NIST SP 800-60) ──────────────────
  -- FK → SecurityCategorizations (NO TenantId column on this table).
  INSERT dbo.InformationTypes
    (Id, SecurityCategorizationId, Sp80060Id, Name, Category,
     ConfidentialityImpact, IntegrityImpact, AvailabilityImpact,
     UsesProvisionalImpactLevels, AdjustmentJustification)
  VALUES
    ('fs-seed-it-coastal-admin', 'fs-seed-sca-coastal',   'C.3.5.1', 'Administrative Management',  'Management & Support', 'Low',      'Low',      'Low',      0, NULL),
    ('fs-seed-it-en-isr',        'fs-seed-sca-eaglenest', 'D.20.1',  'ISR Analytics Data',         'Mission Operations',   'Moderate', 'Moderate', 'Moderate', 0, NULL),
    ('fs-seed-it-phx-supply',    'fs-seed-sca-phoenix',   'D.4.4',   'Supply Chain Logistics',     'Logistics',            'Moderate', 'Moderate', 'Low',      0, 'Availability adjusted Low — supplier-portal outage tolerated up to 24h.'),
    ('fs-seed-it-polar-dir',     'fs-seed-sca-polar',     'C.2.8.9', 'Classified Directory Svc',   'Identity Services',    'High',     'High',     'Moderate', 0, NULL);

  PRINT 'InformationTypes inserted: 4';

  -- ─── Step 8b: SecurityCapabilities (tenant-scoped) ───────────────
  -- ImplementationStatus enum: Planned|InProgress|Implemented|Deprecated.
  INSERT dbo.SecurityCapabilities
    (Id, TenantId, Name, Provider, Category, Description, ImplementationStatus, Owner, CreatedAt, CreatedBy)
  VALUES
    -- PEO-790 (Coastal Watch — Low)
    ('fs-seed-cap-peo-ac', @PEO790, 'Entra ID Conditional Access (PEO-790)', 'Microsoft Entra ID',        'AC', 'Conditional access policies enforce least-privilege and device-compliance for all system sign-ins.', 'Implemented', 'identity.peo790@navy.mil', @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-peo-au', @PEO790, 'Azure Monitor + Log Analytics (PEO-790)','Azure Monitor',            'AU', 'Centralized audit-log collection and 1-year retention via Log Analytics workspace.',                 'Implemented', 'soc.peo790@navy.mil',      @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-peo-sc', @PEO790, 'Azure Firewall + NSG (PEO-790)',         'Azure Networking',          'SC', 'Boundary protection at the system edge via Azure Firewall and per-subnet NSGs.',                     'Implemented', 'network.peo790@navy.mil',  @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-peo-si', @PEO790, 'Defender for Cloud (PEO-790)',           'Microsoft Defender',        'SI', 'Continuous flaw monitoring and alerting via Microsoft Defender for Cloud.',                          'Implemented', 'soc.peo790@navy.mil',      @Now, 'system@spin-agent.local'),
    -- PMA-290 (Eagle Nest + Phoenix — Moderate)
    ('fs-seed-cap-pma-ac', @PMA290, 'Entra ID Conditional Access (PMA-290)', 'Microsoft Entra ID',        'AC', 'Conditional access policies enforce least-privilege and device-compliance for all system sign-ins.', 'Implemented', 'identity.pma290@navy.mil', @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-pma-au', @PMA290, 'Azure Monitor + Log Analytics (PMA-290)','Azure Monitor',            'AU', 'Centralized audit-log collection and 1-year retention via Log Analytics workspace.',                 'Implemented', 'soc.pma290@navy.mil',      @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-pma-sc', @PMA290, 'Azure Firewall + NSG (PMA-290)',         'Azure Networking',          'SC', 'Boundary protection at the system edge via Azure Firewall and per-subnet NSGs.',                     'InProgress',  'network.pma290@navy.mil',  @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-pma-si', @PMA290, 'Defender for Cloud (PMA-290)',           'Microsoft Defender',        'SI', 'Continuous flaw monitoring and alerting via Microsoft Defender for Cloud.',                          'Implemented', 'soc.pma290@navy.mil',      @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-pma-cm', @PMA290, 'Azure Policy Guardrails (PMA-290)',      'Azure Policy',              'CM', 'Configuration baselines and drift prevention enforced through Azure Policy assignments.',            'InProgress',  'config.pma290@navy.mil',   @Now, 'system@spin-agent.local'),
    -- PMS-408 (Polar Bear — High / IL6)
    ('fs-seed-cap-pms-ac', @PMS408, 'Entra ID Conditional Access (PMS-408)', 'Microsoft Entra ID',        'AC', 'Conditional access policies enforce least-privilege and device-compliance for all system sign-ins.', 'Implemented',         'identity.pms408@navy.mil', @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-pms-au', @PMS408, 'Azure Monitor + Log Analytics (PMS-408)','Azure Monitor',            'AU', 'Centralized audit-log collection and 1-year retention via Log Analytics workspace.',                 'InProgress',         'soc.pms408@navy.mil',     @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-pms-sc', @PMS408, 'Azure Firewall + NSG (PMS-408)',         'Azure Networking',          'SC', 'Boundary protection at the system edge via Azure Firewall and per-subnet NSGs.',                     'Implemented',         'network.pms408@navy.mil',  @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-pms-si', @PMS408, 'Defender for Cloud (PMS-408)',           'Microsoft Defender',        'SI', 'Continuous flaw monitoring and alerting via Microsoft Defender for Cloud.',                          'Implemented',         'soc.pms408@navy.mil',      @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-pms-cm', @PMS408, 'Azure Policy Guardrails (PMS-408)',      'Azure Policy',              'CM', 'Configuration baselines and drift prevention enforced through Azure Policy assignments.',            'Planned',             'config.pms408@navy.mil',   @Now, 'system@spin-agent.local'),
    ('fs-seed-cap-pms-ia', @PMS408, 'CAC/PIV Phishing-Resistant MFA (PMS-408)','Microsoft Entra ID',       'IA', 'Phishing-resistant CAC/PIV smart-card authentication for all privileged and standard users.',        'Planned',             'identity.pms408@navy.mil', @Now, 'system@spin-agent.local');

  PRINT 'SecurityCapabilities inserted: 14';

  -- ─── Step 8c: SystemCapabilityLinks ──────────────────────────────
  INSERT dbo.SystemCapabilityLinks
    (Id, TenantId, RegisteredSystemId, SecurityCapabilityId, LinkedAt, LinkedBy)
  VALUES
    -- Coastal Watch
    ('fs-seed-scl-coastal-ac', @PEO790, @Sys_Coastal, 'fs-seed-cap-peo-ac', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-coastal-au', @PEO790, @Sys_Coastal, 'fs-seed-cap-peo-au', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-coastal-sc', @PEO790, @Sys_Coastal, 'fs-seed-cap-peo-sc', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-coastal-si', @PEO790, @Sys_Coastal, 'fs-seed-cap-peo-si', @Now, 'system@spin-agent.local'),
    -- Eagle Nest
    ('fs-seed-scl-en-ac', @PMA290, @Sys_EagleNest, 'fs-seed-cap-pma-ac', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-en-au', @PMA290, @Sys_EagleNest, 'fs-seed-cap-pma-au', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-en-sc', @PMA290, @Sys_EagleNest, 'fs-seed-cap-pma-sc', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-en-si', @PMA290, @Sys_EagleNest, 'fs-seed-cap-pma-si', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-en-cm', @PMA290, @Sys_EagleNest, 'fs-seed-cap-pma-cm', @Now, 'system@spin-agent.local'),
    -- Phoenix Falcon
    ('fs-seed-scl-phx-ac', @PMA290, @Sys_Phoenix, 'fs-seed-cap-pma-ac', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-phx-au', @PMA290, @Sys_Phoenix, 'fs-seed-cap-pma-au', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-phx-sc', @PMA290, @Sys_Phoenix, 'fs-seed-cap-pma-sc', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-phx-si', @PMA290, @Sys_Phoenix, 'fs-seed-cap-pma-si', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-phx-cm', @PMA290, @Sys_Phoenix, 'fs-seed-cap-pma-cm', @Now, 'system@spin-agent.local'),
    -- Polar Bear
    ('fs-seed-scl-polar-ac', @PMS408, @Sys_Polar, 'fs-seed-cap-pms-ac', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-polar-au', @PMS408, @Sys_Polar, 'fs-seed-cap-pms-au', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-polar-sc', @PMS408, @Sys_Polar, 'fs-seed-cap-pms-sc', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-polar-si', @PMS408, @Sys_Polar, 'fs-seed-cap-pms-si', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-polar-cm', @PMS408, @Sys_Polar, 'fs-seed-cap-pms-cm', @Now, 'system@spin-agent.local'),
    ('fs-seed-scl-polar-ia', @PMS408, @Sys_Polar, 'fs-seed-cap-pms-ia', @Now, 'system@spin-agent.local');

  PRINT 'SystemCapabilityLinks inserted: 20';

  -- ─── Step 8d: CapabilityControlMappings (capability → control) ───
  -- Role enum: Primary|Supporting|Shared. RegisteredSystemId left NULL =
  -- tenant-wide coverage applying to every system using the capability.
  INSERT dbo.CapabilityControlMappings
    (Id, TenantId, SecurityCapabilityId, ControlId, RegisteredSystemId, Role, CreatedAt, CreatedBy)
  VALUES
    ('fs-seed-ccm-peo-ac2', @PEO790, 'fs-seed-cap-peo-ac', 'AC-2', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-peo-ac3', @PEO790, 'fs-seed-cap-peo-ac', 'AC-3', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-peo-au2', @PEO790, 'fs-seed-cap-peo-au', 'AU-2', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-peo-sc7', @PEO790, 'fs-seed-cap-peo-sc', 'SC-7', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-peo-si4', @PEO790, 'fs-seed-cap-peo-si', 'SI-4', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pma-ac2', @PMA290, 'fs-seed-cap-pma-ac', 'AC-2', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pma-ac3', @PMA290, 'fs-seed-cap-pma-ac', 'AC-3', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pma-au2', @PMA290, 'fs-seed-cap-pma-au', 'AU-2', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pma-sc7', @PMA290, 'fs-seed-cap-pma-sc', 'SC-7', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pma-si4', @PMA290, 'fs-seed-cap-pma-si', 'SI-4', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pma-cm7', @PMA290, 'fs-seed-cap-pma-cm', 'CM-7', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pms-ac2', @PMS408, 'fs-seed-cap-pms-ac', 'AC-2', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pms-ac3', @PMS408, 'fs-seed-cap-pms-ac', 'AC-3', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pms-au2', @PMS408, 'fs-seed-cap-pms-au', 'AU-2', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pms-au12',@PMS408, 'fs-seed-cap-pms-au', 'AU-12',NULL, 'Supporting', @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pms-sc7', @PMS408, 'fs-seed-cap-pms-sc', 'SC-7', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pms-si4', @PMS408, 'fs-seed-cap-pms-si', 'SI-4', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pms-cm7', @PMS408, 'fs-seed-cap-pms-cm', 'CM-7', NULL, 'Primary',    @Now, 'system@spin-agent.local'),
    ('fs-seed-ccm-pms-ia2', @PMS408, 'fs-seed-cap-pms-ia', 'IA-2', NULL, 'Primary',    @Now, 'system@spin-agent.local');

  PRINT 'CapabilityControlMappings inserted: 19';

  -- ─── Step 8e: ControlInheritances ────────────────────────────────
  -- InheritanceType enum: Inherited|Shared|Customer. FK → ControlBaselines.
  INSERT dbo.ControlInheritances
    (Id, TenantId, ControlBaselineId, ControlId, InheritanceType, Provider, CustomerResponsibility, SetBy, SetAt)
  VALUES
    -- Coastal Watch (cb-coastal)
    ('fs-seed-inh-coastal-au2', @PEO790, 'fs-seed-cb-coastal',   'AU-2', 'Inherited', 'Microsoft Azure', NULL,                                                                  'sca.peo790@navy.mil', @Now),
    ('fs-seed-inh-coastal-sc7', @PEO790, 'fs-seed-cb-coastal',   'SC-7', 'Shared',    'Microsoft Azure', 'Customer configures NSG rules; CSP provides platform firewall fabric.', 'sca.peo790@navy.mil', @Now),
    ('fs-seed-inh-coastal-ac3', @PEO790, 'fs-seed-cb-coastal',   'AC-3', 'Customer',  NULL,              'Customer defines and enforces RBAC role assignments.',                  'sca.peo790@navy.mil', @Now),
    -- Eagle Nest (cb-eaglenest)
    ('fs-seed-inh-en-au2',      @PMA290, 'fs-seed-cb-eaglenest', 'AU-2', 'Inherited', 'Microsoft Azure', NULL,                                                                  'sca.pma290@navy.mil', @Now),
    ('fs-seed-inh-en-sc7',      @PMA290, 'fs-seed-cb-eaglenest', 'SC-7', 'Shared',    'Microsoft Azure', 'Customer configures NSG rules; CSP provides platform firewall fabric.', 'sca.pma290@navy.mil', @Now),
    ('fs-seed-inh-en-cm7',      @PMA290, 'fs-seed-cb-eaglenest', 'CM-7', 'Customer',  NULL,              'Customer maintains least-functionality baseline via Azure Policy.',     'sca.pma290@navy.mil', @Now),
    -- Phoenix Falcon (cb-phoenix)
    ('fs-seed-inh-phx-au2',     @PMA290, 'fs-seed-cb-phoenix',   'AU-2', 'Inherited', 'Microsoft Azure', NULL,                                                                  'sca.pma290@navy.mil', @Now),
    ('fs-seed-inh-phx-sc7',     @PMA290, 'fs-seed-cb-phoenix',   'SC-7', 'Shared',    'Microsoft Azure', 'Customer configures NSG rules; CSP provides platform firewall fabric.', 'sca.pma290@navy.mil', @Now),
    -- Polar Bear (cb-polar)
    ('fs-seed-inh-polar-au2',   @PMS408, 'fs-seed-cb-polar',     'AU-2', 'Inherited', 'Microsoft Azure', NULL,                                                                  'sca.pms408@navy.mil', @Now),
    ('fs-seed-inh-polar-sc7',   @PMS408, 'fs-seed-cb-polar',     'SC-7', 'Shared',    'Microsoft Azure', 'Customer configures NSG rules; CSP provides platform firewall fabric.', 'sca.pms408@navy.mil', @Now),
    ('fs-seed-inh-polar-ia2',   @PMS408, 'fs-seed-cb-polar',     'IA-2', 'Customer',  NULL,              'Customer issues and manages CAC/PIV credentials.',                      'sca.pms408@navy.mil', @Now);

  PRINT 'ControlInheritances inserted: 11';

  -- ─── Step 8f: ControlImplementations + NarrativeVersions ─────────
  -- ImplementationStatus: Implemented|PartiallyImplemented|Planned|NotApplicable.
  -- ApprovalStatus (SspSectionStatus): NotStarted|Draft|UnderReview|Approved|NeedsRevision.
  INSERT dbo.ControlImplementations
    (Id, TenantId, RegisteredSystemId, ControlId, ImplementationStatus, Narrative,
     IsAutoPopulated, AiSuggested, AuthoredBy, AuthoredAt, SecurityCapabilityId,
     IsManuallyCustomized, ApprovalStatus, CurrentVersion, ApprovedVersionId)
  VALUES
    -- Coastal Watch — Monitor, fully Approved
    ('fs-seed-ci-coastal-ac2', @PEO790, @Sys_Coastal, 'AC-2', 'Implemented', 'Account lifecycle is managed through Entra ID with automated joiner/mover/leaver workflows and quarterly access recertification.', 1, 1, 'iso.peo790@navy.mil', @Now, 'fs-seed-cap-peo-ac', 0, 'Approved', 1, NULL),
    ('fs-seed-ci-coastal-au2', @PEO790, @Sys_Coastal, 'AU-2', 'Implemented', 'Auditable events are defined per DoD policy and forwarded to a Log Analytics workspace with 1-year retention.',                 1, 1, 'iso.peo790@navy.mil', @Now, 'fs-seed-cap-peo-au', 0, 'Approved', 1, NULL),
    ('fs-seed-ci-coastal-sc7', @PEO790, @Sys_Coastal, 'SC-7', 'Implemented', 'Boundary protection is enforced by Azure Firewall and per-subnet NSGs with deny-by-default egress rules.',                     1, 0, 'iso.peo790@navy.mil', @Now, 'fs-seed-cap-peo-sc', 0, 'Approved', 1, NULL),
    -- Eagle Nest — Monitor, Approved (CM-7 partial → POA&M)
    ('fs-seed-ci-en-ac2', @PMA290, @Sys_EagleNest, 'AC-2', 'Implemented',          'Account management automated through Entra ID; privileged accounts gated behind PIM just-in-time elevation.',          1, 1, 'iso.pma290@navy.mil', @Now, 'fs-seed-cap-pma-ac', 0, 'Approved', 1, NULL),
    ('fs-seed-ci-en-si4', @PMA290, @Sys_EagleNest, 'SI-4', 'PartiallyImplemented', 'Defender for Cloud monitors the workload; alert tuning for the analytics tier is tracked under an open POA&M.',     1, 1, 'iso.pma290@navy.mil', @Now, 'fs-seed-cap-pma-si', 0, 'Approved', 1, NULL),
    ('fs-seed-ci-en-cm7', @PMA290, @Sys_EagleNest, 'CM-7', 'PartiallyImplemented', 'Least-functionality baseline applied via Azure Policy; two non-compliant resources remediated under an open POA&M.', 1, 0, 'iso.pma290@navy.mil', @Now, 'fs-seed-cap-pma-cm', 0, 'Approved', 1, NULL),
    -- Phoenix Falcon — Assess, narratives still in review/draft
    ('fs-seed-ci-phx-ac2', @PMA290, @Sys_Phoenix, 'AC-2', 'Implemented',          'Account management automated through Entra ID with conditional-access enforcement.',                                1, 1, 'iso.pma290@navy.mil', @Now, 'fs-seed-cap-pma-ac', 0, 'UnderReview', 1, NULL),
    ('fs-seed-ci-phx-sc7', @PMA290, @Sys_Phoenix, 'SC-7', 'PartiallyImplemented', 'Boundary protection partially deployed; an SC-7 gap from SCA testing is tracked as an open finding and POA&M.',    1, 1, 'iso.pma290@navy.mil', @Now, 'fs-seed-cap-pma-sc', 0, 'Draft',       1, NULL),
    ('fs-seed-ci-phx-au2', @PMA290, @Sys_Phoenix, 'AU-2', 'Implemented',          'Auditable events forwarded to Log Analytics; configuration verified during SCA testing.',                           1, 0, 'iso.pma290@navy.mil', @Now, 'fs-seed-cap-pma-au', 0, 'UnderReview', 1, NULL),
    -- Polar Bear — Authorize, mixed status (DATO drivers)
    ('fs-seed-ci-polar-ia2',  @PMS408, @Sys_Polar, 'IA-2',  'Planned',             'CAC/PIV phishing-resistant MFA is planned but not yet deployed — the primary DATO driver, tracked as a critical POA&M.', 1, 0, 'iso.pms408@navy.mil', @Now, 'fs-seed-cap-pms-ia', 0, 'NeedsRevision', 1, NULL),
    ('fs-seed-ci-polar-au12', @PMS408, @Sys_Polar, 'AU-12', 'PartiallyImplemented','Audit-record generation enabled on most resources; coverage gaps on the directory tier under active remediation.',       1, 1, 'iso.pms408@navy.mil', @Now, 'fs-seed-cap-pms-au', 0, 'UnderReview',   1, NULL),
    ('fs-seed-ci-polar-cm7',  @PMS408, @Sys_Polar, 'CM-7',  'PartiallyImplemented','Least-functionality baseline drafted; several IL6 hardening items remain open and are tracked via POA&M.',              1, 0, 'iso.pms408@navy.mil', @Now, 'fs-seed-cap-pms-cm', 0, 'Draft',         1, NULL),
    ('fs-seed-ci-polar-sc7',  @PMS408, @Sys_Polar, 'SC-7',  'Implemented',         'Boundary protection enforced via Azure Firewall and NSGs across the IL6 enclave.',                                       1, 0, 'iso.pms408@navy.mil', @Now, 'fs-seed-cap-pms-sc', 0, 'UnderReview',   1, NULL);

  PRINT 'ControlImplementations inserted: 13';

  INSERT dbo.NarrativeVersions
    (Id, TenantId, ControlImplementationId, VersionNumber, Content, Status, AuthoredBy, AuthoredAt, ChangeReason)
  VALUES
    ('fs-seed-nv-coastal-ac2', @PEO790, 'fs-seed-ci-coastal-ac2', 1, 'Account lifecycle is managed through Entra ID with automated joiner/mover/leaver workflows and quarterly access recertification.', 'Approved', 'iso.peo790@navy.mil', @Now, 'Initial approved baseline narrative.'),
    ('fs-seed-nv-coastal-au2', @PEO790, 'fs-seed-ci-coastal-au2', 1, 'Auditable events are defined per DoD policy and forwarded to a Log Analytics workspace with 1-year retention.',                 'Approved', 'iso.peo790@navy.mil', @Now, 'Initial approved baseline narrative.'),
    ('fs-seed-nv-coastal-sc7', @PEO790, 'fs-seed-ci-coastal-sc7', 1, 'Boundary protection is enforced by Azure Firewall and per-subnet NSGs with deny-by-default egress rules.',                     'Approved', 'iso.peo790@navy.mil', @Now, 'Initial approved baseline narrative.'),
    ('fs-seed-nv-en-ac2', @PMA290, 'fs-seed-ci-en-ac2', 1, 'Account management automated through Entra ID; privileged accounts gated behind PIM just-in-time elevation.',          'Approved', 'iso.pma290@navy.mil', @Now, 'Initial approved baseline narrative.'),
    ('fs-seed-nv-en-si4', @PMA290, 'fs-seed-ci-en-si4', 1, 'Defender for Cloud monitors the workload; alert tuning for the analytics tier is tracked under an open POA&M.',     'Approved', 'iso.pma290@navy.mil', @Now, 'Initial approved baseline narrative.'),
    ('fs-seed-nv-en-cm7', @PMA290, 'fs-seed-ci-en-cm7', 1, 'Least-functionality baseline applied via Azure Policy; two non-compliant resources remediated under an open POA&M.', 'Approved', 'iso.pma290@navy.mil', @Now, 'Initial approved baseline narrative.'),
    ('fs-seed-nv-phx-ac2', @PMA290, 'fs-seed-ci-phx-ac2', 1, 'Account management automated through Entra ID with conditional-access enforcement.',                             'UnderReview', 'iso.pma290@navy.mil', @Now, 'Submitted for ISSM review during assessment.'),
    ('fs-seed-nv-phx-sc7', @PMA290, 'fs-seed-ci-phx-sc7', 1, 'Boundary protection partially deployed; an SC-7 gap from SCA testing is tracked as an open finding and POA&M.',   'Draft',       'iso.pma290@navy.mil', @Now, 'Draft pending SC-7 remediation.'),
    ('fs-seed-nv-phx-au2', @PMA290, 'fs-seed-ci-phx-au2', 1, 'Auditable events forwarded to Log Analytics; configuration verified during SCA testing.',                        'UnderReview', 'iso.pma290@navy.mil', @Now, 'Submitted for ISSM review during assessment.'),
    ('fs-seed-nv-polar-ia2',  @PMS408, 'fs-seed-ci-polar-ia2',  1, 'CAC/PIV phishing-resistant MFA is planned but not yet deployed — the primary DATO driver, tracked as a critical POA&M.', 'NeedsRevision', 'iso.pms408@navy.mil', @Now, 'Returned by AO — control must be implemented before re-authorization.'),
    ('fs-seed-nv-polar-au12', @PMS408, 'fs-seed-ci-polar-au12', 1, 'Audit-record generation enabled on most resources; coverage gaps on the directory tier under active remediation.',       'UnderReview', 'iso.pms408@navy.mil', @Now, 'Submitted for ISSM review.'),
    ('fs-seed-nv-polar-cm7',  @PMS408, 'fs-seed-ci-polar-cm7',  1, 'Least-functionality baseline drafted; several IL6 hardening items remain open and are tracked via POA&M.',              'Draft',       'iso.pms408@navy.mil', @Now, 'Draft pending IL6 hardening.'),
    ('fs-seed-nv-polar-sc7',  @PMS408, 'fs-seed-ci-polar-sc7',  1, 'Boundary protection enforced via Azure Firewall and NSGs across the IL6 enclave.',                                       'UnderReview', 'iso.pms408@navy.mil', @Now, 'Submitted for ISSM review.');

  PRINT 'NarrativeVersions inserted: 13';

  -- Backfill ApprovedVersionId now that the version rows exist (the
  -- ControlImplementations ↔ NarrativeVersions FK is circular).
  UPDATE ci SET ci.ApprovedVersionId = nv.Id
  FROM dbo.ControlImplementations ci
  JOIN dbo.NarrativeVersions nv
    ON nv.ControlImplementationId = ci.Id AND nv.VersionNumber = 1
  WHERE ci.Id IN ('fs-seed-ci-coastal-ac2','fs-seed-ci-coastal-au2','fs-seed-ci-coastal-sc7',
                  'fs-seed-ci-en-ac2','fs-seed-ci-en-si4','fs-seed-ci-en-cm7');

  PRINT 'ControlImplementations.ApprovedVersionId backfilled: 6';

  -- ─── Step 8g: RemediationBoards + RemediationTasks ───────────────
  -- Severity (int): Critical=0 High=1 Medium=2 Low=3 Info=4.
  -- Status   (int): Backlog=0 ToDo=1 InProgress=2 InReview=3 Blocked=4 Done=5.
  INSERT dbo.RemediationBoards
    (Id, TenantId, Name, SubscriptionId, AssessmentId, Owner, CreatedAt, UpdatedAt, IsArchived, NextTaskNumber, RowVersion)
  VALUES
    ('fs-seed-rb-en',    @PMA290, 'Eagle Nest Remediation',     'fs-sub-pma290', @Assess_EagleNest, 'iso.pma290@navy.mil', @Now, @Now, 0, 3, NEWID()),
    ('fs-seed-rb-phx',   @PMA290, 'Phoenix Falcon Remediation', 'fs-sub-pma290', @Assess_Phoenix,   'iso.pma290@navy.mil', @Now, @Now, 0, 2, NEWID()),
    ('fs-seed-rb-polar', @PMS408, 'Polar Bear Remediation',     'fs-sub-pms408', @Assess_Polar,     'iso.pms408@navy.mil', @Now, @Now, 0, 4, NEWID());

  PRINT 'RemediationBoards inserted: 3';

  INSERT dbo.RemediationTasks
    (Id, TenantId, TaskNumber, BoardId, Title, Description, ControlId, ControlFamily, Severity, Status,
     AssigneeName, DueDate, CreatedAt, UpdatedAt, AffectedResources, ValidationCriteria, FindingId, PoamItemId, CreatedBy, RowVersion)
  VALUES
    -- Eagle Nest (2 tasks ↔ its 2 POA&Ms)
    ('fs-seed-rt-en-ac2', @PMA290, 'EN-1', 'fs-seed-rb-en', 'Recertify dormant analytics accounts', 'Disable or recertify 4 dormant service accounts flagged during the AC-2 review.', 'AC-2', 'AC', 2, 1, 'engineer.pma290@navy.mil', @90d, @Now, @Now, '["sa-analytics-01","sa-analytics-02","sa-etl-03","sa-etl-04"]', 'Zero dormant accounts older than 35 days in Entra ID access review.', NULL, @P_EN_AC2, 'system@spin-agent.local', NEWID()),
    ('fs-seed-rt-en-si4', @PMA290, 'EN-2', 'fs-seed-rb-en', 'Tune Defender alerts for analytics tier', 'Reduce SI-4 false-positive rate by tuning Defender for Cloud analytics-tier alert rules.', 'SI-4', 'SI', 1, 2, 'engineer.pma290@navy.mil', @90d, @Now, @Now, '["law-eaglenest","defender-plan-servers"]', 'Defender alert precision >= 90% over a rolling 14-day window.', NULL, @P_EN_SI4, 'system@spin-agent.local', NEWID()),
    -- Phoenix Falcon (1 task ↔ SC-7 finding/POA&M)
    ('fs-seed-rt-phx-sc7', @PMA290, 'PHX-1', 'fs-seed-rb-phx', 'Close SC-7 egress gap', 'Apply deny-by-default egress NSG rules on the supplier-portal subnet identified during SCA testing.', 'SC-7', 'SC', 1, 2, 'engineer.pma290@navy.mil', @90d, @Now, @Now, '["nsg-supplier-portal","subnet-supplier-portal"]', 'No allow-all egress rules present; SC-7 retest passes.', @F_PHX_SC7, @P_PHX_SC7, 'system@spin-agent.local', NEWID()),
    -- Polar Bear (3 tasks — DATO remediation)
    ('fs-seed-rt-polar-ia2', @PMS408, 'PB-1', 'fs-seed-rb-polar', 'Deploy CAC/PIV phishing-resistant MFA', 'Implement IA-2 CAC/PIV smart-card authentication across the IL6 enclave — primary DATO driver.', 'IA-2', 'IA', 0, 1, 'engineer.pms408@navy.mil', @90d, @Now, @Now, '["entra-tenant-il6","conditional-access-policy-mfa"]', 'All privileged and standard users authenticate via CAC/PIV; legacy password auth disabled.', @F_PB_IA2, @P_PB_IA2, 'system@spin-agent.local', NEWID()),
    ('fs-seed-rt-polar-au12', @PMS408, 'PB-2', 'fs-seed-rb-polar', 'Close AU-12 audit coverage gaps', 'Enable audit-record generation on directory-tier resources missing AU-12 coverage.', 'AU-12', 'AU', 1, 2, 'engineer.pms408@navy.mil', @90d, @Now, @Now, '["dc-il6-01","dc-il6-02","law-polar"]', 'AU-12 audit events present for 100% of directory-tier resources.', @F_PB_AU12, NULL, 'system@spin-agent.local', NEWID()),
    ('fs-seed-rt-polar-cm7', @PMS408, 'PB-3', 'fs-seed-rb-polar', 'Complete CM-7 IL6 hardening', 'Apply remaining least-functionality / IL6 hardening items tracked under the CM-7 POA&M.', 'CM-7', 'CM', 2, 0, NULL, @180d, @Now, @Now, '["vmss-polar-app","policy-il6-baseline"]', 'CM-7 baseline 100% compliant in Azure Policy; no exempt resources.', @F_PB_CM7, @P_PB_CM7, 'system@spin-agent.local', NEWID());

  PRINT 'RemediationTasks inserted: 6';

  -- ─── Step 8h: Evidence ───────────────────────────────────────────
  -- EvidenceCategory (int): Configuration=0 PolicyCompliance=1 ResourceCompliance=2
  --                         SecurityAssessment=3 ActivityLog=4 Inventory=5.
  -- ContentHash is computed from Content (NOT NULL, 64-hex SHA-256).
  INSERT dbo.Evidence
    (Id, TenantId, ControlId, SubscriptionId, EvidenceType, Description, Content, CollectedAt, CollectedBy,
     AssessmentId, EvidenceCategory, ResourceId, ContentHash, CollectorIdentity, CollectionMethod)
  VALUES
    -- Coastal Watch
    ('fs-seed-ev-coastal-ac2', @PEO790, 'AC-2', 'fs-sub-peo790', 'AccessReview',  'Quarterly Entra ID access recertification export.',     'Q3 access review completed; 0 stale accounts; 142 accounts recertified.', @Now, 'soc.peo790@navy.mil', @Assess_Coastal, 1, '/subscriptions/fs-sub-peo790/accessReviews/q3', CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', 'Q3 access review completed; 0 stale accounts; 142 accounts recertified.'), 2), 'AzureAutomation', 'Automated'),
    ('fs-seed-ev-coastal-au2', @PEO790, 'AU-2', 'fs-sub-peo790', 'ConfigExport',  'Log Analytics diagnostic-settings export.',             'Diagnostic settings enabled on all resources; retention=365d; workspace=law-coastal.', @Now, 'soc.peo790@navy.mil', @Assess_Coastal, 0, '/subscriptions/fs-sub-peo790/workspaces/law-coastal', CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', 'Diagnostic settings enabled on all resources; retention=365d; workspace=law-coastal.'), 2), 'AzureResourceGraph', 'Automated'),
    ('fs-seed-ev-coastal-sc7', @PEO790, 'SC-7', 'fs-sub-peo790', 'PolicyScan',    'Azure Firewall + NSG rule export.',                     'Deny-by-default egress confirmed; 0 allow-all rules; Azure Firewall premium SKU.', @Now, 'network.peo790@navy.mil', @Assess_Coastal, 2, '/subscriptions/fs-sub-peo790/azureFirewalls/afw-coastal', CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', 'Deny-by-default egress confirmed; 0 allow-all rules; Azure Firewall premium SKU.'), 2), 'AzureResourceGraph', 'Automated'),
    -- Eagle Nest
    ('fs-seed-ev-en-ac2', @PMA290, 'AC-2', 'fs-sub-pma290', 'AccessReview', 'PIM eligibility + activation report.',           'PIM enabled for 12 privileged roles; JIT activation enforced; 4 dormant accounts flagged.', @Now, 'soc.pma290@navy.mil', @Assess_EagleNest, 1, '/subscriptions/fs-sub-pma290/pim/report', CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', 'PIM enabled for 12 privileged roles; JIT activation enforced; 4 dormant accounts flagged.'), 2), 'MicrosoftGraph', 'Automated'),
    ('fs-seed-ev-en-cm7', @PMA290, 'CM-7', 'fs-sub-pma290', 'PolicyScan',   'Azure Policy compliance export for CM-7.',      'Least-functionality initiative 96% compliant; 2 non-compliant resources tracked via POA&M.', @Now, 'config.pma290@navy.mil', @Assess_EagleNest, 1, '/subscriptions/fs-sub-pma290/policyStates/cm7', CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', 'Least-functionality initiative 96% compliant; 2 non-compliant resources tracked via POA&M.'), 2), 'AzurePolicy', 'Automated'),
    -- Phoenix Falcon
    ('fs-seed-ev-phx-sc7', @PMA290, 'SC-7', 'fs-sub-pma290', 'AssessmentResult', 'SCA SC-7 test result for the supplier-portal subnet.', 'SC-7 test FAILED — allow-all egress rule found on subnet-supplier-portal; POA&M opened.', @Now, 'sca.pma290@navy.mil', @Assess_Phoenix, 3, '/subscriptions/fs-sub-pma290/nsg/nsg-supplier-portal', CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', 'SC-7 test FAILED — allow-all egress rule found on subnet-supplier-portal; POA&M opened.'), 2), 'ManualAssessor', 'Manual'),
    -- Polar Bear
    ('fs-seed-ev-polar-ia2', @PMS408, 'IA-2', 'fs-sub-pms408', 'AssessmentResult', 'SCA IA-2 test result for the IL6 enclave.', 'IA-2 test FAILED — CAC/PIV MFA not deployed; password authentication still active. Critical.', @Now, 'sca.pms408@navy.mil', @Assess_Polar, 3, '/subscriptions/fs-sub-pms408/entra/conditionalAccess', CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', 'IA-2 test FAILED — CAC/PIV MFA not deployed; password authentication still active. Critical.'), 2), 'ManualAssessor', 'Manual'),
    ('fs-seed-ev-polar-au12', @PMS408, 'AU-12', 'fs-sub-pms408', 'ConfigExport', 'Directory-tier audit-coverage export.', 'AU-12 coverage 71% — 2 domain controllers missing audit-record generation; remediation in progress.', @Now, 'soc.pms408@navy.mil', @Assess_Polar, 2, '/subscriptions/fs-sub-pms408/resources/dc-il6', CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', 'AU-12 coverage 71% — 2 domain controllers missing audit-record generation; remediation in progress.'), 2), 'AzureResourceGraph', 'Automated');

  PRINT 'Evidence inserted: 8';

  -- ─── Step 8i: Documents (SSP / SAR) ──────────────────────────────
  -- DocumentType nvarchar(10): 'SSP' | 'SAR'. Generated artifacts exist from
  -- Implement (SSP) and Authorize/Monitor (SAR) onward — never for Prepare.
  INSERT dbo.Documents
    (Id, TenantId, DocumentType, SystemName, Framework, Content, GeneratedAt, AssessmentId, Owner, GeneratedBy,
     Metadata_SystemDescription, Metadata_AuthorizationBoundary, Metadata_DateRange, Metadata_PreparedBy, Metadata_ApprovedBy)
  VALUES
    -- Coastal Watch (Monitor) — SSP + SAR
    ('fs-seed-doc-coastal-ssp', @PEO790, 'SSP', 'Coastal Watch', 'NIST SP 800-53 Rev 5', 'System Security Plan for Coastal Watch. Categorized Low (FIPS 199). All baseline controls Implemented and approved.', @Now, @Assess_Coastal, 'iso.peo790@navy.mil', 'system@spin-agent.local', 'Maritime situational-awareness portal hosted in Azure Government.', 'Azure Gov subscription fs-sub-peo790; single VNet enclave behind Azure Firewall.', CONVERT(NVARCHAR(20), @Now, 23) + ' to ' + CONVERT(NVARCHAR(20), @3y, 23), 'iso.peo790@navy.mil', 'ao.peo790@navy.mil'),
    ('fs-seed-doc-coastal-sar', @PEO790, 'SAR', 'Coastal Watch', 'NIST SP 800-53 Rev 5', 'Security Assessment Report for Coastal Watch. Compliance 92.5%; 0 open findings; full ATO recommended.', @Now, @Assess_Coastal, 'sca.peo790@navy.mil', 'system@spin-agent.local', 'Maritime situational-awareness portal hosted in Azure Government.', 'Azure Gov subscription fs-sub-peo790; single VNet enclave behind Azure Firewall.', CONVERT(NVARCHAR(20), @Now, 23), 'sca.peo790@navy.mil', 'ao.peo790@navy.mil'),
    -- Eagle Nest (Monitor) — SSP + SAR
    ('fs-seed-doc-en-ssp', @PMA290, 'SSP', 'Eagle Nest', 'NIST SP 800-53 Rev 5', 'System Security Plan for Eagle Nest. Categorized Moderate. Two conditions tracked as POA&Ms.', @Now, @Assess_EagleNest, 'iso.pma290@navy.mil', 'system@spin-agent.local', 'ISR analytics back-end serving mission data.', 'Azure Gov subscription fs-sub-pma290; analytics enclave with Defender for Cloud.', CONVERT(NVARCHAR(20), @Now, 23) + ' to ' + CONVERT(NVARCHAR(20), @1y, 23), 'iso.pma290@navy.mil', 'ao.pma290@navy.mil'),
    ('fs-seed-doc-en-sar', @PMA290, 'SAR', 'Eagle Nest', 'NIST SP 800-53 Rev 5', 'Security Assessment Report for Eagle Nest. Compliance 78%; ATOwC with two conditions.', @Now, @Assess_EagleNest, 'sca.pma290@navy.mil', 'system@spin-agent.local', 'ISR analytics back-end serving mission data.', 'Azure Gov subscription fs-sub-pma290; analytics enclave with Defender for Cloud.', CONVERT(NVARCHAR(20), @Now, 23), 'sca.pma290@navy.mil', 'ao.pma290@navy.mil'),
    -- Phoenix Falcon (Assess) — SSP only (no SAR yet; assessment in progress)
    ('fs-seed-doc-phx-ssp', @PMA290, 'SSP', 'Phoenix Falcon', 'NIST SP 800-53 Rev 5', 'System Security Plan (draft) for Phoenix Falcon. Categorized Moderate w/ FedRAMP High overlay. Under SCA assessment.', @Now, @Assess_Phoenix, 'iso.pma290@navy.mil', 'system@spin-agent.local', 'Supply-chain logistics platform under IATT for SCA testing.', 'Azure Gov subscription fs-sub-pma290; supplier-portal subnet under assessment.', CONVERT(NVARCHAR(20), @Now, 23) + ' to ' + CONVERT(NVARCHAR(20), @90d, 23), 'iso.pma290@navy.mil', 'pending'),
    -- Polar Bear (Authorize) — SSP + SAR (DATO)
    ('fs-seed-doc-polar-ssp', @PMS408, 'SSP', 'Polar Bear', 'CNSSI 1253 IL6', 'System Security Plan for Polar Bear. NSS / IL6. Multiple controls Planned/Partial — not authorized.', @Now, @Assess_Polar, 'iso.pms408@navy.mil', 'system@spin-agent.local', 'Classified directory services enclave at IL6.', 'Azure Gov Secret subscription fs-sub-pms408; isolated IL6 enclave.', CONVERT(NVARCHAR(20), @Now, 23), 'iso.pms408@navy.mil', 'pending'),
    ('fs-seed-doc-polar-sar', @PMS408, 'SAR', 'Polar Bear', 'CNSSI 1253 IL6', 'Security Assessment Report for Polar Bear. Compliance 42%; 1 Critical + 1 High + 1 Medium finding. DATO recommended.', @Now, @Assess_Polar, 'sca.pms408@navy.mil', 'system@spin-agent.local', 'Classified directory services enclave at IL6.', 'Azure Gov Secret subscription fs-sub-pms408; isolated IL6 enclave.', CONVERT(NVARCHAR(20), @Now, 23), 'sca.pms408@navy.mil', 'ao.pms408@navy.mil');

  PRINT 'Documents inserted: 7';

  -- ─── Step 8j: ConMonPlans + ConMonReports (Monitor phase only) ───
  -- Only Coastal Watch and Eagle Nest are in Monitor; the others have none.
  INSERT dbo.ConMonPlans
    (Id, TenantId, RegisteredSystemId, AssessmentFrequency, AnnualReviewDate, ReportDistribution, SignificantChangeTriggers, CreatedBy, CreatedAt)
  VALUES
    ('fs-seed-cmp-coastal', @PEO790, @Sys_Coastal,   'Monthly', @1y, '["ao.peo790@navy.mil","iso.peo790@navy.mil","sca.peo790@navy.mil"]', '["Boundary change","New high/critical finding","Significant architecture change","Categorization change"]', 'iso.peo790@navy.mil', @Now),
    ('fs-seed-cmp-en',      @PMA290, @Sys_EagleNest, 'Monthly', @1y, '["ao.pma290@navy.mil","iso.pma290@navy.mil","sca.pma290@navy.mil"]', '["Boundary change","New high/critical finding","Significant architecture change","Categorization change"]', 'iso.pma290@navy.mil', @Now);

  PRINT 'ConMonPlans inserted: 2';

  INSERT dbo.ConMonReports
    (Id, TenantId, ConMonPlanId, RegisteredSystemId, ReportPeriod, ReportType, ComplianceScore, AuthorizedBaselineScore,
     NewFindings, ResolvedFindings, OpenPoamItems, OverduePoamItems, ReportContent, GeneratedAt, GeneratedBy,
     MonitoringEnabled, DriftAlertCount, AutoRemediationRuleCount, LastMonitoringCheck)
  VALUES
    ('fs-seed-cmr-coastal', @PEO790, 'fs-seed-cmp-coastal', @Sys_Coastal,   CONVERT(NVARCHAR(7), @Now, 126), 'Monthly', 92.5, 92.5, 0, 1, 0, 0, 'Monthly ConMon report — Coastal Watch holds steady at 92.5%. No new findings; 1 resolved. Posture green.', @Now, 'system@spin-agent.local', 1, 0, 3, @Now),
    ('fs-seed-cmr-en',      @PMA290, 'fs-seed-cmp-en',      @Sys_EagleNest, CONVERT(NVARCHAR(7), @Now, 126), 'Monthly', 78.0, 80.0, 0, 0, 2, 0, 'Monthly ConMon report — Eagle Nest at 78% vs 80% authorized baseline. 2 conditions open as POA&Ms; none overdue.', @Now, 'system@spin-agent.local', 1, 1, 2, @Now);

  PRINT 'ConMonReports inserted: 2';

  -- ─── Step 8k: PoamMilestones for the open POA&Ms ─────────────────
  INSERT dbo.PoamMilestones
    (Id, TenantId, PoamItemId, Description, TargetDate, CompletedDate, Sequence)
  VALUES
    ('fs-seed-pm-en-ac2-1',  @PMA290, @P_EN_AC2,  'Identify and disable dormant accounts.',          @90d,  NULL, 1),
    ('fs-seed-pm-en-ac2-2',  @PMA290, @P_EN_AC2,  'Verify quarterly recertification automation.',    @180d, NULL, 2),
    ('fs-seed-pm-en-si4-1',  @PMA290, @P_EN_SI4,  'Tune Defender alert rules for analytics tier.',   @90d,  NULL, 1),
    ('fs-seed-pm-phx-sc7-1', @PMA290, @P_PHX_SC7, 'Apply deny-by-default egress NSG rules.',         @90d,  NULL, 1),
    ('fs-seed-pm-pb-ia2-1',  @PMS408, @P_PB_IA2,  'Procure and configure CAC/PIV MFA tenant-wide.',  @90d,  NULL, 1),
    ('fs-seed-pm-pb-ia2-2',  @PMS408, @P_PB_IA2,  'Disable legacy password authentication.',         @180d, NULL, 2),
    ('fs-seed-pm-pb-cm7-1',  @PMS408, @P_PB_CM7,  'Complete IL6 least-functionality hardening.',     @180d, NULL, 1),
    -- Coastal Watch closed POA&Ms — milestones completed during remediation.
    ('fs-seed-pm-cw-ac2-1',  @PEO790, @P_CW_AC2,  'Deploy automated quarterly access reviews.',      @60dAgo, @45dAgo, 1),
    ('fs-seed-pm-cw-ac2-2',  @PEO790, @P_CW_AC2,  'Validate 0 stale privileged accounts.',           @60dAgo, @45dAgo, 2),
    ('fs-seed-pm-cw-sc8-1',  @PEO790, @P_CW_SC8,  'Enforce TLS 1.2+ and HSTS on all endpoints.',     @60dAgo, @60dAgo, 1);

  PRINT 'PoamMilestones inserted: 7';

  COMMIT TRAN;

  PRINT '─────────────────────────────────────────────────────────────';
  PRINT 'Flankspeed seed COMMITTED. Per-system state:';
  SELECT
    rs.Name                       AS [System],
    rs.CurrentRmfStep             AS [Phase],
    ad.DecisionType               AS [ATO],
    CAST(a.ComplianceScore AS DECIMAL(5,1)) AS [Score],
    (SELECT COUNT(*) FROM dbo.Findings  f WHERE f.AssessmentId = a.Id AND f.Status IN (0,1)) AS [OpenFindings],
    (SELECT COUNT(*) FROM dbo.PoamItems p WHERE p.RegisteredSystemId = rs.Id AND p.Status = 'Ongoing') AS [OpenPoams],
    (SELECT COUNT(*) FROM dbo.Deviations d WHERE d.RegisteredSystemId = rs.Id AND d.Status IN ('Pending','Approved')) AS [OpenDeviations]
  FROM dbo.RegisteredSystems rs
  LEFT JOIN dbo.Assessments a
    ON a.RegisteredSystemId = rs.Id AND a.Id LIKE 'flankspeed-seed-%'
  LEFT JOIN dbo.AuthorizationDecisions ad
    ON ad.RegisteredSystemId = rs.Id AND ad.Id LIKE 'fs-seed-%' AND ad.IsActive = 1
  WHERE rs.Id IN (@Sys_Coastal, @Sys_EagleEye, @Sys_EagleNest, @Sys_Phoenix, @Sys_Polar)
  ORDER BY rs.Name;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRAN;
  PRINT 'ROLLED BACK: ' + ERROR_MESSAGE();
  PRINT '  Line: ' + CAST(ERROR_LINE() AS NVARCHAR(10));
  THROW;
END CATCH;
