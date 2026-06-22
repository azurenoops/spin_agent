using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Feature418_EnhancedEvidenceAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─── ControlEvidenceMapping ────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ControlEvidenceMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ControlId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    EvidenceSourceType = table.Column<int>(type: "int", nullable: false),
                    EvidenceReferenceId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MappingNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrelationScore = table.Column<double>(type: "float", nullable: false),
                    MappedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MappedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlEvidenceMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ControlEvidenceMapping_ControlId_Sub",
                table: "ControlEvidenceMappings",
                columns: new[] { "ControlId", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ControlEvidenceMapping_MappedAt",
                table: "ControlEvidenceMappings",
                column: "MappedAt");

            // ─── EvidenceFreshnessRecord ────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "EvidenceFreshnessRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ControlId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    LastCollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FreshnessWindowHours = table.Column<int>(type: "int", nullable: false),
                    EvidenceSourceType = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceFreshnessRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceFreshness_ControlId_Sub_Unique",
                table: "EvidenceFreshnessRecords",
                columns: new[] { "ControlId", "SubscriptionId" },
                unique: true);

            // ─── EvidenceAuditEvent ─────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "EvidenceAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ControlId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Metadata = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceAuditEvent_ControlId",
                table: "EvidenceAuditEvents",
                column: "ControlId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceAuditEvent_OccurredAt",
                table: "EvidenceAuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceAuditEvent_Sub_OccurredAt",
                table: "EvidenceAuditEvents",
                columns: new[] { "SubscriptionId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ControlEvidenceMappings");
            migrationBuilder.DropTable(name: "EvidenceFreshnessRecords");
            migrationBuilder.DropTable(name: "EvidenceAuditEvents");
        }
    }
}
