using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using MembershipFactory = Ato.Copilot.Tests.Integration.Tenancy.WorkspaceMembershipFactory;
using SeedService = Ato.Copilot.Tests.Integration.Tenancy.TenancySeedHostedService;

namespace Ato.Copilot.Tests.Integration.Chat;

/// <summary>Actual native integrity verification through production middleware, roles and SQLite.</summary>
[Collection("IntegrationTests")]
public sealed class WorkspaceEvidenceIntegrityPipelineTests : IClassFixture<MembershipFactory>, IAsyncLifetime
{
    private static readonly Guid DirectoryId = Guid.Parse("eded0000-0000-0000-0000-000000000001");
    private static readonly Guid TenantId = MembershipFactory.TenantAId;
    private readonly WebApplicationFactory<McpProgram> _host;
    private readonly Guid _subject = Guid.NewGuid();
    private readonly Guid _person = Guid.NewGuid();
    private string _system = "";
    private string _evidence = "";
    private string _foreignEvidence = "";
    private string _unownedEvidence = "";
    private string _assessment = "";

    public WorkspaceEvidenceIntegrityPipelineTests(MembershipFactory factory)
    {
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            for (var index = services.Count - 1; index >= 0; index--)
            {
                var registration = services[index];
                if (registration.ServiceType == typeof(IHostedService)
                    && registration.ImplementationType != typeof(SeedService)
                    && registration.ImplementationType?.Namespace?.StartsWith("Microsoft.", StringComparison.Ordinal) != true)
                    services.RemoveAt(index);
            }
        }));
    }

    public async Task InitializeAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = new RegisteredSystem { TenantId = TenantId, Name = $"Integrity {_subject:N}", CreatedBy = "fixture" };
        var foreignSystem = new RegisteredSystem { TenantId = MembershipFactory.TenantBId, Name = $"Foreign {_subject:N}", CreatedBy = "fixture" };
        db.Persons.Add(new() { Id = _person, TenantId = TenantId, DisplayName = "Integrity verifier", Email = $"{_subject:N}@example.invalid" });
        db.RegisteredSystems.AddRange(system, foreignSystem);
        await db.SaveChangesAsync();
        db.OrganizationMemberships.Add(new() { TenantId = TenantId, PersonId = _person,
            DirectoryTenantId = DirectoryId, ObjectId = _subject, GrantedBy = "fixture" });
        var assessment = new ComplianceAssessment { TenantId = TenantId, RegisteredSystemId = system.Id, InitiatedBy = "fixture" };
        var foreignAssessment = new ComplianceAssessment { TenantId = MembershipFactory.TenantBId, RegisteredSystemId = foreignSystem.Id, InitiatedBy = "fixture" };
        db.Assessments.AddRange(assessment, foreignAssessment);
        await db.SaveChangesAsync();
        var evidence = Evidence(TenantId, assessment.Id);
        var foreign = Evidence(MembershipFactory.TenantBId, foreignAssessment.Id);
        var unowned = Evidence(TenantId, null);
        db.Evidence.AddRange(evidence, foreign, unowned);
        await db.SaveChangesAsync();
        _system = system.Id;
        _assessment = assessment.Id;
        _evidence = evidence.Id;
        _foreignEvidence = foreign.Id;
        _unownedEvidence = unowned.Id;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(OrganizationRole.Assessor)]
    [InlineData(OrganizationRole.Isso)]
    [InlineData(OrganizationRole.Issm)]
    public async Task EffectiveScaOrEvidenceManager_VerifiesWithoutChangingCollector(OrganizationRole role)
    {
        // Arrange
        await AssignAsync(role);
        using var client = Client();

        // Act
        using var response = await VerifyAsync(client, _evidence);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeFalse(result.ToString());
        var native = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.GetProperty("data");
        native.GetProperty("verification_status").GetString().Should().Be("verified");
        native.GetProperty("verifier_identity").GetString().Should().Be($"{DirectoryId:D}/{_subject:D}");
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var stored = await db.Evidence.SingleAsync(e => e.Id == _evidence);
        stored.IntegrityVerifiedAt.Should().NotBeNull();
        stored.CollectorIdentity.Should().Be("original-collector");
        var audit = await db.AuditLogs.Where(a => a.Action == "EvidenceIntegrityVerification"
            && a.UserId == $"{DirectoryId:D}/{_subject:D}").SingleAsync();
        audit.TenantId.Should().Be(TenantId);
        audit.Details.Should().Contain(_evidence);
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner)]
    [InlineData(OrganizationRole.SystemOwner)]
    [InlineData(OrganizationRole.AuthorizingOfficial)]
    public async Task OtherAssignedRoles_AndGlobalCspClaim_DoNotGainVerification(OrganizationRole role)
    {
        // Arrange
        await AssignAsync(role);
        using var client = Client();
        client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");

        // Act
        using var response = await VerifyAsync(client, _evidence);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("WORKSPACE_OPERATION_NOT_AUTHORIZED");
        await AssertUntouchedAsync(_evidence);
    }

    [Fact]
    public async Task OrganizationScaDefault_IsShadowedBySystemOverride()
    {
        // Arrange
        await AssignAsync(OrganizationRole.SystemOwner);
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.OrganizationRoleAssignments.Add(new() { TenantId = TenantId, PersonId = _person, Role = OrganizationRole.Assessor });
            var other = new Person { TenantId = TenantId, DisplayName = "Assigned replacement", Email = $"{Guid.NewGuid():N}@example.invalid" };
            db.Persons.Add(other);
            await db.SaveChangesAsync();
            db.SystemRoleAssignments.Add(new() { TenantId = TenantId, PersonId = other.Id, RegisteredSystemId = _system, Role = OrganizationRole.Assessor });
            await db.SaveChangesAsync();
        }
        using var client = Client();

        // Act
        using var response = await VerifyAsync(client, _evidence);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertUntouchedAsync(_evidence);
    }

    [Fact]
    public async Task ActiveOrganizationScaDefault_GrantsIntegrityVerification()
    {
        // Arrange
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.OrganizationRoleAssignments.Add(new() { TenantId = TenantId, PersonId = _person, Role = OrganizationRole.Assessor });
            await db.SaveChangesAsync();
        }
        using var client = Client();

        // Act
        using var response = await VerifyAsync(client, _evidence);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("isError").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForeignOrUnownedEvidence_CannotBorrowSuppliedSystem(bool foreign)
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        using var client = Client();
        var evidence = foreign ? _foreignEvidence : _unownedEvidence;

        // Act
        using var response = await VerifyAsync(client, evidence);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertUntouchedAsync(evidence);
    }

    [Fact]
    public async Task ScaVerification_DoesNotGrantNarrativeAuthorshipOrAuthorizationApproval()
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        using var client = Client();
        using var verified = await VerifyAsync(client, _evidence);
        verified.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        using var author = await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 2, method = "tools/call",
            @params = new { name = "narrative_set_policy", arguments = new { system_id = _system,
                control_id = "AC-1", policy_narrative = "Must not be written", user_role = "ISSM" } }
        });
        using var approve = await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 3, method = "tools/call",
            @params = new { name = "compliance_issue_authorization", arguments = new { system_id = _system,
                decision_type = "Ato", residual_risk_level = "Low", user_role = "AuthorizingOfficial" } }
        });

        // Assert
        author.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        approve.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.ControlImplementations.AnyAsync(i => i.RegisteredSystemId == _system)).Should().BeFalse();
        (await db.AuthorizationDecisions.AnyAsync(d => d.RegisteredSystemId == _system)).Should().BeFalse();
    }

    [Fact]
    public async Task TamperedContent_DoesNotUpdateTimestampOrCollector_ButAttributesAttempt()
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.Evidence.SingleAsync(e => e.Id == _evidence)).Content = "changed content";
            await db.SaveChangesAsync();
        }
        using var client = Client();

        // Act
        using var response = await VerifyAsync(client, _evidence);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        var native = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.GetProperty("data");
        native.GetProperty("verification_status").GetString().Should().Be("tampered");
        native.GetProperty("verifier_identity").GetString().Should().Be($"{DirectoryId:D}/{_subject:D}");
        await using var checkScope = _host.Services.CreateAsyncScope();
        var check = checkScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var stored = await check.Evidence.SingleAsync(e => e.Id == _evidence);
        stored.IntegrityVerifiedAt.Should().BeNull();
        stored.CollectorIdentity.Should().Be("original-collector");
        var audit = await check.AuditLogs.SingleAsync(a => a.Action == "EvidenceIntegrityVerification"
            && a.UserId == $"{DirectoryId:D}/{_subject:D}");
        audit.Details.Should().Contain("tampered").And.NotContain("changed content");
    }

    [Fact]
    public async Task Completeness_IsReadOnlyAndDoesNotCountForeignOrUnownedEvidence()
    {
        // Arrange
        await AssignAsync(OrganizationRole.SystemOwner);
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.ControlEffectivenessRecords.Add(new() { TenantId = TenantId, RegisteredSystemId = _system,
                AssessmentId = _assessment, ControlId = "AC-1", AssessorId = "fixture" });
            (await db.Evidence.SingleAsync(e => e.Id == _foreignEvidence)).IntegrityVerifiedAt = DateTime.UtcNow;
            (await db.Evidence.SingleAsync(e => e.Id == _unownedEvidence)).IntegrityVerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        using var client = Client();

        // Act
        using var response = await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 4, method = "tools/call",
            @params = new { name = "compliance_check_evidence_completeness", arguments = new { system_id = _system } }
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeFalse(result.ToString());
        var native = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.GetProperty("data");
        var control = native.GetProperty("control_statuses")[0];
        control.GetProperty("evidence_count").GetInt32().Should().Be(1);
        control.GetProperty("verified_count").GetInt32().Should().Be(0);
        await AssertUntouchedAsync(_evidence);
    }

    private async Task AssignAsync(OrganizationRole role)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.SystemRoleAssignments.Add(new() { TenantId = TenantId, PersonId = _person, RegisteredSystemId = _system, Role = role });
        await db.SaveChangesAsync();
    }

    private async Task AssertUntouchedAsync(string evidenceId)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.Evidence.SingleAsync(e => e.Id == evidenceId)).IntegrityVerifiedAt.Should().BeNull();
        (await db.AuditLogs.AnyAsync(a => a.Action == "EvidenceIntegrityVerification"
            && a.UserId == $"{DirectoryId:D}/{_subject:D}")).Should().BeFalse();
    }

    private HttpClient Client()
    {
        var client = _host.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Tid", DirectoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", _subject.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", TenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        return client;
    }

    private Task<HttpResponseMessage> VerifyAsync(HttpClient client, string evidenceId) =>
        client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "compliance_verify_evidence",
                arguments = new { evidence_id = evidenceId, system_id = _system, user_id = "forged",
                    user_role = "Sca", verifier_identity = "forged" } } });

    private static ComplianceEvidence Evidence(Guid tenant, string? assessment) => new()
    {
        TenantId = tenant, AssessmentId = assessment, ControlId = "AC-1",
        Content = "synthetic evidence", ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("synthetic evidence"))).ToLowerInvariant(),
        CollectorIdentity = "original-collector", CollectedBy = "original-collector", CollectionMethod = "fixture"
    };
}
