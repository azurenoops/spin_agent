using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Feature039_PoamManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_PoamItems_FindingId",
                table: "PoamItems",
                newName: "IX_PoamItem_FindingId");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "PoamItems",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalTicketRef",
                table: "PoamItems",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "PoamItems",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RowVersion",
                table: "PoamItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "EvidenceArtifacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlImplementationId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    SecurityCapabilityId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ArtifactCategory = table.Column<int>(type: "INTEGER", nullable: false),
                    CollectionMethod = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UploadedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceArtifacts_ControlImplementations_ControlImplementationId",
                        column: x => x.ControlImplementationId,
                        principalTable: "ControlImplementations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EvidenceArtifacts_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvidenceArtifacts_SecurityCapabilities_SecurityCapabilityId",
                        column: x => x.SecurityCapabilityId,
                        principalTable: "SecurityCapabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PoamComponentLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    PoamItemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SystemComponentId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LinkedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoamComponentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoamComponentLinks_PoamItems_PoamItemId",
                        column: x => x.PoamItemId,
                        principalTable: "PoamItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PoamComponentLinks_SystemComponents_SystemComponentId",
                        column: x => x.SystemComponentId,
                        principalTable: "SystemComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PoamHistoryEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    PoamItemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OldValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ActingUserId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActingUserName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CascadeOrigin = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoamHistoryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoamHistoryEntries_PoamItems_PoamItemId",
                        column: x => x.PoamItemId,
                        principalTable: "PoamItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SspTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    MergeFields = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UploadedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SspTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketingIntegrations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ProjectKeyOrTableName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IssueType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    KeyVaultSecretUri = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FieldMappingJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    SyncEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketingIntegrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketingIntegrations_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    EvidenceArtifactId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReplacedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReplacedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PurgeAfter = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsFilePurged = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceVersions_EvidenceArtifacts_EvidenceArtifactId",
                        column: x => x.EvidenceArtifactId,
                        principalTable: "EvidenceArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SspExports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GeneratedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ControlCount = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SspExports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SspExports_SspTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "SspTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PoamTicketSyncs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    PoamItemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    TicketingIntegrationId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ExternalTicketId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExternalTicketUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ExternalStatusRaw = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoamTicketSyncs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoamTicketSyncs_PoamItems_PoamItemId",
                        column: x => x.PoamItemId,
                        principalTable: "PoamItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PoamTicketSyncs_TicketingIntegrations_TicketingIntegrationId",
                        column: x => x.TicketingIntegrationId,
                        principalTable: "TicketingIntegrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PoamItem_RemediationTaskId",
                table: "PoamItems",
                column: "RemediationTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceArtifact_Capability",
                table: "EvidenceArtifacts",
                column: "SecurityCapabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceArtifact_System_Control",
                table: "EvidenceArtifacts",
                columns: new[] { "RegisteredSystemId", "ControlImplementationId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceArtifact_System_IsDeleted",
                table: "EvidenceArtifacts",
                columns: new[] { "RegisteredSystemId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceArtifacts_ControlImplementationId",
                table: "EvidenceArtifacts",
                column: "ControlImplementationId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceVersion_Artifact",
                table: "EvidenceVersions",
                column: "EvidenceArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceVersion_PurgeAfter",
                table: "EvidenceVersions",
                columns: new[] { "PurgeAfter", "IsFilePurged" });

            migrationBuilder.CreateIndex(
                name: "IX_PoamComponentLink_ComponentId",
                table: "PoamComponentLinks",
                column: "SystemComponentId");

            migrationBuilder.CreateIndex(
                name: "UX_PoamComponentLink_PoamComponent",
                table: "PoamComponentLinks",
                columns: new[] { "PoamItemId", "SystemComponentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PoamHistory_ItemTimestamp",
                table: "PoamHistoryEntries",
                columns: new[] { "PoamItemId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_PoamTicketSyncs_TicketingIntegrationId",
                table: "PoamTicketSyncs",
                column: "TicketingIntegrationId");

            migrationBuilder.CreateIndex(
                name: "UX_PoamTicketSync_ItemIntegration",
                table: "PoamTicketSyncs",
                columns: new[] { "PoamItemId", "TicketingIntegrationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SspExports_TemplateId",
                table: "SspExports",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "UX_TicketingIntegration_SystemProvider",
                table: "TicketingIntegrations",
                columns: new[] { "RegisteredSystemId", "Provider" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RemediationTasks_PoamItems_PoamItemId",
                table: "RemediationTasks",
                column: "PoamItemId",
                principalTable: "PoamItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RemediationTasks_PoamItems_PoamItemId",
                table: "RemediationTasks");

            migrationBuilder.DropTable(
                name: "EvidenceVersions");

            migrationBuilder.DropTable(
                name: "PoamComponentLinks");

            migrationBuilder.DropTable(
                name: "PoamHistoryEntries");

            migrationBuilder.DropTable(
                name: "PoamTicketSyncs");

            migrationBuilder.DropTable(
                name: "SspExports");

            migrationBuilder.DropTable(
                name: "EvidenceArtifacts");

            migrationBuilder.DropTable(
                name: "TicketingIntegrations");

            migrationBuilder.DropTable(
                name: "SspTemplates");

            migrationBuilder.DropIndex(
                name: "IX_PoamItem_RemediationTaskId",
                table: "PoamItems");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "PoamItems");

            migrationBuilder.DropColumn(
                name: "ExternalTicketRef",
                table: "PoamItems");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "PoamItems");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PoamItems");

            migrationBuilder.RenameIndex(
                name: "IX_PoamItem_FindingId",
                table: "PoamItems",
                newName: "IX_PoamItems_FindingId");
        }
    }
}
