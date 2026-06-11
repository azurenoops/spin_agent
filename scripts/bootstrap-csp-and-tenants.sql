-- ============================================================================
--  ATO Copilot — CSP Profile + Default Tenant Bootstrap
--
--  PURPOSE: Seeds the minimum data needed for the app to start accepting
--  requests in MultiTenant mode. Without a CspProfile row in OnboardingState
--  = Active (2), TenantResolutionMiddleware blocks ALL API calls (including
--  the /api/csp/onboarding/* paths needed by seed-systems.sh) with 503.
--
--  IDEMPOTENT: All inserts are conditional (IF NOT EXISTS). Safe to re-run.
--
--  MUST run BEFORE seed-systems.sh or any API-based seed.
--
--  Enum values:
--    OnboardingState: Pending=0, InWizard=1, Active=2
--    ClassificationLevel: Unclassified=0, CUI=1, Secret=2, TopSecret=3
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE AtoCopilot;

PRINT '── CSP Bootstrap starting ──';

BEGIN TRANSACTION;

-- ── Step 1: Seed CspProfile (singleton row) ────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM CspProfiles)
BEGIN
    PRINT 'Inserting CspProfile (PEO-790 / flankspeed)...';
    INSERT INTO CspProfiles (
        Id,
        LegalEntityName,
        DisplayName,
        LogoUrl,
        PrimarySupportEmail,
        SupportPhone,
        DefaultClassificationFloor,
        OnboardingState,
        OnboardingCompletedAt,
        IdentityCompletedAt,
        SupportCompletedAt,
        ClassificationCompletedAt,
        CreatedAt,
        CreatedBy,
        UpdatedAt,
        UpdatedBy,
        RowVersion
    )
    VALUES (
        NEWID(),
        N'PEO-790',
        N'flankspeed',
        NULL,
        N'roger.potts@mil.mil',
        NULL,
        1,         -- CUI
        2,         -- Active
        SYSDATETIMEOFFSET(),
        SYSDATETIMEOFFSET(),
        SYSDATETIMEOFFSET(),
        SYSDATETIMEOFFSET(),
        SYSDATETIMEOFFSET(),
        N'system',
        NULL,
        NULL,
        NULL
    );
    PRINT '  CspProfile inserted.';
END
ELSE
BEGIN
    PRINT '  CspProfile already exists — skipping insert.';

    -- Ensure OnboardingState = Active regardless (in case it was left Pending)
    UPDATE CspProfiles
    SET OnboardingState = 2,
        OnboardingCompletedAt = ISNULL(OnboardingCompletedAt, SYSDATETIMEOFFSET()),
        IdentityCompletedAt   = ISNULL(IdentityCompletedAt,   SYSDATETIMEOFFSET()),
        SupportCompletedAt    = ISNULL(SupportCompletedAt,    SYSDATETIMEOFFSET()),
        ClassificationCompletedAt = ISNULL(ClassificationCompletedAt, SYSDATETIMEOFFSET()),
        UpdatedAt = SYSDATETIMEOFFSET(),
        UpdatedBy = N'system-bootstrap'
    WHERE OnboardingState != 2;

    IF @@ROWCOUNT > 0
        PRINT '  Updated CspProfile OnboardingState → Active.';
END

-- ── Step 2: Seed default system tenant ─────────────────────────────────────
-- The app needs at least one Active tenant so the middleware can resolve
-- a default context for unauthenticated/seed API calls.
IF NOT EXISTS (SELECT 1 FROM Tenants WHERE OnboardingState = 2)
BEGIN
    PRINT 'Inserting default system tenant (FlankSpeed-Demo-Org)...';
    INSERT INTO Tenants (
        Id,
        EntraTenantId,
        DisplayName,
        LegalEntityName,
        DoDComponent,
        PrimaryPocName,
        PrimaryPocEmail,
        PrimaryPocPhone,
        HqAddressLine1,
        HqAddressLine2,
        HqCity,
        HqStateOrProvince,
        HqPostalCode,
        OnboardingState,
        CreatedAt,
        CreatedBy
    )
    VALUES (
        NEWID(),
        NULL,
        N'FlankSpeed-Demo-Org',
        N'PEO-790 Demo Organization',
        N'Navy',
        NULL,
        NULL,
        NULL,
        NULL,
        NULL,
        NULL,
        NULL,
        NULL,
        2,         -- Active
        SYSDATETIMEOFFSET(),
        N'system'
    );
    PRINT '  Default tenant inserted.';
END
ELSE
BEGIN
    PRINT '  Active tenant already exists — skipping insert.';
END

COMMIT TRANSACTION;

PRINT '';
PRINT '── Bootstrap summary ──';
SELECT COUNT(*) AS CspProfileCount, MAX(CAST(OnboardingState AS INT)) AS MaxOnboardingState FROM CspProfiles;
SELECT COUNT(*) AS TenantCount FROM Tenants;
PRINT '── Bootstrap complete ──';
