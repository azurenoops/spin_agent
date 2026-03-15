-- =====================================================================
-- Feature 030: Dashboard Tables DDL for SQL Server
-- Creates 6 new tables and adds 2 columns to ControlImplementations
-- =====================================================================

-- 1. SecurityCapabilities
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SecurityCapabilities')
BEGIN
    CREATE TABLE [SecurityCapabilities] (
        [Id]                    NVARCHAR(36)   NOT NULL,
        [Name]                  NVARCHAR(200)  NOT NULL,
        [Provider]              NVARCHAR(200)  NOT NULL,
        [Category]              NVARCHAR(5)    NOT NULL,
        [Description]           NVARCHAR(MAX)  NOT NULL,
        [ImplementationStatus]  NVARCHAR(20)   NOT NULL,
        [Owner]                 NVARCHAR(200)  NOT NULL,
        [CreatedAt]             DATETIME2(7)   NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]             NVARCHAR(200)  NOT NULL,
        [ModifiedAt]            DATETIME2(7)   NULL,
        [ModifiedBy]            NVARCHAR(200)  NULL,
        CONSTRAINT [PK_SecurityCapabilities] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_SecurityCapability_Name] ON [SecurityCapabilities]([Name]);
    CREATE INDEX [IX_SecurityCapability_Category] ON [SecurityCapabilities]([Category]);
    CREATE INDEX [IX_SecurityCapability_Status] ON [SecurityCapabilities]([ImplementationStatus]);
END;
GO

-- 2. CapabilityControlMappings
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CapabilityControlMappings')
BEGIN
    CREATE TABLE [CapabilityControlMappings] (
        [Id]                    NVARCHAR(36)   NOT NULL,
        [SecurityCapabilityId]  NVARCHAR(36)   NOT NULL,
        [ControlId]             NVARCHAR(20)   NOT NULL,
        [RegisteredSystemId]    NVARCHAR(36)   NULL,
        [Role]                  NVARCHAR(20)   NOT NULL,
        [CreatedAt]             DATETIME2(7)   NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]             NVARCHAR(200)  NOT NULL,
        CONSTRAINT [PK_CapabilityControlMappings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CapabilityControlMappings_SecurityCapabilities] FOREIGN KEY ([SecurityCapabilityId])
            REFERENCES [SecurityCapabilities]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CapabilityControlMappings_RegisteredSystems] FOREIGN KEY ([RegisteredSystemId])
            REFERENCES [RegisteredSystems]([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_CapabilityControlMapping_Unique] ON [CapabilityControlMappings]([SecurityCapabilityId], [ControlId], [RegisteredSystemId]);
    CREATE INDEX [IX_CapabilityControlMapping_ControlId] ON [CapabilityControlMappings]([ControlId]);
    CREATE INDEX [IX_CapabilityControlMapping_SystemId] ON [CapabilityControlMappings]([RegisteredSystemId]);
END;
GO

-- 3. SystemComponents
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SystemComponents')
BEGIN
    CREATE TABLE [SystemComponents] (
        [Id]                  NVARCHAR(36)   NOT NULL,
        [RegisteredSystemId]  NVARCHAR(36)   NOT NULL,
        [Name]                NVARCHAR(200)  NOT NULL,
        [ComponentType]       NVARCHAR(20)   NOT NULL,
        [SubType]             NVARCHAR(100)  NULL,
        [Description]         NVARCHAR(2000) NULL,
        [Owner]               NVARCHAR(200)  NULL,
        [Status]              NVARCHAR(20)   NOT NULL,
        [CreatedAt]           DATETIME2(7)   NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]           NVARCHAR(200)  NOT NULL,
        [ModifiedAt]          DATETIME2(7)   NULL,
        CONSTRAINT [PK_SystemComponents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SystemComponents_RegisteredSystems] FOREIGN KEY ([RegisteredSystemId])
            REFERENCES [RegisteredSystems]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_SystemComponent_System_Type] ON [SystemComponents]([RegisteredSystemId], [ComponentType]);
    CREATE INDEX [IX_SystemComponent_Status] ON [SystemComponents]([Status]);
END;
GO

-- 4. ComponentCapabilityLinks (composite PK)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ComponentCapabilityLinks')
BEGIN
    CREATE TABLE [ComponentCapabilityLinks] (
        [SystemComponentId]     NVARCHAR(36)  NOT NULL,
        [SecurityCapabilityId]  NVARCHAR(36)  NOT NULL,
        CONSTRAINT [PK_ComponentCapabilityLinks] PRIMARY KEY ([SystemComponentId], [SecurityCapabilityId]),
        CONSTRAINT [FK_ComponentCapabilityLinks_SystemComponents] FOREIGN KEY ([SystemComponentId])
            REFERENCES [SystemComponents]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ComponentCapabilityLinks_SecurityCapabilities] FOREIGN KEY ([SecurityCapabilityId])
            REFERENCES [SecurityCapabilities]([Id]) ON DELETE CASCADE
    );
END;
GO

-- 5. ComplianceTrendSnapshots
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ComplianceTrendSnapshots')
BEGIN
    CREATE TABLE [ComplianceTrendSnapshots] (
        [Id]                  NVARCHAR(36)   NOT NULL,
        [RegisteredSystemId]  NVARCHAR(36)   NOT NULL,
        [CapturedAt]          DATETIME2(7)   NOT NULL DEFAULT GETUTCDATE(),
        [ComplianceScore]     FLOAT          NOT NULL DEFAULT 0,
        [CatICount]           INT            NOT NULL DEFAULT 0,
        [CatIICount]          INT            NOT NULL DEFAULT 0,
        [CatIIICount]         INT            NOT NULL DEFAULT 0,
        [OpenPoamCount]       INT            NOT NULL DEFAULT 0,
        [OverduePoamCount]    INT            NOT NULL DEFAULT 0,
        [NarrativeCoverage]   FLOAT          NOT NULL DEFAULT 0,
        [Source]              NVARCHAR(50)   NOT NULL DEFAULT 'Scheduled',
        CONSTRAINT [PK_ComplianceTrendSnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ComplianceTrendSnapshots_RegisteredSystems] FOREIGN KEY ([RegisteredSystemId])
            REFERENCES [RegisteredSystems]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_ComplianceTrendSnapshot_System_CapturedAt] ON [ComplianceTrendSnapshots]([RegisteredSystemId], [CapturedAt] DESC);
END;
GO

-- 6. DashboardActivities
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DashboardActivities')
BEGIN
    CREATE TABLE [DashboardActivities] (
        [Id]                  NVARCHAR(36)   NOT NULL,
        [RegisteredSystemId]  NVARCHAR(36)   NOT NULL,
        [EventType]           NVARCHAR(50)   NOT NULL,
        [Timestamp]           DATETIME2(7)   NOT NULL DEFAULT GETUTCDATE(),
        [Actor]               NVARCHAR(200)  NOT NULL,
        [Summary]             NVARCHAR(500)  NOT NULL,
        [RelatedEntityType]   NVARCHAR(100)  NULL,
        [RelatedEntityId]     NVARCHAR(100)  NULL,
        CONSTRAINT [PK_DashboardActivities] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DashboardActivities_RegisteredSystems] FOREIGN KEY ([RegisteredSystemId])
            REFERENCES [RegisteredSystems]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_DashboardActivity_System_Timestamp] ON [DashboardActivities]([RegisteredSystemId], [Timestamp] DESC);
END;
GO

-- 7. Add SecurityCapabilityId column to ControlImplementations (if missing)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ControlImplementations' AND COLUMN_NAME = 'SecurityCapabilityId')
BEGIN
    ALTER TABLE [ControlImplementations] ADD [SecurityCapabilityId] NVARCHAR(36) NULL;
    CREATE INDEX [IX_ControlImplementation_SecurityCapabilityId] ON [ControlImplementations]([SecurityCapabilityId]);
    ALTER TABLE [ControlImplementations] ADD CONSTRAINT [FK_ControlImplementations_SecurityCapabilities]
        FOREIGN KEY ([SecurityCapabilityId]) REFERENCES [SecurityCapabilities]([Id]) ON DELETE SET NULL;
END;
GO

-- 8. Add IsManuallyCustomized column to ControlImplementations (if missing)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ControlImplementations' AND COLUMN_NAME = 'IsManuallyCustomized')
BEGIN
    ALTER TABLE [ControlImplementations] ADD [IsManuallyCustomized] BIT NOT NULL DEFAULT 0;
END;
GO

PRINT 'Dashboard DDL applied successfully.';
GO
