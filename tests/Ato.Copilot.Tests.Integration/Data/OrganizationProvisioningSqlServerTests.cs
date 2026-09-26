using System.Data.Common;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Auditing;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class OrganizationProvisioningSqlServerTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableTheory]
    [InlineData("concurrent")]
    [InlineData("lost-commit-response")]
    [InlineData("atomic-rollback")]
    public async Task AdministratorEnrollment_IsSerializedAuditedAndRetrySafe(string scenario)
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var database = await fixture.CreateDatabaseAsync();
        await database.Database.EnsureCreatedAsync();
        var tenant = new Ato.Copilot.Core.Models.Tenancy.Tenant { DisplayName = "Organization" };
        var person = new Ato.Copilot.Core.Models.Onboarding.Person
        {
            TenantId = tenant.Id, DisplayName = "Admin", Email = "admin@example.invalid"
        };
        database.Tenants.Add(tenant);
        database.Persons.Add(person);
        database.OrganizationMemberships.Add(new Ato.Copilot.Core.Models.Tenancy.OrganizationMembership
        {
            TenantId = tenant.Id, PersonId = person.Id, DirectoryTenantId = Guid.NewGuid(),
            ObjectId = Guid.NewGuid(), GrantedBy = "test"
        });
        await database.SaveChangesAsync();
        var faults = new Faults();
        var factory = new Factory(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(database.Database.GetConnectionString(), sql => sql.EnableRetryOnFailure())
            .AddInterceptors(faults, faults.Commands).Options);
        var workspace = new Mock<Ato.Copilot.Mcp.Services.Tenancy.IWorkspaceService>();
        workspace.Setup(x => x.IsCspAdministrator(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).Returns(true);
        workspace.SetupGet(x => x.Current).Returns(new Ato.Copilot.Mcp.Services.Tenancy.WorkspaceResponse(
            "csp", null, "Provider", "ordinary", null, [], new(true, true, true)));
        var audit = new WizardAuditService(factory, NullLogger<WizardAuditService>.Instance);
        var roles = new OrganizationRoleAssignmentService(factory, audit, NullLogger<OrganizationRoleAssignmentService>.Instance);
        var service = new Ato.Copilot.Mcp.Services.Tenancy.OrganizationMembershipService(factory,
            new Ato.Copilot.Core.Services.Tenancy.TenantContext(),
            new Ato.Copilot.Core.Services.Tenancy.TenantContextAccessor(), workspace.Object,
            Mock.Of<Ato.Copilot.Core.Interfaces.Auth.ILoginAuditService>(MockBehavior.Strict),
            new Ato.Copilot.Mcp.Middleware.LoginAuditContextAccessor(), roles);
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new("tid", Guid.NewGuid().ToString()), new("oid", Guid.NewGuid().ToString())], "test"))
        };
        Task<Ato.Copilot.Mcp.Services.Tenancy.OrganizationAdministratorResponse> Enroll()
            => service.EnrollAdministratorAsync(http, tenant.Id, person.Id, default);

        // Act
        if (scenario == "atomic-rollback")
        {
            faults.Commands.FailAudit = true;
            await FluentActions.Awaiting(Enroll).Should().ThrowAsync<DbUpdateException>();
            (await database.OrganizationRoleAssignments.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenant.Id))
                .Should().Be(0);
            await Enroll();
        }
        else if (scenario == "lost-commit-response")
        {
            faults.LoseCommitResponse = true;
            await Enroll();
            faults.CommitResponseLost.Should().BeTrue();
        }
        else await Task.WhenAll(Enroll(), Enroll());
        await Enroll();

        // Assert
        (await database.OrganizationRoleAssignments.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenant.Id)).Should().Be(1);
        (await database.WizardAuditEntries.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenant.Id)).Should().Be(1);
        (await database.SystemRoleAssignments.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenant.Id)).Should().Be(0);
    }

    [SkippableTheory]
    [InlineData("concurrent")]
    [InlineData("lost-commit-response")]
    [InlineData("atomic-rollback")]
    public async Task PersonBinding_IsAtomicAuditedAndRetrySafe(string scenario)
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var database = await fixture.CreateDatabaseAsync();
        await database.Database.EnsureCreatedAsync();
        var fault = new Faults();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(database.Database.GetConnectionString(), sql => sql.EnableRetryOnFailure())
            .AddInterceptors(fault, fault.Commands).Options;
        var factory = new Factory(options);
        var audit = new WizardAuditService(factory, NullLogger<WizardAuditService>.Instance);
        var people = new PersonService(factory, Mock.Of<IDirectorySearchClient>(MockBehavior.Strict),
            audit, NullLogger<PersonService>.Instance);
        var service = new WorkspaceOperationsService(factory, people);
        var identity = new UpdateProvisioningRequest(Guid.NewGuid(), Guid.NewGuid(),
            NewPerson: new("Administrator", "administrator@example.invalid"));
        var created = await service.CreateOrganizationAsync(
            new($"Organization {Guid.NewGuid():N}", null, null, null, identity),
            Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString(), default);
        var actor = Guid.NewGuid();
        Task<OrganizationProvisioningResult> Bind() => service.UpdateProvisioningAsync(
            created.TenantId, created.OperationId, identity, default, actor);

        // Act
        if (scenario == "atomic-rollback")
        {
            fault.Commands.FailAudit = true;
            await FluentActions.Awaiting(Bind).Should().ThrowAsync<DbUpdateException>();
            database.ChangeTracker.Clear();
            (await database.Persons.IgnoreQueryFilters().CountAsync(x => x.TenantId == created.TenantId)).Should().Be(0);
            var unbound = await database.OrganizationProvisioningOperations.AsNoTracking().SingleAsync(x => x.Id == created.OperationId);
            unbound.PersonId.Should().BeNull();
            unbound.AdministratorBoundAt.Should().BeNull();
            (await database.WizardAuditEntries.IgnoreQueryFilters().CountAsync(x => x.TenantId == created.TenantId)).Should().Be(0);
            await Bind();
        }
        else if (scenario == "lost-commit-response")
        {
            fault.LoseCommitResponse = true;
            await Bind();
            fault.CommitResponseLost.Should().BeTrue();
        }
        else
        {
            await Task.WhenAll(Bind(), Bind());
        }
        var replay = await Bind();

        // Assert
        replay.PersonState.Should().Be("Completed");
        replay.CanEditAdministrator.Should().BeFalse();
        database.ChangeTracker.Clear();
        var person = await database.Persons.IgnoreQueryFilters().SingleAsync(x => x.TenantId == created.TenantId);
        var operation = await database.OrganizationProvisioningOperations.AsNoTracking().SingleAsync(x => x.Id == created.OperationId);
        operation.PersonId.Should().Be(person.Id);
        (await database.WizardAuditEntries.IgnoreQueryFilters().CountAsync(x =>
            x.TenantId == created.TenantId && x.ResourceId == person.Id)).Should().Be(1);
        (await database.RegisteredSystems.IgnoreQueryFilters().CountAsync(x => x.TenantId == created.TenantId)).Should().Be(0);
    }

    [SkippableFact]
    public async Task ConcurrentDifferentBeginKeys_ResolveOneInitialSetupOperation()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var database = await fixture.CreateDatabaseAsync();
        await database.Database.EnsureCreatedAsync();
        var tenant = new Ato.Copilot.Core.Models.Tenancy.Tenant { DisplayName = "Legacy organization" };
        database.Tenants.Add(tenant);
        await database.SaveChangesAsync();
        var factory = new Factory(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(database.Database.GetConnectionString(), sql => sql.EnableRetryOnFailure()).Options);
        var service = new WorkspaceOperationsService(factory);

        // Act
        var results = await Task.WhenAll(service.GetOrCreateProvisioningAsync(tenant.Id, "first", default),
            service.GetOrCreateProvisioningAsync(tenant.Id, "second", default));

        // Assert
        results.Select(x => x.OperationId).Distinct().Should().ContainSingle();
        (await database.OrganizationProvisioningOperations.CountAsync(x => x.TenantId == tenant.Id)).Should().Be(1);
    }

    private sealed class Factory(DbContextOptions<AtoCopilotContext> options) : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }

    private sealed class Faults : DbTransactionInterceptor
    {
        public bool LoseCommitResponse { get; set; }
        public bool CommitResponseLost { get; private set; }
        public AuditCommandFault Commands { get; } = new();
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (LoseCommitResponse)
            {
                LoseCommitResponse = false;
                CommitResponseLost = true;
                throw new TimeoutException("Injected lost commit response after durable commit.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class AuditCommandFault : DbCommandInterceptor
    {
        public bool FailAudit { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (FailAudit && command.CommandText.Contains("INSERT INTO [WizardAuditEntries]", StringComparison.Ordinal))
            {
                FailAudit = false;
                throw new InvalidOperationException("Injected persistent-audit failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
