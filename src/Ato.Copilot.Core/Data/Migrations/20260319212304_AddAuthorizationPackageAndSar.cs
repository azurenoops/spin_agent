using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationPackageAndSar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RmfRoleName",
                table: "SystemComponents",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuthorizationPackages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    FailedArtifactType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EvidenceMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TotalArtifactCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalEvidenceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalEvidenceSize = table.Column<long>(type: "INTEGER", nullable: false),
                    ValidationPassed = table.Column<bool>(type: "INTEGER", nullable: true),
                    ValidationErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidationWarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthorizationPackages_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAssessmentReports",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SapId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AssessmentStartDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AssessmentEndDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalControlsAssessed = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalControlsPending = table.Column<int>(type: "INTEGER", nullable: false),
                    SatisfiedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NotSatisfiedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FindingsBySeverity = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    FindingsByFamily = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAssessmentReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityAssessmentReports_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecurityAssessmentReports_SecurityAssessmentPlans_SapId",
                        column: x => x.SapId,
                        principalTable: "SecurityAssessmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PackageArtifacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    AuthorizationPackageId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ArtifactType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OscalVersion = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SchemaValid = table.Column<bool>(type: "INTEGER", nullable: true),
                    SchemaErrors = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageArtifacts_AuthorizationPackages_AuthorizationPackageId",
                        column: x => x.AuthorizationPackageId,
                        principalTable: "AuthorizationPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackageValidationResults",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    AuthorizationPackageId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    IsValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValidatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageValidationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageValidationResults_AuthorizationPackages_AuthorizationPackageId",
                        column: x => x.AuthorizationPackageId,
                        principalTable: "AuthorizationPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SarSections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SecurityAssessmentReportId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SectionType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 32000, nullable: true),
                    IsAutoGenerated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SarSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SarSections_SecurityAssessmentReports_SecurityAssessmentReportId",
                        column: x => x.SecurityAssessmentReportId,
                        principalTable: "SecurityAssessmentReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ValidationFindings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    PackageValidationResultId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ArtifactType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Remediation = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    JsonPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationFindings_PackageValidationResults_PackageValidationResultId",
                        column: x => x.PackageValidationResultId,
                        principalTable: "PackageValidationResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationPackage_ExpiresAt",
                table: "AuthorizationPackages",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationPackage_GeneratedAt",
                table: "AuthorizationPackages",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationPackage_SystemId",
                table: "AuthorizationPackages",
                column: "RegisteredSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageArtifact_Package_Type",
                table: "PackageArtifacts",
                columns: new[] { "AuthorizationPackageId", "ArtifactType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageArtifact_PackageId",
                table: "PackageArtifacts",
                column: "AuthorizationPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageValidationResult_PackageId",
                table: "PackageValidationResults",
                column: "AuthorizationPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SarSection_Report_Type",
                table: "SarSections",
                columns: new[] { "SecurityAssessmentReportId", "SectionType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SAR_Status",
                table: "SecurityAssessmentReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SAR_SystemId",
                table: "SecurityAssessmentReports",
                column: "RegisteredSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAssessmentReports_SapId",
                table: "SecurityAssessmentReports",
                column: "SapId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationFinding_ResultId",
                table: "ValidationFindings",
                column: "PackageValidationResultId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageArtifacts");

            migrationBuilder.DropTable(
                name: "SarSections");

            migrationBuilder.DropTable(
                name: "ValidationFindings");

            migrationBuilder.DropTable(
                name: "SecurityAssessmentReports");

            migrationBuilder.DropTable(
                name: "PackageValidationResults");

            migrationBuilder.DropTable(
                name: "AuthorizationPackages");

            migrationBuilder.DropColumn(
                name: "RmfRoleName",
                table: "SystemComponents");
        }
    }
}
