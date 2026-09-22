-- ============================================================================
--  Security Posture Intelligence Navigator — Reassign the 5 demo RegisteredSystems from the system tenant
--  (00...000 / Ato.Copilot.System) to the 3 mission-owner tenants of the
--  Flankspeed CSP portfolio (PEO-790 / PMA 290 / PMS 408).
--
--  Idempotent: re-running is a no-op (UPDATEs only touch rows whose TenantId
--  is still the system tenant).
--
--  Every tenant-scoped dependent row that references one of these systems
--  moves to the same target tenant so the global TenantScoped query filter
--  stays consistent (Constitution § Tenant Isolation NON-NEGOTIABLE).
--
--  Mapping:
--    Coastal Watch  → PEO-790
--    Eagle Eye      → PEO-790
--    Eagle Nest     → PMA 290
--    Phoenix Falcon → PMA 290
--    Polar Bear     → PMS 408
--
--  Run via:
--    scripts/seed-flankspeed.sh
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
USE AtoCopilot;

IF OBJECT_ID('tempdb..#Map') IS NOT NULL DROP TABLE #Map;
CREATE TABLE #Map (SystemId UNIQUEIDENTIFIER PRIMARY KEY, TargetTenantId UNIQUEIDENTIFIER NOT NULL);
INSERT #Map VALUES
  ('44a2f788-4808-4b88-bcf4-fea1d63779ab', '4A5E1C76-4743-48E8-9C19-DDAF27CE376F'),
  ('c267a009-dd48-4555-a194-7edecbbb45b4', '4A5E1C76-4743-48E8-9C19-DDAF27CE376F'),
  ('9b6bd346-d188-4d51-a1ae-7371f8b93607', '62373322-306C-462C-A29D-41491287AD49'),
  ('32ef294f-e725-4430-94cc-785a7b47398c', '62373322-306C-462C-A29D-41491287AD49'),
  ('939b29a5-3010-4dd1-8c2c-420e4d74eca3', '4A87B9F7-027C-445F-B3EE-6F3DC3D87F60');

BEGIN TRY
  BEGIN TRAN;

  -- Step 1: move the systems themselves ------------------------------
  UPDATE rs
     SET rs.TenantId = m.TargetTenantId
    FROM dbo.RegisteredSystems rs
    JOIN #Map m ON CAST(m.SystemId AS NVARCHAR(72)) = rs.Id
   WHERE rs.TenantId <> m.TargetTenantId;
  PRINT CONCAT('RegisteredSystems moved this run: ', @@ROWCOUNT);

  -- Step 2: cascade through every tenant-scoped dependent table ------
  DECLARE @TableName SYSNAME, @RefCol SYSNAME, @sql NVARCHAR(MAX), @totalDep BIGINT = 0;
  DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT t.name, c.name
      FROM sys.tables t
      JOIN sys.columns c ON c.object_id = t.object_id
     WHERE c.name IN ('RegisteredSystemId', 'SystemId')
       AND EXISTS (SELECT 1 FROM sys.columns c2 WHERE c2.object_id = t.object_id AND c2.name = 'TenantId')
       AND t.name <> 'RegisteredSystems';

  OPEN cur;
  FETCH NEXT FROM cur INTO @TableName, @RefCol;
  WHILE @@FETCH_STATUS = 0
  BEGIN
    SET @sql = N'
      UPDATE c
         SET c.TenantId = m.TargetTenantId
        FROM dbo.' + QUOTENAME(@TableName) + N' c
        JOIN #Map m ON CAST(m.SystemId AS NVARCHAR(72)) = c.' + QUOTENAME(@RefCol) + N'
       WHERE c.TenantId <> m.TargetTenantId;';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM cur INTO @TableName, @RefCol;
  END
  CLOSE cur;
  DEALLOCATE cur;

  COMMIT TRAN;
  PRINT 'Reassignment COMMITTED.';
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRAN;
  PRINT 'ROLLED BACK: ' + ERROR_MESSAGE();
  THROW;
END CATCH;
