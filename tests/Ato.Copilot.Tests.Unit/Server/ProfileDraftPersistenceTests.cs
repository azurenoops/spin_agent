using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Interceptors;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Server;

public sealed partial class ProfileDraftPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly Mock<ITenantContextAccessor> _tenantAccessor = new();
    private readonly ProfileSaveHook _saveHook = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private const string Root = "/api/systems/mission/profile/";

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_tenantAccessor.Object);
        builder.Services.AddSingleton<TenantStampingSaveChangesInterceptor>();
        builder.Services.AddDbContext<AtoCopilotContext>((services, options) => options.UseSqlite(_connection)
            .AddInterceptors(services.GetRequiredService<TenantStampingSaveChangesInterceptor>(), _saveHook));
        builder.Services.AddScoped<ISystemProfileService, SystemProfileService>();
        _user.SetupGet(x => x.CurrentUserId).Returns("owner");
        _app = builder.Build();
        typeof(DashboardEndpoints).GetMethod("MapProfileRoutes", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [_app.MapGroup("/api"), _app, _user.Object]);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            await db.Database.EnsureCreatedAsync();
            var foreignTenant = Guid.NewGuid();
            db.Tenants.Add(new Tenant { Id = _tenant, DisplayName = "Tenant", CreatedBy = "test" });
            db.RegisteredSystems.AddRange(
                new RegisteredSystem { Id = "mission", Name = "Mission", TenantId = _tenant },
                new RegisteredSystem { Id = "other", Name = "Other", TenantId = _tenant },
                new RegisteredSystem { Id = "foreign", Name = "Foreign", TenantId = foreignTenant });
            db.SystemProfileSections.Add(new SystemProfileSection
            {
                Id = "foreign-section", TenantId = foreignTenant, RegisteredSystemId = "foreign",
                SectionType = ProfileSectionType.UsersAndAccess, DraftContent = "foreign original",
                UserCategories = [new UserCategory { Id = "foreign-child", TenantId = foreignTenant, CategoryName = "Foreign" }]
            });
            db.RmfRoleAssignments.Add(new RmfRoleAssignment
            {
                TenantId = _tenant, RegisteredSystemId = "mission", UserId = "owner",
                RmfRole = RmfRole.MissionOwner, IsActive = true
            });
            await db.SaveChangesAsync();
        }
        _tenantAccessor.SetupGet(x => x.Current).Returns(new TenantContext(_tenant));
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public static TheoryData<string, string, string, string> Sections => new()
    {
        { "UsersAndAccess", "userCategories", "categoryName",
            """{"categoryName":"Operators","description":"People","approximateCount":12,"accessMethod":"SSO","dataSensitivityLevel":"CUI"}""" },
        { "DataTypes", "dataTypeEntries", "dataTypeName",
            """{"dataTypeName":"Records","description":"Data","sensitivityClassification":"CUI","source":"Office","destination":"Archive","applicableRegulations":"FISMA"}""" },
        { "PortsProtocolsAndServices", "ppsEntries", "serviceName",
            """{"portOrRange":"443","protocol":"TCP","serviceName":"HTTPS","direction":"Inbound","justification":"Portal"}""" },
        { "LeveragedAuthorizations", "leveragedAuthorizations", "providerName",
            """{"providerName":"Provider","authorizationType":"FedRAMP","authorizationDate":"2026-09-01","coveredControlFamilies":"AC,IA"}""" }
    };

    [Theory]
    [MemberData(nameof(Sections))]
    public async Task Put_PersistsRowsAndReturnsSameCanonicalListsAsGet(
        string section, string list, string field, string row)
    {
        // Arrange
        var body = $$"""{"content":"original","childItems":[{{row}}]}""";

        // Act
        var response = await PutAsync(section, body);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        var read = await _client.GetFromJsonAsync<JsonElement>(Root + section);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        saved.GetProperty(list).GetArrayLength().Should().Be(1);
        saved.GetProperty(list)[0].GetProperty(field).GetString().Should().NotBeNullOrEmpty();
        foreach (var name in new[] { "userCategories", "dataTypeEntries", "ppsEntries", "leveragedAuthorizations" })
            saved.GetProperty(name).GetRawText().Should().Be(read.GetProperty(name).GetRawText());
        Guid.TryParse(saved.GetProperty(list)[0].GetProperty("id").GetString(), out _).Should().BeTrue();
        saved.GetProperty(list)[0].GetProperty("sortOrder").GetInt32().Should().Be(0);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var persisted = await db.SystemProfileSections.AsNoTracking().SingleAsync();
        persisted.DraftContent.Should().Be("original");
    }

    [Theory]
    [InlineData("""[null]""")]
    [InlineData("""["not an object"]""")]
    [InlineData("""[{}]""")]
    [InlineData("""[{"categoryName":12}]""")]
    [InlineData("""[{"categoryName":" "}]""")]
    [InlineData("""[{"categoryName":"Users","approximateCount":-1}]""")]
    [InlineData("""[{"categoryName":"Users","approximateCount":1.5}]""")]
    [InlineData("""[{"categoryName":"Users","approximateCount":2147483648}]""")]
    [InlineData("""[{"categoryName":"Users","approximateCount":"12"}]""")]
    [InlineData("""[{"categoryName":"Users","description":false}]""")]
    [InlineData("""[{"categoryName":"Users","id":"foreign-id"}]""")]
    [InlineData("""[{"categoryName":"Users","id":"foreign-child"}]""")]
    public async Task Put_InvalidChildren_DoesNotSaveScalarOrAudit(string children)
    {
        // Arrange
        await SeedSectionAsync();

        // Act
        var response = await PutAsync("UsersAndAccess", $$"""{"content":"must not save","childItems":{{children}}}""");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("original");
        (await db.UserCategories.SingleAsync()).CategoryName.Should().Be("Existing");
        (await db.ProfileAuditEntries.CountAsync()).Should().Be(0);
        (await db.UserCategories.IgnoreQueryFilters().SingleAsync(c => c.Id == "foreign-child"))
            .CategoryName.Should().Be("Foreign");
    }

    [Theory]
    [InlineData("""{"content":"changed"}""", 1)]
    [InlineData("""{"content":"changed","childItems":null}""", 1)]
    [InlineData("""{"content":"changed","childItems":[]}""", 0)]
    public async Task Put_OmittedOrNullPreserves_EmptyClears(string body, int expected)
    {
        // Arrange
        await SeedSectionAsync();

        // Act
        var response = await PutAsync("UsersAndAccess", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.UserCategories.CountAsync()).Should().Be(expected);
        (await db.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("changed");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("userCategories").GetArrayLength().Should().Be(expected);
    }

    [Theory]
    [InlineData("random", "mission", HttpStatusCode.Forbidden)]
    [InlineData("owner", "foreign", HttpStatusCode.NotFound)]
    public async Task Put_PermissionAndTenantBoundary_DoNotWrite(string actor, string system, HttpStatusCode status)
    {
        // Arrange
        await SeedSectionAsync();
        _user.SetupGet(x => x.CurrentUserId).Returns(actor);

        // Act
        var response = await _client.PutAsJsonAsync($"/api/systems/{system}/profile/UsersAndAccess",
            new { content = "must not save", childItems = Array.Empty<object>() });

        // Assert
        response.StatusCode.Should().Be(status);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("original");
        (await db.UserCategories.CountAsync()).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Sections))]
    public async Task Service_RoundTripUpdateReorderAndClear_PreservesIdsAndAllFields(
        string section, string list, string field, string row)
    {
        // Arrange
        using var scope = _app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemProfileService>();
        var type = Enum.Parse<ProfileSectionType>(section);
        var input = JsonSerializer.Deserialize<Dictionary<string, object?>>(row)!;
        input["_tempId"] = "client-only";
        var first = JsonSerializer.SerializeToElement(input);
        input[field] = "Second";
        var second = JsonSerializer.SerializeToElement(input);

        // Act
        await service.SaveDraftWithChildrenAsync("mission", type, "original", [first, second], "owner");
        var created = await _client.GetFromJsonAsync<JsonElement>(Root + section);
        var originalRows = created.GetProperty(list);
        var edited = JsonSerializer.Deserialize<Dictionary<string, object?>>(originalRows[1])!;
        edited[field] = "Updated existing row";
        var reordered = new[] { JsonSerializer.SerializeToElement(edited), originalRows[0].Clone() };
        await service.SaveDraftWithChildrenAsync("mission", type, "updated", reordered, "owner");
        var read = await _client.GetFromJsonAsync<JsonElement>(Root + section);
        await service.SaveDraftAsync("mission", type, "scalar-only tool save", "owner");
        var afterScalarSave = await _client.GetFromJsonAsync<JsonElement>(Root + section);
        await service.SaveDraftWithChildrenAsync("mission", type, "cleared", [], "owner");
        var cleared = await _client.GetFromJsonAsync<JsonElement>(Root + section);

        // Assert
        var rows = read.GetProperty(list);
        rows[0].GetProperty("id").GetString().Should().Be(originalRows[1].GetProperty("id").GetString());
        rows[1].GetProperty("id").GetString().Should().Be(originalRows[0].GetProperty("id").GetString());
        rows[0].GetProperty("sortOrder").GetInt32().Should().Be(0);
        rows[1].GetProperty("sortOrder").GetInt32().Should().Be(1);
        rows[0].GetProperty(field).GetString().Should().Be("Updated existing row");
        using var expected = JsonDocument.Parse(row);
        foreach (var property in expected.RootElement.EnumerateObject())
        {
            if (property.Name == "authorizationDate")
                rows[1].GetProperty(property.Name).GetDateTime().Should().Be(new DateTime(2026, 9, 1));
            else
                rows[1].GetProperty(property.Name).GetRawText().Should().Be(property.Value.GetRawText());
        }
        rows[0].GetProperty("id").GetString().Should().NotBe("client-only");
        rows[0].TryGetProperty("_tempId", out _).Should().BeFalse();
        afterScalarSave.GetProperty(list).GetRawText().Should().Be(rows.GetRawText());
        cleared.GetProperty(list).GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("""[{"id":"existing","revision":1,"categoryName":"First"},{"id":"existing","revision":1,"categoryName":"Duplicate"}]""")]
    [InlineData("""[{"id":"other-child","categoryName":"Foreign"}]""")]
    [InlineData("""[{"categoryName":"Valid"},{"categoryName":"Later invalid","approximateCount":-1}]""")]
    [InlineData("""[{"categoryName":"Users","categoryName":"Repeated field"}]""")]
    [InlineData("""[{"categoryName":"Users","tenantId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}]""")]
    [InlineData("""[{"categoryName":"Users","sortOrder":-1}]""")]
    public async Task Put_DuplicateForeignOrLaterInvalidRows_DoesNotPartiallyApply(string children)
    {
        // Arrange
        await SeedSectionAsync();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.SystemProfileSections.Add(new SystemProfileSection
            {
                Id = "other-section", TenantId = _tenant, RegisteredSystemId = "other",
                SectionType = ProfileSectionType.UsersAndAccess,
                UserCategories = [new UserCategory { Id = "other-child", TenantId = _tenant, CategoryName = "Other" }]
            });
            await db.SaveChangesAsync();
        }

        // Act
        var response = await PutAsync("UsersAndAccess", $$"""{"content":"must not save","childItems":{{children}}}""");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var inspect = _app.Services.CreateScope();
        var context = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await context.SystemProfileSections.SingleAsync(s => s.Id == "section")).DraftContent.Should().Be("original");
        (await context.UserCategories.SingleAsync(c => c.Id == "existing")).CategoryName.Should().Be("Existing");
        (await context.UserCategories.SingleAsync(c => c.Id == "other-child")).CategoryName.Should().Be("Other");
        (await context.UserCategories.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData("DataTypes", """{"dataTypeName":"Records"}""")]
    [InlineData("DataTypes", """{"dataTypeName":"Records","sensitivityClassification":true}""")]
    [InlineData("PortsProtocolsAndServices", """{"portOrRange":"443","protocol":"TCP","serviceName":"HTTPS"}""")]
    [InlineData("LeveragedAuthorizations", """{"providerName":"Provider"}""")]
    [InlineData("LeveragedAuthorizations", """{"providerName":"Provider","authorizationType":"ATO","authorizationDate":"not a date"}""")]
    [InlineData("MissionAndPurpose", """{"categoryName":"Wrong section"}""")]
    public async Task Put_InvalidSectionSpecificFields_DoesNotCreateSection(string section, string row)
    {
        // Arrange
        var body = $$"""{"content":"must not save","childItems":[{{row}}]}""";

        // Act
        var response = await PutAsync(section, body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemProfileSections.CountAsync()).Should().Be(0);
        (await db.ProfileAuditEntries.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("categoryName", 201)]
    [InlineData("description", 2001)]
    [InlineData("accessMethod", 501)]
    [InlineData("dataSensitivityLevel", 101)]
    public async Task Put_OverlengthFields_DoesNotChangeExistingDraft(string field, int length)
    {
        // Arrange
        await SeedSectionAsync();
        var row = new Dictionary<string, object?> { ["categoryName"] = "Users", [field] = new string('x', length) };

        // Act
        var response = await _client.PutAsJsonAsync(Root + "UsersAndAccess", new { content = "changed", childItems = new[] { row } });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("original");
        (await db.UserCategories.SingleAsync()).CategoryName.Should().Be("Existing");
    }

    [Fact]
    public async Task Put_UnderReview_DoesNotClearOrEdit()
    {
        // Arrange
        await SeedSectionAsync(SspSectionStatus.UnderReview);

        // Act
        var response = await PutAsync("UsersAndAccess", """{"content":"changed","childItems":[]}""");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemProfileSections.SingleAsync()).GovernanceStatus.Should().Be(SspSectionStatus.UnderReview);
        (await db.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("original");
        (await db.UserCategories.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Service_ClearApprovedDraft_RetainsExactApprovedChildSnapshot()
    {
        // Arrange
        await SeedSectionAsync(SspSectionStatus.UnderReview);
        using var scope = _app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemProfileService>();
        var approved = await service.ReviewSectionAsync("mission", ProfileSectionType.UsersAndAccess,
            ReviewDecision.Approve, "reviewer", simulatedRole: RmfRole.Issm);
        await service.ReviewUserCategoryAsync("mission", "existing", "submit", 1, "owner");
        approved = await service.ReviewUserCategoryAsync("mission", "existing", "approve", 2, "reviewer", simulatedRole: RmfRole.Issm);
        string snapshot;
        using (var before = _app.Services.CreateScope())
            snapshot = (await before.ServiceProvider.GetRequiredService<AtoCopilotContext>()
                .ProfileAuditEntries.SingleAsync(a => a.Id == approved.ApprovedSnapshotId)).SnapshotJson!;

        // Act
        await service.SaveDraftWithChildrenAsync("mission", ProfileSectionType.UsersAndAccess, "changed", [], "owner");

        // Assert
        using var inspect = _app.Services.CreateScope();
        var db = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var section = await db.SystemProfileSections.SingleAsync();
        section.GovernanceStatus.Should().Be(SspSectionStatus.Draft);
        section.ApprovedContent.Should().Be("original");
        section.ApprovedSnapshotId.Should().Be(approved.ApprovedSnapshotId);
        (await db.ProfileAuditEntries.SingleAsync(a => a.Id == section.ApprovedSnapshotId))
            .SnapshotJson.Should().Be(snapshot).And.Contain("Existing");
        (await db.UserCategories.SingleAsync()).PendingDeletion.Should().BeTrue();
        (await db.UserCategories.SingleAsync()).ApprovedSnapshotId.Should().NotBeNull();
    }

    [Fact]
    public async Task Service_DatabaseChildFailure_RollsBackScalarAndChildDeletion()
    {
        // Arrange
        await SeedSectionAsync();
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER RejectProfileChild BEFORE INSERT ON UserCategories
            BEGIN SELECT RAISE(ABORT, 'Synthetic persistence failure'); END;
            """);
        var service = scope.ServiceProvider.GetRequiredService<ISystemProfileService>();
        var row = JsonSerializer.SerializeToElement(new { categoryName = "Rejected" });

        // Act
        var save = () => service.SaveDraftWithChildrenAsync("mission", ProfileSectionType.UsersAndAccess, "must roll back", [row], "owner");

        // Assert
        await save.Should().ThrowAsync<DbUpdateException>();
        using var inspect = _app.Services.CreateScope();
        var fresh = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await fresh.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("original");
        (await fresh.UserCategories.SingleAsync()).CategoryName.Should().Be("Existing");
        (await fresh.ProfileAuditEntries.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("""{"categoryName":"Wrong outer shape"}""")]
    [InlineData("false")]
    [InlineData("1")]
    [InlineData("\"text\"")]
    public async Task Put_ChildItemsMustBeArray_DoesNotWrite(string children)
    {
        // Arrange
        await SeedSectionAsync();

        // Act
        var response = await PutAsync("UsersAndAccess", $$"""{"content":"changed","childItems":{{children}}}""");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("original");
        (await db.UserCategories.CountAsync()).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Sections))]
    public async Task Service_AllStringLengthsAreValidatedBeforeSave(
        string section, string list, string field, string row)
    {
        // Arrange
        var type = Enum.Parse<ProfileSectionType>(section);
        var entityType = type switch
        {
            ProfileSectionType.UsersAndAccess => typeof(UserCategory),
            ProfileSectionType.DataTypes => typeof(DataTypeEntry),
            ProfileSectionType.PortsProtocolsAndServices => typeof(PpsEntry),
            _ => typeof(LeveragedAuthorization)
        };
        var input = JsonSerializer.Deserialize<Dictionary<string, object?>>(row)!;
        using var scope = _app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemProfileService>();
        input.Should().ContainKey(field);

        // Act
        foreach (var member in entityType.GetProperties())
        {
            var length = member.GetCustomAttribute<System.ComponentModel.DataAnnotations.MaxLengthAttribute>()?.Length;
            var name = JsonNamingPolicy.CamelCase.ConvertName(member.Name);
            if (length is null || !input.ContainsKey(name)) continue;
            var invalid = new Dictionary<string, object?>(input) { [name] = new string('x', length.Value + 1) };
            var save = () => service.SaveDraftWithChildrenAsync("mission", type, "must not save",
                [JsonSerializer.SerializeToElement(invalid)], "owner");
            await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("INVALID_INPUT:*");
        }

        // Assert
        using var inspect = _app.Services.CreateScope();
        (await inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>()
            .SystemProfileSections.CountAsync()).Should().Be(0);
        (await _client.GetFromJsonAsync<JsonElement>(Root + section)).GetProperty(list).GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task Service_CountBoundsPersist_AndOverlengthScalarCannotClearRows(int count)
    {
        // Arrange
        using var scope = _app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemProfileService>();
        var row = JsonSerializer.SerializeToElement(new { categoryName = "Users", approximateCount = count });

        // Act
        await service.SaveDraftWithChildrenAsync("mission", ProfileSectionType.UsersAndAccess, "original", [row], "owner");
        var save = () => service.SaveDraftWithChildrenAsync("mission", ProfileSectionType.UsersAndAccess,
            new string('x', 16001), [], "owner");

        // Assert
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("INVALID_INPUT:*");
        using var inspect = _app.Services.CreateScope();
        var db = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.UserCategories.SingleAsync()).ApproximateCount.Should().Be(count);
        (await db.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("original");
    }

    private Task<HttpResponseMessage> PutAsync(string section, string json) =>
        _client.PutAsync(Root + section, new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task SeedSectionAsync(SspSectionStatus status = SspSectionStatus.Draft)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.SystemProfileSections.Add(new SystemProfileSection
        {
            Id = "section", TenantId = _tenant, RegisteredSystemId = "mission",
            SectionType = ProfileSectionType.UsersAndAccess, GovernanceStatus = status, DraftContent = "original",
            UserCategories = [new UserCategory { Id = "existing", TenantId = _tenant, CategoryName = "Existing" }]
        });
        await db.SaveChangesAsync();
    }

    private sealed class ProfileSaveHook : SaveChangesInterceptor
    {
        public Func<DbContext, Task>? BeforeSave { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var callback = BeforeSave;
            BeforeSave = null;
            if (callback is not null) await callback(eventData.Context!);
            return result;
        }
    }
}
