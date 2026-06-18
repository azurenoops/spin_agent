using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Migrations
{
    /// <inheritdoc />
    public partial class Feature076_OscalDecompositionDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── OscalDecompositionDrafts ─────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "OscalDecompositionDrafts",
                columns: table => new
                {
                    Id              = table.Column<string>(type: "nvarchar(36)",  maxLength: 36,  nullable: false),
                    TenantId        = table.Column<Guid>  (type: "uniqueidentifier",              nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ControlId       = table.Column<string>(type: "nvarchar(20)",  maxLength: 20,  nullable: false),
                    Status          = table.Column<int>   (type: "int",                           nullable: false),
                    GeneratedAt     = table.Column<DateTime>(type: "datetime2",                   nullable: false),
                    GeneratedBy     = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    ApprovedBy      = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    ApprovedAt      = table.Column<DateTime>(type: "datetime2",                   nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OscalDecompositionDrafts", x => x.Id);
                });

            // ── OscalDecompositionFragments ──────────────────────────────────
            migrationBuilder.CreateTable(
                name: "OscalDecompositionFragments",
                columns: table => new
                {
                    Id              = table.Column<string>(type: "nvarchar(36)",   maxLength: 36,   nullable: false),
                    TenantId        = table.Column<Guid>  (type: "uniqueidentifier",                nullable: false),
                    DraftId         = table.Column<string>(type: "nvarchar(36)",   maxLength: 36,   nullable: false),
                    StatementId     = table.Column<string>(type: "nvarchar(64)",   maxLength: 64,   nullable: false),
                    ComponentUuid   = table.Column<string>(type: "nvarchar(36)",   maxLength: 36,   nullable: true),
                    Description     = table.Column<string>(type: "nvarchar(max)",                   nullable: false),
                    SuggestedParamsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ConfidenceScore = table.Column<double> (type: "float",                           nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OscalDecompositionFragments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OscalDecompositionFragments_OscalDecompositionDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "OscalDecompositionDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── OscalImportRuns ──────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "OscalImportRuns",
                columns: table => new
                {
                    Id              = table.Column<string>(type: "nvarchar(36)",   maxLength: 36,   nullable: false),
                    TenantId        = table.Column<Guid>  (type: "uniqueidentifier",                nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ImportedBy      = table.Column<string>(type: "nvarchar(200)",  maxLength: 200,  nullable: false),
                    ImportedAt      = table.Column<DateTimeOffset>(type: "datetimeoffset",          nullable: false),
                    SchemaValid     = table.Column<bool>  (type: "bit",                             nullable: false),
                    OscalVersion    = table.Column<string>(type: "nvarchar(10)",   maxLength: 10,   nullable: false),
                    SourceDocumentUuid = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    Mode            = table.Column<int>   (type: "int",                             nullable: false),
                    ControlsCreated = table.Column<int>   (type: "int",                             nullable: false),
                    ControlsUpdated = table.Column<int>   (type: "int",                             nullable: false),
                    ControlsSkipped = table.Column<int>   (type: "int",                             nullable: false),
                    ControlsFailed  = table.Column<int>   (type: "int",                             nullable: false),
                    WarningsJson    = table.Column<string>(type: "nvarchar(max)",                   nullable: true),
                    ErrorsJson      = table.Column<string>(type: "nvarchar(max)",                   nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OscalImportRuns", x => x.Id);
                });

            // ── Indexes: OscalDecompositionDrafts ────────────────────────────

            // Primary read path: latest pending draft for a given system+control per tenant
            migrationBuilder.CreateIndex(
                name: "IX_OscalDecompositionDrafts_Tenant_System_Control_Status",
                table: "OscalDecompositionDrafts",
                columns: new[] { "TenantId", "RegisteredSystemId", "ControlId", "Status" });

            // ── Indexes: OscalDecompositionFragments ─────────────────────────

            migrationBuilder.CreateIndex(
                name: "IX_OscalDecompositionFragments_DraftId",
                table: "OscalDecompositionFragments",
                column: "DraftId");

            migrationBuilder.CreateIndex(
                name: "IX_OscalDecompositionFragments_Tenant_DraftId",
                table: "OscalDecompositionFragments",
                columns: new[] { "TenantId", "DraftId" });

            // ── Indexes: OscalImportRuns ──────────────────────────────────────

            migrationBuilder.CreateIndex(
                name: "IX_OscalImportRuns_Tenant_System",
                table: "OscalImportRuns",
                columns: new[] { "TenantId", "RegisteredSystemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OscalDecompositionFragments");
            migrationBuilder.DropTable(name: "OscalDecompositionDrafts");
            migrationBuilder.DropTable(name: "OscalImportRuns");
        }
    }
}
