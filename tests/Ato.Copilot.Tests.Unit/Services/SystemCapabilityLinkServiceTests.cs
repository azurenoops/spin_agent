using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Services;

public class SystemCapabilityLinkServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly SystemCapabilityLinkService _sut;

    private const string SystemId = "sys-link-001";
    private const string Cap1 = "cap-001";
    private const string Cap2 = "cap-002";
    private const string Cap3 = "cap-003";

    public SystemCapabilityLinkServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CapabilityLinkTests_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(dbOptions);
        var logger = Mock.Of<ILogger<SystemCapabilityLinkService>>();
        _sut = new SystemCapabilityLinkService(_db, logger);

        SeedData();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedData()
    {
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId,
            Name = "Link Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true,
        });

        _db.SecurityCapabilities.AddRange(
            new SecurityCapability { Id = Cap1, Name = "MFA", Provider = "Entra ID", Category = "IA", Description = "Multi-factor auth" },
            new SecurityCapability { Id = Cap2, Name = "WAF", Provider = "Azure", Category = "SC", Description = "Web app firewall" },
            new SecurityCapability { Id = Cap3, Name = "SIEM", Provider = "Sentinel", Category = "AU", Description = "Log aggregation" }
        );

        _db.SaveChanges();
    }

    // ─── LinkCapabilitiesAsync Tests ─────────────────────────────────────────

    [Fact]
    public async Task LinkCapabilitiesAsync_WithValidData_CreatesLinks()
    {
        var (count, items) = await _sut.LinkCapabilitiesAsync(SystemId, new[] { Cap1, Cap2 }, "test-user");

        count.Should().Be(2);
        items.Should().HaveCount(2);
        items.Should().Contain(l => l.SecurityCapabilityId == Cap1);
        items.Should().Contain(l => l.SecurityCapabilityId == Cap2);
    }

    [Fact]
    public async Task LinkCapabilitiesAsync_SkipsDuplicates()
    {
        // Link Cap1 first
        await _sut.LinkCapabilitiesAsync(SystemId, new[] { Cap1 }, "test-user");

        // Link Cap1 again + Cap2 — should skip Cap1
        var (count, items) = await _sut.LinkCapabilitiesAsync(SystemId, new[] { Cap1, Cap2 }, "test-user");

        count.Should().Be(1);
        items.Should().HaveCount(2); // returns all linked items (Cap1 + Cap2)
    }

    [Fact]
    public async Task LinkCapabilitiesAsync_InvalidSystem_ThrowsKeyNotFound()
    {
        var act = () => _sut.LinkCapabilitiesAsync("nonexistent", new[] { Cap1 }, "test-user");

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*nonexistent*");
    }

    [Fact]
    public async Task LinkCapabilitiesAsync_InvalidCapabilityIds_ThrowsArgument()
    {
        var act = () => _sut.LinkCapabilitiesAsync(SystemId, new[] { "bad-id" }, "test-user");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*bad-id*");
    }

    // ─── GetLinksForSystemAsync Tests ────────────────────────────────────────

    [Fact]
    public async Task GetLinksForSystemAsync_Empty_ReturnsEmpty()
    {
        var result = await _sut.GetLinksForSystemAsync(SystemId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLinksForSystemAsync_WithLinks_ReturnsAllLinks()
    {
        await _sut.LinkCapabilitiesAsync(SystemId, new[] { Cap1, Cap3 }, "test-user");

        var result = await _sut.GetLinksForSystemAsync(SystemId);

        result.Should().HaveCount(2);
        result.Should().Contain(l => l.SecurityCapabilityId == Cap1);
        result.Should().Contain(l => l.SecurityCapabilityId == Cap3);
    }

    // ─── RemoveLinkAsync Tests ──────────────────────────────────────────────

    [Fact]
    public async Task RemoveLinkAsync_ExistingLink_ReturnsTrue()
    {
        var (_, items) = await _sut.LinkCapabilitiesAsync(SystemId, new[] { Cap1 }, "test-user");
        var linkId = items.First().Id;

        var result = await _sut.RemoveLinkAsync(SystemId, linkId);

        result.Should().BeTrue();
        (await _sut.GetLinksForSystemAsync(SystemId)).Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveLinkAsync_NotFound_ReturnsFalse()
    {
        var result = await _sut.RemoveLinkAsync(SystemId, "nonexistent-link-id");

        result.Should().BeFalse();
    }
}
