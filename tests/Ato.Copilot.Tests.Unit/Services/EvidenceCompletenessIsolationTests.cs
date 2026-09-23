using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class EvidenceCompletenessIsolationTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Completeness_WithoutAssessmentFilter_CountsOnlyEvidenceOwnedByRequestedSystem(bool includeOwn, int expected)
    {
        // Arrange
        using var services = await ServicesAsync(includeOwn);
        var service = new AssessmentArtifactService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AssessmentArtifactService>.Instance);

        // Act
        var result = await service.CheckEvidenceCompletenessAsync("system-a");

        // Assert
        result.ControlStatuses.Should().ContainSingle();
        result.ControlStatuses[0].EvidenceCount.Should().Be(expected);
        result.ControlStatuses[0].Status.Should().Be(includeOwn ? "verified" : "missing");
    }

    [Theory]
    [InlineData("assessment-b")]
    [InlineData("unowned-assessment")]
    [InlineData("missing")]
    public async Task Completeness_RejectsAnAssessmentNotOwnedByRequestedSystem(string assessment)
    {
        // Arrange
        using var services = await ServicesAsync(true);
        var service = new AssessmentArtifactService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AssessmentArtifactService>.Instance);

        // Act
        var act = () => service.CheckEvidenceCompletenessAsync("system-a", assessment);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static async Task<ServiceProvider> ServicesAsync(bool includeOwn)
    {
        var database = Guid.NewGuid().ToString();
        var services = new ServiceCollection().AddDbContext<AtoCopilotContext>(o =>
            o.UseInMemoryDatabase(database)).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Assessments.AddRange(
            new() { Id = "assessment-a", RegisteredSystemId = "system-a" },
            new() { Id = "assessment-b", RegisteredSystemId = "system-b" },
            new() { Id = "unowned-assessment", RegisteredSystemId = null });
        db.ControlEffectivenessRecords.Add(new() { AssessmentId = "assessment-a",
            RegisteredSystemId = "system-a", ControlId = "AC-1", AssessorId = "fixture" });
        db.Evidence.AddRange(
            new() { ControlId = "AC-1", AssessmentId = "assessment-b", IntegrityVerifiedAt = DateTime.UtcNow },
            new() { ControlId = "AC-1", AssessmentId = null, IntegrityVerifiedAt = DateTime.UtcNow });
        if (includeOwn)
            db.Evidence.Add(new() { ControlId = "AC-1", AssessmentId = "assessment-a", IntegrityVerifiedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return services;
    }
}
