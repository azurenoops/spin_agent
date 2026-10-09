using Ato.Copilot.Core.Authorization;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Authorization;

public sealed class ProviderAccessServiceTests
{
    [Fact]
    public async Task SqliteSchemaAdditions_ReplayWithoutReplacingProviderAccessRows()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);
        await db.Database.EnsureCreatedAsync();
        var provider = new CspProfile
        {
            DisplayName = "Retained provider", LegalEntityName = "Retained provider"
        };
        db.Add(provider);
        var request = new ProviderAccessRequest
        {
            ProviderId = provider.Id,
            DirectoryTenantId = Guid.NewGuid(),
            ObjectId = Guid.NewGuid(),
            Justification = "Retain this request"
        };
        db.Add(request);
        await db.SaveChangesAsync();

        // Act
        await ProviderAccessSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ProviderAccessSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        db.ChangeTracker.Clear();
        var retained = await db.ProviderAccessRequests.IgnoreQueryFilters().SingleAsync();

        // Assert
        retained.Id.Should().Be(request.Id);
        retained.Justification.Should().Be("Retain this request");
        retained.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task PlatformOperator_ExplicitlyGrantsScopedMembership_WithoutLoginOrderBootstrap()
    {
        // Arrange
        var factory = CreateFactory();
        var provider = await SeedProviderAsync(factory, OnboardingState.Pending);
        var platformOperator = Subject();
        var member = Subject();
        var service = Service(factory, platformOperator.ObjectId);

        // Act
        var granted = await service.GrantMembershipAsync(platformOperator,
            new(provider.Id, member.DirectoryTenantId, member.ObjectId, member.DisplayName,
                member.Email!, [ProviderRole.PortalAdministrator], null, null));
        var resolved = await service.ResolveAsync(member);
        var compatibilityOnly = await service.ResolveAsync(member with
        {
            ObjectId = Guid.NewGuid(),
            IsCspAdmin = true
        });

        // Assert
        granted.Created.Should().BeTrue();
        resolved.State.Should().Be(ProviderAccessState.Active);
        resolved.Actions.Should().Contain(ProviderActions.SetupManage);
        resolved.Actions.Should().NotContain(ProviderActions.AuthorizationDecide);
        compatibilityOnly.Source.Should().Be("CSP.Admin");
        await using var db = await factory.CreateDbContextAsync();
        (await db.ProviderMemberships.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InvitationAcceptance_RequiresMatchingIdentityAndToken_IsHashedSingleUse()
    {
        // Arrange
        var factory = CreateFactory();
        var provider = await SeedProviderAsync(factory, OnboardingState.Active);
        var admin = Subject();
        var invitee = Subject();
        var service = Service(factory, admin.ObjectId);
        var invitation = await service.CreateInvitationAsync(admin,
            new(provider.Id, invitee.Email, invitee.DirectoryTenantId, invitee.ObjectId,
                [ProviderRole.Isso], null, null, DateTimeOffset.UtcNow.AddHours(1)));

        // Act
        var mismatch = () => service.AcceptInvitationAsync(invitation.Invitation.Id,
            invitation.Token, invitee with { ObjectId = Guid.NewGuid() });
        await mismatch.Should().ThrowAsync<UnauthorizedAccessException>();
        var accepted = await service.AcceptInvitationAsync(
            invitation.Invitation.Id, invitation.Token, invitee);
        var replay = () => service.AcceptInvitationAsync(
            invitation.Invitation.Id, invitation.Token, invitee);

        // Assert
        accepted.Destination.Should().Be("/workspaces/csp/authorizations");
        await replay.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already*");
        await using var db = await factory.CreateDbContextAsync();
        var stored = await db.ProviderInvitations.SingleAsync();
        stored.TokenHash.Should().NotBe(invitation.Token);
        stored.Status.Should().Be("Accepted");
        (await db.ProviderRoleAssignments.SingleAsync()).Role.Should().Be("Isso");
    }

    [Fact]
    public async Task AccessRequest_DecisionIsExplicit_AndDeniedRequestGrantsNothing()
    {
        // Arrange
        var factory = CreateFactory();
        var provider = await SeedProviderAsync(factory, OnboardingState.Active);
        var platformOperator = Subject();
        var requester = Subject();
        var service = Service(factory, platformOperator.ObjectId);
        var request = await service.CreateAccessRequestAsync(requester,
            new("Need assigned provider work", provider.Id));

        // Act
        var denied = await service.DecideAccessRequestAsync(platformOperator, request.Id,
            new(false, "Scope not approved", []));
        var current = await service.GetCurrentAccessRequestAsync(requester);

        // Assert
        denied.Status.Should().Be("Denied");
        current!.Id.Should().Be(request.Id);
        (await service.ResolveAsync(requester)).State.Should().Be(ProviderAccessState.None);
        await using var db = await factory.CreateDbContextAsync();
        (await db.ProviderMemberships.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EffectiveAccessV2_RoutesSetupResumeInvitationMemberAndBlockedStates()
    {
        // Arrange
        var factory = CreateFactory();
        var provider = await SeedProviderAsync(factory, OnboardingState.InWizard);
        var platformOperator = Subject();
        var admin = Subject();
        var invitee = Subject();
        var service = Service(factory, platformOperator.ObjectId);
        await service.GrantMembershipAsync(platformOperator,
            new(provider.Id, admin.DirectoryTenantId, admin.ObjectId, admin.DisplayName,
                admin.Email!, [ProviderRole.PortalAdministrator], null, null));
        var effective = new EffectiveAccessService(factory,
            Options.Create(new PlatformOperationsOptions { AuthorizedObjectIds = [platformOperator.ObjectId] }),
            service);
        var newAdminResult = await effective.ResolveAsync(ToEffective(admin));
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProviderSetupDrafts.Add(new ProviderSetupDraft
            {
                ProviderId = provider.Id, CreatedBy = "test", UpdatedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        var invitation = await service.CreateInvitationAsync(platformOperator,
            new(provider.Id, invitee.Email, invitee.DirectoryTenantId, invitee.ObjectId,
                [ProviderRole.Assessor], null, null, DateTimeOffset.UtcNow.AddHours(1)));
        var requester = Subject();
        await service.CreateAccessRequestAsync(requester, new("Need access", provider.Id));

        // Act
        var adminResult = await effective.ResolveAsync(ToEffective(admin));
        var invitedResult = await effective.ResolveAsync(ToEffective(invitee));
        var requestResult = await effective.ResolveAsync(ToEffective(requester));
        await service.RevokeMembershipAsync(platformOperator,
            (await factory.CreateDbContextAsync()).ProviderMemberships.Single().Id);
        var blockedResult = await effective.ResolveAsync(ToEffective(admin));

        // Assert
        adminResult.Version.Should().Be("2");
        newAdminResult.EntryRoute!.Kind.Should().Be("ProviderSetup");
        adminResult.EntryRoute!.Kind.Should().Be("ProviderSetupResume");
        invitedResult.EntryRoute!.Kind.Should().Be("ProviderInvitation");
        invitedResult.EntryRoute.InvitationId.Should().Be(invitation.Invitation.Id.ToString("D"));
        requestResult.EntryRoute!.Kind.Should().Be("AccessRequired");
        requestResult.EntryRoute.ReasonCode.Should().Be("PROVIDER_ACCESS_REQUEST_PENDING");
        blockedResult.EntryRoute!.Kind.Should().Be("Blocked");
        blockedResult.EntryRoute.ReasonCode.Should().Be("PROVIDER_MEMBERSHIP_REVOKED");
    }

    private static ProviderAccessSubject Subject() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Synthetic user", $"{Guid.NewGuid():N}@example.invalid");

    private static EffectiveAccessSubject ToEffective(ProviderAccessSubject subject) => new(
        subject.ObjectId, subject.DisplayName, Guid.NewGuid(), subject.IsCspAdmin,
        subject.DirectoryTenantId, subject.Email);

    private static ProviderAccessService Service(
        IDbContextFactory<AtoCopilotContext> factory, Guid platformOperator) =>
        new(factory, Options.Create(new PlatformOperationsOptions
        {
            AuthorizedObjectIds = [platformOperator]
        }));

    private static IDbContextFactory<AtoCopilotContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"provider-access-{Guid.NewGuid():N}").Options;
        return new TestFactory(options);
    }

    private static async Task<CspProfile> SeedProviderAsync(
        IDbContextFactory<AtoCopilotContext> factory, OnboardingState state)
    {
        await using var db = await factory.CreateDbContextAsync();
        var provider = new CspProfile
        {
            DisplayName = "Synthetic provider", LegalEntityName = "Synthetic provider",
            OnboardingState = state
        };
        db.Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    private sealed class TestFactory(DbContextOptions<AtoCopilotContext> options)
        : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }
}
