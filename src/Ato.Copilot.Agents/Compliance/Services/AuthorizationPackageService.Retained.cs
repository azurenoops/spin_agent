using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Ato.Copilot.Agents.Compliance.Services;

public partial class AuthorizationPackageService
{
    public async Task<PackageValidationResult> ValidateRetainedPackageAsync(string systemId, PackagePurpose purpose,
        RetainedPackageSelection? selection, string validatedBy = "mcp-user", CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        try
        {
            var context = await RetainedPackageContext.ResolveAsync(db, scope.ServiceProvider, systemId, purpose, selection, validatedBy, cancellationToken);
            var findings = new List<ValidationFinding>();
            if (purpose == PackagePurpose.ChangeSubmission)
                findings.Add(new ValidationFinding { Severity = ValidationSeverity.Warning, Category = "bundle-scope",
                    Description = context.BundleScope, Remediation = "Verify the receiving workflow accepts this SSP delta bundle; this is not a regenerated full authorization package." });
            if (context.DecisionBaselineLinkage == "UnverifiedLegacyPairing")
                findings.Add(new ValidationFinding { Severity = ValidationSeverity.Warning, Category = "decision-baseline-linkage",
                    Description = "The legacy decision has no recorded baseline linkage. Selecting this pair does not establish authorization coverage.",
                    Remediation = "Retain this as an explicitly unverified historical pairing, or select a decision with recorded exact baseline linkage." });
            return new PackageValidationResult
            {
                IsValid = true, ValidatedBy = validatedBy, RetainedContext = context,
                SourceContextHash = AuthorizationPackageContextOptions.SourceContextHash(context),
                WarningCount = findings.Count, Findings = findings
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            return new PackageValidationResult
            {
                IsValid = false, ErrorCount = 1, ValidatedBy = validatedBy,
                Findings = [new ValidationFinding { Severity = ValidationSeverity.Error, Category = "retained-context",
                    Description = ex is IOException ? "Retained source bytes cannot be verified." : ex.Message,
                    Remediation = "Select a completed same-system baseline and its exact hash, a recorded decision, and (for changes) a reviewed retained SSP preview." }]
            };
        }
    }

    public async Task<AuthorizationPackage> EnqueueRetainedPackageAsync(string systemId, PackagePurpose purpose,
        RetainedPackageSelection selection, string generatedBy = "mcp-user", CancellationToken cancellationToken = default, string? idempotencyKey = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var readiness = scope.ServiceProvider.GetRequiredService<PackageReadinessService>();
        var run = await readiness.ValidateAsync(systemId, new(purpose, selection), generatedBy, cancellationToken);
        await readiness.RequireReadyAsync(systemId, new(purpose, selection), run.Id, run.SourceHash ?? "", generatedBy, cancellationToken);
        return await EnqueueRetainedWithRunAsync(systemId, purpose, selection, generatedBy, cancellationToken, idempotencyKey, run);
    }

    private async Task<AuthorizationPackage> EnqueueRetainedWithRunAsync(string systemId, PackagePurpose purpose,
        RetainedPackageSelection selection, string generatedBy, CancellationToken cancellationToken, string? idempotencyKey,
        PackageReadinessRun run)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        string? requestKey = null;
        var intentHash = ApprovedProfileDocumentData.Hash(JsonSerializer.Serialize(new { purpose, selection }));
        if (idempotencyKey != null)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
                throw new ArgumentException("Idempotency-Key must contain 1-100 characters.");
            var tenant = await db.RegisteredSystems.Where(s => s.Id == systemId && s.IsActive)
                .Select(s => (Guid?)s.TenantId).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("System not found in this workspace.");
            requestKey = ApprovedProfileDocumentData.Hash(JsonSerializer.Serialize(new
            {
                tenant, systemId, actor = generatedBy, person = db.WorkspacePersonId, key = idempotencyKey
            }));
            var prior = await db.AuthorizationPackages.AsNoTracking().SingleOrDefaultAsync(p => p.RequestScopeKey == requestKey, cancellationToken);
            if (prior != null) return ReplayRetainedPackage(prior, intentHash);
        }
        var validation = await ValidateRetainedPackageAsync(systemId, purpose, selection, generatedBy, cancellationToken);
        if (!validation.IsValid || validation.RetainedContext == null)
            throw new InvalidOperationException(string.Join("; ", validation.Findings.Select(f => f.Description)));
        var context = validation.RetainedContext;
        var json = RetainedPackageContext.Serialize(context);
        var package = new AuthorizationPackage
        {
            ReadinessRunId = run.Id, ReadinessSourceHash = run.SourceHash,
            TenantId = context.TenantId, RegisteredSystemId = systemId, Purpose = purpose, Status = PackageStatus.Pending,
            EvidenceMode = EvidenceMode.ManifestOnly, GeneratedBy = generatedBy, ExpiresAt = DateTimeOffset.UtcNow.AddDays(90),
            RetainedContextJson = json, RetainedContextHash = ApprovedProfileDocumentData.Hash(json),
            RequestScopeKey = requestKey, RequestIntentHash = requestKey == null ? null : intentHash
        };
        validation.AuthorizationPackageId = package.Id;
        validation.TenantId = context.TenantId;
        foreach (var finding in validation.Findings) finding.TenantId = context.TenantId;
        db.AddRange(package, validation);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) when (requestKey != null)
        {
            db.ChangeTracker.Clear();
            var winner = await db.AuthorizationPackages.AsNoTracking().SingleOrDefaultAsync(p => p.RequestScopeKey == requestKey, cancellationToken);
            if (winner == null) throw;
            return ReplayRetainedPackage(winner, intentHash);
        }
        await _channel.Writer.WriteAsync(new(package.Id, systemId, EvidenceMode.ManifestOnly, generatedBy, purpose,
            run.TenantId, db.WorkspacePersonId), CancellationToken.None);
        return package;
    }

    private static AuthorizationPackage ReplayRetainedPackage(AuthorizationPackage package, string intentHash)
    {
        if (package.RequestIntentHash != intentHash)
            throw new DbUpdateConcurrencyException("Idempotency-Key already identifies a different retained package selection.");
        return package;
    }
}
