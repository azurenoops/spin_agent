namespace Ato.Copilot.Tests.Integration.Data;

internal static class BoundaryComponentSchemaTestData
{
    public const string LegacySchema = """
        CREATE TABLE Tenants (Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
        CREATE TABLE SystemComponents (Id NVARCHAR(36) NOT NULL PRIMARY KEY);
        CREATE TABLE AuthorizationBoundaryDefinitions (Id NVARCHAR(36) NOT NULL PRIMARY KEY);
        CREATE TABLE CspInheritedComponents (Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
        CREATE TABLE BoundaryComponentAssignments (
            Id NVARCHAR(36) NOT NULL PRIMARY KEY,
            TenantId UNIQUEIDENTIFIER NOT NULL,
            SystemComponentId NVARCHAR(36) NOT NULL,
            AuthorizationBoundaryDefinitionId NVARCHAR(36) NOT NULL,
            IsInScope BIT NOT NULL,
            ExclusionRationale NVARCHAR(1000) NULL,
            InheritanceProvider NVARCHAR(200) NULL,
            CreatedAt DATETIME2 NOT NULL,
            CreatedBy NVARCHAR(200) NOT NULL,
            ModifiedAt DATETIME2 NULL,
            ModifiedBy NVARCHAR(200) NULL,
            CONSTRAINT FK_BCA_Tenant FOREIGN KEY (TenantId) REFERENCES Tenants(Id),
            CONSTRAINT FK_BCA_Component FOREIGN KEY (SystemComponentId) REFERENCES SystemComponents(Id),
            CONSTRAINT FK_BCA_Boundary FOREIGN KEY (AuthorizationBoundaryDefinitionId) REFERENCES AuthorizationBoundaryDefinitions(Id)
        );
        CREATE UNIQUE INDEX IX_BCA_ComponentBoundary ON BoundaryComponentAssignments (SystemComponentId, AuthorizationBoundaryDefinitionId);
        CREATE INDEX IX_BCA_BoundaryId ON BoundaryComponentAssignments (AuthorizationBoundaryDefinitionId);
        CREATE INDEX IX_BCA_TenantId ON BoundaryComponentAssignments (TenantId);
        INSERT INTO Tenants VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');
        INSERT INTO SystemComponents VALUES ('component-1'), ('component-2');
        INSERT INTO AuthorizationBoundaryDefinitions VALUES ('boundary-1');
        INSERT INTO CspInheritedComponents VALUES ('cccccccc-cccc-cccc-cccc-cccccccccccc');
        INSERT INTO BoundaryComponentAssignments VALUES (
            'assignment-1', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'component-1', 'boundary-1',
            0, 'Retain scope rationale', 'Retain provider', '2026-01-01', 'creator',
            '2026-02-01', 'modifier');
        """;

    public static string LegacySchemaWithNonDefaultCollation => LegacySchema.Replace(
        "NVARCHAR(36)", "NVARCHAR(36) COLLATE Latin1_General_100_BIN2", StringComparison.Ordinal);

    public const string SnapshotRows = """
        SELECT Id, TenantId, SystemComponentId, AuthorizationBoundaryDefinitionId,
               IsInScope, ExclusionRationale, InheritanceProvider, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
        FROM BoundaryComponentAssignments ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES;
        """;

    public const string SnapshotIndexes = """
        SELECT i.name, i.is_unique, i.filter_definition, p.hobt_id
        FROM sys.indexes i
        JOIN sys.partitions p ON p.object_id = i.object_id AND p.index_id = i.index_id
        WHERE i.object_id = OBJECT_ID('BoundaryComponentAssignments')
        ORDER BY i.name FOR JSON PATH, INCLUDE_NULL_VALUES;
        """;

    public const string SnapshotForeignKeys = """
        SELECT name, is_disabled, is_not_trusted
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID('BoundaryComponentAssignments')
          AND name IN ('FK_BCA_Tenant', 'FK_BCA_Component', 'FK_BCA_Boundary')
        ORDER BY name FOR JSON PATH;
        """;

    public const string ComponentCollation = """
        SELECT collation_name FROM sys.columns
        WHERE object_id = OBJECT_ID('BoundaryComponentAssignments') AND name = 'SystemComponentId';
        """;

    public const string InsertAssignment = """
        INSERT INTO BoundaryComponentAssignments (
            Id, TenantId, SystemComponentId, CspInheritedComponentId, AuthorizationBoundaryDefinitionId,
            IsInScope, CreatedAt, CreatedBy)
        VALUES (@id, @tenant, @component, @csp, 'boundary-1', 1, SYSUTCDATETIME(), 'test');
        """;
}
