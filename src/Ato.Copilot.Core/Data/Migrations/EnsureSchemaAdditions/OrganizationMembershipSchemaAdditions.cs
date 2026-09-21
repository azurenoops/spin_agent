using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>
/// Additive #942 authorization schema. Also initializes the existing SystemRoleAssignment
/// model's table when absent from older migration histories. Neither path inserts grants or roles.
/// </summary>
public static class OrganizationMembershipSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (db.Database.IsSqlite())
            await db.Database.ExecuteSqlRawAsync(SqliteScript, ct);
        else if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync(SqlServerScript, ct);
        else
            throw new NotSupportedException($"Membership schema does not support {db.Database.ProviderName}.");
        logger.LogInformation("Verified organization membership schema on {Provider}", db.Database.ProviderName);
    }

    public const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS SystemRoleAssignments (
            Id TEXT NOT NULL PRIMARY KEY,
            TenantId TEXT NOT NULL,
            RegisteredSystemId TEXT NOT NULL,
            Role INTEGER NOT NULL,
            PersonId TEXT NOT NULL REFERENCES Persons(Id) ON DELETE CASCADE,
            IsInherited INTEGER NOT NULL,
            SourceOrganizationRoleAssignmentId TEXT NULL,
            CreatedAt TEXT NOT NULL,
            CreatedBy TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            UpdatedBy TEXT NOT NULL,
            RemovedAt TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_SystemRoleAssignments_PersonId ON SystemRoleAssignments(PersonId);
        CREATE TABLE IF NOT EXISTS OrganizationMemberships (
            Id TEXT NOT NULL PRIMARY KEY,
            TenantId TEXT NOT NULL REFERENCES Tenants(Id) ON DELETE CASCADE,
            DirectoryTenantId TEXT NOT NULL,
            ObjectId TEXT NOT NULL,
            PersonId TEXT NOT NULL REFERENCES Persons(Id),
            GrantedAt TEXT NOT NULL,
            GrantedBy TEXT NOT NULL,
            RevokedAt TEXT NULL,
            RevokedBy TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_OrganizationMemberships_TenantId_DirectoryTenantId_ObjectId
            ON OrganizationMemberships(TenantId, DirectoryTenantId, ObjectId);
        CREATE INDEX IF NOT EXISTS IX_OrganizationMemberships_DirectoryTenantId_ObjectId
            ON OrganizationMemberships(DirectoryTenantId, ObjectId);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_OrganizationMemberships_PersonId
            ON OrganizationMemberships(PersonId) WHERE RevokedAt IS NULL;
        """;

    public const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.SystemRoleAssignments', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.SystemRoleAssignments (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL,
                RegisteredSystemId NVARCHAR(MAX) NOT NULL,
                Role INT NOT NULL,
                PersonId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Persons(Id) ON DELETE CASCADE,
                IsInherited BIT NOT NULL,
                SourceOrganizationRoleAssignmentId UNIQUEIDENTIFIER NULL,
                CreatedAt DATETIMEOFFSET NOT NULL,
                CreatedBy UNIQUEIDENTIFIER NOT NULL,
                UpdatedAt DATETIMEOFFSET NOT NULL,
                UpdatedBy UNIQUEIDENTIFIER NOT NULL,
                RemovedAt DATETIMEOFFSET NULL
            );
            CREATE INDEX IX_SystemRoleAssignments_PersonId ON dbo.SystemRoleAssignments(PersonId);
        END;
        IF OBJECT_ID(N'dbo.OrganizationMemberships', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.OrganizationMemberships (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Tenants(Id) ON DELETE CASCADE,
                DirectoryTenantId UNIQUEIDENTIFIER NOT NULL,
                ObjectId UNIQUEIDENTIFIER NOT NULL,
                PersonId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Persons(Id),
                GrantedAt DATETIMEOFFSET NOT NULL,
                GrantedBy NVARCHAR(80) NOT NULL,
                RevokedAt DATETIMEOFFSET NULL,
                RevokedBy NVARCHAR(80) NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationMemberships_TenantId_DirectoryTenantId_ObjectId'
                       AND object_id = OBJECT_ID(N'dbo.OrganizationMemberships'))
            CREATE UNIQUE INDEX IX_OrganizationMemberships_TenantId_DirectoryTenantId_ObjectId
                ON dbo.OrganizationMemberships(TenantId, DirectoryTenantId, ObjectId);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationMemberships_DirectoryTenantId_ObjectId'
                       AND object_id = OBJECT_ID(N'dbo.OrganizationMemberships'))
            CREATE INDEX IX_OrganizationMemberships_DirectoryTenantId_ObjectId
                ON dbo.OrganizationMemberships(DirectoryTenantId, ObjectId);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationMemberships_PersonId'
                       AND object_id = OBJECT_ID(N'dbo.OrganizationMemberships'))
            CREATE UNIQUE INDEX IX_OrganizationMemberships_PersonId
                ON dbo.OrganizationMemberships(PersonId) WHERE RevokedAt IS NULL;
        """;
}
