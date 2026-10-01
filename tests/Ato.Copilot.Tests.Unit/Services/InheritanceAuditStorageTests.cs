using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class InheritanceAuditStorageTests
{
    [Fact]
    public void ChangeSourceMapping_FitsEveryDefinedAuditSource()
    {
        // Arrange
        using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"audit-storage-{Guid.NewGuid():N}").Options);
        var property = db.Model.FindEntityType(typeof(InheritanceAuditEntry))!
            .FindProperty(nameof(InheritanceAuditEntry.ChangeSource))!;

        // Act
        var capacity = property.GetMaxLength();

        // Assert
        capacity.Should().NotBeNull();
        Enum.GetNames<InheritanceChangeSource>().Should().OnlyContain(name => name.Length <= capacity!.Value);
    }
}
