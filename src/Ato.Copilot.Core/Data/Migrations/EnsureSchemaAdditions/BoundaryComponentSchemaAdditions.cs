using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>SQL Server boundary-component upgrade shared by startup and the #936 migration.</summary>
public static class BoundaryComponentSchemaAdditions
{
    public static IReadOnlyList<string> SqlServerBatches { get; } = Array.AsReadOnly(new[]
    {
        ColumnsSql,
        ConstraintsSql,
    });

    public static async Task ApplySqlServerAsync(
        AtoCopilotContext db,
        CancellationToken cancellationToken = default)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // SQL Server must compile dependent DDL only after the new column exists (#987).
            foreach (var batch in SqlServerBatches)
                await db.Database.ExecuteSqlRawAsync(batch, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private const string ColumnsSql = """
        IF OBJECT_ID('BoundaryComponentAssignments', 'U') IS NULL
        CREATE TABLE BoundaryComponentAssignments (
            Id NVARCHAR(450) NOT NULL PRIMARY KEY,
            SystemComponentId NVARCHAR(450) NULL,
            CspInheritedComponentId UNIQUEIDENTIFIER NULL,
            AuthorizationBoundaryDefinitionId NVARCHAR(450) NOT NULL,
            IsInScope BIT NOT NULL DEFAULT 1,
            ExclusionRationale NVARCHAR(1000) NULL,
            InheritanceProvider NVARCHAR(500) NULL,
            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
            CreatedBy NVARCHAR(200) NULL
        );

        IF COL_LENGTH('BoundaryComponentAssignments', 'CspInheritedComponentId') IS NULL
            ALTER TABLE BoundaryComponentAssignments ADD CspInheritedComponentId UNIQUEIDENTIFIER NULL;
        """;

    private const string ConstraintsSql = """
        IF EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = 'IX_BCA_ComponentBoundary'
              AND object_id = OBJECT_ID('BoundaryComponentAssignments')
              AND (has_filter = 0 OR EXISTS (
                  SELECT 1 FROM sys.columns
                  WHERE object_id = OBJECT_ID('BoundaryComponentAssignments')
                    AND name = 'SystemComponentId' AND is_nullable = 0)))
            DROP INDEX IX_BCA_ComponentBoundary ON BoundaryComponentAssignments;

        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('BoundaryComponentAssignments')
              AND name = 'SystemComponentId' AND is_nullable = 0)
        BEGIN
            -- Preserve the deployed width/collation and existing component FK while relaxing nullability.
            -- COLLATE requires a bare catalog collation token, not a QUOTENAME-delimited identifier.
            DECLARE @componentColumn NVARCHAR(500);
            SELECT @componentColumn = N'NVARCHAR('
                + CASE WHEN max_length = -1 THEN N'MAX' ELSE CONVERT(NVARCHAR(10), max_length / 2) END
                + N') COLLATE ' + collation_name + N' NULL'
            FROM sys.columns
            WHERE object_id = OBJECT_ID('BoundaryComponentAssignments') AND name = 'SystemComponentId';
            EXEC(N'ALTER TABLE BoundaryComponentAssignments ALTER COLUMN SystemComponentId ' + @componentColumn);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BCA_ComponentBoundary' AND object_id = OBJECT_ID('BoundaryComponentAssignments'))
            CREATE UNIQUE INDEX IX_BCA_ComponentBoundary
                ON BoundaryComponentAssignments (SystemComponentId, AuthorizationBoundaryDefinitionId)
                WHERE SystemComponentId IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BCA_CspComponentBoundary' AND object_id = OBJECT_ID('BoundaryComponentAssignments'))
            CREATE UNIQUE INDEX IX_BCA_CspComponentBoundary
                ON BoundaryComponentAssignments (CspInheritedComponentId, AuthorizationBoundaryDefinitionId)
                WHERE CspInheritedComponentId IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BCA_BoundaryId' AND object_id = OBJECT_ID('BoundaryComponentAssignments'))
            CREATE INDEX IX_BCA_BoundaryId ON BoundaryComponentAssignments (AuthorizationBoundaryDefinitionId);

        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_BCA_ExactlyOneComponent' AND parent_object_id = OBJECT_ID('BoundaryComponentAssignments'))
            ALTER TABLE BoundaryComponentAssignments ADD CONSTRAINT CK_BCA_ExactlyOneComponent CHECK (
                (SystemComponentId IS NOT NULL AND CspInheritedComponentId IS NULL) OR
                (SystemComponentId IS NULL AND CspInheritedComponentId IS NOT NULL));

        IF OBJECT_ID('CspInheritedComponents', 'U') IS NOT NULL
            AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_BoundaryComponentAssignments_CspInheritedComponents_CspInheritedComponentId' AND parent_object_id = OBJECT_ID('BoundaryComponentAssignments'))
            ALTER TABLE BoundaryComponentAssignments ADD CONSTRAINT FK_BoundaryComponentAssignments_CspInheritedComponents_CspInheritedComponentId
                FOREIGN KEY (CspInheritedComponentId) REFERENCES CspInheritedComponents(Id);
        """;
}
