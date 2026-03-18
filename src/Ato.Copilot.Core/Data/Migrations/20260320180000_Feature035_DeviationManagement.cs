using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Feature035_DeviationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Deviations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    DeviationType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CatSeverity = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Justification = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CompensatingControls = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    EvidenceReferences = table.Column<string>(type: "TEXT", nullable: false),
                    ExpirationDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewCycle = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FindingId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    PoamEntryId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    AuthorizationDecisionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    BoundaryDefinitionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewerRole = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ReviewerComments = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ISSMRecommendation = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ISSMRecommendedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ISSMRecommendedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevocationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deviations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deviations_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Deviations_Findings_FindingId",
                        column: x => x.FindingId,
                        principalTable: "ComplianceFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deviations_PoamItems_PoamEntryId",
                        column: x => x.PoamEntryId,
                        principalTable: "PoamItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deviations_AuthorizationDecisions_AuthorizationDecisionId",
                        column: x => x.AuthorizationDecisionId,
                        principalTable: "AuthorizationDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deviations_AuthorizationBoundaryDefinitions_BoundaryDefinitionId",
                        column: x => x.BoundaryDefinitionId,
                        principalTable: "AuthorizationBoundaryDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_RegisteredSystemId",
                table: "Deviations",
                column: "RegisteredSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_Status",
                table: "Deviations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_FindingId",
                table: "Deviations",
                column: "FindingId");

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_ExpirationDate",
                table: "Deviations",
                column: "ExpirationDate");

            // Add DeviationId FK column to ComplianceFindings
            migrationBuilder.AddColumn<string>(
                name: "DeviationId",
                table: "ComplianceFindings",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceFindings_DeviationId",
                table: "ComplianceFindings",
                column: "DeviationId");

            // Add DeviationId FK column to PoamItems
            migrationBuilder.AddColumn<string>(
                name: "DeviationId",
                table: "PoamItems",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PoamItems_DeviationId",
                table: "PoamItems",
                column: "DeviationId");

            // Copy existing RiskAcceptance data into Deviations.
            // The RiskAcceptances table is preserved for backward compatibility until all code
            // references are migrated (AuthorizationService, DocumentTemplateService, etc.).
            migrationBuilder.Sql(@"
                INSERT INTO Deviations (
                    Id, RegisteredSystemId, DeviationType, Status, ControlId,
                    CatSeverity, Justification, CompensatingControls, EvidenceReferences,
                    ExpirationDate, ReviewCycle, FindingId, AuthorizationDecisionId,
                    RequestedBy, RequestedAt, ReviewedBy, ReviewedAt,
                    CreatedAt
                )
                SELECT
                    ra.Id || '-dev',
                    ad.RegisteredSystemId,
                    'RiskAcceptance',
                    CASE WHEN ra.IsActive = 1 THEN 'Approved' ELSE 'Expired' END,
                    ra.ControlId,
                    ra.CatSeverity,
                    ra.Justification,
                    ra.CompensatingControl,
                    '[]',
                    ra.ExpirationDate,
                    '180d',
                    ra.FindingId,
                    ra.AuthorizationDecisionId,
                    ra.AcceptedBy,
                    ra.AcceptedAt,
                    ra.AcceptedBy,
                    ra.AcceptedAt,
                    ra.AcceptedAt
                FROM RiskAcceptances ra
                INNER JOIN AuthorizationDecisions ad ON ra.AuthorizationDecisionId = ad.Id;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove copied Deviation records
            migrationBuilder.Sql("DELETE FROM Deviations WHERE Id LIKE '%-dev';");

            migrationBuilder.DropIndex(name: "IX_PoamItems_DeviationId", table: "PoamItems");
            migrationBuilder.DropColumn(name: "DeviationId", table: "PoamItems");

            migrationBuilder.DropIndex(name: "IX_ComplianceFindings_DeviationId", table: "ComplianceFindings");
            migrationBuilder.DropColumn(name: "DeviationId", table: "ComplianceFindings");

            migrationBuilder.DropTable(name: "Deviations");
        }
    }
}
