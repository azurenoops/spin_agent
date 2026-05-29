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

DECLARE @Now  DATETIME2 = SYSUTCDATETIME();
DECLARE @90d  DATETIME2 = DATEADD(DAY, 90, @Now);
DECLARE @180d DATETIME2 = DATEADD(DAY, 180, @Now);
DECLARE @1y   DATETIME2 = DATEADD(YEAR, 1, @Now);
DECLARE @3y   DATETIME2 = DATEADD(YEAR, 3, @Now);

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

DECLARE @D_PB_CM7  NVARCHAR(72) = 'fs-seed-d-pb-cm7';

BEGIN TRY
  BEGIN TRAN;

  -- ─── Cleanup (delete-then-insert pattern) ────────────────────────
  -- Wipe any prior Flankspeed seed rows (FK-safe order: children first).
  -- Anything tagged with our deterministic 'flankspeed-seed-' /
  -- 'fs-seed-' prefix is owned by this script; nothing else is touched.
  DELETE FROM dbo.Deviations              WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.PoamItems               WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.Findings                WHERE Id LIKE 'flankspeed-seed-%';
  DELETE FROM dbo.AuthorizationDecisions  WHERE Id LIKE 'fs-seed-%';
  DELETE FROM dbo.Assessments             WHERE Id LIKE 'flankspeed-seed-%';

  -- ─── Step 1: align RegisteredSystems.CurrentRmfStep ──────────────
  -- RmfPhase enum: Prepare=0, Categorize=1, Select=2, Implement=3,
  --                Assess=4, Authorize=5, Monitor=6
  -- EF stores enums as string by configuration here, so we use names.
  UPDATE dbo.RegisteredSystems SET CurrentRmfStep = 'Monitor'   WHERE Id = @Sys_Coastal;
  UPDATE dbo.RegisteredSystems SET CurrentRmfStep = 'Prepare'   WHERE Id = @Sys_EagleEye;
  UPDATE dbo.RegisteredSystems SET CurrentRmfStep = 'Monitor'   WHERE Id = @Sys_EagleNest;
  UPDATE dbo.RegisteredSystems SET CurrentRmfStep = 'Assess'    WHERE Id = @Sys_Phoenix;
  UPDATE dbo.RegisteredSystems SET CurrentRmfStep = 'Authorize' WHERE Id = @Sys_Polar;

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
