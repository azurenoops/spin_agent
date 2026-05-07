using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Migrations
{
    /// <inheritdoc />
    public partial class Feature047_OnboardingWizard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PtaId",
                table: "PrivacyImpactAssessments",
                type: "TEXT",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 36);

            migrationBuilder.CreateTable(
                name: "AzureSubscriptionRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ParentTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastSeenVisibleAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AzureSubscriptionRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessContextControlFlags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    FlaggedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FlaggedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessContextControlFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessContextControlFlags_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BusinessContextDrafts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlImplementationId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    GovernanceStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AuthoredBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AuthoredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmittedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewerComments = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessContextDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessContextDrafts_ControlImplementations_ControlImplementationId",
                        column: x => x.ControlImplementationId,
                        principalTable: "ControlImplementations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmassImportSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    StorageBlobKey = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentChecksumSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ParseJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommitJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Preview = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmassImportSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NarrativeSeedDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceArtifactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IndexingStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IndexJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NarrativeSeedDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BranchQualifier = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SubOrganization = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ClassificationPosture = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    AuthoritativeRepositoryUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    PrimaryPocEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationContexts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationDocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    StorageBlobKey = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileFormat = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentChecksumSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValidationStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ValidationWarnings = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationDocumentTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    EntraObjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsLinkedToDirectory = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastPromotedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SspPdfImportSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    StorageBlobKey = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentChecksumSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExtractJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExtractionResult = table.Column<string>(type: "TEXT", nullable: true),
                    UserCorrections = table.Column<string>(type: "TEXT", nullable: true),
                    RejectReason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedSystemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SspPdfImportSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemProfileSections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SectionType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    GovernanceStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DraftContent = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    ApprovedContent = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    CompletionPercentage = table.Column<int>(type: "INTEGER", nullable: false),
                    LastEditedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastEditedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubmittedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewerComments = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemProfileSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemProfileSections_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantOnboardingStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastStep = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OnboardingStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastReRunAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantOnboardingStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WizardArtifactDependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceArtifactType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceArtifactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceVersionTag = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DependentType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DependentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DerivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsStale = table.Column<bool>(type: "INTEGER", nullable: false),
                    StaleSince = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    StaleReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastReRunJobId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WizardArtifactDependencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WizardAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    EffectsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WizardAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WizardJobStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Percent = table.Column<int>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Suggestion = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: true),
                    EnqueuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EnqueuedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WizardJobStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationRoleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationRoleAssignments_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DataTypeEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SystemProfileSectionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    DataTypeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SensitivityClassification = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ApplicableRegulations = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataTypeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataTypeEntries_SystemProfileSections_SystemProfileSectionId",
                        column: x => x.SystemProfileSectionId,
                        principalTable: "SystemProfileSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeveragedAuthorizations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SystemProfileSectionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    AuthorizationType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AuthorizationDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CoveredControlFamilies = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeveragedAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeveragedAuthorizations_SystemProfileSections_SystemProfileSectionId",
                        column: x => x.SystemProfileSectionId,
                        principalTable: "SystemProfileSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PpsEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SystemProfileSectionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    PortOrRange = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Justification = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpsEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PpsEntries_SystemProfileSections_SystemProfileSectionId",
                        column: x => x.SystemProfileSectionId,
                        principalTable: "SystemProfileSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileAuditEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SystemProfileSectionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PerformedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreviousStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    NewStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Comments = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileAuditEntries_SystemProfileSections_SystemProfileSectionId",
                        column: x => x.SystemProfileSectionId,
                        principalTable: "SystemProfileSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserCategories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SystemProfileSectionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    CategoryName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ApproximateCount = table.Column<int>(type: "INTEGER", nullable: true),
                    AccessMethod = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DataSensitivityLevel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCategories_SystemProfileSections_SystemProfileSectionId",
                        column: x => x.SystemProfileSectionId,
                        principalTable: "SystemProfileSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OnboardingStepCompletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantOnboardingStateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingStepCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnboardingStepCompletions_TenantOnboardingStates_TenantOnboardingStateId",
                        column: x => x.TenantOnboardingStateId,
                        principalTable: "TenantOnboardingStates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AzureSubReg_Tenant_Subscription",
                table: "AzureSubscriptionRegistrations",
                columns: new[] { "TenantId", "SubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BizCtxFlag_System_Control",
                table: "BusinessContextControlFlags",
                columns: new[] { "RegisteredSystemId", "ControlId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessContextDraft_CtrlImpl",
                table: "BusinessContextDrafts",
                column: "ControlImplementationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataTypeEntries_SystemProfileSectionId",
                table: "DataTypeEntries",
                column: "SystemProfileSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmassImportSession_Tenant_Status",
                table: "EmassImportSessions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LeveragedAuthorizations_SystemProfileSectionId",
                table: "LeveragedAuthorizations",
                column: "SystemProfileSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_NarrativeSeed_Tenant_Status",
                table: "NarrativeSeedDocuments",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_OnboardingStepCompletion_State_Step",
                table: "OnboardingStepCompletions",
                columns: new[] { "TenantOnboardingStateId", "StepName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OrganizationContext_TenantId",
                table: "OrganizationContexts",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgDocTemplate_Tenant_Type_Status",
                table: "OrganizationDocumentTemplates",
                columns: new[] { "TenantId", "TemplateType", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_OrgDocTemplate_DefaultPerType",
                table: "OrganizationDocumentTemplates",
                columns: new[] { "TenantId", "TemplateType" },
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRoleAssignments_PersonId",
                table: "OrganizationRoleAssignments",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgRoleAssignment_Tenant_Role_Person",
                table: "OrganizationRoleAssignments",
                columns: new[] { "TenantId", "Role", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Person_Tenant_Email",
                table: "Persons",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "UX_Person_Tenant_EntraObjectId",
                table: "Persons",
                columns: new[] { "TenantId", "EntraObjectId" },
                unique: true,
                filter: "[EntraObjectId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PpsEntries_SystemProfileSectionId",
                table: "PpsEntries",
                column: "SystemProfileSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileAuditEntry_SectionId",
                table: "ProfileAuditEntries",
                column: "SystemProfileSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SspPdfImportSession_Tenant_Batch",
                table: "SspPdfImportSessions",
                columns: new[] { "TenantId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemProfileSection_Status",
                table: "SystemProfileSections",
                column: "GovernanceStatus");

            migrationBuilder.CreateIndex(
                name: "IX_SystemProfileSection_System_Type",
                table: "SystemProfileSections",
                columns: new[] { "RegisteredSystemId", "SectionType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemProfileSection_SystemId",
                table: "SystemProfileSections",
                column: "RegisteredSystemId");

            migrationBuilder.CreateIndex(
                name: "UX_TenantOnboardingState_TenantId",
                table: "TenantOnboardingStates",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCategories_SystemProfileSectionId",
                table: "UserCategories",
                column: "SystemProfileSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_WizardArtifactDep_Dependent",
                table: "WizardArtifactDependencies",
                columns: new[] { "TenantId", "DependentType", "DependentId" });

            migrationBuilder.CreateIndex(
                name: "IX_WizardArtifactDep_Source",
                table: "WizardArtifactDependencies",
                columns: new[] { "TenantId", "SourceArtifactType", "SourceArtifactId" });

            migrationBuilder.CreateIndex(
                name: "IX_WizardAudit_Tenant_TimestampDesc",
                table: "WizardAuditEntries",
                columns: new[] { "TenantId", "Timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WizardJob_Tenant_Status",
                table: "WizardJobStatuses",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AzureSubscriptionRegistrations");

            migrationBuilder.DropTable(
                name: "BusinessContextControlFlags");

            migrationBuilder.DropTable(
                name: "BusinessContextDrafts");

            migrationBuilder.DropTable(
                name: "DataTypeEntries");

            migrationBuilder.DropTable(
                name: "EmassImportSessions");

            migrationBuilder.DropTable(
                name: "LeveragedAuthorizations");

            migrationBuilder.DropTable(
                name: "NarrativeSeedDocuments");

            migrationBuilder.DropTable(
                name: "OnboardingStepCompletions");

            migrationBuilder.DropTable(
                name: "OrganizationContexts");

            migrationBuilder.DropTable(
                name: "OrganizationDocumentTemplates");

            migrationBuilder.DropTable(
                name: "OrganizationRoleAssignments");

            migrationBuilder.DropTable(
                name: "PpsEntries");

            migrationBuilder.DropTable(
                name: "ProfileAuditEntries");

            migrationBuilder.DropTable(
                name: "SspPdfImportSessions");

            migrationBuilder.DropTable(
                name: "UserCategories");

            migrationBuilder.DropTable(
                name: "WizardArtifactDependencies");

            migrationBuilder.DropTable(
                name: "WizardAuditEntries");

            migrationBuilder.DropTable(
                name: "WizardJobStatuses");

            migrationBuilder.DropTable(
                name: "TenantOnboardingStates");

            migrationBuilder.DropTable(
                name: "Persons");

            migrationBuilder.DropTable(
                name: "SystemProfileSections");

            migrationBuilder.AlterColumn<string>(
                name: "PtaId",
                table: "PrivacyImpactAssessments",
                type: "TEXT",
                maxLength: 36,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 36,
                oldNullable: true);
        }
    }
}
