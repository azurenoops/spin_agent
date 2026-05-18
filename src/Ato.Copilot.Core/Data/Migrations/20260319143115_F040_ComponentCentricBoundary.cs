using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class F040_ComponentCentricBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureLocation",
                table: "SystemComponents",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureResourceGroup",
                table: "SystemComponents",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureResourceId",
                table: "SystemComponents",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureResourceType",
                table: "SystemComponents",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComponentId",
                table: "Findings",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BoundaryComponentAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SystemComponentId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    AuthorizationBoundaryDefinitionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    IsInScope = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExclusionRationale = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    InheritanceProvider = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoundaryComponentAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoundaryComponentAssignments_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                        column: x => x.AuthorizationBoundaryDefinitionId,
                        principalTable: "AuthorizationBoundaryDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BoundaryComponentAssignments_SystemComponents_SystemComponentId",
                        column: x => x.SystemComponentId,
                        principalTable: "SystemComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemComponent_AzureResourceId",
                table: "SystemComponents",
                column: "AzureResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceFinding_ComponentId",
                table: "Findings",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_BCA_BoundaryId",
                table: "BoundaryComponentAssignments",
                column: "AuthorizationBoundaryDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_BCA_ComponentBoundary",
                table: "BoundaryComponentAssignments",
                columns: new[] { "SystemComponentId", "AuthorizationBoundaryDefinitionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Findings_SystemComponents_ComponentId",
                table: "Findings",
                column: "ComponentId",
                principalTable: "SystemComponents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Findings_SystemComponents_ComponentId",
                table: "Findings");

            migrationBuilder.DropTable(
                name: "BoundaryComponentAssignments");

            migrationBuilder.DropIndex(
                name: "IX_SystemComponent_AzureResourceId",
                table: "SystemComponents");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceFinding_ComponentId",
                table: "Findings");

            migrationBuilder.DropColumn(
                name: "AzureLocation",
                table: "SystemComponents");

            migrationBuilder.DropColumn(
                name: "AzureResourceGroup",
                table: "SystemComponents");

            migrationBuilder.DropColumn(
                name: "AzureResourceId",
                table: "SystemComponents");

            migrationBuilder.DropColumn(
                name: "AzureResourceType",
                table: "SystemComponents");

            migrationBuilder.DropColumn(
                name: "ComponentId",
                table: "Findings");
        }
    }
}
