using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Immutable purpose-bound validation with independent current-source freshness.</summary>
public sealed class PackageReadinessService(IServiceScopeFactory scopes, ILogger<PackageReadinessService> logger)
{
    public const string RuleVersion = "package-readiness/2";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed record SourceState(string State, string? Hash, string RuleVersion, string? Reason);

    public static string SelectionHash(PackageReadinessSelection selection)
    {
        ValidateSelection(selection);
        var s = selection.RetainedContext;
        return PackageReadinessSources.Hash(new
        {
            selection.Purpose, s?.BaselinePackageId, baselineContentHash = s?.BaselineContentHash.ToLowerInvariant(),
            s?.AuthorizationDecisionId, s?.ChangePreviewId, changeContentHash = s?.ChangeContentHash?.ToLowerInvariant()
        });
    }

    public static void ValidateSelection(PackageReadinessSelection selection)
    {
        if (!Enum.IsDefined(selection.Purpose)) throw new ArgumentException("Invalid package purpose.");
        var retained = selection.Purpose is PackagePurpose.AuthorizedBaselineArchive or PackagePurpose.ChangeSubmission;
        var s = selection.RetainedContext;
        if (!retained && s != null || retained && (s == null || string.IsNullOrWhiteSpace(s.BaselinePackageId)
            || !ValidHash(s.BaselineContentHash) || string.IsNullOrWhiteSpace(s.AuthorizationDecisionId)))
            throw new ArgumentException("Select the exact retained context only for archive/change purposes.");
        if (s is null) return;
        if (s.ExpectedSourceContextHash != null && !ValidHash(s.ExpectedSourceContextHash)
            || s.ExpectedDecisionSnapshotHash != null && !ValidHash(s.ExpectedDecisionSnapshotHash))
            throw new ArgumentException("Expected retained source hashes must be SHA-256.");
        if (selection.Purpose == PackagePurpose.ChangeSubmission && (s.ChangePreviewId == null || !ValidHash(s.ChangeContentHash))
            || selection.Purpose == PackagePurpose.AuthorizedBaselineArchive && (s.ChangePreviewId != null || s.ChangeContentHash != null))
            throw new ArgumentException("Only change submissions require a reviewed SSP change ID/hash.");
    }

    private static bool ValidHash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);

    public async Task<SourceState> ReadSourceAsync(string systemId, PackageReadinessSelection selection,
        string actor, CancellationToken ct)
    {
        ValidateSelection(selection);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = await RequireSystem(db, systemId, ct);
        try
        {
            var source = await PackageReadinessSources.CaptureAsync(db, scope.ServiceProvider, system, selection, actor, ct);
            return new("Available", source.Hash, RuleVersion, null);
        }
        catch (Exception ex) when (SourceFailure(ex))
        {
            logger.LogWarning(ex, "Readiness source unavailable for {SystemId} and {Purpose}", systemId, selection.Purpose);
            return new("Unavailable", null, RuleVersion, "The selected source identity could not be verified. Restore source access and recheck.");
        }
    }

    public async Task<PackageReadinessRun> ValidateAsync(string systemId, PackageReadinessSelection selection,
        string actor, CancellationToken ct)
    {
        var selectionHash = SelectionHash(selection);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = await RequireSystem(db, systemId, ct);
        var run = new PackageReadinessRun
        {
            TenantId = system.TenantId, RegisteredSystemId = systemId, Purpose = selection.Purpose,
            RetainedSelectionJson = selection.RetainedContext == null ? null : JsonSerializer.Serialize(selection.RetainedContext, Json),
            SelectionHash = selectionHash, StartedAt = DateTime.UtcNow, EvaluatedBy = actor,
            EvaluatedPersonId = db.WorkspacePersonId, RuleVersion = RuleVersion
        };
        IReadOnlyList<PackageReadinessCheck> checks;
        try
        {
            var before = await PackageReadinessSources.CaptureAsync(db, scope.ServiceProvider, system, selection, actor, ct);
            run.SourceHash = before.Hash;
            run.SourcesJson = JsonSerializer.Serialize(before.Sources, Json);
            run.ValidUntil = before.ValidUntil;
            PackageValidationResult validation;
            if (selection.Purpose is PackagePurpose.AuthorizedBaselineArchive or PackagePurpose.ChangeSubmission)
            {
                validation = await scope.ServiceProvider.GetRequiredService<IAuthorizationPackageService>()
                    .ValidateRetainedPackageAsync(systemId, selection.Purpose, selection.RetainedContext, actor, ct);
                checks = [PackageValidationService.Check("retained-context", "Retained source integrity",
                    validation.IsValid ? "Passed" : "Blocking", true,
                    validation.IsValid ? "Selected retained bytes, decision and evidence pins were verified."
                        : string.Join("\n", validation.Findings.Select(x => x.Description)),
                    "Review the exact retained baseline and decision selection.", "Issm"),
                    .. validation.Findings.Where(x => x.Severity == ValidationSeverity.Warning).Select(x =>
                        PackageValidationService.Check(x.Category, x.Category, "FollowUp", false, x.Description, x.Remediation ?? "Review source scope.", "Issm"))];
            }
            else
            {
                validation = await scope.ServiceProvider.GetRequiredService<IPackageValidationService>()
                    .ValidateAsync(systemId, selection.Purpose, actor, ct);
                checks = validation.Checks;
                if (checks.Count == 0)
                    throw new InvalidOperationException("The validation implementation did not return evaluated checks.");
            }
            checks = checks.Select(check => check with
            {
                Sources = before.Sources.Where(s => s.Kind == check.Category || check.Category == "schema").Take(200).ToArray()
            }).ToArray();
            var after = await PackageReadinessSources.CaptureAsync(db, scope.ServiceProvider, await RequireSystem(db, systemId, ct), selection, actor, ct);
            run.SourceHashAfter = after.Hash;
            run.Outcome = before.Hash != after.Hash ? "SourceChanged"
                : validation.IsValid && !checks.Any(x => x.Outcome == "Blocking" || x.Required && x.Outcome == "Unavailable") ? "Ready" : "Blocked";
        }
        catch (Exception ex) when (SourceFailure(ex))
        {
            logger.LogError(ex, "Package readiness evaluation failed for {SystemId}", systemId);
            run.Outcome = "Failed";
            run.FailureJson = JsonSerializer.Serialize(new PackageReadinessFailure("READINESS_EVALUATION_FAILED",
                "Evaluation could not complete. Restore the selected sources and retry; no ready snapshot was established."), Json);
            checks = [PackageValidationService.Check("evaluation-unavailable", "Evaluation unavailable", "Unavailable", true,
                "The selected source evaluation could not be completed.", "Restore source availability and retry.", null)];
        }
        var owners = new Dictionary<string, PackageReadinessOwner?>();
        foreach (var role in checks.Select(x => x.ExpectedRole).OfType<string>().Distinct())
            owners[role] = await RecordedOwnerAsync(db, system, role, ct);
        checks = checks.Select(x => x with { RecordedOwner = x.ExpectedRole == null ? null : owners[x.ExpectedRole] }).ToArray();
        run.ChecksJson = JsonSerializer.Serialize(checks, Json);
        run.EvaluatedAt = DateTime.UtcNow;
        db.PackageReadinessRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public static IReadOnlyList<PackageReadinessCheck> Checks(PackageReadinessRun run) =>
        JsonSerializer.Deserialize<PackageReadinessCheck[]>(run.ChecksJson, Json)
        ?? throw new InvalidDataException("Stored readiness checks are invalid.");

    public static PackageReadinessFreshness Freshness(PackageReadinessRun run, SourceState source)
    {
        var now = DateTime.UtcNow;
        if (source.State != "Available") return new("Unavailable", now, null, source.Reason);
        if (run.SourceHash == null || run.SourceHash != run.SourceHashAfter || run.SourceHash != source.Hash
            || run.RuleVersion != RuleVersion || run.ValidUntil <= now)
            return new("Stale", now, source.Hash, "Source identity, validation rules or time-dependent validity changed. Revalidation is required.");
        return new("Current", now, source.Hash, null);
    }

    public async Task<PackageReadinessRun> RequireReadyAsync(string systemId, PackageReadinessSelection selection,
        string id, string expectedHash, string actor, CancellationToken ct)
    {
        var selectionHash = SelectionHash(selection);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = await RequireSystem(db, systemId, ct);
        var run = await db.PackageReadinessRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id
            && x.RegisteredSystemId == systemId && x.TenantId == system.TenantId, ct)
            ?? throw new KeyNotFoundException("Readiness run not found.");
        if (run.Purpose != selection.Purpose || run.SelectionHash != selectionHash || run.SourceHash != expectedHash)
            throw new DbUpdateConcurrencyException("READINESS_CONTEXT_MISMATCH: The run does not match this purpose, selection and hash.");
        var source = await ReadSourceAsync(systemId, selection, actor, ct);
        if (Freshness(run, source).State != "Current")
            throw new DbUpdateConcurrencyException("READINESS_SOURCE_CHANGED: Source verification changed since validation.");
        if (run.Outcome != "Ready") throw new DbUpdateConcurrencyException("READINESS_NOT_READY: The retained evaluation is not ready.");
        return run;
    }

    public async Task<PackageReadinessRun> RequirePackageSourceAsync(AuthorizationPackage package, CancellationToken ct)
    {
        if (package.ReadinessRunId == null || package.ReadinessSourceHash == null)
            throw new InvalidOperationException("Package has no pinned readiness source. Revalidate and queue a new package.");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        if (db.IsWorkspaceRequest)
        {
            var access = await scope.ServiceProvider.GetRequiredService<Ato.Copilot.Core.Interfaces.Tenancy.ISystemWorkspaceAccessService>()
                .GetAccessAsync(package.TenantId, db.WorkspacePersonId, package.RegisteredSystemId, false, ct);
            if (!access.Permissions.CanRead) throw new UnauthorizedAccessException("Package source access has been revoked.");
        }
        var run = await db.PackageReadinessRuns.AsNoTracking().SingleAsync(x => x.Id == package.ReadinessRunId, ct);
        var selected = run.RetainedSelectionJson == null ? null : JsonSerializer.Deserialize<RetainedPackageSelection>(run.RetainedSelectionJson, Json);
        return await RequireReadyAsync(package.RegisteredSystemId, new(package.Purpose, selected), run.Id,
            package.ReadinessSourceHash, run.EvaluatedBy, ct);
    }

    private static Task<RegisteredSystem> RequireSystem(AtoCopilotContext db, string systemId, CancellationToken ct) =>
        RequireSystemCore(db, systemId, ct);

    private static async Task<RegisteredSystem> RequireSystemCore(AtoCopilotContext db, string systemId, CancellationToken ct) =>
        await db.RegisteredSystems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == systemId && x.IsActive, ct)
        ?? throw new KeyNotFoundException("System not found in this workspace.");

    private static bool SourceFailure(Exception ex) => ex is InvalidOperationException or ArgumentException or IOException or JsonException or KeyNotFoundException;

    private static async Task<PackageReadinessOwner?> RecordedOwnerAsync(AtoCopilotContext db, RegisteredSystem system,
        string role, CancellationToken ct)
    {
        if (!Enum.TryParse<OrganizationRole>(role == "Sca" ? "Assessor" : role, out var mapped)) return null;
        var systemRoles = await db.SystemRoleAssignments.AsNoTracking().Where(x => x.TenantId == system.TenantId
            && x.RegisteredSystemId == system.Id && x.Role == mapped && x.RemovedAt == null).ToListAsync(ct);
        var direct = systemRoles.Where(x => !x.IsInherited).ToList();
        var applicable = direct.Count > 0 ? direct : systemRoles;
        Guid personId; string assignmentId; string scope;
        if (applicable.Count > 0)
        {
            if (applicable.Select(x => x.PersonId).Distinct().Count() != 1) return null;
            var assignment = applicable.OrderBy(x => x.Id).First();
            personId = assignment.PersonId; assignmentId = assignment.Id.ToString(); scope = "System";
        }
        else
        {
            var organization = await db.OrganizationRoleAssignments.AsNoTracking().Where(x =>
                x.TenantId == system.TenantId && x.Role == mapped && x.RemovedAt == null).ToListAsync(ct);
            if (organization.Select(x => x.PersonId).Distinct().Count() != 1) return null;
            var assignment = organization.OrderBy(x => x.Id).First();
            personId = assignment.PersonId; assignmentId = assignment.Id.ToString(); scope = "Organization";
        }
        var person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(x => x.Id == personId && x.TenantId == system.TenantId, ct);
        return person == null ? null : new(person.Id.ToString(), person.DisplayName, role, assignmentId, scope);
    }
}
