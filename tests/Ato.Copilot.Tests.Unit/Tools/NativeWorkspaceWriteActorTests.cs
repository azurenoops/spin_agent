using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.State.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tools;

public class NativeWorkspaceWriteActorTests
{
    private static readonly ConversationIdentity Identity = new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        "organization", Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        "member", Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "system-1");

    public static TheoryData<Type, string, int> Writes => new()
    {
        { typeof(WriteNarrativeTool), nameof(ISspService.WriteNarrativeAsync), 4 },
        { typeof(RollbackNarrativeTool), nameof(INarrativeGovernanceService.RollbackNarrativeAsync), 3 },
        { typeof(SubmitNarrativeTool), nameof(INarrativeGovernanceService.SubmitNarrativeAsync), 2 },
        { typeof(ReviewNarrativeTool), nameof(INarrativeGovernanceService.ReviewNarrativeAsync), 3 },
        { typeof(IssueAuthorizationTool), nameof(IAuthorizationService.IssueAuthorizationAsync), 7 },
        { typeof(AcceptRiskTool), nameof(IAuthorizationService.AcceptRiskAsync), 7 }
    };

    public static IEnumerable<object[]> WriteMethods => Writes.Select(row => new[] { row[0], row[1] });

    [Fact]
    public void IssueAuthorization_RiskAcceptancesMetadata_UsesNativeJsonPropertyNames()
    {
        // Arrange
        using var fixture = new Fixture(typeof(IssueAuthorizationTool));

        // Act
        var parameter = fixture.Tool.Parameters["risk_acceptances"];

        // Assert
        parameter.Name.Should().Be("risk_acceptances");
        parameter.Type.Should().Be("string");
        parameter.Required.Should().BeFalse();
        parameter.Description.Should().Be(
            "JSON array of risk acceptances: [{findingId, controlId, catSeverity, justification, compensatingControl?, expirationDate}]");
    }

    [Theory]
    [InlineData("""[{"findingId":"finding-1","controlId":"AC-1","catSeverity":"CatIII","justification":"Test justification.","compensatingControl":"Test compensating control.","expirationDate":"2027-01-01T00:00:00Z"}]""")]
    [InlineData("""[{"FindingId":"finding-1","ControlId":"AC-1","CatSeverity":"CatIII","Justification":"Test justification.","CompensatingControl":"Test compensating control.","ExpirationDate":"2027-01-01T00:00:00Z"}]""")]
    public async Task IssueAuthorization_NativeRiskAcceptancesJson_BindsAllProperties(string json)
    {
        // Arrange
        using var fixture = new Fixture(typeof(IssueAuthorizationTool));
        var arguments = Arguments();
        arguments["risk_acceptances"] = json;

        // Act
        var result = await fixture.Tool.ExecuteAsync(arguments);

        // Assert
        result.Should().Contain("\"success\"");
        var invocation = fixture.Service.Invocations.Single(
            x => x.Method.Name == nameof(IAuthorizationService.IssueAuthorizationAsync));
        var acceptances = invocation.Arguments[6].Should().BeOfType<List<RiskAcceptanceInput>>().Which;
        acceptances.Should().ContainSingle().Which.Should().BeEquivalentTo(new RiskAcceptanceInput
        {
            FindingId = "finding-1",
            ControlId = "AC-1",
            CatSeverity = "CatIII",
            Justification = "Test justification.",
            CompensatingControl = "Test compensating control.",
            ExpirationDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }

    [Theory]
    [InlineData("""[{"finding_id":"other-finding"}]""", "")]
    [InlineData("""[{"findingId":"finding-1","finding_id":"other-finding"}]""", "finding-1")]
    [InlineData("""[{"finding_id":"other-finding","FindingId":"finding-1"}]""", "finding-1")]
    public async Task IssueAuthorization_SnakeCaseFindingId_IsNotPromotedToNativeTarget(
        string json, string expectedFindingId)
    {
        // Arrange
        using var fixture = new Fixture(typeof(IssueAuthorizationTool));
        var arguments = Arguments();
        arguments["risk_acceptances"] = json;

        // Act
        var result = await fixture.Tool.ExecuteAsync(arguments);

        // Assert
        result.Should().Contain("\"success\"");
        var invocation = fixture.Service.Invocations.Single(
            x => x.Method.Name == nameof(IAuthorizationService.IssueAuthorizationAsync));
        var acceptances = invocation.Arguments[6].Should().BeOfType<List<RiskAcceptanceInput>>().Which;
        acceptances.Should().ContainSingle().Which.FindingId.Should().Be(expectedFindingId);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ValidatedRequestActor_OverridesEveryCallerActorHint(Type type, string method, int actorIndex)
    {
        // Arrange
        var accessor = new Mock<IConversationIdentityAccessor>();
        accessor.SetupGet(x => x.Current).Returns(Identity);
        using var fixture = new Fixture(type, accessor.Object);
        var arguments = Arguments();
        using var authorization = ToolExecutionAuthorization.Push((_, args, _) =>
        {
            args["user_id"] = Identity.ActorId;
            return Task.CompletedTask;
        });

        // Act
        var result = await fixture.Tool.ExecuteAsync(arguments);

        // Assert
        result.Should().Contain("\"success\"");
        var invocation = fixture.Service.Invocations.Single(x => x.Method.Name == method);
        invocation.Arguments[actorIndex].Should().Be(Identity.ActorId);
        if (type == typeof(IssueAuthorizationTool))
            invocation.Arguments[8].Should().Be(Identity.ActorId);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReusedTool_ResolvesEachRequestIdentity(Type type, string method, int actorIndex)
    {
        // Arrange
        var next = Identity with { ObjectId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee") };
        var accessor = new Mock<IConversationIdentityAccessor>();
        accessor.SetupSequence(x => x.Current).Returns(Identity).Returns(next);
        using var fixture = new Fixture(type, accessor.Object);

        // Act
        await fixture.Tool.ExecuteAsync(Arguments());
        await fixture.Tool.ExecuteAsync(Arguments());

        // Assert
        fixture.Service.Invocations.Where(x => x.Method.Name == method)
            .Select(x => x.Arguments[actorIndex]).Should().Equal(Identity.ActorId, next.ActorId);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task LocalCallerWithoutAccessor_PreservesLegacyActor(Type type, string method, int actorIndex)
    {
        // Arrange
        using var fixture = new Fixture(type);

        // Act
        var result = await fixture.Tool.ExecuteAsync(Arguments());

        // Assert
        result.Should().Contain("\"success\"");
        var invocation = fixture.Service.Invocations.Single(x => x.Method.Name == method);
        invocation.Arguments[actorIndex].Should().Be("mcp-user");
        if (type == typeof(IssueAuthorizationTool))
            invocation.Arguments[8].Should().Be("MCP User");
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task LocalCallerWithNoAmbientRequest_PreservesLegacyActor(Type type, string method, int actorIndex)
    {
        // Arrange
        var accessor = new Mock<IConversationIdentityAccessor>();
        accessor.SetupGet(x => x.Current).Returns((ConversationIdentity?)null);
        using var fixture = new Fixture(type, accessor.Object);

        // Act
        await fixture.Tool.ExecuteAsync(Arguments());

        // Assert
        fixture.Service.Invocations.Single(x => x.Method.Name == method)
            .Arguments[actorIndex].Should().Be("mcp-user");
    }

    [Theory]
    [MemberData(nameof(WriteMethods))]
    public async Task UnvalidatedHttpIdentity_DoesNotFallBackToCallerOrLocalActor(Type type, string method)
    {
        // Arrange
        var accessor = new Mock<IConversationIdentityAccessor>();
        accessor.SetupGet(x => x.Current).Throws(new UnauthorizedAccessException("Workspace is not validated."));
        using var fixture = new Fixture(type, accessor.Object);

        // Act
        var act = () => fixture.Tool.ExecuteAsync(Arguments());

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Service.Invocations.Should().NotContain(x => x.Method.Name == method);
    }

    [Theory]
    [MemberData(nameof(WriteMethods))]
    public async Task DeniedExecution_DoesNotInvokeDomainService(Type type, string method)
    {
        // Arrange
        var accessor = new Mock<IConversationIdentityAccessor>();
        accessor.SetupGet(x => x.Current).Returns(Identity);
        using var fixture = new Fixture(type, accessor.Object);
        using var authorization = ToolExecutionAuthorization.Push((_, _, _) =>
            throw new UnauthorizedAccessException("Operation denied."));

        // Act
        var act = () => fixture.Tool.ExecuteAsync(Arguments());

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Service.Invocations.Should().NotContain(x => x.Method.Name == method);
        accessor.VerifyGet(x => x.Current, Times.Never);
    }

    private static Dictionary<string, object?> Arguments() => new()
    {
        ["system_id"] = "system-1",
        ["control_id"] = "AC-1",
        ["narrative"] = "A test narrative.",
        ["target_version"] = "1",
        ["decision"] = "approve",
        ["decision_type"] = "ATO",
        ["residual_risk_level"] = "Low",
        ["expiration_date"] = "2027-01-01",
        ["finding_id"] = "finding-1",
        ["cat_severity"] = "CatIII",
        ["justification"] = "Test justification.",
        ["user_id"] = "forged-user",
        ["userId"] = "forged-user",
        ["authored_by"] = "forged-author",
        ["reviewer"] = "forged-reviewer",
        ["issued_by"] = "forged-ao",
        ["issued_by_name"] = "Forged AO",
        ["accepted_by"] = "forged-ao",
        ["user_role"] = "AuthorizingOfficial"
    };

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        public BaseTool Tool { get; }
        public Mock Service { get; }

        public Fixture(Type type, IConversationIdentityAccessor? accessor = null)
        {
            var services = new ServiceCollection().AddLogging();
            if (accessor is not null)
                services.AddSingleton(accessor);

            if (type == typeof(WriteNarrativeTool))
            {
                var service = new Mock<ISspService>();
                service.SetReturnsDefault(Task.FromResult(new ControlImplementation()));
                services.AddSingleton(service.Object);
                Service = service;
            }
            else if (type == typeof(IssueAuthorizationTool) || type == typeof(AcceptRiskTool))
            {
                var service = new Mock<IAuthorizationService>();
                service.SetReturnsDefault(Task.FromResult(new AuthorizationDecision()));
                service.SetReturnsDefault(Task.FromResult(new RiskAcceptance()));
                services.AddSingleton(service.Object);
                Service = service;
            }
            else
            {
                var service = new Mock<INarrativeGovernanceService>();
                service.SetReturnsDefault(Task.FromResult(new NarrativeVersion()));
                service.SetReturnsDefault(Task.FromResult(new NarrativeReview()));
                services.AddSingleton(service.Object);
                Service = service;
            }

            _provider = services.BuildServiceProvider();
            Tool = (BaseTool)ActivatorUtilities.CreateInstance(_provider, type);
        }

        public void Dispose() => _provider.Dispose();
    }
}
