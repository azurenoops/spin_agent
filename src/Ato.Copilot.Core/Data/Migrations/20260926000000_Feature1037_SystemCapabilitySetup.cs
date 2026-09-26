using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ato.Copilot.Core.Data.Migrations;

[DbContext(typeof(AtoCopilotContext))]
[Migration("20260926000000_Feature1037_SystemCapabilitySetup")]
public sealed class Feature1037_SystemCapabilitySetup : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("SqlServer", StringComparison.Ordinal))
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'dbo.CapabilitySetupOperations', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.CapabilitySetupOperations', N'SystemIntentJson') IS NULL
                        ALTER TABLE dbo.CapabilitySetupOperations ADD SystemIntentJson NVARCHAR(MAX) NULL;
                    IF COL_LENGTH(N'dbo.CapabilitySetupOperations', N'SystemPlanJson') IS NULL
                        ALTER TABLE dbo.CapabilitySetupOperations ADD SystemPlanJson NVARCHAR(MAX) NULL;
                END;
                IF OBJECT_ID(N'dbo.SystemCapabilityLinks', N'U') IS NOT NULL
                    AND COL_LENGTH(N'dbo.SystemCapabilityLinks', N'SupportingProviderCapabilityIdsJson') IS NULL
                    ALTER TABLE dbo.SystemCapabilityLinks ADD SupportingProviderCapabilityIdsJson NVARCHAR(MAX) NOT NULL
                        CONSTRAINT DF_SystemCapabilityLinks_SupportingProviderCapabilityIdsJson DEFAULT N'[]';
                IF OBJECT_ID(N'dbo.CapabilityResponsibilityConfirmations', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.CapabilityResponsibilityConfirmations', N'ProviderCoverageVerified') IS NULL
                        ALTER TABLE dbo.CapabilityResponsibilityConfirmations ADD ProviderCoverageVerified BIT NULL;
                    IF COL_LENGTH(N'dbo.CapabilityResponsibilityConfirmations', N'CustomerDutiesReviewed') IS NULL
                        ALTER TABLE dbo.CapabilityResponsibilityConfirmations ADD CustomerDutiesReviewed BIT NULL;
                    IF COL_LENGTH(N'dbo.CapabilityResponsibilityConfirmations', N'ReviewNotes') IS NULL
                        ALTER TABLE dbo.CapabilityResponsibilityConfirmations ADD ReviewNotes NVARCHAR(2000) NULL;
                END;
                """);
        // SQLite's idempotent, introspective startup additions own its upgrade path.
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Removing durable setup history requires an explicit data-retention migration.");
}
