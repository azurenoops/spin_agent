using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations;

[DbContext(typeof(AtoCopilotContext))]
[Migration("20260920113000_Feature936_BoundaryCspReferences")]
public sealed class Feature936_BoundaryCspReferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("SqlServer", StringComparison.Ordinal))
        {
            foreach (var batch in BoundaryComponentSchemaAdditions.SqlServerBatches)
                migrationBuilder.Sql(batch);
            return;
        }

        // SQLite upgrades run through the schema-introspective startup rebuild,
        // which preserves optional tenant and audit columns from newer deployments.
        migrationBuilder.Sql("SELECT 1;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("SqlServer", StringComparison.Ordinal))
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_BoundaryComponentAssignments_CspInheritedComponents_CspInheritedComponentId')
                    ALTER TABLE BoundaryComponentAssignments DROP CONSTRAINT FK_BoundaryComponentAssignments_CspInheritedComponents_CspInheritedComponentId;
                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_BCA_ExactlyOneComponent')
                    ALTER TABLE BoundaryComponentAssignments DROP CONSTRAINT CK_BCA_ExactlyOneComponent;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BCA_CspComponentBoundary' AND object_id = OBJECT_ID('BoundaryComponentAssignments'))
                    DROP INDEX IX_BCA_CspComponentBoundary ON BoundaryComponentAssignments;
                DELETE FROM BoundaryComponentAssignments WHERE SystemComponentId IS NULL;
                ALTER TABLE BoundaryComponentAssignments ALTER COLUMN SystemComponentId NVARCHAR(450) NOT NULL;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BCA_ComponentBoundary' AND object_id = OBJECT_ID('BoundaryComponentAssignments'))
                    DROP INDEX IX_BCA_ComponentBoundary ON BoundaryComponentAssignments;
                CREATE UNIQUE INDEX IX_BCA_ComponentBoundary
                    ON BoundaryComponentAssignments (SystemComponentId, AuthorizationBoundaryDefinitionId);
                IF COL_LENGTH('BoundaryComponentAssignments', 'CspInheritedComponentId') IS NOT NULL
                    ALTER TABLE BoundaryComponentAssignments DROP COLUMN CspInheritedComponentId;
                """);
            return;
        }

        migrationBuilder.Sql("SELECT 1;");
    }
}