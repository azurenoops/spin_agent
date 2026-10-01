using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class ResponsibilityDraftSchemaAdditions
{
    public static Task ApplyAsync(AtoCopilotContext db, CancellationToken ct = default) =>
        db.Database.ExecuteSqlRawAsync(db.Database.IsSqlServer() ? SqlServer : db.Database.IsSqlite()
            ? Sqlite : throw new NotSupportedException("Responsibility drafts require SQL Server or SQLite."), ct);

    private const string SqlServer = """
        IF OBJECT_ID(N'dbo.ResponsibilityDrafts', N'U') IS NULL
        BEGIN
          CREATE TABLE dbo.ResponsibilityDrafts (
            Id uniqueidentifier NOT NULL PRIMARY KEY, TenantId uniqueidentifier NOT NULL,
            RegisteredSystemId nvarchar(36) NOT NULL, ControlId nvarchar(20) NOT NULL,
            ScopeKey nvarchar(36) NOT NULL, Revision bigint NOT NULL,
            SourceHash nvarchar(64) NOT NULL, SourceJson nvarchar(max) NOT NULL,
            ValuesJson nvarchar(max) NOT NULL, SuggestionJson nvarchar(max) NOT NULL,
            Status nvarchar(24) NOT NULL,
            GenerationState nvarchar(32) NOT NULL, GenerationError nvarchar(1000) NULL,
            PreparedAt datetimeoffset NOT NULL, GeneratedAt datetimeoffset NULL,
            PreparedBy nvarchar(200) NOT NULL, ReviewedAt datetimeoffset NULL, ReviewedBy nvarchar(200) NULL,
            CONSTRAINT FK_ResponsibilityDraft_System FOREIGN KEY(RegisteredSystemId) REFERENCES dbo.RegisteredSystems(Id));
          CREATE UNIQUE INDEX IX_ResponsibilityDraft_Scope
            ON dbo.ResponsibilityDrafts(TenantId,RegisteredSystemId,ControlId,ScopeKey);
        END;
        IF OBJECT_ID(N'dbo.ResponsibilityDraftHistory', N'U') IS NULL
        BEGIN
          CREATE TABLE dbo.ResponsibilityDraftHistory (
            Id uniqueidentifier NOT NULL PRIMARY KEY, TenantId uniqueidentifier NOT NULL,
            DraftId uniqueidentifier NOT NULL, Revision bigint NOT NULL, Action nvarchar(32) NOT NULL,
            Actor nvarchar(200) NOT NULL, At datetimeoffset NOT NULL, SourceHash nvarchar(64) NOT NULL,
            ValuesJson nvarchar(max) NOT NULL, SourceJson nvarchar(max) NULL, ReviewNotes nvarchar(2000) NULL,
            CONSTRAINT FK_ResponsibilityDraftHistory_Draft FOREIGN KEY(DraftId) REFERENCES dbo.ResponsibilityDrafts(Id));
          CREATE UNIQUE INDEX IX_ResponsibilityDraftHistory_Revision
            ON dbo.ResponsibilityDraftHistory(TenantId,DraftId,Revision);
        END;
        """;
    private const string Sqlite = """
        CREATE TABLE IF NOT EXISTS ResponsibilityDrafts (
          Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, RegisteredSystemId TEXT NOT NULL,
          ControlId TEXT NOT NULL, ScopeKey TEXT NOT NULL, Revision INTEGER NOT NULL,
          SourceHash TEXT NOT NULL, SourceJson TEXT NOT NULL, ValuesJson TEXT NOT NULL,
          SuggestionJson TEXT NOT NULL, Status TEXT NOT NULL,
          GenerationState TEXT NOT NULL, GenerationError TEXT NULL, PreparedAt TEXT NOT NULL,
          GeneratedAt TEXT NULL, PreparedBy TEXT NOT NULL, ReviewedAt TEXT NULL, ReviewedBy TEXT NULL,
          FOREIGN KEY(RegisteredSystemId) REFERENCES RegisteredSystems(Id));
        CREATE UNIQUE INDEX IF NOT EXISTS IX_ResponsibilityDraft_Scope
          ON ResponsibilityDrafts(TenantId,RegisteredSystemId,ControlId,ScopeKey);
        CREATE TABLE IF NOT EXISTS ResponsibilityDraftHistory (
          Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, DraftId TEXT NOT NULL,
          Revision INTEGER NOT NULL, Action TEXT NOT NULL, Actor TEXT NOT NULL, At TEXT NOT NULL,
          SourceHash TEXT NOT NULL, ValuesJson TEXT NOT NULL, SourceJson TEXT NULL, ReviewNotes TEXT NULL,
          FOREIGN KEY(DraftId) REFERENCES ResponsibilityDrafts(Id));
        CREATE UNIQUE INDEX IF NOT EXISTS IX_ResponsibilityDraftHistory_Revision
          ON ResponsibilityDraftHistory(TenantId,DraftId,Revision);
        """;
}
