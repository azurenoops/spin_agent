using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class ResponsibilityDraftServiceConcurrencyTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _systemId = Guid.NewGuid().ToString();
    private readonly TenantContext _tenant;
    private DbContextOptions<AtoCopilotContext> _options = null!;
    private Mock<ISystemEnvironmentService> _environments = null!;
    private Mock<ICapabilityResponsibilityService> _responsibilities = null!;

    public ResponsibilityDraftServiceConcurrencyTests()
    {
        _tenant = new TenantContext(_tenantId);
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options;
        await using var db = new AtoCopilotContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new() { Id = _tenantId, DisplayName = "Synthetic draft tenant" });
        db.RegisteredSystems.Add(new()
        {
            Id = _systemId,
            TenantId = _tenantId,
            Name = "Synthetic responsibility system",
            HostingEnvironment = "Synthetic environment"
        });
        db.ControlBaselines.Add(new()
        {
            TenantId = _tenantId,
            RegisteredSystemId = _systemId,
            BaselineLevel = "Moderate",
            ControlIds = ["AU-6"],
            CreatedBy = "test"
        });
        await db.SaveChangesAsync();

        _environments = new Mock<ISystemEnvironmentService>();
        _environments.Setup(x => x.ListAsync(_systemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemEnvironmentsResponse(_systemId, 0,
                new(false, false, false, false, false), [], []));
        _responsibilities = new Mock<ICapabilityResponsibilityService>();
        _responsibilities.Setup(x => x.AuthorizeAsync(_systemId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    [Fact]
    public async Task ConcurrentHumanEditDuringGenerationIsPreservedAndGenerationConflicts()
    {
        // Arrange
        var generator = new Mock<IResponsibilityDraftGenerator>(MockBehavior.Strict);
        await using var generationDb = new AtoCopilotContext(_options);
        var service = CreateService(generationDb, generator.Object);
        var initial = await service.PrepareAsync(_systemId, "AU-6",
            new PrepareResponsibilityDraftRequest(null, 0, Generate: false), "test-author", default);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ResponsibilityDraftSuggestion>(TaskCreationOptions.RunContinuationsAsynchronously);
        generator.Setup(x => x.GenerateAsync("AU-6", It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                started.TrySetResult();
                return release.Task;
            });

        // Act
        var preparation = service.PrepareAsync(_systemId, "AU-6",
            new PrepareResponsibilityDraftRequest(null, initial.Draft!.Revision), "test-author", default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var editDb = new AtoCopilotContext(_options);
        var editService = CreateService(editDb, Mock.Of<IResponsibilityDraftGenerator>());
        var values = initial.Draft.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["customer"] = "Concurrent human correction retained.";
        values["basis"] = "Human-reviewed local responsibility.";
        var edited = await editService.SaveAsync(_systemId, initial.Draft.Id,
            new SaveResponsibilityDraftRequest(initial.Draft.Revision, values), "test-editor", default);
        release.SetResult(new(new(), [], []));

        // Assert
        Func<Task> act = async () => await preparation;
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("CONCURRENCY_CONFLICT:*");
        edited.Values["customer"].Value.Should().Be("Concurrent human correction retained.");
        await using var verifyDb = new AtoCopilotContext(_options);
        var retained = await verifyDb.Set<ResponsibilityDraft>().SingleAsync(x => x.Id == initial.Draft.Id);
        retained.Revision.Should().Be(edited.Revision);
        retained.Status.Should().Be("Proposed");
        var history = await verifyDb.Set<ResponsibilityDraftHistory>()
            .Where(x => x.DraftId == retained.Id).OrderBy(x => x.Revision).ToListAsync();
        history.Should().ContainSingle(x => x.Action == "DraftEdited");
        var persisted = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ResponsibilityDraftValue>>(
            retained.ValuesJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        persisted!["customer"].Value.Should().Be("Concurrent human correction retained.");
    }

    private ResponsibilityDraftService CreateService(AtoCopilotContext db, IResponsibilityDraftGenerator generator)
    {
        var library = new NarrativeLibraryService(db, _tenant);
        return new(db, _responsibilities.Object, _environments.Object, library, generator,
            NullLogger<ResponsibilityDraftService>.Instance);
    }
}
