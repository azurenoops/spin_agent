using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

/// <summary>
/// Feature 044 regression — model-builder width contract for
/// <see cref="FrameworkControl"/>. The OSCAL import (Feature 044) loads NIST
/// SP 800-171 Rev 3, whose family ids are long (e.g. <c>SP_800_171_03.01</c>,
/// 16 chars) and whose normalized control ids reach ~19 chars
/// (<c>SP_800_171_03(01.01</c>). On SQL Server (which enforces nvarchar
/// lengths) the previous Fluent config of <c>Family</c> = nvarchar(10) /
/// <c>ControlId</c> = nvarchar(20) raised
/// "String or binary data would be truncated in column 'Family'" and aborted
/// the whole 800-171 import.
/// </summary>
/// <remarks>
/// SQLite does not enforce column lengths, so this asserts the EF model's
/// configured <see cref="Microsoft.EntityFrameworkCore.Metadata.IReadOnlyProperty.GetMaxLength"/>
/// directly — the same metadata SQL Server's <c>EnsureCreated</c> uses to size
/// the physical columns. The configured width must match the migration and the
/// <c>[MaxLength(50)]</c> data annotation on the model class.
/// </remarks>
public sealed class FrameworkControlModelBuilderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AtoCopilotContext _ctx;

    public FrameworkControlModelBuilderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(_connection)
            .Options;
        _ctx = new AtoCopilotContext(options);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _connection.Dispose();
    }

    [Theory]
    [InlineData(nameof(FrameworkControl.ControlId))]
    [InlineData(nameof(FrameworkControl.Family))]
    [InlineData(nameof(FrameworkControl.ParentControlId))]
    [InlineData(nameof(FrameworkControl.WithdrawnTo))]
    public void StringColumns_AreWideEnoughForNist800171(string propertyName)
    {
        // Arrange
        var entityType = _ctx.Model.FindEntityType(typeof(FrameworkControl))!;

        // Act
        var maxLength = entityType.FindProperty(propertyName)!.GetMaxLength();

        // Assert — must match the migration (nvarchar(50)) and the model's
        // [MaxLength(50)] annotation so SQL Server EnsureCreated sizes the
        // columns wide enough for NIST 800-171 Rev 3 ids/families.
        maxLength.Should().Be(50,
            $"{propertyName} must be nvarchar(50) — the narrower 800-53-sized width truncates 800-171 values on SQL Server");
    }
}
