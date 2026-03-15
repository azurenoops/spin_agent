using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ControlImplementations_NarrativeVersions_ApprovedVersionId",
                table: "ControlImplementations");

            migrationBuilder.AddColumn<bool>(
                name: "NessusCredentialedScan",
                table: "ScanImportRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NessusCriticalCount",
                table: "ScanImportRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NessusHighCount",
                table: "ScanImportRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NessusHostCount",
                table: "ScanImportRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NessusInformationalCount",
                table: "ScanImportRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NessusLowCount",
                table: "ScanImportRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NessusMediumCount",
                table: "ScanImportRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NessusPoamCreatedCount",
                table: "ScanImportRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NessusControlMappingSource",
                table: "ScanImportFindings",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NessusCves",
                table: "ScanImportFindings",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<double>(
                name: "NessusCvssV2BaseScore",
                table: "ScanImportFindings",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NessusCvssV3BaseScore",
                table: "ScanImportFindings",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NessusCvssV3Vector",
                table: "ScanImportFindings",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NessusExploitAvailable",
                table: "ScanImportFindings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NessusHostIp",
                table: "ScanImportFindings",
                type: "TEXT",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NessusHostname",
                table: "ScanImportFindings",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NessusPluginFamily",
                table: "ScanImportFindings",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NessusPluginId",
                table: "ScanImportFindings",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NessusPluginName",
                table: "ScanImportFindings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NessusPort",
                table: "ScanImportFindings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NessusProtocol",
                table: "ScanImportFindings",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NessusServiceName",
                table: "ScanImportFindings",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NessusVprScore",
                table: "ScanImportFindings",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsManuallyCustomized",
                table: "ControlImplementations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SecurityCapabilityId",
                table: "ControlImplementations",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CachedResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CacheKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ToolName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Response = table.Column<string>(type: "TEXT", nullable: false),
                    CachedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TtlSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    HitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SubscriptionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComplianceTrendSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ComplianceScore = table.Column<double>(type: "REAL", nullable: false),
                    CatICount = table.Column<int>(type: "INTEGER", nullable: false),
                    CatIICount = table.Column<int>(type: "INTEGER", nullable: false),
                    CatIIICount = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenPoamCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OverduePoamCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NarrativeCoverage = table.Column<double>(type: "REAL", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceTrendSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplianceTrendSnapshots_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DashboardActivities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RelatedEntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RelatedEntityId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DashboardActivities_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    HardwareFunction = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SoftwareFunction = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Manufacturer = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    MacAddress = table.Column<string>(type: "TEXT", maxLength: 17, nullable: true),
                    Location = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Vendor = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PatchLevel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LicenseType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ParentHardwareId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    BoundaryResourceId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    DecommissionDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecommissionRationale = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryItems_AuthorizationBoundaries_BoundaryResourceId",
                        column: x => x.BoundaryResourceId,
                        principalTable: "AuthorizationBoundaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InventoryItems_InventoryItems_ParentHardwareId",
                        column: x => x.ParentHardwareId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryItems_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityCapabilities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    ImplementationStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityCapabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemComponents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ComponentType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SubType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemComponents_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapabilityControlMappings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SecurityCapabilityId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityControlMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapabilityControlMappings_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapabilityControlMappings_SecurityCapabilities_SecurityCapabilityId",
                        column: x => x.SecurityCapabilityId,
                        principalTable: "SecurityCapabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComponentCapabilityLinks",
                columns: table => new
                {
                    SystemComponentId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SecurityCapabilityId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComponentCapabilityLinks", x => new { x.SystemComponentId, x.SecurityCapabilityId });
                    table.ForeignKey(
                        name: "FK_ComponentCapabilityLinks_SecurityCapabilities_SecurityCapabilityId",
                        column: x => x.SecurityCapabilityId,
                        principalTable: "SecurityCapabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComponentCapabilityLinks_SystemComponents_SystemComponentId",
                        column: x => x.SystemComponentId,
                        principalTable: "SystemComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ControlImplementation_SecurityCapabilityId",
                table: "ControlImplementations",
                column: "SecurityCapabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_CachedResponse_CacheKey",
                table: "CachedResponses",
                column: "CacheKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CachedResponse_Tool_Sub",
                table: "CachedResponses",
                columns: new[] { "ToolName", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityControlMapping_ControlId",
                table: "CapabilityControlMappings",
                column: "ControlId");

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityControlMapping_SystemId",
                table: "CapabilityControlMappings",
                column: "RegisteredSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityControlMapping_Unique",
                table: "CapabilityControlMappings",
                columns: new[] { "SecurityCapabilityId", "ControlId", "RegisteredSystemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceTrendSnapshot_System_CapturedAt",
                table: "ComplianceTrendSnapshots",
                columns: new[] { "RegisteredSystemId", "CapturedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ComponentCapabilityLinks_SecurityCapabilityId",
                table: "ComponentCapabilityLinks",
                column: "SecurityCapabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardActivity_System_Timestamp",
                table: "DashboardActivities",
                columns: new[] { "RegisteredSystemId", "Timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItem_BoundaryResourceId",
                table: "InventoryItems",
                column: "BoundaryResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItem_System_Ip",
                table: "InventoryItems",
                columns: new[] { "RegisteredSystemId", "IpAddress" },
                unique: true,
                filter: "[IpAddress] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItem_System_Type",
                table: "InventoryItems",
                columns: new[] { "RegisteredSystemId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItem_SystemId",
                table: "InventoryItems",
                column: "RegisteredSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_ParentHardwareId",
                table: "InventoryItems",
                column: "ParentHardwareId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityCapability_Category",
                table: "SecurityCapabilities",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityCapability_Name",
                table: "SecurityCapabilities",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityCapability_Status",
                table: "SecurityCapabilities",
                column: "ImplementationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_SystemComponent_Status",
                table: "SystemComponents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SystemComponent_System_Type",
                table: "SystemComponents",
                columns: new[] { "RegisteredSystemId", "ComponentType" });

            migrationBuilder.AddForeignKey(
                name: "FK_ControlImplementations_NarrativeVersions_ApprovedVersionId",
                table: "ControlImplementations",
                column: "ApprovedVersionId",
                principalTable: "NarrativeVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ControlImplementations_SecurityCapabilities_SecurityCapabilityId",
                table: "ControlImplementations",
                column: "SecurityCapabilityId",
                principalTable: "SecurityCapabilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ControlImplementations_NarrativeVersions_ApprovedVersionId",
                table: "ControlImplementations");

            migrationBuilder.DropForeignKey(
                name: "FK_ControlImplementations_SecurityCapabilities_SecurityCapabilityId",
                table: "ControlImplementations");

            migrationBuilder.DropTable(
                name: "CachedResponses");

            migrationBuilder.DropTable(
                name: "CapabilityControlMappings");

            migrationBuilder.DropTable(
                name: "ComplianceTrendSnapshots");

            migrationBuilder.DropTable(
                name: "ComponentCapabilityLinks");

            migrationBuilder.DropTable(
                name: "DashboardActivities");

            migrationBuilder.DropTable(
                name: "InventoryItems");

            migrationBuilder.DropTable(
                name: "SecurityCapabilities");

            migrationBuilder.DropTable(
                name: "SystemComponents");

            migrationBuilder.DropIndex(
                name: "IX_ControlImplementation_SecurityCapabilityId",
                table: "ControlImplementations");

            migrationBuilder.DropColumn(
                name: "NessusCredentialedScan",
                table: "ScanImportRecords");

            migrationBuilder.DropColumn(
                name: "NessusCriticalCount",
                table: "ScanImportRecords");

            migrationBuilder.DropColumn(
                name: "NessusHighCount",
                table: "ScanImportRecords");

            migrationBuilder.DropColumn(
                name: "NessusHostCount",
                table: "ScanImportRecords");

            migrationBuilder.DropColumn(
                name: "NessusInformationalCount",
                table: "ScanImportRecords");

            migrationBuilder.DropColumn(
                name: "NessusLowCount",
                table: "ScanImportRecords");

            migrationBuilder.DropColumn(
                name: "NessusMediumCount",
                table: "ScanImportRecords");

            migrationBuilder.DropColumn(
                name: "NessusPoamCreatedCount",
                table: "ScanImportRecords");

            migrationBuilder.DropColumn(
                name: "NessusControlMappingSource",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusCves",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusCvssV2BaseScore",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusCvssV3BaseScore",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusCvssV3Vector",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusExploitAvailable",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusHostIp",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusHostname",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusPluginFamily",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusPluginId",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusPluginName",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusPort",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusProtocol",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusServiceName",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "NessusVprScore",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "IsManuallyCustomized",
                table: "ControlImplementations");

            migrationBuilder.DropColumn(
                name: "SecurityCapabilityId",
                table: "ControlImplementations");

            migrationBuilder.AddForeignKey(
                name: "FK_ControlImplementations_NarrativeVersions_ApprovedVersionId",
                table: "ControlImplementations",
                column: "ApprovedVersionId",
                principalTable: "NarrativeVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
