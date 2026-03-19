using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Agents.Compliance.Tools.Poam;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Tools.Poam;

/// <summary>
/// Unit tests for POA&M MCP tools — parameter validation, success paths, and error paths.
/// Scope-factory tools use a real in-memory DB; IAuthorizationService tools use Moq.
/// </summary>
public class PoamToolTests : IDisposable
{
    private readonly string _dbName = $"PoamTools_{Guid.NewGuid()}";
    private readonly Mock<IAuthorizationService> _authServiceMock = new();
    private const string SystemId = "sys-tool-test-001";

    public void Dispose()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AtoCopilotContext(options);
        db.Database.EnsureDeleted();
    }

    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opt =>
            opt.UseInMemoryDatabase(_dbName));
        services.AddScoped<PoamService>();
        services.AddScoped<TicketingService>();
        services.AddLogging();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private void SeedTestData()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AtoCopilotContext(options);
        if (!db.RegisteredSystems.Any(s => s.Id == SystemId))
        {
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                Id = SystemId,
                Name = "Tool Test System",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionEssential,
                HostingEnvironment = "Azure Gov",
                CreatedBy = "test",
                IsActive = true,
            });
            db.SaveChanges();
        }
    }

    private PoamItem CreatePoamInDb(string controlId = "AC-2", CatSeverity severity = CatSeverity.CatII)
    {
        SeedTestData();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AtoCopilotContext(options);
        var poam = new PoamItem
        {
            RegisteredSystemId = SystemId,
            Weakness = $"Test weakness for {controlId}",
            WeaknessSource = "STIG",
            SecurityControlNumber = controlId,
            CatSeverity = severity,
            PointOfContact = "test-poc",
            ScheduledCompletionDate = DateTime.UtcNow.AddDays(30),
            Status = PoamStatus.Ongoing,
            CreatedBy = "test-user",
        };
        db.PoamItems.Add(poam);
        db.SaveChanges();
        return poam;
    }

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    // ═══════════════════════════════════════════════════════════════════════
    // GetPoamTool
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPoam_ValidId_ReturnsDetail()
    {
        var poam = CreatePoamInDb();
        var sf = BuildScopeFactory();
        var tool = new GetPoamTool(sf, Mock.Of<ILogger<GetPoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["poam_id"] = poam.Id
        });

        var root = Parse(result);
        root.GetProperty("status").GetString().Should().Be("success");
        root.GetProperty("data").GetProperty("control_id").GetString().Should().Be("AC-2");
    }

    [Fact]
    public async Task GetPoam_MissingId_ReturnsError()
    {
        var sf = BuildScopeFactory();
        var tool = new GetPoamTool(sf, Mock.Of<ILogger<GetPoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var root = Parse(result);
        root.GetProperty("status").GetString().Should().Be("error");
        root.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task GetPoam_NotFound_ReturnsNotFoundError()
    {
        var sf = BuildScopeFactory();
        var tool = new GetPoamTool(sf, Mock.Of<ILogger<GetPoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["poam_id"] = "non-existent-id"
        });

        var root = Parse(result);
        root.GetProperty("status").GetString().Should().Be("error");
        root.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PoamMetricsTool
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Metrics_WithSystemId_ReturnsMetrics()
    {
        CreatePoamInDb("AC-3", CatSeverity.CatI);
        CreatePoamInDb("AC-4", CatSeverity.CatII);
        var sf = BuildScopeFactory();
        var tool = new PoamMetricsTool(sf, Mock.Of<ILogger<PoamMetricsTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = SystemId
        });

        var root = Parse(result);
        root.GetProperty("totalOpen").GetInt32().Should().BeGreaterOrEqualTo(2);
        root.GetProperty("catI").GetInt32().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task Metrics_NoSystemId_ReturnsCrossSystemMetrics()
    {
        CreatePoamInDb();
        var sf = BuildScopeFactory();
        var tool = new PoamMetricsTool(sf, Mock.Of<ILogger<PoamMetricsTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var root = Parse(result);
        root.GetProperty("totalOpen").GetInt32().Should().BeGreaterOrEqualTo(1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ClosePoamTool
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ClosePoam_ValidInput_CompletesPoam()
    {
        var poam = CreatePoamInDb("AC-5");
        var sf = BuildScopeFactory();
        var tool = new ClosePoamTool(sf, Mock.Of<ILogger<ClosePoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["poam_id"] = poam.Id,
            ["row_version"] = poam.RowVersion.ToString(),
            ["comments"] = "All remediation steps verified"
        });

        var root = Parse(result);
        root.GetProperty("status").GetString().Should().Be("success");
        root.GetProperty("data").GetProperty("new_status").GetString().Should().Be("Completed");
    }

    [Fact]
    public async Task ClosePoam_MissingPoamId_ReturnsError()
    {
        var sf = BuildScopeFactory();
        var tool = new ClosePoamTool(sf, Mock.Of<ILogger<ClosePoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["row_version"] = Guid.NewGuid().ToString()
        });

        var root = Parse(result);
        root.GetProperty("status").GetString().Should().Be("error");
        root.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task ClosePoam_InvalidRowVersion_ReturnsError()
    {
        var sf = BuildScopeFactory();
        var tool = new ClosePoamTool(sf, Mock.Of<ILogger<ClosePoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["poam_id"] = "some-id",
            ["row_version"] = "not-a-guid"
        });

        var root = Parse(result);
        root.GetProperty("status").GetString().Should().Be("error");
        root.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ExportPoamTool
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportPoam_CsvFormat_ReturnsBase64()
    {
        CreatePoamInDb("AC-6");
        var sf = BuildScopeFactory();
        var tool = new ExportPoamTool(sf, Mock.Of<ILogger<ExportPoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = SystemId,
            ["format"] = "csv"
        });

        var root = Parse(result);
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("format").GetString().Should().Be("csv");
        root.GetProperty("content").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExportPoam_MissingFormat_ReturnsError()
    {
        var sf = BuildScopeFactory();
        var tool = new ExportPoamTool(sf, Mock.Of<ILogger<ExportPoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = SystemId
        });

        var root = Parse(result);
        root.GetProperty("error").GetString().Should().Be("MISSING_PARAM");
    }

    [Fact]
    public async Task ExportPoam_InvalidFormat_ReturnsError()
    {
        var sf = BuildScopeFactory();
        var tool = new ExportPoamTool(sf, Mock.Of<ILogger<ExportPoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = SystemId,
            ["format"] = "pdf"
        });

        var root = Parse(result);
        root.GetProperty("error").GetString().Should().Be("INVALID_FORMAT");
    }

    [Fact]
    public async Task ExportPoam_EmassExcel_ReturnsBase64()
    {
        CreatePoamInDb("AC-7");
        var sf = BuildScopeFactory();
        var tool = new ExportPoamTool(sf, Mock.Of<ILogger<ExportPoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = SystemId,
            ["format"] = "emass_excel"
        });

        var root = Parse(result);
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("format").GetString().Should().Be("emass_excel");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CreatePoamTool (uses IAuthorizationService)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreatePoam_ValidInput_ReturnsSuccess()
    {
        var poamItem = new PoamItem
        {
            Id = "poam-new-1",
            RegisteredSystemId = SystemId,
            Weakness = "Weak cipher suites",
            WeaknessSource = "STIG",
            SecurityControlNumber = "SC-13",
            CatSeverity = CatSeverity.CatII,
            PointOfContact = "sca-user",
            ScheduledCompletionDate = DateTime.UtcNow.AddDays(60),
            Status = PoamStatus.Ongoing,
            RowVersion = Guid.NewGuid(),
        };

        _authServiceMock
            .Setup(s => s.CreatePoamAsync(
                SystemId, "Weak cipher suites", "SC-13", "CatII", "sca-user",
                It.IsAny<DateTime>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(poamItem);

        var tool = new CreatePoamTool(_authServiceMock.Object, Mock.Of<ILogger<CreatePoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = SystemId,
            ["weakness"] = "Weak cipher suites",
            ["control_id"] = "SC-13",
            ["cat_severity"] = "CatII",
            ["poc"] = "sca-user",
            ["scheduled_completion"] = DateTime.UtcNow.AddDays(60).ToString("O"),
        });

        var root = Parse(result);
        root.GetProperty("status").GetString().Should().Be("success");
    }

    [Fact]
    public async Task CreatePoam_MissingRequiredFields_ReturnsError()
    {
        var tool = new CreatePoamTool(_authServiceMock.Object, Mock.Of<ILogger<CreatePoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = SystemId,
        });

        var root = Parse(result);
        root.GetProperty("status").GetString().Should().Be("error");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ListPoamTool (uses IAuthorizationService)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListPoam_ReturnsItems()
    {
        var items = new List<PoamItem>
        {
            new()
            {
                Id = "poam-list-1", RegisteredSystemId = SystemId,
                Weakness = "W1", WeaknessSource = "STIG",
                SecurityControlNumber = "AC-2", CatSeverity = CatSeverity.CatI,
                PointOfContact = "poc", Status = PoamStatus.Ongoing,
                ScheduledCompletionDate = DateTime.UtcNow.AddDays(10),
                RowVersion = Guid.NewGuid(),
            }
        };

        _authServiceMock
            .Setup(s => s.ListPoamAsync(SystemId, null, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var tool = new ListPoamTool(_authServiceMock.Object, Mock.Of<ILogger<ListPoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = SystemId
        });

        var root = Parse(result);
        root.GetProperty("status").GetString().Should().Be("success");
        root.GetProperty("data").GetProperty("total_items").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ListPoam_MissingSystemId_ReturnsError()
    {
        var tool = new ListPoamTool(_authServiceMock.Object, Mock.Of<ILogger<ListPoamTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var root = Parse(result);
        root.GetProperty("status").GetString().Should().Be("error");
    }
}
