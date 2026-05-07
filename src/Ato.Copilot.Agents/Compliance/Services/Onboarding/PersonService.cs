using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding;

/// <summary>
/// EF-backed Person directory service (research §R1 / FR-022).
/// </summary>
public class PersonService : IPersonService
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IDirectorySearchClient _directory;
    private readonly IWizardAuditService _audit;
    private readonly ILogger<PersonService> _logger;

    public PersonService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IDirectorySearchClient directory,
        IWizardAuditService audit,
        ILogger<PersonService> logger)
    {
        _contextFactory = contextFactory;
        _directory = directory;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Person>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Persons
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Person>> SearchLocalAsync(
        Guid tenantId, string query, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        if (string.IsNullOrWhiteSpace(query))
        {
            return await db.Persons
                .AsNoTracking()
                .Where(p => p.TenantId == tenantId)
                .OrderBy(p => p.DisplayName)
                .Take(50)
                .ToListAsync(ct);
        }
        var trimmed = query.Trim();
        return await db.Persons
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId &&
                (EF.Functions.Like(p.DisplayName, $"%{trimmed}%") ||
                 EF.Functions.Like(p.Email, $"%{trimmed}%")))
            .OrderBy(p => p.DisplayName)
            .Take(50)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<Person> CreateLocalAsync(
        Guid tenantId,
        string displayName,
        string email,
        string? phoneNumber,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var trimmedEmail = email.Trim();
        var existing = await db.Persons
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Email == trimmedEmail, ct);
        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"A person with email '{trimmedEmail}' already exists for this tenant.");
        }

        var now = DateTimeOffset.UtcNow;
        var person = new Person
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = displayName.Trim(),
            Email = trimmedEmail,
            PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
            EntraObjectId = null,
            IsLinkedToDirectory = false,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId,
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.PersonCreated,
            nameof(Person), person.Id,
            beforeJson: null,
            afterJson: JsonSerializer.Serialize(Project(person)),
            effectsJson: null,
            correlationId: correlationId,
            ct: ct);

        return person;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DirectoryPersonDto>> SearchDirectoryAsync(
        string query, CancellationToken ct = default)
        => _directory.SearchAsync(query, ct);

    /// <inheritdoc />
    public async Task<Person> PromoteToDirectoryAsync(
        Guid tenantId,
        Guid personId,
        Guid entraObjectId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var person = await db.Persons
            .FirstOrDefaultAsync(p => p.Id == personId && p.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException(
                $"Person {personId} not found for tenant {tenantId}.");
        if (person.IsLinkedToDirectory)
        {
            throw new InvalidOperationException(
                $"Person {personId} is already linked to a directory account.");
        }

        var beforeJson = JsonSerializer.Serialize(Project(person));
        person.EntraObjectId = entraObjectId;
        person.IsLinkedToDirectory = true;
        person.LastPromotedAt = DateTimeOffset.UtcNow;
        person.UpdatedAt = DateTimeOffset.UtcNow;
        person.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.PersonPromoted,
            nameof(Person), person.Id,
            beforeJson: beforeJson,
            afterJson: JsonSerializer.Serialize(Project(person)),
            effectsJson: null,
            correlationId: correlationId,
            ct: ct);

        return person;
    }

    private static object Project(Person p) => new
    {
        p.Id,
        p.DisplayName,
        p.Email,
        p.PhoneNumber,
        p.EntraObjectId,
        p.IsLinkedToDirectory,
    };
}
