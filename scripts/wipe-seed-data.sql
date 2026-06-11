-- ============================================================================
--  ATO Copilot — Full seed data wipe (complete FK-ordered deletion)
--  Wipes ALL registered systems and every cascade-dependent row.
--  Preserves: tenant records, onboarding state, CSP profile, user accounts.
--  Run this before a full re-seed to guarantee a clean slate.
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE AtoCopilot;

PRINT '── Wipe starting ──';
DECLARE @cnt_before INT = (SELECT COUNT(*) FROM RegisteredSystems);
PRINT CONCAT('RegisteredSystems before: ', CAST(@cnt_before AS NVARCHAR(20)));

BEGIN TRANSACTION;

-- ── Step 1: Break self-referencing FK in AuthorizationDecisions ────────────
UPDATE AuthorizationDecisions SET SupersededById = NULL WHERE SupersededById IS NOT NULL;

-- ── Step 2: Deepest PoamItem children ─────────────────────────────────────
DELETE FROM PoamComponentLinks;
DELETE FROM PoamHistoryEntries;
DELETE FROM PoamMilestones;
DELETE FROM PoamTicketSyncs;
DELETE FROM RemediationTasks;

-- ── Step 3: Finding children ───────────────────────────────────────────────
DELETE FROM ScanImportFindings;

-- ── Step 4: Tables referencing multiple parents (Deviations, RiskAcceptances) ─
DELETE FROM Deviations;
DELETE FROM RiskAcceptances;

-- ── Step 5: PoamItems ─────────────────────────────────────────────────────
DELETE FROM PoamItems;

-- ── Step 6: Findings ─────────────────────────────────────────────────────
DELETE FROM Findings;

-- ── Step 7: Assessment children ───────────────────────────────────────────
DELETE FROM AssessmentRecords;
DELETE FROM ControlEffectivenessRecords;
DELETE FROM RemediationBoards;
DELETE FROM ScanImportRecords;
DELETE FROM SecurityAssessmentPlans;

-- ── Step 8: Assessments ──────────────────────────────────────────────────
DELETE FROM Assessments;

-- ── Step 9: AuthorizationDecisions ───────────────────────────────────────
DELETE FROM AuthorizationDecisions;

-- ── Step 10: ControlBaseline children ─────────────────────────────────────
DELETE FROM ControlInheritances;
DELETE FROM ControlTailorings;
DELETE FROM ControlBaselines;

-- ── Step 11: SecurityCategorization children ──────────────────────────────
DELETE FROM InformationTypes;
DELETE FROM SecurityCategorizations;

-- ── Step 12: Capability/component children (require order among themselves) ─
DELETE FROM ComponentCapabilityLinks;    -- refs SystemComponents + SecurityCapabilities
DELETE FROM SystemCapabilityLinks;       -- refs RegisteredSystems + SecurityCapabilities
DELETE FROM CapabilityControlMappings;   -- refs RegisteredSystems + SecurityCapabilities
DELETE FROM ControlImplementations;      -- refs RegisteredSystems + SecurityCapabilities
DELETE FROM SystemComponents;            -- refs RegisteredSystems (must follow ComponentCapabilityLinks)

-- ── Step 13: Remaining RegisteredSystem dependents ────────────────────────
DELETE FROM AuthorizationBoundaries;
DELETE FROM AuthorizationBoundaryDefinitions;
DELETE FROM AuthorizationPackages;
DELETE FROM BusinessContextControlFlags;
DELETE FROM ComplianceAlerts;
DELETE FROM ComplianceTrendSnapshots;
DELETE FROM ComponentSystemAssignments;
DELETE FROM ConMonPlans;
DELETE FROM ConMonReports;
DELETE FROM ContingencyPlanReferences;
DELETE FROM DashboardActivities;
DELETE FROM DeferredPrerequisites;
DELETE FROM EvidenceArtifacts;
DELETE FROM InventoryItems;
DELETE FROM PrivacyImpactAssessments;
DELETE FROM PrivacyThresholdAnalyses;
DELETE FROM RmfRoleAssignments;
DELETE FROM SecurityAssessmentReports;
DELETE FROM SignificantChanges;
DELETE FROM SspSections;
DELETE FROM SystemInterconnections;
DELETE FROM SystemProfileSections;
DELETE FROM TicketingIntegrations;

-- ── Step 14: RegisteredSystems (parent of all above) ──────────────────────
DELETE FROM RegisteredSystems;

-- ── Step 15: SecurityCapabilities (standalone, no FK to systems) ──────────
DELETE FROM SecurityCapabilities;

COMMIT TRANSACTION;

PRINT 'Wipe complete.';
DECLARE @cnt_after   INT = (SELECT COUNT(*) FROM RegisteredSystems);
DECLARE @cnt_poams   INT = (SELECT COUNT(*) FROM PoamItems);
DECLARE @cnt_find    INT = (SELECT COUNT(*) FROM Findings);
DECLARE @cnt_assess  INT = (SELECT COUNT(*) FROM Assessments);
PRINT CONCAT('RegisteredSystems after:  ', CAST(@cnt_after  AS NVARCHAR(20)));
PRINT CONCAT('POA&Ms remaining:         ', CAST(@cnt_poams  AS NVARCHAR(20)));
PRINT CONCAT('Findings remaining:       ', CAST(@cnt_find   AS NVARCHAR(20)));
PRINT CONCAT('Assessments remaining:    ', CAST(@cnt_assess AS NVARCHAR(20)));
