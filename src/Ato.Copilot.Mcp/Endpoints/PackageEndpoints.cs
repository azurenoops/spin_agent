using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Dashboard API endpoints for SAR lifecycle and authorization package management.
/// </summary>
public static class PackageEndpoints
{
    private static bool TryPurpose(string? value, out PackagePurpose purpose)
    {
        purpose = PackagePurpose.Legacy;
        return value == null || (Enum.TryParse(value, true, out purpose) && Enum.IsDefined(purpose));
    }

    public static IEndpointRouteBuilder MapPackageEndpoints(this IEndpointRouteBuilder app)
    {
        var currentUser = app.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var systems = app.MapGroup("/api/v1/systems/{systemId}")
            .WithTags("AuthorizationPackage");
        systems.AddEndpointFilter(async (invocation, next) =>
        {
            var http = invocation.HttpContext;
            if (http.Request.Path.Value?.Contains("/sar", StringComparison.OrdinalIgnoreCase) != true)
                return await next(invocation);
            if (http.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            var systemId = http.Request.RouteValues["systemId"]?.ToString();
            var tenant = http.RequestServices.GetRequiredService<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContext>();
            var access = await http.RequestServices.GetRequiredService<Ato.Copilot.Core.Interfaces.Tenancy.ISystemWorkspaceAccessService>()
                .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId!, tenant.IsCspAdmin, http.RequestAborted);
            if (!access.Permissions.CanRead) return Results.NotFound();
            if (http.Request.Method != "GET" && !access.Permissions.CanGenerateSar) return Results.Forbid();
            if (http.Request.RouteValues["sarId"] is { } sarId)
            {
                var db = http.RequestServices.GetRequiredService<Ato.Copilot.Core.Data.Context.AtoCopilotContext>();
                if (!await db.SecurityAssessmentReports.AnyAsync(x => x.Id == sarId.ToString()
                    && x.RegisteredSystemId == systemId, http.RequestAborted)) return Results.NotFound();
            }
            return await next(invocation);
        });

        // ─── SAR Endpoints ─────────────────────────────────────────────────

        systems.MapPost("/sar", async (
                string systemId,
                CreateSarRequest request,
                ISecurityAssessmentReportService service,
                CancellationToken ct) =>
            {
                try
                {
                    var sar = await service.CreateSarAsync(systemId, request, "mcp-user", ct);
                    return Results.Created($"/api/v1/systems/{systemId}/sar/{sar.Id}", MapSarResponse(sar));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "SAR_CREATION_FAILED" });
                }
            })
            .WithName("CreateSar")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.GenerateSar);

        systems.MapGet("/sar", async (
                string systemId,
                ISecurityAssessmentReportService service,
                CancellationToken ct) =>
            {
                var sar = await service.GetSarForSystemAsync(systemId, ct);
                if (sar == null)
                    return Results.NotFound();
                return Results.Ok(MapSarResponse(sar));
            })
            .WithName("GetLatestSar");

        systems.MapGet("/sar/{sarId}", async (
                string systemId,
                string sarId,
                ISecurityAssessmentReportService service,
                CancellationToken ct) =>
            {
                var sar = await service.GetSarAsync(sarId, ct);
                if (sar == null || sar.RegisteredSystemId != systemId)
                    return Results.NotFound();
                return Results.Ok(MapSarResponse(sar));
            })
            .WithName("GetSar");

        systems.MapPut("/sar/{sarId}/sections/{sectionType}", async (
                string systemId,
                string sarId,
                string sectionType,
                EditSarSectionRequest request,
                ISecurityAssessmentReportService service,
                CancellationToken ct) =>
            {
                if (!Enum.TryParse<SarSectionType>(sectionType, true, out var parsedType))
                    return Results.BadRequest(new ErrorResponse { Error = $"Invalid section type: {sectionType}", ErrorCode = "INVALID_SECTION_TYPE" });

                try
                {
                    var section = await service.EditSectionAsync(sarId, parsedType, request, "mcp-user", ct);
                    return Results.Ok(new SarSectionResponse
                    {
                        SectionType = section.SectionType.ToString(),
                        Title = section.Title,
                        Content = section.Content ?? "",
                        IsAutoGenerated = section.IsAutoGenerated,
                        ModifiedBy = section.ModifiedBy,
                        ModifiedAt = section.ModifiedAt
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "EDIT_FAILED" });
                }
            })
            .WithName("EditSarSection");

        systems.MapPost("/sar/{sarId}/submit", async (
                string systemId,
                string sarId,
                ISecurityAssessmentReportService service,
                CancellationToken ct) =>
            {
                try
                {
                    var sar = await service.SubmitForReviewAsync(sarId, "mcp-user", ct);
                    return Results.Ok(MapSarResponse(sar));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "SUBMIT_FAILED" });
                }
            })
            .WithName("SubmitSarForReview");

        systems.MapPost("/sar/{sarId}/review", async (
                string systemId,
                string sarId,
                ReviewSarRequest request,
                ISecurityAssessmentReportService service,
                CancellationToken ct) =>
            {
                try
                {
                    var sar = await service.ReviewSarAsync(sarId, request, "mcp-user", ct);
                    return Results.Ok(MapSarResponse(sar));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "REVIEW_FAILED" });
                }
            })
            .WithName("ReviewSar");

        systems.MapGet("/sar/{sarId}/export", async (
                string systemId,
                string sarId,
                ISecurityAssessmentReportService service,
                CancellationToken ct) =>
            {
                try
                {
                    var stream = await service.ExportToWordAsync(sarId, ct);
                    return Results.File(stream,
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                        $"security-assessment-report-{sarId[..8]}.docx");
                }
                catch (InvalidOperationException)
                {
                    return Results.NotFound();
                }
            })
            .WithName("ExportSarWord");

        // ─── SAP Endpoints ─────────────────────────────────────────────────

        systems.MapPost("/sap", async (
                string systemId,
                ISapService service,
                CancellationToken ct) =>
            {
                try
                {
                    var input = new SapGenerationInput(SystemId: systemId);
                    var sap = await service.GenerateSapAsync(input, currentUser.CurrentUserId, ct);
                    return Results.Created($"/api/v1/systems/{systemId}/sap/{sap.SapId}", MapSapResponse(sap));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "SAP_GENERATION_FAILED" });
                }
            })
            .WithName("GenerateSap")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.GenerateSap);

        systems.MapGet("/sap", async (
                string systemId,
                ISapService service,
                CancellationToken ct) =>
            {
                var sap = await service.GetSapStatusAsync(systemId, ct);
                if (sap == null)
                    return Results.NotFound();
                return Results.Ok(MapSapResponse(sap));
            })
            .WithName("GetLatestSap");

        systems.MapPost("/sap/{sapId}/finalize", async (
                string systemId,
                string sapId,
                ISapService service,
                CancellationToken ct) =>
            {
                try
                {
                    var sap = await service.FinalizeSapAsync(sapId, currentUser.CurrentUserId, ct);
                    return Results.Ok(MapSapResponse(sap));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "SAP_FINALIZE_FAILED" });
                }
            })
            .WithName("FinalizeSap")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.FinalizeSap);

        // ─── OSCAL Schema Validation Endpoints ─────────────────────────────

        systems.MapPost("/exports/validate-oscal", async (
                string systemId,
                ValidateOscalRequest request,
                IOscalSchemaValidationService service,
                CancellationToken ct) =>
            {
                var result = await service.ValidateForSystemAsync(systemId, request.Model, ct);
                return Results.Ok(new
                {
                    isValid = result.IsValid,
                    modelType = result.ModelType,
                    schemaVersion = result.SchemaVersion,
                    violationCount = result.Violations.Count,
                    violations = result.Violations.Select(v => new
                    {
                        jsonPath = v.JsonPath,
                        message = v.Message,
                        expectedFormat = v.ExpectedFormat,
                        actualValue = v.ActualValue
                    })
                });
            })
            .WithName("ValidateOscalSchema");

        // ─── Standalone OSCAL Export Endpoints ──────────────────────────────

        systems.MapGet("/exports/oscal-poam", async (
                string systemId,
                IEmassExportService emassService,
                IOscalSchemaValidationService schemaValidator,
                CancellationToken ct) =>
            {
                var json = await emassService.ExportOscalAsync(systemId, OscalModelType.Poam, ct);
                var validation = await schemaValidator.ValidateAsync(json, "poam", ct);
                if (!validation.IsValid)
                    return OscalExportValidationFailure("poam", validation);

                return Results.Text(json, "application/json");
            })
            .WithName("ExportOscalPoam");

        systems.MapGet("/exports/oscal-assessment-results", async (
                string systemId,
                IEmassExportService emassService,
                IOscalSchemaValidationService schemaValidator,
                CancellationToken ct) =>
            {
                var json = await emassService.ExportOscalAsync(systemId, OscalModelType.AssessmentResults, ct);
                var validation = await schemaValidator.ValidateAsync(json, "assessment-results", ct);
                if (!validation.IsValid)
                    return OscalExportValidationFailure("assessment-results", validation);

                return Results.Text(json, "application/json");
            })
            .WithName("ExportOscalAssessmentResults");

        systems.MapGet("/exports/oscal-sap", async (
                string systemId,
                IOscalSapExportService sapService,
                IOscalSchemaValidationService schemaValidator,
                CancellationToken ct) =>
            {
                var json = await sapService.ExportAsync(systemId, ct);
                var validation = await schemaValidator.ValidateAsync(json, "assessment-plan", ct);
                if (!validation.IsValid)
                    return OscalExportValidationFailure("assessment-plan", validation);

                return Results.Text(json, "application/json");
            })
            .WithName("ExportOscalSap");

        // ─── Package Validation Endpoints ───────────────────────────────────

        systems.MapPost("/packages/validate", async (
                string systemId,
                string? purpose,
                HttpRequest request,
                IPackageValidationService service,
                IAuthorizationPackageService packages,
                CancellationToken ct) =>
            {
                if (!TryPurpose(purpose, out var parsedPurpose))
                    return Results.BadRequest(new ErrorResponse { Error = "Use Legacy, InitialSubmission, AuthorizedBaselineArchive or ChangeSubmission.", ErrorCode = "INVALID_PACKAGE_PURPOSE" });
                PackageValidationResult result;
                if (parsedPurpose is PackagePurpose.AuthorizedBaselineArchive or PackagePurpose.ChangeSubmission)
                {
                    RetainedPackageSelection? selection = null;
                    try
                    {
                        if (request.ContentLength > 0 || request.Headers.TransferEncoding.Count > 0 || request.HasJsonContentType())
                            selection = await request.ReadFromJsonAsync<RetainedPackageSelection>(ct);
                        result = await packages.ValidateRetainedPackageAsync(systemId, parsedPurpose, selection, currentUser.CurrentUserId, ct);
                    }
                    catch (JsonException) { return Results.BadRequest(new ErrorResponse { Error = "Invalid retained-context JSON.", ErrorCode = "INVALID_RETAINED_CONTEXT" }); }
                    catch (UnauthorizedAccessException) { return Results.Forbid(); }
                }
                else result = parsedPurpose == PackagePurpose.Legacy
                        ? await service.ValidateAsync(systemId, currentUser.CurrentUserId, ct)
                        : await service.ValidateAsync(systemId, parsedPurpose, currentUser.CurrentUserId, ct);
                return Results.Ok(new
                {
                    purpose = parsedPurpose.ToString(),
                    retainedContext = result.RetainedContext,
                    sourceContextHash = result.SourceContextHash,
                    isValid = result.IsValid,
                    errorCount = result.ErrorCount,
                    warningCount = result.WarningCount,
                    validatedAt = result.ValidatedAt,
                    findings = result.Findings.Select(f => new
                    {
                        severity = f.Severity.ToString().ToLowerInvariant(),
                        category = f.Category,
                        artifactType = f.ArtifactType,
                        description = f.Description,
                        remediation = f.Remediation
                    })
                });
            })
            .WithName("ValidatePackage")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        // ─── Package Generation & Management Endpoints ──────────────────────

        systems.MapPost("/packages", async (
                string systemId,
                GeneratePackageRequest request,
                HttpRequest httpRequest,
                IAuthorizationPackageService service,
                CancellationToken ct) =>
            {
                try
                {
                    if (!Enum.IsDefined(request.Purpose))
                        return Results.BadRequest(new ErrorResponse { Error = "Use Legacy, InitialSubmission, AuthorizedBaselineArchive or ChangeSubmission.", ErrorCode = "INVALID_PACKAGE_PURPOSE" });
                    if (!Enum.IsDefined(request.EvidenceMode) || !request.IncludeEvidence)
                        return Results.BadRequest(new ErrorResponse { Error = "Use Embedded or ManifestOnly; required package evidence cannot be omitted.", ErrorCode = "INVALID_PACKAGE_EVIDENCE" });
                    AuthorizationPackage package;
                    if (request.ReadinessRunId != null || request.ExpectedSourceHash != null)
                    {
                        if (request.ReadinessRunId == null || request.ExpectedSourceHash == null || !request.IncludeEvidence)
                            throw new ArgumentException("Supply the readiness run and source hash; package evidence cannot be silently omitted.");
                        if (service is not Ato.Copilot.Agents.Compliance.Services.AuthorizationPackageService implementation)
                            throw new InvalidOperationException("Readiness-bound package generation is unavailable.");
                        var keys = httpRequest.Headers["Idempotency-Key"];
                        if (keys.Count > 1) throw new ArgumentException("Supply one Idempotency-Key header.");
                        package = await implementation.EnqueueFromReadinessAsync(systemId, new(request.Purpose, request.RetainedContext),
                            request.ReadinessRunId, request.ExpectedSourceHash, request.EvidenceMode, currentUser.CurrentUserId, ct,
                            keys.Count == 0 ? null : keys[0]);
                    }
                    else if (request.Purpose is PackagePurpose.AuthorizedBaselineArchive or PackagePurpose.ChangeSubmission)
                    {
                        var keys = httpRequest.Headers["Idempotency-Key"];
                        if (keys.Count > 1) throw new ArgumentException("Supply one Idempotency-Key header.");
                        package = await service.EnqueueRetainedPackageAsync(systemId, request.Purpose,
                            request.RetainedContext ?? throw new InvalidOperationException("RetainedContext is required for archive/change packages."),
                            currentUser.CurrentUserId, ct, keys.Count == 0 ? null : keys[0]);
                    }
                    else
                    {
                        if (request.RetainedContext != null)
                            throw new InvalidOperationException("RetainedContext applies only to archive/change purposes.");
                        package = request.Purpose == PackagePurpose.Legacy
                            ? await service.EnqueuePackageAsync(systemId, request.EvidenceMode, currentUser.CurrentUserId, ct)
                            : await service.EnqueuePackageAsync(systemId, request.Purpose, request.EvidenceMode, currentUser.CurrentUserId, ct);
                    }
                    return Results.Accepted(
                        $"/api/v1/systems/{systemId}/packages/{package.Id}",
                        new
                        {
                            systemId,
                            readinessRunId = package.ReadinessRunId,
                            sourceHash = package.ReadinessSourceHash,
                            packageId = package.Id,
                            purpose = package.Purpose.ToString(),
                            retainedContextHash = package.RetainedContextHash,
                            sourceContextHash = package.RetainedContextJson == null ? null :
                                Ato.Copilot.Agents.Compliance.Services.AuthorizationPackageContextOptions.SourceContextHash(package.RetainedContextJson),
                            status = package.Status.ToString(),
                            message = "Package generation has been queued."
                        });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "READINESS_CHECK_FAILED" });
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    var code = ex.Message.StartsWith("READINESS_", StringComparison.Ordinal) ? ex.Message.Split(':', 2)[0] : "PACKAGE_REQUEST_CONFLICT";
                    return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = code, Suggestion = "Refresh the selected readiness context and revalidate." });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_RETAINED_CONTEXT" });
                }
                catch (UnauthorizedAccessException) { return Results.Forbid(); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            })
            .WithName("GeneratePackage")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        systems.MapGet("/packages", async (
                string systemId,
                int? limit,
                int? offset,
                bool? includeFailed,
                IAuthorizationPackageService service,
                CancellationToken ct) =>
            {
                var result = await service.ListPackagesAsync(
                    systemId,
                    limit ?? 20,
                    offset ?? 0,
                    includeFailed ?? false,
                    ct);
                return Results.Ok(result);
            })
            .WithName("ListPackages");

        systems.MapGet("/packages/{packageId}", async (
                string systemId,
                string packageId,
                IAuthorizationPackageService service,
                CancellationToken ct) =>
            {
                var package = await service.GetPackageAsync(packageId, ct);
                if (package == null || package.RegisteredSystemId != systemId)
                    return Results.NotFound();

                return Results.Ok(new PackageDetailResponse
                {
                    ReadinessRunId = package.ReadinessRunId, SourceHash = package.ReadinessSourceHash,
                    PackageId = package.Id,
                    Purpose = package.Purpose.ToString(),
                    RetainedContext = package.RetainedContextJson == null ? null :
                        JsonSerializer.Deserialize<RetainedPackageManifest>(package.RetainedContextJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    RetainedContextHash = package.RetainedContextHash,
                    SourceContextHash = package.RetainedContextJson == null ? null :
                        Ato.Copilot.Agents.Compliance.Services.AuthorizationPackageContextOptions.SourceContextHash(package.RetainedContextJson),
                    SystemId = package.RegisteredSystemId,
                    Status = package.Status.ToString(),
                    EvidenceMode = package.EvidenceMode.ToString(),
                    Artifacts = (package.Artifacts ?? []).Select(a => new PackageArtifactDto
                    {
                        ArtifactId = a.Id,
                        Type = a.ArtifactType.ToString(),
                        Format = a.Format ?? "",
                        FileName = a.FileName ?? "",
                        FileSize = a.FileSize,
                        OscalVersion = a.OscalVersion,
                        SchemaValid = a.SchemaValid,
                        GeneratedAt = a.GeneratedAt
                    }).ToList(),
                    Validation = package.ValidationResult != null
                        ? new PackageValidationDto
                        {
                            IsValid = package.ValidationResult.IsValid,
                            ErrorCount = package.ValidationResult.ErrorCount,
                            WarningCount = package.ValidationResult.WarningCount,
                            Findings = (package.ValidationResult.Findings ?? []).Select(f => new ValidationFindingDto
                            {
                                Severity = f.Severity.ToString().ToLowerInvariant(),
                                Category = f.Category ?? "",
                                ArtifactType = f.ArtifactType,
                                Description = f.Description ?? "",
                                Remediation = f.Remediation
                            }).ToList()
                        }
                        : null,
                    FileSize = package.FileSize,
                    FailureReason = package.FailureReason,
                    FailedArtifactType = package.FailedArtifactType?.ToString(),
                    GeneratedBy = package.GeneratedBy ?? "",
                    GeneratedAt = package.GeneratedAt,
                    CompletedAt = package.CompletedAt,
                    ExpiresAt = package.ExpiresAt
                });
            })
            .WithName("GetPackage");

        systems.MapGet("/packages/{packageId}/download", async (
                string systemId,
                string packageId,
                IAuthorizationPackageService service,
                CancellationToken ct) =>
            {
                var package = await service.GetPackageAsync(packageId, ct);
                if (package == null || package.RegisteredSystemId != systemId)
                    return Results.NotFound();

                Stream? stream;
                try { stream = await service.DownloadPackageAsync(packageId, ct); }
                catch (UnauthorizedAccessException) { return Results.Forbid(); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
                catch (IOException)
                {
                    return Results.Conflict(new ErrorResponse { Error = "Retained package evidence could not be verified.", ErrorCode = "PACKAGE_EVIDENCE_UNAVAILABLE" });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "PACKAGE_CONTEXT_UNAVAILABLE" });
                }
                if (stream == null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Package file not available. It may have expired or generation may still be in progress.",
                        ErrorCode = "DOWNLOAD_NOT_AVAILABLE"
                    });

                return Results.File(stream,
                    "application/zip",
                    $"authorization-package-{packageId[..8]}.zip");
            })
            .WithName("DownloadPackage")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        return app;
    }

    private static SarResponse MapSarResponse(SecurityAssessmentReport sar)
    {
        return new SarResponse
        {
            SarId = sar.Id,
            SystemId = sar.RegisteredSystemId,
            Title = sar.Title,
            Status = sar.Status.ToString(),
            TotalControlsAssessed = sar.TotalControlsAssessed,
            TotalControlsPending = sar.TotalControlsPending,
            SatisfiedCount = sar.SatisfiedCount,
            NotSatisfiedCount = sar.NotSatisfiedCount,
            Sections = (sar.Sections ?? []).Select(s => new SarSectionSummaryDto
            {
                SectionType = s.SectionType.ToString(),
                Title = s.Title,
                IsAutoGenerated = s.IsAutoGenerated,
                HasContent = !string.IsNullOrWhiteSpace(s.Content)
            }).ToList(),
            CreatedBy = sar.CreatedBy,
            CreatedAt = sar.CreatedAt,
            ModifiedBy = sar.ModifiedBy,
            ModifiedAt = sar.ModifiedAt,
            ReviewedBy = sar.ReviewedBy,
            ReviewedAt = sar.ReviewedAt,
            ApprovedBy = sar.ApprovedBy,
            ApprovedAt = sar.ApprovedAt
        };
    }

    private static object MapSapResponse(SapDocument sap)
    {
        return new
        {
            sapId = sap.SapId,
            systemId = sap.SystemId,
            assessmentId = sap.AssessmentId,
            title = sap.Title,
            status = sap.Status,
            format = sap.Format,
            baselineLevel = sap.BaselineLevel,
            contentHash = sap.ContentHash,
            totalControls = sap.TotalControls,
            customerControls = sap.CustomerControls,
            inheritedControls = sap.InheritedControls,
            sharedControls = sap.SharedControls,
            stigBenchmarkCount = sap.StigBenchmarkCount,
            controlsWithObjectives = sap.ControlsWithObjectives,
            evidenceGaps = sap.EvidenceGaps,
            familySummaries = sap.FamilySummaries.Select(f => new
            {
                family = f.Family,
                controlCount = f.ControlCount,
                customerCount = f.CustomerCount,
                inheritedCount = f.InheritedCount,
                methods = f.Methods
            }),
            generatedAt = sap.GeneratedAt,
            finalizedAt = sap.FinalizedAt,
            warnings = sap.Warnings
        };
    }

    private static IResult OscalExportValidationFailure(
        string documentType,
        OscalSchemaValidationResult validation) =>
        Results.UnprocessableEntity(new
        {
            errorCode = "OSCAL_SCHEMA_VALIDATION_FAILED",
            message = $"The generated OSCAL {documentType} artifact failed schema validation and was not exported.",
            documentType,
            schemaVersion = validation.SchemaVersion,
            violations = validation.Violations
        });
}
