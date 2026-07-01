-- ============================================================
-- Fix #583: Reassign Coastal Watch to PEO-790 tenant
-- System GUID: 92afdc15-bc6f-4648-8073-ad6af396cf97
-- Target Tenant (PEO-790): 5d43b3ad-3084-4d36-b4bd-64286c79ff60
-- ============================================================
-- Idempotent: safe to re-run. Wraps in a transaction with rollback.
-- ============================================================

BEGIN TRY
    BEGIN TRAN T1;

    DECLARE @SystemId NVARCHAR(36) = '92afdc15-bc6f-4648-8073-ad6af396cf97';
    DECLARE @TargetTenantId NVARCHAR(36) = '5d43b3ad-3084-4d36-b4bd-64286c79ff60';

    -- Pre-flight check: system must exist
    IF NOT EXISTS (SELECT 1 FROM dbo.RegisteredSystems WHERE Id = @SystemId)
    BEGIN
        RAISERROR('Coastal Watch system not found: %s', 16, 1, @SystemId);
    END

    -- Pre-flight check: target tenant must exist
    IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TargetTenantId)
    BEGIN
        PRINT 'Warning: PEO-790 tenant not found in Tenants table — proceeding anyway (may be seed data only)';
    END

    -- Capture current tenant for audit
    DECLARE @PreviousTenantId NVARCHAR(36);
    SELECT @PreviousTenantId = TenantId FROM dbo.RegisteredSystems WHERE Id = @SystemId;

    IF @PreviousTenantId = @TargetTenantId
    BEGIN
        PRINT 'Coastal Watch is already in PEO-790. No changes needed.';
        ROLLBACK TRAN T1;
        RETURN;
    END

    -- Update the system itself
    UPDATE dbo.RegisteredSystems
    SET TenantId = @TargetTenantId
    WHERE Id = @SystemId;

    PRINT CONCAT('Moved RegisteredSystem ', @SystemId, ' from ', @PreviousTenantId, ' to ', @TargetTenantId);

    -- Cascade to all tenant-scoped dependent tables
    -- Pattern: find columns named TenantId where the table also has a FK to RegisteredSystems
    DECLARE @TableName NVARCHAR(256);
    DECLARE @SQL NVARCHAR(MAX);

    DECLARE tenant_cascade CURSOR FOR
        SELECT DISTINCT t.name
        FROM sys.tables t
        INNER JOIN sys.columns c_tenant ON c_tenant.object_id = t.object_id AND c_tenant.name = 'TenantId'
        INNER JOIN sys.columns c_sys ON c_sys.object_id = t.object_id AND c_sys.name IN ('RegisteredSystemId', 'SystemId')
        WHERE t.name != 'RegisteredSystems';

    OPEN tenant_cascade;
    FETCH NEXT FROM tenant_cascade INTO @TableName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Detect the FK column name
        DECLARE @FkCol NVARCHAR(128);
        SELECT TOP 1 @FkCol = c.name
        FROM sys.columns c
        INNER JOIN sys.tables t ON t.object_id = c.object_id
        WHERE t.name = @TableName AND c.name IN ('RegisteredSystemId', 'SystemId');

        SET @SQL = N'UPDATE dbo.' + QUOTENAME(@TableName) + N'
                     SET TenantId = @TargetTenantId
                     WHERE ' + QUOTENAME(@FkCol) + N' = @SystemId';

        EXEC sp_executesql @SQL,
            N'@TargetTenantId NVARCHAR(36), @SystemId NVARCHAR(36)',
            @TargetTenantId = @TargetTenantId, @SystemId = @SystemId;

        PRINT CONCAT('  Updated TenantId in ', @TableName, ': ', @@ROWCOUNT, ' rows');

        FETCH NEXT FROM tenant_cascade INTO @TableName;
    END

    CLOSE tenant_cascade;
    DEALLOCATE tenant_cascade;

    -- Verification
    SELECT Id, Name, TenantId FROM dbo.RegisteredSystems WHERE Id = @SystemId;

    COMMIT TRAN T1;
    PRINT 'Fix #583 applied successfully.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN T1;
    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrSev INT = ERROR_SEVERITY();
    RAISERROR(@ErrMsg, @ErrSev, 1);
END CATCH
