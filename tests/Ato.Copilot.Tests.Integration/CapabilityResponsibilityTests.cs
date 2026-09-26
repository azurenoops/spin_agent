using System.Net;
using System.Data;
using System.Data.Common;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Configuration.Tenancy;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

public sealed partial class CapabilityResponsibilityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _person = Guid.NewGuid();
    private readonly string _system = Guid.NewGuid().ToString();
    private readonly Guid _capability = Guid.NewGuid();
    private readonly Mock<INarrativeChangeImpactService> _impactConsumer = new(MockBehavior.Strict);
    private readonly CommitFailureInterceptor _commitFailure = new();
    private readonly RoutingBoundaryInterceptor _routingBoundary = new();
    private readonly RoutingAckFailureInterceptor _routingAckFailure = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private DbContextOptions<AtoCopilotContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).AddInterceptors(_commitFailure, _routingAckFailure).Options;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AtoCopilotContext>(o => o.UseSqlite(_connection).AddInterceptors(_commitFailure));
        builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options));
        builder.Services.AddSingleton(factory.Object);
        builder.Services.AddSingleton<ISystemWorkspaceAccessService, SystemWorkspaceAccessService>();
        builder.Services.AddScoped<ICapabilityResponsibilityService, CapabilityResponsibilityService>();
        builder.Services.AddScoped<ICapabilityResponsibilityImpactDispatcher, CapabilityResponsibilityImpactDispatcher>();
        builder.Services.AddSingleton(_impactConsumer.Object);
        builder.Services.AddSingleton<ITenantContext>(new TenantContext(_tenant)
        {
            PersonId = _person,
            IsWorkspaceRequest = true
        });
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
        builder.Services.AddAuthorization();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.CurrentUserId).Returns("fixture-reviewer");
        user.SetupGet(u => u.CurrentUserName).Returns("Fixture Reviewer");
        builder.Services.AddSingleton(user.Object);
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapCapabilitySubscriptionEndpoints();
        await using (var db = new AtoCopilotContext(_options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Tenants.Add(new Tenant { Id = _tenant, DisplayName = "Synthetic organization", OnboardingState = OnboardingState.Active });
            db.Persons.Add(new Person { Id = _person, TenantId = _tenant, DisplayName = "Reviewer", Email = "reviewer@example.invalid" });
            db.OrganizationMemberships.Add(new() { TenantId = _tenant, PersonId = _person, DirectoryTenantId = Guid.NewGuid(),
                ObjectId = Guid.NewGuid(), GrantedBy = "fixture" });
            db.RegisteredSystems.Add(new() { Id = _system, TenantId = _tenant, Name = "Synthetic system", CreatedBy = "fixture" });
            db.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person, Role = OrganizationRole.Isso });
            var profile = new CspProfile { DisplayName = "Synthetic CSP", LegalEntityName = "Synthetic CSP", OnboardingState = OnboardingState.Active };
            var component = new CspInheritedComponent { CspProfileId = profile.Id, Name = "Synthetic component", Status = CspInheritedComponentStatus.Published };
            db.AddRange(profile, component, new CspInheritedCapability
            {
                Id = _capability, CspInheritedComponentId = component.Id,
                Name = "Synthetic logging", Description = "Synthetic fixture",
                Status = CspInheritedCapabilityStatus.Mapped, MappedNistControlIds = ["AC-2", "AU-6", "SI-4"]
            });
            await db.SaveChangesAsync();
        }
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private string Route => $"/api/dashboard/systems/{_system}/capability-subscriptions";

    [Fact]
    public async Task Preview_DistinguishesMissingBaselineAndAllocation_WithoutInferringInheritance()
    {
        // Arrange
        (await _client.PostAsJsonAsync(Route, new { capabilityId = _capability })).EnsureSuccessStatusCode();

        // Act
        var before = await _client.GetAsync($"{Route}/responsibilities");
        await AddBaselineAsync();
        var after = await _client.GetAsync($"{Route}/responsibilities");

        // Assert
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        (await before.Content.ReadAsStringAsync()).Should().Contain("MissingBaseline");
        after.StatusCode.Should().Be(HttpStatusCode.OK);
        (await after.Content.ReadAsStringAsync()).Should().Contain("MissingAllocation").And.Contain("OutsideBaseline");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
        (await verify.OrgInheritanceDefaults.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Preview_ExposesAvailableReviewSnapshotWithoutArtifactCredentials()
    {
        // Arrange
        await AddBaselineAsync();
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspInheritedComponents.SingleAsync()).SourceArtifactReference =
                "https://example.invalid/provider-evidence?sig=synthetic-secret";
            await db.SaveChangesAsync();
        }
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });

        // Act
        var preview = await _client.GetFromJsonAsync<JsonElement>($"{Route}/responsibilities");

        // Assert
        var item = preview.GetProperty("items").EnumerateArray().First();
        item.GetProperty("sourceAvailable").GetBoolean().Should().BeTrue();
        var snapshot = item.GetProperty("sourceSnapshotJson").GetString();
        snapshot.Should().Contain("Synthetic logging").And.Contain("[redacted]")
            .And.NotContain("synthetic-secret").And.NotContain("https://example.invalid");
        (await ConfirmAsync(_capability, "AU-6")).StatusCode.Should().Be(HttpStatusCode.OK,
            "the display snapshot is redacted but the opaque server revision still pins the full source");
    }

    [Fact]
    public async Task Preview_UnavailableSourceWithholdsUnpublishedSnapshotContent()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspInheritedComponents.SingleAsync()).Status = CspInheritedComponentStatus.Draft;
            (await db.CspInheritedCapabilities.SingleAsync()).Description = "Unpublished synthetic provider draft";
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"{Route}/responsibilities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Unpublished synthetic provider draft");
        using var preview = JsonDocument.Parse(body);
        var item = preview.RootElement.GetProperty("items").EnumerateArray().First();
        item.GetProperty("sourceAvailable").GetBoolean().Should().BeFalse();
        item.GetProperty("sourceSnapshotJson").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("state").GetString().Should().Be("PendingReview");
    }

    [Fact]
    public async Task Preview_ReviewedSnapshotIsPersistedHistory_NotCurrentUnpublishedContent()
    {
        // Arrange
        await AddBaselineAsync();
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspInheritedCapabilities.SingleAsync()).Description = "Previously reviewed provider content";
            (await db.CspInheritedComponents.SingleAsync()).SourceArtifactReference =
                "https://example.invalid/evidence?sig=synthetic-old-secret";
            await db.SaveChangesAsync();
        }
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspInheritedCapabilities.SingleAsync()).Description = "Unpublished replacement content";
            (await db.CspInheritedComponents.SingleAsync()).Status = CspInheritedComponentStatus.Draft;
            await db.SaveChangesAsync();
        }

        // Act
        var preview = await _client.GetFromJsonAsync<JsonElement>($"{Route}/responsibilities");

        // Assert
        var item = preview.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("controlId").GetString() == "AU-6");
        item.GetProperty("sourceAvailable").GetBoolean().Should().BeFalse();
        item.GetProperty("sourceSnapshotJson").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("reviewedSourceSnapshotJson").GetString().Should()
            .Contain("Previously reviewed provider content").And.Contain("[redacted]")
            .And.NotContain("Unpublished replacement content").And.NotContain("synthetic-old-secret");
        item.GetProperty("reviewedSourceRevision").GetString().Should().NotBe(item.GetProperty("sourceRevision").GetString());
        var unconfirmed = preview.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("controlId").GetString() == "AC-2");
        unconfirmed.GetProperty("reviewedSourceSnapshotJson").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner, HttpStatusCode.Forbidden)]
    [InlineData(OrganizationRole.AuthorizingOfficial, HttpStatusCode.Forbidden)]
    [InlineData(OrganizationRole.Administrator, HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.NotFound)]
    public async Task Subscribe_DeniesWithoutEffectiveIssoOrIssm(
        OrganizationRole? role, HttpStatusCode expected)
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemRoleAssignments.RemoveRange(await db.SystemRoleAssignments.ToListAsync());
            if (role == OrganizationRole.Administrator)
                db.OrganizationRoleAssignments.Add(new() { TenantId = _tenant, PersonId = _person, Role = role.Value });
            else if (role.HasValue)
                db.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person, Role = role.Value });
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });

        // Assert
        response.StatusCode.Should().Be(expected);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.CapabilitySubscriptions.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Subscribe_CallerTransactionRetainsOwnershipAndAtomicity(bool commit)
    {
        // Arrange
        await AddBaselineAsync();
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var service = scope.ServiceProvider.GetRequiredService<ICapabilityResponsibilityService>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        // Act
        await service.SubscribeAsync(_system, _capability, "fixture-reviewer");
        db.Database.CurrentTransaction.Should().BeSameAs(transaction);
        (await db.RegisteredSystems.SingleAsync(s => s.Id == _system)).Name = "Caller transaction marker";
        await db.SaveChangesAsync();
        if (commit) await transaction.CommitAsync();
        else await transaction.RollbackAsync();

        // Assert
        await using var verify = new AtoCopilotContext(_options);
        (await verify.CapabilitySubscriptions.CountAsync()).Should().Be(commit ? 1 : 0);
        (await verify.Set<CapabilityResponsibilityProjection>().CountAsync()).Should().Be(commit ? 2 : 0);
        (await verify.Set<CapabilityResponsibilityImpact>().CountAsync()).Should().Be(commit ? 2 : 0);
        (await verify.RegisteredSystems.SingleAsync(s => s.Id == _system)).Name
            .Should().Be(commit ? "Caller transaction marker" : "Synthetic system");
    }

    [Fact]
    public async Task Subscribe_CallerCommitFailureLeavesRollbackWithCaller()
    {
        // Arrange
        await AddBaselineAsync();
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var service = scope.ServiceProvider.GetRequiredService<ICapabilityResponsibilityService>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        _commitFailure.FailNext = true;

        // Act
        await service.SubscribeAsync(_system, _capability, "fixture-reviewer");
        Func<Task> commit = () => transaction.CommitAsync();

        // Assert
        _commitFailure.FailNext.Should().BeTrue("the service must not commit a caller-owned transaction");
        await commit.Should().ThrowAsync<InvalidOperationException>().WithMessage("Synthetic commit failure");
        db.Database.CurrentTransaction.Should().BeSameAs(transaction);
        await transaction.RollbackAsync();
        await using var verify = new AtoCopilotContext(_options);
        (await verify.CapabilitySubscriptions.CountAsync()).Should().Be(0);
        (await verify.Set<CapabilityResponsibilityProjection>().CountAsync()).Should().Be(0);
        (await verify.Set<CapabilityResponsibilityImpact>().CountAsync()).Should().Be(0);
    }

    private async Task AddBaselineAsync()
    {
        await using var db = new AtoCopilotContext(_options);
        db.ControlBaselines.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, BaselineLevel = "Moderate",
            ControlIds = ["AC-2", "AU-6"], TotalControls = 2, CreatedBy = "fixture" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Confirm_PersistsOnlyApplicableControls_AndPreservesNarrativeStatus()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        await using (var db = new AtoCopilotContext(_options))
        {
            var implementation = new ControlImplementation { TenantId = _tenant, RegisteredSystemId = _system, ControlId = "AU-6",
                Narrative = "Approved synthetic text", TechnicalNarrative = "Approved synthetic text",
                ApprovalStatus = SspSectionStatus.Approved, ImplementationStatus = ImplementationStatus.Planned };
            var approved = new NarrativeVersion { TenantId = _tenant, ControlImplementationId = implementation.Id,
                Content = "Approved synthetic text", Status = SspSectionStatus.Approved, AuthoredBy = "fixture-author" };
            db.AddRange(implementation, approved);
            await db.SaveChangesAsync();
            implementation.ApprovedVersionId = approved.Id;
            await db.SaveChangesAsync();
        }

        // Act
        var response = await ConfirmAsync(_capability, "AU-6");
        var repeat = await _client.PostAsync($"{Route}/reconcile", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verify = new AtoCopilotContext(_options);
        var designation = await verify.ControlInheritances.SingleAsync();
        designation.ControlId.Should().Be("AU-6");
        designation.InheritanceType.Should().Be(InheritanceType.Shared);
        designation.DesignationSource.Should().Be("CspSubscription");
        var narrative = await verify.ControlImplementations.SingleAsync();
        narrative.Narrative.Should().Be("Approved synthetic text");
        narrative.TechnicalNarrative.Should().Be("Approved synthetic text");
        narrative.ApprovalStatus.Should().Be(SspSectionStatus.Approved);
        narrative.ImplementationStatus.Should().Be(ImplementationStatus.Planned);
        (await verify.ControlBaselines.SingleAsync()).SharedControls.Should().Be(1);
        (await verify.OrgInheritanceDefaults.CountAsync()).Should().Be(0);
        var baselineService = new BaselineService(_app.Services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IReferenceDataService>(), NullLogger<BaselineService>.Instance, Mock.Of<IOrgInheritanceService>());
        var crm = await baselineService.GenerateCrmAsync(_system);
        crm.SharedControls.Should().Be(1);
        crm.FamilyGroups.SelectMany(f => f.Controls).Single(c => c.ControlId == "AU-6")
            .CustomerResponsibility.Should().Be("Review alerts daily");
        crm.FamilyGroups.SelectMany(f => f.Controls).Single(c => c.ControlId == "AU-6")
            .Provider.Should().Be("Synthetic CSP");
        var ssp = await new SspService(_app.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SspService>.Instance).GenerateSspAsync(_system, sections: ["controls"]);
        ssp.Content.Should().Contain("**Responsibility**: Shared").And.Contain("Approved synthetic text")
            .And.Contain("**Status**: Planned");
    }

    [Fact]
    public async Task Overlap_UnsubscribeAndRepeat_KeepOtherSourcesWithoutDuplicateDesignations()
    {
        // Arrange
        await AddBaselineAsync();
        var second = await AddCapabilityAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        await _client.PostAsJsonAsync(Route, new { capabilityId = second });

        // Act
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        (await ConfirmAsync(second, "AU-6")).EnsureSuccessStatusCode();
        (await _client.DeleteAsync($"{Route}/{_capability}")).EnsureSuccessStatusCode();
        (await _client.DeleteAsync($"{Route}/{_capability}")).EnsureSuccessStatusCode();
        (await _client.PostAsync($"{Route}/reconcile", null)).EnsureSuccessStatusCode();

        // Assert
        await using (var verify = new AtoCopilotContext(_options))
        {
            (await verify.ControlInheritances.CountAsync()).Should().Be(1);
            (await verify.CapabilitySubscriptions.CountAsync(s => s.IsActive)).Should().Be(1);
        }
        (await _client.DeleteAsync($"{Route}/{second}")).EnsureSuccessStatusCode();
        (await _client.DeleteAsync($"{Route}/{second}")).EnsureSuccessStatusCode();
        await using var final = new AtoCopilotContext(_options);
        (await final.ControlInheritances.CountAsync()).Should().Be(0);
        (await final.ControlBaselines.SingleAsync()).SharedControls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderWithdrawsOneOverlap_PreservesOtherCurrentConfirmedSource(bool archive)
    {
        // Arrange
        await AddBaselineAsync();
        var second = await AddCapabilityAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        await _client.PostAsJsonAsync(Route, new { capabilityId = second });
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        (await ConfirmAsync(second, "AU-6")).EnsureSuccessStatusCode();
        string designationId;
        await using (var db = new AtoCopilotContext(_options))
        {
            designationId = (await db.ControlInheritances.SingleAsync()).Id;
            var withdrawn = await db.CspInheritedCapabilities.SingleAsync(c => c.Id == second);
            if (archive) withdrawn.Status = CspInheritedCapabilityStatus.Archived;
            else withdrawn.MappedNistControlIds = ["SI-4"];
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsync($"{Route}/reconcile", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verify = new AtoCopilotContext(_options);
        var designation = await verify.ControlInheritances.SingleAsync();
        designation.Id.Should().Be(designationId);
        designation.InheritanceType.Should().Be(InheritanceType.Shared);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PendingReview").And.Contain("Applied");
    }

    [Fact]
    public async Task OverlapAwaitingProviderReview_RemainsUnresolvedRatherThanBeingTreatedAsWithdrawn()
    {
        // Arrange
        await AddBaselineAsync();
        var second = await AddCapabilityAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        await _client.PostAsJsonAsync(Route, new { capabilityId = second });
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        (await ConfirmAsync(second, "AU-6")).EnsureSuccessStatusCode();
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspInheritedCapabilities.SingleAsync(c => c.Id == second)).Status = CspInheritedCapabilityStatus.NeedsReview;
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsync($"{Route}/reconcile", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PendingReview");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("OrgDerived")]
    [InlineData("CrmImport")]
    [InlineData("ProfileApply")]
    public async Task ConfirmAndUnsubscribe_PreserveOtherSourceRows(string source)
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        var id = Guid.NewGuid().ToString();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.ControlInheritances.Add(new() { Id = id, TenantId = _tenant,
                ControlBaselineId = (await db.ControlBaselines.SingleAsync()).Id,
                ControlId = "AU-6", InheritanceType = InheritanceType.Customer,
                CustomerResponsibility = "Local override", DesignationSource = source, SetBy = "original-reviewer" });
            await db.SaveChangesAsync();
        }

        // Act
        var confirmed = await ConfirmAsync(_capability, "AU-6");
        var removed = await _client.DeleteAsync($"{Route}/{_capability}");

        // Assert
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        removed.IsSuccessStatusCode.Should().BeTrue();
        await using var verify = new AtoCopilotContext(_options);
        var designation = await verify.ControlInheritances.SingleAsync();
        designation.Id.Should().Be(id);
        designation.DesignationSource.Should().Be(source);
        designation.CustomerResponsibility.Should().Be("Local override");
        designation.SetBy.Should().Be("original-reviewer");
    }

    [Fact]
    public async Task ConflictingOverlaps_RemainPending_AndDoNotSelectAnAllocation()
    {
        // Arrange
        await AddBaselineAsync();
        var second = await AddCapabilityAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        await _client.PostAsJsonAsync(Route, new { capabilityId = second });

        // Act
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        var response = await ConfirmAsync(second, "AU-6", "Customer");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ConflictingAllocations");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Confirmation_RejectsStaleProviderSnapshot_AndOutsideBaseline_Atomically()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        var preview = await _client.GetFromJsonAsync<JsonElement>($"{Route}/responsibilities");
        var staleRequest = ConfirmationBody(preview, _capability, "AU-6", "Shared");
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspInheritedCapabilities.SingleAsync()).Description = "Changed synthetic provider description";
            await db.SaveChangesAsync();
        }

        // Act
        var stale = await _client.PutAsJsonAsync($"{Route}/{_capability}/responsibilities", staleRequest);
        var outside = await ConfirmAsync(_capability, "SI-4");

        // Assert
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        outside.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ForeignSystem_IsDeniedEvenWithGlobalClaimsOrTenantVisibility()
    {
        // Arrange
        var foreignTenant = Guid.NewGuid();
        var foreignSystem = Guid.NewGuid().ToString();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Tenants.Add(new Tenant { Id = foreignTenant, DisplayName = "Foreign synthetic organization" });
            db.RegisteredSystems.Add(new() { Id = foreignSystem, TenantId = foreignTenant, Name = "Foreign system", CreatedBy = "fixture" });
            await db.SaveChangesAsync();
        }

        // Act
        var read = await _client.GetAsync($"/api/dashboard/systems/{foreignSystem}/capability-subscriptions");
        var write = await _client.PostAsJsonAsync($"/api/dashboard/systems/{foreignSystem}/capability-subscriptions", new { capabilityId = _capability });

        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        write.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.CapabilitySubscriptions.CountAsync()).Should().Be(0);
    }

    private async Task<Guid> AddCapabilityAsync()
    {
        await using var db = new AtoCopilotContext(_options);
        var capability = new CspInheritedCapability
        {
            CspInheritedComponentId = (await db.CspInheritedComponents.SingleAsync()).Id,
            Name = "Second synthetic logging", Status = CspInheritedCapabilityStatus.Mapped,
            MappedNistControlIds = ["AU-6"]
        };
        db.Add(capability);
        await db.SaveChangesAsync();
        return capability.Id;
    }

    private async Task<HttpResponseMessage> ConfirmAsync(Guid capability, string control, string type = "Shared")
    {
        var preview = await _client.GetFromJsonAsync<JsonElement>($"{Route}/responsibilities");
        return await _client.PutAsJsonAsync($"{Route}/{capability}/responsibilities", ConfirmationBody(preview, capability, control, type));
    }

    private static object ConfirmationBody(JsonElement preview, Guid capability, string control, string type) => new
    {
        baselineId = preview.GetProperty("baselineId").GetString(),
        sourceRevision = preview.GetProperty("items").EnumerateArray().First(i => i.GetProperty("capabilityId").GetGuid() == capability)
            .GetProperty("sourceRevision").GetString(),
        reviewRevision = preview.GetProperty("items").EnumerateArray().First(i => i.GetProperty("capabilityId").GetGuid() == capability)
            .GetProperty("reviewRevision").GetString(),
        allocations = new[] { new { controlId = control, inheritanceType = type, provider = "Synthetic CSP",
            customerResponsibility = "Review alerts daily" } }
    };

    [Fact]
    public async Task ProviderUpdate_PersistsSourceEventInSameSave_WithoutTouchingCustomerText()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var componentId = (await db.CspInheritedComponents.SingleAsync()).Id;
        var factory = _app.Services.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        var provider = new CspInheritedComponentService(factory, Mock.Of<ICspCapabilityMappingService>(),
            Options.Create(new CspInheritedOptions()), NullLogger<CspInheritedComponentService>.Instance,
            new CapabilityHistoryService(factory, NullLogger<CapabilityHistoryService>.Instance),
            new TenantContext(_tenant, isCspAdmin: true));

        // Act
        await provider.UpdateCapabilityAsync(componentId, _capability, "Changed provider source",
            "Synthetic change", ["AC-2", "AU-6"], null, "provider-fixture");
        await provider.UpdateCapabilityAsync(componentId, _capability, "Changed provider source",
            "Synthetic change", ["AC-2", "AU-6"], null, "provider-fixture");

        // Assert
        await using var verify = new AtoCopilotContext(_options);
        var events = await verify.Set<CspResponsibilitySourceEvent>().ToListAsync();
        events.Should().ContainSingle();
        events[0].CapabilityId.Should().Be(_capability);
        events[0].IsAvailable.Should().BeTrue();
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
        (await verify.ControlImplementations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Confirmation_RequestPipelineDeniesMissionOwnerAndCspAdministrator()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        var preview = await _client.GetFromJsonAsync<JsonElement>($"{Route}/responsibilities");
        var request = ConfirmationBody(preview, _capability, "AU-6", "Shared");
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.SystemRoleAssignments.SingleAsync()).Role = OrganizationRole.MissionOwner;
            await db.SaveChangesAsync();
        }

        // Act
        var missionOwner = await _client.PutAsJsonAsync($"{Route}/{_capability}/responsibilities", request);
        ((TenantContext)_app.Services.GetRequiredService<ITenantContext>()).IsCspAdmin = true;
        var csp = await _client.PutAsJsonAsync($"{Route}/{_capability}/responsibilities", request);

        // Assert
        missionOwner.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        csp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityConfirmation>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProviderOversight_CanReadAuthorizedSystemResponsibilities_ButCannotConfirm()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        var prior = await _client.GetFromJsonAsync<JsonElement>($"{Route}/responsibilities");
        var request = ConfirmationBody(prior, _capability, "AU-6", "Shared");
        var context = (TenantContext)_app.Services.GetRequiredService<ITenantContext>();
        context.TenantId = Guid.Empty;
        context.IsCspAdmin = true;

        // Act
        var read = await _client.GetAsync($"{Route}/responsibilities");
        var write = await _client.PutAsJsonAsync($"{Route}/{_capability}/responsibilities", request);

        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await read.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("canConfirm").GetBoolean().Should().BeFalse();
        payload.GetProperty("items").GetArrayLength().Should().Be(3);
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityConfirmation>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ChangedProviderAndTailoring_RemoveOnlyOwnedDesignations_AndRetainReviewImpact()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspInheritedCapabilities.SingleAsync()).Description = "Provider change";
            await db.SaveChangesAsync();
        }

        // Act
        var changed = await _client.PostAsync($"{Route}/reconcile", null);
        var repeated = await _client.PostAsync($"{Route}/reconcile", null);

        // Assert
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await changed.Content.ReadAsStringAsync()).Should().Contain("PendingReview");
        await using (var verify = new AtoCopilotContext(_options))
        {
            (await verify.ControlInheritances.CountAsync()).Should().Be(0);
            (await verify.Set<CapabilityResponsibilityImpact>().CountAsync()).Should().BeGreaterThan(0);
            var before = JsonDocument.Parse(await changed.Content.ReadAsStringAsync()).RootElement.GetProperty("pendingImpacts").GetArrayLength();
            var after = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync()).RootElement.GetProperty("pendingImpacts").GetArrayLength();
            after.Should().Be(before);
        }
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.ControlBaselines.SingleAsync()).ControlIds = ["AC-2"];
            await db.SaveChangesAsync();
        }
        (await _client.PostAsync($"{Route}/reconcile", null)).EnsureSuccessStatusCode();
        await using var final = new AtoCopilotContext(_options);
        (await final.ControlInheritances.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImpactDispatch_QueuesDurableMarkOnlyWork_Idempotently()
    {
        // Arrange
        await AddBaselineAsync();
        await AddNarrativesAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        var proposalId = Guid.NewGuid();
        _impactConsumer.Setup(s => s.QueueAsync(It.IsAny<NarrativeChangeImpactRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NarrativeChangeImpactResult([proposalId]));

        // Act
        var first = await _client.PostAsync($"{Route}/review-impacts/dispatch", null);
        var second = await _client.PostAsync($"{Route}/review-impacts/dispatch", null);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await first.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("proposalIds").EnumerateArray().Select(p => p.GetGuid()).Should().Equal(proposalId);
        _impactConsumer.Verify(s => s.QueueAsync(It.Is<NarrativeChangeImpactRequest>(r =>
            r.TenantId == _tenant && r.SystemId == _system && r.SourceKind == "CspCapability"
                && r.SourceId == _capability.ToString() && r.ImpactId != null && r.SourceContext != null),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _impactConsumer.Verify(s => s.GenerateQueuedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityImpact>().CountAsync(i => i.AcknowledgedAt == null)).Should().Be(0);
    }

    [Fact]
    public async Task ImpactDispatch_ConsumerFailureLeavesDurablePendingWork()
    {
        // Arrange
        await AddBaselineAsync();
        await AddNarrativesAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        _impactConsumer.Setup(s => s.QueueAsync(It.IsAny<NarrativeChangeImpactRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Synthetic consumer failure"));
        await using var scope = _app.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICapabilityResponsibilityImpactDispatcher>();

        // Act
        Func<Task> deliver = () => dispatcher.DispatchAsync(_system);

        // Assert
        await deliver.Should().ThrowAsync<IOException>().WithMessage("Synthetic consumer failure");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityImpact>().CountAsync(i => i.AcknowledgedAt == null)).Should().BeGreaterThan(0);
        _impactConsumer.Verify(s => s.GenerateQueuedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetryingOldReviewToken_RejectsConcurrentConfirmation()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        var preview = await _client.GetFromJsonAsync<JsonElement>($"{Route}/responsibilities");
        var stale = ConfirmationBody(preview, _capability, "AU-6", "Customer");
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();

        // Act
        var response = await _client.PutAsJsonAsync($"{Route}/{_capability}/responsibilities", stale);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.ControlInheritances.SingleAsync()).InheritanceType.Should().Be(InheritanceType.Shared);
        (await verify.Set<CapabilityResponsibilityConfirmation>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImpactDispatch_MissingNarrativeIsExplicitlyDeferred_WithoutFabricatingRows()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        _impactConsumer.Setup(s => s.QueueAsync(It.IsAny<NarrativeChangeImpactRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NarrativeChangeImpactResult([]));

        // Act
        var response = await _client.PostAsync($"{Route}/review-impacts/dispatch", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MissingNarrative");
        _impactConsumer.Verify(s => s.QueueAsync(It.IsAny<NarrativeChangeImpactRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityImpact>().CountAsync(i => i.AcknowledgedAt == null)).Should().Be(2);
        (await verify.ControlImplementations.CountAsync()).Should().Be(0);
    }

    private async Task AddNarrativesAsync()
    {
        await using var db = new AtoCopilotContext(_options);
        foreach (var control in new[] { "AC-2", "AU-6" })
            db.ControlImplementations.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, ControlId = control,
                TechnicalNarrative = "Synthetic technical statement", PolicyNarrative = "Synthetic policy",
                AuthoredBy = "fixture-reviewer", ApprovalStatus = SspSectionStatus.Approved });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ProviderFanout_ReconcilesOnlyAffectedCustomerSystem_AndQueuesRealNarrativeReceipts()
    {
        // Arrange
        await AddBaselineAsync();
        await AddNarrativesAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        var factory = _app.Services.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        Guid componentId;
        await using (var db = new AtoCopilotContext(_options))
        {
            componentId = (await db.CspInheritedComponents.SingleAsync()).Id;
            var unrelated = new RegisteredSystem { TenantId = _tenant, Name = "Unrelated system", CreatedBy = "fixture" };
            db.Add(unrelated);
            db.ControlImplementations.Add(new() { TenantId = _tenant, RegisteredSystemId = unrelated.Id,
                ControlId = "AU-6", TechnicalNarrative = "Unrelated approved narrative" });
            await db.SaveChangesAsync();
        }
        var provider = new CspInheritedComponentService(factory, Mock.Of<ICspCapabilityMappingService>(),
            Options.Create(new CspInheritedOptions()), NullLogger<CspInheritedComponentService>.Instance,
            new CapabilityHistoryService(factory, NullLogger<CapabilityHistoryService>.Instance),
            new TenantContext(_tenant, isCspAdmin: true));
        await provider.UpdateCapabilityAsync(componentId, _capability, "Changed provider", "Changed synthetic source",
            ["AC-2", "AU-6"], null, "actual-provider-fixture");
        await using var scoped = new AtoCopilotContext(_options);
        var customer = new TenantContext(_tenant);
        var generator = new Mock<IControlNarrativeService>(MockBehavior.Strict);
        var consumer = new NarrativeProposalService(scoped, customer, new NarrativeLibraryService(scoped, customer), generator.Object);
        var fanout = new CspResponsibilityFanoutService(scoped, customer,
            _app.Services.GetRequiredService<ISystemWorkspaceAccessService>(), consumer, NullLoggerFactory.Instance);

        // Act
        for (var pass = 0; pass < 3; pass++)
        {
            await using var routing = new AtoCopilotContext(_options);
            await CapabilityResponsibilityRouting.ExpandAsync(routing);
            foreach (var claim in await CapabilityResponsibilityRouting.ClaimAsync(routing))
                await fanout.ProcessAsync(claim);
        }

        // Assert
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityDelivery>().CountAsync(d => d.SourceEventId != null && d.CompletedAt != null)).Should().Be(1);
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
        (await verify.NarrativeProposals.ToListAsync()).Should().OnlyContain(p =>
            p.RegisteredSystemId == _system && p.Status == "PendingGeneration");
        (await verify.Set<NarrativeImpactReceipt>().CountAsync()).Should().BeGreaterThan(0);
        (await verify.ControlImplementations.SingleAsync(i => i.RegisteredSystemId == _system && i.ControlId == "AU-6"))
            .TechnicalNarrative.Should().Be("Synthetic technical statement");
        generator.Verify(g => g.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProviderWorker_UsesSeparateTenantScopes_PreservingForeignManualResponsibilities()
    {
        // Arrange
        await AddBaselineAsync();
        await AddNarrativesAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        var otherTenant = Guid.NewGuid();
        var otherSystem = Guid.NewGuid().ToString();
        var manualId = Guid.NewGuid().ToString();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Tenants.Add(new() { Id = otherTenant, DisplayName = "Second synthetic organization" });
            db.RegisteredSystems.Add(new() { Id = otherSystem, TenantId = otherTenant, Name = "Other organization system", CreatedBy = "fixture" });
            var baseline = new ControlBaseline { TenantId = otherTenant, RegisteredSystemId = otherSystem,
                BaselineLevel = "Moderate", ControlIds = ["AU-6"], TotalControls = 1, CreatedBy = "fixture" };
            db.Add(baseline);
            db.CapabilitySubscriptions.Add(new() { RegisteredSystemId = otherSystem, CspInheritedCapabilityId = _capability.ToString(),
                RoutingTenantId = otherTenant, RoutingCapabilityId = _capability.ToString(), SubscribedBy = "other-reviewer" });
            db.ControlInheritances.Add(new() { Id = manualId, TenantId = otherTenant, ControlBaselineId = baseline.Id,
                ControlId = "AU-6", InheritanceType = InheritanceType.Customer, DesignationSource = "Manual", SetBy = "other-reviewer" });
            db.ControlImplementations.Add(new() { TenantId = otherTenant, RegisteredSystemId = otherSystem, ControlId = "AU-6",
                TechnicalNarrative = "Other approved text", ApprovalStatus = SspSectionStatus.Approved });
            await db.SaveChangesAsync();
            (await db.CspInheritedCapabilities.SingleAsync()).Description = "Published provider change";
            await CspResponsibilitySourceTracker.StageAsync(db, "actual-provider-fixture", default);
            await db.SaveChangesAsync();
        }
        var generator = new Mock<IControlNarrativeService>(MockBehavior.Strict);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<AtoCopilotContext>(o => o.UseSqlite(_connection).AddInterceptors(_routingBoundary));
        services.AddSingleton(_app.Services.GetRequiredService<ISystemWorkspaceAccessService>());
        services.AddSingleton(generator.Object);
        services.AddScoped<NarrativeLibraryService>();
        services.AddScoped<NarrativeProposalService>();
        services.AddScoped<INarrativeChangeImpactService>(sp => sp.GetRequiredService<NarrativeProposalService>());
        services.AddScoped<CspResponsibilityFanoutService>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new CspResponsibilityFanoutWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ITenantContextAccessor>(), NullLogger<CspResponsibilityFanoutWorker>.Instance);

        // Act
        _routingBoundary.RejectTenantDirectoryScan = true;
        _routingBoundary.CurrentTenant = () => provider.GetRequiredService<ITenantContextAccessor>().Current;
        await worker.RunOnceAsync();
        await worker.RunOnceAsync();

        // Assert
        await using var verify = new AtoCopilotContext(_options);
        var manual = await verify.ControlInheritances.SingleAsync();
        manual.Id.Should().Be(manualId);
        manual.TenantId.Should().Be(otherTenant);
        manual.InheritanceType.Should().Be(InheritanceType.Customer);
        (await verify.NarrativeProposals.Select(p => p.TenantId).Distinct().ToListAsync())
            .Should().BeEquivalentTo(new[] { _tenant, otherTenant });
        (await verify.NarrativeProposals.ToListAsync()).Should().OnlyContain(p =>
            p.TenantId == _tenant && p.RegisteredSystemId == _system ||
            p.TenantId == otherTenant && p.RegisteredSystemId == otherSystem);
        (await verify.ControlImplementations.SingleAsync(i => i.RegisteredSystemId == otherSystem))
            .TechnicalNarrative.Should().Be("Other approved text");
        generator.Verify(g => g.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProviderEvents_RecordRestoredContentAsNewChange_ButNotIdenticalRetries()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var capability = await db.CspInheritedCapabilities.Include(c => c.CspInheritedComponent).SingleAsync();
        capability.Description = "First provider revision";
        await CspResponsibilitySourceTracker.StageAsync(db, "provider-author", default);
        await db.SaveChangesAsync();
        capability.Description = "Second provider revision";
        await CspResponsibilitySourceTracker.StageAsync(db, "provider-author", default);
        await db.SaveChangesAsync();

        // Act
        capability.Description = "First provider revision";
        await CspResponsibilitySourceTracker.StageAsync(db, "provider-author", default);
        await db.SaveChangesAsync();
        await CspResponsibilitySourceTracker.StageAsync(db, "provider-author", default);
        await db.SaveChangesAsync();

        // Assert
        var events = await db.Set<CspResponsibilitySourceEvent>().OrderBy(e => e.Sequence).ToListAsync();
        events.Should().HaveCount(3);
        events.Select(e => e.Sequence).Should().Equal(1, 2, 3);
        events[0].SourceRevision.Should().Be(events[2].SourceRevision);
        events[1].SourceRevision.Should().NotBe(events[2].SourceRevision);
        events.Should().OnlyContain(e => e.Actor == "provider-author");
    }

    [Fact]
    public async Task ProviderFanout_RejectsHumanOrPrivilegedScope()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var caller = new TenantContext(_tenant, isCspAdmin: true);
        var service = new CspResponsibilityFanoutService(db, caller,
            _app.Services.GetRequiredService<ISystemWorkspaceAccessService>(), _impactConsumer.Object, NullLoggerFactory.Instance);

        // Act
        Func<Task> execute = () => service.ProcessAsync(new("fixture", _tenant, _system, Guid.NewGuid()));

        // Assert
        await execute.Should().ThrowAsync<UnauthorizedAccessException>();
        (await db.Set<CapabilityResponsibilityImpact>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Confirmation_CommitFailureRollsBackAllocationsDesignationsAndNewImpact()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        _commitFailure.FailNext = true;

        // Act
        Func<Task> confirm = () => ConfirmAsync(_capability, "AU-6");

        // Assert
        await confirm.Should().ThrowAsync<InvalidOperationException>().WithMessage("Synthetic commit failure");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityConfirmation>().CountAsync()).Should().Be(0);
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
        (await verify.Set<CapabilityResponsibilityImpact>().CountAsync()).Should().Be(2);
        (await verify.ControlBaselines.SingleAsync()).SharedControls.Should().Be(0);
    }

    [Fact]
    public async Task AdditiveSchema_IsIdempotentAndSupportsProductionModelPersistence()
    {
        // Arrange
        await AddBaselineAsync();
        await using (var db = new AtoCopilotContext(_options))
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TABLE CapabilityResponsibilityImpacts;
                DROP TABLE CapabilityResponsibilityProjections;
                DROP TABLE CapabilityResponsibilityConfirmations;
                DROP TABLE CspResponsibilitySourceEvents;
                """);
            await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);
            await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        }

        // Act
        var subscribed = await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        var confirmed = await ConfirmAsync(_capability, "AU-6");

        // Assert
        subscribed.StatusCode.Should().Be(HttpStatusCode.Created);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityConfirmation>().CountAsync()).Should().Be(1);
        (await verify.ControlInheritances.SingleAsync()).InheritanceType.Should().Be(InheritanceType.Shared);
    }

    [Fact]
    public async Task AdditiveSchema_UpgradesPriorSourceEventsWithoutInventingActor()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE CspResponsibilitySourceEvents;
            CREATE TABLE CspResponsibilitySourceEvents (
              Id TEXT NOT NULL PRIMARY KEY, CapabilityId TEXT NOT NULL, ComponentId TEXT NOT NULL,
              CspProfileId TEXT NOT NULL, SourceRevision TEXT NOT NULL, IsAvailable INTEGER NOT NULL, CreatedAt TEXT NOT NULL);
            CREATE UNIQUE INDEX IX_CspResponsibilitySourceEvents_CapabilityId_SourceRevision
              ON CspResponsibilitySourceEvents(CapabilityId,SourceRevision);
            """);
        var component = await db.CspInheritedComponents.SingleAsync();
        var eventId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO CspResponsibilitySourceEvents
              (Id,CapabilityId,ComponentId,CspProfileId,SourceRevision,IsAvailable,CreatedAt)
            VALUES ({eventId},{_capability},{component.Id},{component.CspProfileId},{"synthetic-legacy-revision"},{true},{DateTimeOffset.UtcNow})
            """);

        // Act
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        var record = await db.Set<CspResponsibilitySourceEvent>().SingleAsync();
        record.Id.Should().Be(eventId);
        record.Sequence.Should().Be(1);
        record.Actor.Should().BeNull("the original event did not capture an actor; migration must not fabricate one");
    }

    [Fact]
    public async Task ImpactDispatch_MissingBaselineRetainsPendingWork()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        await using (var db = new AtoCopilotContext(_options))
        {
            db.ControlBaselines.Remove(await db.ControlBaselines.SingleAsync());
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsync($"{Route}/review-impacts/dispatch", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MissingBaseline");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityImpact>().CountAsync(i => i.AcknowledgedAt == null)).Should().Be(2);
    }

    [Fact]
    public async Task ImpactDispatch_DeferredWorkCannotStarveLaterReadyControls()
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        await using (var db = new AtoCopilotContext(_options))
        {
            db.ControlImplementations.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
                ControlId = "AU-6", TechnicalNarrative = "Ready narrative" });
            var first = await db.Set<CapabilityResponsibilityImpact>().FirstAsync(i => i.ControlId == "AC-2");
            for (var i = 0; i < 100; i++)
                db.Set<CapabilityResponsibilityImpact>().Add(new()
                {
                    TenantId = _tenant, RegisteredSystemId = _system, ControlBaselineId = first.ControlBaselineId,
                    ControlId = "AC-2", ProjectionId = Guid.NewGuid(), Revision = 1, StateHash = first.StateHash,
                    Reason = first.Reason, SourcesJson = first.SourcesJson, Actor = first.Actor,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                });
            await db.SaveChangesAsync();
        }
        _impactConsumer.Setup(s => s.QueueAsync(It.IsAny<NarrativeChangeImpactRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NarrativeChangeImpactResult([]));

        // Act
        var response = await _client.PostAsync($"{Route}/review-impacts/dispatch", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _impactConsumer.Verify(s => s.QueueAsync(It.Is<NarrativeChangeImpactRequest>(r => r.ControlIds.Contains("AU-6")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class CommitFailureInterceptor : DbTransactionInterceptor
    {
        public bool FailNext { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Synthetic commit failure");
            }

            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class RoutingBoundaryInterceptor : DbCommandInterceptor
    {
        public bool RejectTenantDirectoryScan { get; set; }
        public Func<ITenantContext?>? CurrentTenant { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (RejectTenantDirectoryScan && command.CommandText.Contains("FROM \"Tenants\"", StringComparison.Ordinal)
                && !command.CommandText.Contains("\"Id\" =", StringComparison.Ordinal))
                throw new InvalidOperationException("Fanout must not scan the tenant directory.");
            var protectedRead = new[] { "RegisteredSystems", "ControlBaselines", "ControlImplementations", "CapabilityResponsibilityConfirmations" }
                .Any(table => command.CommandText.Contains($"FROM \"{table}\"", StringComparison.Ordinal));
            if (RejectTenantDirectoryScan && protectedRead &&
                (CurrentTenant?.Invoke() is not { IsCspAdmin: false, PersonId: null, IsWorkspaceRequest: false } tenant || tenant.EffectiveTenantId == Guid.Empty))
                throw new InvalidOperationException("Customer content requires an explicit non-privileged tenant scope.");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class RoutingAckFailureInterceptor : DbCommandInterceptor
    {
        public bool FailNextCompletion { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (FailNextCompletion && command.CommandText.Contains("UPDATE \"CapabilityResponsibilityDeliveries\"", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, "Completed")))
            {
                FailNextCompletion = false;
                throw new IOException("Synthetic routing acknowledgment failure");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class TestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "fixture-reviewer")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
