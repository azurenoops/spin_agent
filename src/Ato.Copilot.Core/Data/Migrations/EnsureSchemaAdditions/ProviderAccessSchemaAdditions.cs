using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class ProviderAccessSchemaAdditions
{
    public static async Task ApplyAsync(
        AtoCopilotContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsSqlServer() && !db.Database.IsSqlite())
            throw new NotSupportedException("Provider access persistence requires SQLite or SQL Server.");
        var sqlServer = db.Database.IsSqlServer();
        var guid = sqlServer ? "uniqueidentifier" : "TEXT";
        var text = sqlServer ? "nvarchar(max)" : "TEXT";
        var date = sqlServer ? "datetimeoffset" : "TEXT";
        var number = sqlServer ? "bigint" : "INTEGER";
        string Short(int length) => sqlServer ? $"nvarchar({length})" : "TEXT";

        var definitions = new Dictionary<string, string>
        {
            ["ProviderPrincipals"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL,
                DisplayName {Short(256)} NOT NULL, Email {Short(320)} NULL, State {Short(32)} NOT NULL,
                Revision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL,
                UpdatedAt {date} NULL, UpdatedBy {Short(254)} NULL, UNIQUE(ProviderId,Id),
                FOREIGN KEY(ProviderId) REFERENCES CspProfiles(Id)
                """,
            ["ProviderDirectoryMatches"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, PrincipalId {guid} NOT NULL,
                DirectoryTenantId {guid} NOT NULL, ObjectId {guid} NOT NULL, State {Short(32)} NOT NULL,
                Provenance {Short(64)} NOT NULL, VerifiedAt {date} NOT NULL, VerifiedBy {Short(254)} NOT NULL,
                Revision {number} NOT NULL, UNIQUE(ProviderId,Id),
                FOREIGN KEY(ProviderId,PrincipalId) REFERENCES ProviderPrincipals(ProviderId,Id)
                """,
            ["ProviderMemberships"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, PrincipalId {guid} NOT NULL,
                DirectoryMatchId {guid} NOT NULL, State {Short(32)} NOT NULL,
                GrantedAt {date} NOT NULL, GrantedBy {Short(254)} NOT NULL, ExpiresAt {date} NULL,
                RevokedAt {date} NULL, RevokedBy {Short(254)} NULL, RevocationReason {Short(2000)} NULL,
                Revision {number} NOT NULL, UNIQUE(ProviderId,Id),
                FOREIGN KEY(ProviderId,PrincipalId) REFERENCES ProviderPrincipals(ProviderId,Id),
                FOREIGN KEY(ProviderId,DirectoryMatchId) REFERENCES ProviderDirectoryMatches(ProviderId,Id)
                """,
            ["ProviderRoleAssignments"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, MembershipId {guid} NOT NULL,
                Role {Short(32)} NOT NULL, PortfolioId {guid} NULL, OfferingId {guid} NULL,
                GrantedAt {date} NOT NULL, GrantedBy {Short(254)} NOT NULL,
                RemovedAt {date} NULL, RemovedBy {Short(254)} NULL, RemovalReason {Short(2000)} NULL,
                Revision {number} NOT NULL,
                FOREIGN KEY(ProviderId,MembershipId) REFERENCES ProviderMemberships(ProviderId,Id),
                FOREIGN KEY(ProviderId,PortfolioId) REFERENCES ServicePortfolios(ProviderId,Id),
                FOREIGN KEY(ProviderId,OfferingId) REFERENCES ProviderOfferings(ProviderId,Id)
                """,
            ["ProviderInvitations"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, TokenHash {Short(64)} NOT NULL UNIQUE,
                TargetEmail {Short(320)} NULL, TargetDirectoryTenantId {guid} NULL, TargetObjectId {guid} NULL,
                RequestedRolesJson {text} NOT NULL, PortfolioId {guid} NULL, OfferingId {guid} NULL,
                Status {Short(32)} NOT NULL, ExpiresAt {date} NOT NULL, CreatedAt {date} NOT NULL,
                CreatedBy {Short(254)} NOT NULL, AcceptedAt {date} NULL, AcceptedPrincipalId {guid} NULL,
                RevokedAt {date} NULL, RevokedBy {Short(254)} NULL, Revision {number} NOT NULL,
                FOREIGN KEY(ProviderId) REFERENCES CspProfiles(Id)
                """,
            ["ProviderAccessRequests"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL,
                DirectoryTenantId {guid} NOT NULL, ObjectId {guid} NOT NULL, Email {Short(320)} NULL,
                Justification {Short(2000)} NOT NULL, Status {Short(32)} NOT NULL, CreatedAt {date} NOT NULL,
                DecidedAt {date} NULL, DecidedBy {Short(254)} NULL, DecisionReason {Short(2000)} NULL,
                Revision {number} NOT NULL, FOREIGN KEY(ProviderId) REFERENCES CspProfiles(Id)
                """,
            ["ProviderContacts"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, PrincipalId {guid} NULL,
                Category {Short(32)} NOT NULL, DisplayName {Short(256)} NULL, Email {Short(320)} NULL,
                Phone {Short(40)} NULL, State {Short(32)} NOT NULL, Provenance {Short(64)} NOT NULL,
                Revision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL,
                UpdatedAt {date} NULL, UpdatedBy {Short(254)} NULL,
                FOREIGN KEY(ProviderId) REFERENCES CspProfiles(Id),
                FOREIGN KEY(ProviderId,PrincipalId) REFERENCES ProviderPrincipals(ProviderId,Id)
                """
        };
        foreach (var (table, definition) in definitions)
        {
            var statement = sqlServer
                ? $"IF OBJECT_ID(N'[{table}]', N'U') IS NULL CREATE TABLE [{table}] ({definition})"
                : $"CREATE TABLE IF NOT EXISTS [{table}] ({definition})";
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        foreach (var statement in Indexes(sqlServer))
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        logger.LogInformation("Verified additive provider access schema");
    }

    private static IEnumerable<string> Indexes(bool sqlServer)
    {
        if (sqlServer)
        {
            yield return "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProviderDirectoryMatches_Verified' AND object_id=OBJECT_ID(N'ProviderDirectoryMatches')) CREATE UNIQUE INDEX IX_ProviderDirectoryMatches_Verified ON ProviderDirectoryMatches(DirectoryTenantId,ObjectId) WHERE State='Verified'";
            yield return "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProviderMemberships_Active' AND object_id=OBJECT_ID(N'ProviderMemberships')) CREATE UNIQUE INDEX IX_ProviderMemberships_Active ON ProviderMemberships(ProviderId,DirectoryMatchId) WHERE State='Active' AND RevokedAt IS NULL";
            yield return "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProviderRoleAssignments_Active' AND object_id=OBJECT_ID(N'ProviderRoleAssignments')) CREATE UNIQUE INDEX IX_ProviderRoleAssignments_Active ON ProviderRoleAssignments(ProviderId,MembershipId,Role,PortfolioId,OfferingId) WHERE RemovedAt IS NULL";
            yield return "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProviderAccessRequests_Pending' AND object_id=OBJECT_ID(N'ProviderAccessRequests')) CREATE UNIQUE INDEX IX_ProviderAccessRequests_Pending ON ProviderAccessRequests(DirectoryTenantId,ObjectId) WHERE Status='Pending'";
        }
        else
        {
            yield return "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProviderDirectoryMatches_Verified ON ProviderDirectoryMatches(DirectoryTenantId,ObjectId) WHERE State='Verified'";
            yield return "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProviderMemberships_Active ON ProviderMemberships(ProviderId,DirectoryMatchId) WHERE State='Active' AND RevokedAt IS NULL";
            yield return "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProviderRoleAssignments_Active ON ProviderRoleAssignments(ProviderId,MembershipId,Role,PortfolioId,OfferingId) WHERE RemovedAt IS NULL";
            yield return "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProviderAccessRequests_Pending ON ProviderAccessRequests(DirectoryTenantId,ObjectId) WHERE Status='Pending'";
        }
    }
}
