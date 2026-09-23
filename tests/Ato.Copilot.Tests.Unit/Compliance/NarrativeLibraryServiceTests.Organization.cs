using System.Text;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public partial class NarrativeLibraryServiceTests
{
    [Fact]
    public async Task OrganizationLibrary_ImportsEditsAndPublishesWithoutAnySystem()
    {
        // Arrange
        var publisher = new Person { TenantId = _tenantId, DisplayName = "Publisher", Email = "publisher@example.invalid" };
        _db.Persons.Add(publisher);
        _db.OrganizationRoleAssignments.Add(new() { TenantId = _tenantId, Person = publisher, PersonId = publisher.Id,
            Role = OrganizationRole.Administrator });
        _db.NistControls.Add(new NistControl { Id = "ac-2", Title = "Account management" });
        await _db.SaveChangesAsync();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("Unmapped organization reference"));

        // Act
        var draft = await _service.ImportOrganizationAsync(publisher.Id.ToString(), "Policy reference", "Organization",
            _tenantId.ToString(), "reference.txt", input);
        var edited = await _service.UpdateOrganizationDraftAsync(draft.Id, publisher.Id.ToString(), draft.Revision,
            "Organization", _tenantId.ToString(), [new("AC-2", "Policy", "Unverified reference claim")]);
        var published = await _service.PublishOrganizationAsync(draft.Id, publisher.Id.ToString(), edited.Revision, edited.Passages, true);

        // Assert
        published.IsPublished.Should().BeTrue();
        (await _db.NarrativeReferences.SingleAsync()).ImportedForSystemId.Should().BeNull();
        (await _db.RegisteredSystems.CountAsync()).Should().Be(0);
        (await _db.ControlImplementations.CountAsync()).Should().Be(0);
        (await _service.ListOrganizationAsync(publisher.Id.ToString())).Should().ContainSingle();
    }

    [Fact]
    public async Task OrganizationLibrary_SystemAssignmentDoesNotGrantSharedPublicationAuthority()
    {
        // Arrange
        await SeedAsync();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("Reference"));

        // Act
        var import = () => _service.ImportOrganizationAsync("author", "Reference", "Organization",
            _tenantId.ToString(), "reference.txt", input);

        // Assert
        await import.Should().ThrowAsync<UnauthorizedAccessException>();
        (await _db.NarrativeReferences.CountAsync()).Should().Be(0);
    }
}
