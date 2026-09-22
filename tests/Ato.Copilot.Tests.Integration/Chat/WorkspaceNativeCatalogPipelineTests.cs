using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Models.Roadmap;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;
using WorkspaceMembershipFactory = Ato.Copilot.Tests.Integration.Tenancy.WorkspaceMembershipFactory;
using TenancySeedHostedService = Ato.Copilot.Tests.Integration.Tenancy.TenancySeedHostedService;

namespace Ato.Copilot.Tests.Integration.Chat;

/// <summary>
/// Red-first catalog contract: real Program, membership middleware, SQLite, registered
/// BaseTools and domain services. No agent/tool/service replacement or cloud invocation.
/// The assessor has only system assignments; the AO has a separate qualified tid/oid.
/// Reads and denials must not attempt business saves (request audit entries are allowed).
/// Authorization fixtures satisfy the domain's phase, PTA and active-decision gates.
/// </summary>
[Collection("IntegrationTests")]
public sealed class WorkspaceNativeCatalogPipelineTests
    : IClassFixture<WorkspaceNativeCatalogPipelineTests.NativeCatalogFixture>, IAsyncLifetime
{
    private static readonly Guid DirectoryId = Guid.Parse("caba0000-0000-0000-0000-000000000001");
    private static readonly Guid OrgA = WorkspaceMembershipFactory.TenantAId;
    private static readonly Guid OrgB = WorkspaceMembershipFactory.TenantBId;
    private readonly NativeCatalogFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly Guid _assessor = Guid.NewGuid();
    private readonly Guid _ao = Guid.NewGuid();
    private CatalogSeed _own = null!;
    private CatalogSeed _other = null!;
    private CatalogSeed _foreign = null!;
    private string _unownedFinding = "";
    private string _unownedAssessment = "";

    public WorkspaceNativeCatalogPipelineTests(NativeCatalogFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public static IEnumerable<object[]> ReadCases()
    {
        string[] names =
        [
            "compliance_get_baseline", "compliance_get_categorization",
            "compliance_get_system_profile", "compliance_get_profile_completeness",
            "compliance_get_roadmap", "compliance_get_roadmap_progress",
            "compliance_get_control_validation",
            "compliance_list_imports", "compliance_get_import_summary", "compliance_list_nessus_imports",
            "compliance_list_prisma_policies", "compliance_prisma_trend",
            "compliance_list_boundary_definitions", "compliance_list_boundary_components",
            "compliance_boundary_gap_analysis", "compliance_list_rmf_roles",
            "inventory_get", "inventory_list", "inventory_completeness",
            "compliance_list_interconnections", "compliance_check_privacy_compliance",
            "compliance_list_poam", "compliance_poam_metrics", "compliance_poam_trend",
            "compliance_poam_by_component", "compliance_list_saps", "compliance_get_sap",
            "compliance_narrative_progress", "compliance_narrative_approval_progress",
            "compliance_ssp_completeness", "compliance_generate_ssp",
            "compliance_generate_sar", "compliance_generate_rar"
        ];
        foreach (var name in names)
            yield return [name];
    }

    public static IEnumerable<object[]> IndirectCases()
    {
        yield return ["inventory_get", "item_id"];
        yield return ["compliance_get_import_summary", "import_id"];
        yield return ["compliance_get_sap", "sap_id"];
        yield return ["compliance_list_boundary_components", "boundary_id"];
        yield return ["compliance_boundary_gap_analysis", "boundary_id"];
        yield return ["compliance_poam_by_component", "component_id"];
        yield return ["compliance_generate_sar", "assessment_id"];
        yield return ["compliance_generate_rar", "assessment_id"];
    }

    public async Task InitializeAsync()
    {
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Database.IsSqlite().Should().BeTrue("this suite must never use the optional SQL Server fixture");
        if (!await db.NistControls.AnyAsync(control => control.Id == "AC-1"))
            db.NistControls.Add(new NistControl { Id = "AC-1", Family = "AC", Title = "Synthetic control" });

        _own = SeedSystem(db, OrgA, "own");
        _other = SeedSystem(db, OrgA, "other-readable");
        _foreign = SeedSystem(db, OrgB, "foreign");
        var unowned = new ComplianceAssessment
        {
            TenantId = OrgA, InitiatedBy = "native-fixture", RegisteredSystemId = null
        };
        var finding = NewFinding(unowned, "Unowned assessment finding");
        db.Assessments.Add(unowned);
        db.Findings.Add(finding);
        _unownedAssessment = unowned.Id;
        _unownedFinding = finding.Id;

        foreach (var (actor, role) in new[]
        {
            (_assessor, OrganizationRole.Assessor), (_ao, OrganizationRole.AuthorizingOfficial)
        })
        {
            var person = new Person
            {
                TenantId = OrgA, DisplayName = role.ToString(), Email = $"{actor:N}@example.invalid"
            };
            db.Persons.Add(person);
            db.OrganizationMemberships.Add(new()
            {
                TenantId = OrgA, DirectoryTenantId = DirectoryId, ObjectId = actor,
                PersonId = person.Id, GrantedBy = "native-fixture"
            });
            foreach (var system in new[] { _own, _other })
                db.SystemRoleAssignments.Add(new()
                {
                    TenantId = OrgA, PersonId = person.Id, RegisteredSystemId = system.System.Id, Role = role
                });
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException error)
        {
            _output.WriteLine("Native fixture seed failed for: " +
                string.Join(", ", error.Entries.Select(entry => entry.Metadata.ClrType.Name)));
            throw;
        }
        _fixture.Writes.BusinessChanges.Should().Contain("RegisteredSystem:Added",
            "the observer must be attached to the same EF options used by the native services");
        _fixture.Writes.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExistingMappedNativeRead_ProvesAuthenticatedProductionPipelineAndSqlite()
    {
        // Arrange
        using var client = Client(_assessor);

        // Act
        using var response = await Call(client, "compliance_get_system", Target(_own));

        // Assert
        var payload = await Success(response);
        payload.GetRawText().Should().Contain(_own.System.Name).And.NotContain(_foreign.System.Name);
        NoBusinessWrites();
    }

    [Theory]
    [MemberData(nameof(ReadCases))]
    public async Task AssessorWithoutGlobalRole_ReadsNativeCatalogWithoutBusinessWrites(string tool)
    {
        // Arrange
        using var client = Client(_assessor);
        var arguments = ReadArguments(tool, _own);

        // Act
        using var response = await Call(client, tool, arguments);

        // Assert
        NoBusinessWrites();
        AssertNativeRead(tool, await Success(response), _own);
    }

    [Fact]
    public async Task AnotherAssignedSystemInSameOrganization_IsReadableWithoutBorrowingFirstSystem()
    {
        // Arrange
        using var client = Client(_assessor);

        // Act
        using var response = await Call(client, "inventory_list", Target(_other));

        // Assert
        AssertNativeRead("inventory_list", await Success(response), _other);
        NoBusinessWrites();
    }

    [Theory]
    [MemberData(nameof(ReadCases))]
    public async Task ForeignOrganizationSystem_CannotBeReadThroughNativeCatalog(string tool)
    {
        // Arrange
        using var client = Client(_assessor);

        // Act
        using var response = await Call(client, tool, ReadArguments(tool, _foreign));

        // Assert
        NoBusinessWrites();
        await Denied(response, HttpStatusCode.Forbidden, "WORKSPACE_OPERATION_NOT_AUTHORIZED",
            HttpStatusCode.NotFound, "WORKSPACE_RESOURCE_NOT_FOUND");
    }

    [Theory]
    [InlineData("inventory_get", "item_id")]
    [InlineData("compliance_get_import_summary", "import_id")]
    [InlineData("compliance_get_sap", "sap_id")]
    public async Task IndirectNativeRead_ResolvesOwnerWithoutCallerSystem(string tool, string key)
    {
        // Arrange
        using var client = Client(_assessor);
        var arguments = new Dictionary<string, object?> { [key] = ResourceId(_own, key) };

        // Act
        using var response = await Call(client, tool, arguments);

        // Assert
        NoBusinessWrites();
        AssertNativeRead(tool, await Success(response), _own);
    }

    [Theory]
    [MemberData(nameof(IndirectCases))]
    public async Task ReadableOtherSystemReference_CannotBorrowSelectedSystem(string tool, string key)
    {
        // Arrange
        using var client = Client(_assessor);
        var arguments = ReadArguments(tool, _own);
        arguments[key] = ResourceId(_other, key);

        // Act
        using var response = await Call(client, tool, arguments);

        // Assert
        NoBusinessWrites();
        await Denied(response, HttpStatusCode.Forbidden, "WORKSPACE_TOOL_TARGET_MISMATCH");
    }

    [Theory]
    [MemberData(nameof(IndirectCases))]
    public async Task ForeignOrganizationReference_CannotBorrowSelectedSystem(string tool, string key)
    {
        // Arrange
        using var client = Client(_assessor);
        var arguments = ReadArguments(tool, _own);
        arguments[key] = ResourceId(_foreign, key);

        // Act
        using var response = await Call(client, tool, arguments);

        // Assert
        NoBusinessWrites();
        await Denied(response, HttpStatusCode.NotFound, "WORKSPACE_RESOURCE_NOT_FOUND");
    }

    [Theory]
    [InlineData("compliance_generate_sar")]
    [InlineData("compliance_generate_rar")]
    public async Task RenderCannotUseAssessmentWithoutSystemOwner(string tool)
    {
        // Arrange
        using var client = Client(_assessor);
        var arguments = ReadArguments(tool, _own);
        arguments["assessment_id"] = _unownedAssessment;

        // Act
        using var response = await Call(client, tool, arguments);

        // Assert
        NoBusinessWrites();
        await Denied(response, HttpStatusCode.NotFound, "WORKSPACE_RESOURCE_NOT_FOUND");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrismaTrend_ValidatesEveryImportInJsonStringArray(bool crossOrganization)
    {
        // Arrange
        using var client = Client(_assessor);
        var arguments = ReadArguments("compliance_prisma_trend", _own);
        arguments["import_ids"] = JsonSerializer.Serialize(new[]
        {
            _own.Import.Id, crossOrganization ? _foreign.Import.Id : _other.Import.Id
        });

        // Act
        using var response = await Call(client, "compliance_prisma_trend", arguments);

        // Assert
        NoBusinessWrites();
        await Denied(response, crossOrganization ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden,
            crossOrganization ? "WORKSPACE_RESOURCE_NOT_FOUND" : "WORKSPACE_TOOL_TARGET_MISMATCH");
    }

    [Fact]
    public async Task OptionalMetricsSystem_MustNotFallBackToOrganizationAggregate()
    {
        // Arrange
        using var client = Client(_assessor);

        // Act
        using var response = await Call(client, "compliance_poam_metrics", new());

        // Assert
        NoBusinessWrites();
        await Denied(response, HttpStatusCode.BadRequest, "SYSTEM_TARGET_REQUIRED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("get")]
    public async Task ControlValidation_OnlyDefaultAndExplicitGetAreReadOperations(string? action)
    {
        // Arrange
        using var client = Client(_assessor);
        var arguments = ReadArguments("compliance_get_control_validation", _own);
        if (action is not null) arguments["action"] = action;

        // Act
        using var response = await Call(client, "compliance_get_control_validation", arguments);

        // Assert
        NoBusinessWrites();
        AssertNativeRead("compliance_get_control_validation", await Success(response), _own);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("ADD")]
    [InlineData(" delete ")]
    public async Task ControlValidationMutation_IsDeniedBeforeAnyNativeSideEffect(string action)
    {
        // Arrange
        using var client = Client(_assessor);
        var arguments = ReadArguments("compliance_get_control_validation", _own);
        arguments["action"] = action;
        arguments["link_id"] = _own.Validation.Id;
        arguments["link_type"] = "ExternalUrl";
        arguments["link_target"] = "https://example.invalid/new-validation";

        // Act
        using var response = await Call(client, "compliance_get_control_validation", arguments);

        // Assert
        NoBusinessWrites();
        await Denied(response, HttpStatusCode.Forbidden, "WORKSPACE_TOOL_NOT_SUPPORTED");
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.ControlValidationLinks.Where(l => l.ControlImplementationId == _own.Implementation.Id)
            .Select(l => l.Id).ToListAsync()).Should().Equal(_own.Validation.Id);
    }

    [Fact]
    public async Task ConflictingActionAndOperation_CannotTurnReadMappingIntoMutation()
    {
        // Arrange
        using var client = Client(_assessor);
        var arguments = ReadArguments("compliance_get_control_validation", _own);
        arguments["action"] = "get";
        arguments["operation"] = "delete";
        arguments["link_id"] = _own.Validation.Id;

        // Act
        using var response = await Call(client, "compliance_get_control_validation", arguments);

        // Assert
        NoBusinessWrites();
        await Denied(response, HttpStatusCode.Forbidden, "WORKSPACE_TOOL_NOT_SUPPORTED");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthorizingOfficial_IssuesNativeDecisionWithOwnFindingAndQualifiedActor(bool camelCase)
    {
        // Arrange
        using var client = Client(_ao);
        var arguments = IssueArguments([_own.Finding.Id], camelCase);

        // Act
        using var response = await Call(client, "compliance_issue_authorization", arguments);

        // Assert
        var data = (await Success(response)).GetProperty("data");
        data.GetProperty("issued_by").GetString().Should().Be(QualifiedAo);
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var decision = await db.AuthorizationDecisions.Include(d => d.RiskAcceptances)
            .SingleAsync(d => d.Id == data.GetProperty("id").GetString());
        decision.RegisteredSystemId.Should().Be(_own.System.Id);
        decision.TenantId.Should().Be(OrgA);
        decision.IssuedBy.Should().Be(QualifiedAo);
        decision.RiskAcceptances.Should().ContainSingle().Which.AcceptedBy.Should().Be(QualifiedAo);
        decision.RiskAcceptances.Single().FindingId.Should().Be(_own.Finding.Id);
        (await db.RegisteredSystems.SingleAsync(s => s.Id == _own.System.Id))
            .CurrentRmfStep.Should().Be(RmfPhase.Monitor);
        (await db.AuthorizationDecisions.SingleAsync(d => d.Id == _own.Decision.Id)).IsActive.Should().BeFalse();
        _fixture.Writes.BusinessChanges.Should().Contain(c => c == "AuthorizationDecision:Added");
    }

    [Fact]
    public async Task AuthorizingOfficial_AcceptsOwnFindingUsingNativeServiceAndQualifiedActor()
    {
        // Arrange
        using var client = Client(_ao);

        // Act
        using var response = await Call(client, "compliance_accept_risk", AcceptArguments(_own.Finding.Id));

        // Assert
        var data = (await Success(response)).GetProperty("data");
        data.GetProperty("accepted_by").GetString().Should().Be(QualifiedAo);
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var acceptance = await db.RiskAcceptances.SingleAsync(r => r.Id == data.GetProperty("id").GetString());
        acceptance.AuthorizationDecisionId.Should().Be(_own.Decision.Id);
        acceptance.FindingId.Should().Be(_own.Finding.Id);
        acceptance.AcceptedBy.Should().Be(QualifiedAo);
        acceptance.TenantId.Should().Be(OrgA);
        _fixture.Writes.BusinessChanges.Should().Contain("RiskAcceptance:Added");
    }

    [Theory]
    [InlineData("other", false)]
    [InlineData("other", true)]
    [InlineData("foreign", false)]
    [InlineData("foreign", true)]
    [InlineData("unowned", false)]
    [InlineData("unowned", true)]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    public async Task InlineRiskAcceptance_ValidatesAllFindingOwnersBeforeSupersedingDecision(
        string owner, bool camelCase)
    {
        // Arrange
        using var client = Client(_ao);
        var foreignFinding = FindingFor(owner);
        var arguments = IssueArguments([_own.Finding.Id, foreignFinding], camelCase);

        // Act
        using var response = await Call(client, "compliance_issue_authorization", arguments);

        // Assert
        NoBusinessWrites();
        await AssertFindingDenial(response, owner);
        await AssertDecisionUnchanged();
    }

    [Theory]
    [InlineData("other")]
    [InlineData("foreign")]
    [InlineData("unowned")]
    [InlineData("missing")]
    public async Task AcceptRisk_CannotAttachForeignOrUnownedFindingToOwnAuthorization(string owner)
    {
        // Arrange
        using var client = Client(_ao);

        // Act
        using var response = await Call(client, "compliance_accept_risk", AcceptArguments(FindingFor(owner)));

        // Assert
        NoBusinessWrites();
        await AssertFindingDenial(response, owner);
        await AssertDecisionUnchanged();
    }

    [Theory]
    [InlineData("compliance_issue_authorization")]
    [InlineData("compliance_accept_risk")]
    public async Task AssessorCannotDecideAuthorization_EvenWithForgedActorAndRole(string tool)
    {
        // Arrange
        using var client = Client(_assessor);
        var arguments = tool == "compliance_issue_authorization"
            ? IssueArguments([_own.Finding.Id], true) : AcceptArguments(_own.Finding.Id);

        // Act
        using var response = await Call(client, tool, arguments);

        // Assert
        NoBusinessWrites();
        await Denied(response, HttpStatusCode.Forbidden, "WORKSPACE_OPERATION_NOT_AUTHORIZED");
        await AssertDecisionUnchanged();
    }

    private string QualifiedAo => $"{DirectoryId:D}/{_ao:D}";

    private HttpClient Client(Guid actor)
    {
        var client = _fixture.Host.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", OrgA.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Test-Tid", DirectoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        return client;
    }

    private Task<HttpResponseMessage> Call(HttpClient client, string name, Dictionary<string, object?> arguments)
    {
        var tool = _fixture.Host.Services.GetServices<BaseTool>().Single(t => t.Name == name);
        tool.GetType().Assembly.FullName.Should().Be(typeof(BaseTool).Assembly.FullName,
            "the actual registered native tool, not a recording or policy-only substitute, must execute");
        return client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments }
        });
    }

    private async Task<JsonElement> Success(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"{(int)response.StatusCode}: {body}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var envelope = JsonDocument.Parse(body);
        (envelope.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            .Should().BeFalse(body);
        var result = envelope.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeFalse(body);
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        text.Should().NotBeNullOrWhiteSpace();
        using var payload = JsonDocument.Parse(text!);
        return payload.RootElement.Clone();
    }

    private async Task Denied(HttpResponseMessage response, HttpStatusCode status, string code,
        HttpStatusCode? alternateStatus = null, string? alternateCode = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"{(int)response.StatusCode}: {body}");
        // Resource-owner lookup may conceal an inaccessible child before system authorization.
        if (alternateStatus.HasValue && response.StatusCode == alternateStatus.Value)
            body.Should().Contain(alternateCode!);
        else
        {
            response.StatusCode.Should().Be(status, body);
            body.Should().Contain(code);
        }
        body.Should().NotContain("\"isError\":false").And.NotContain(_foreign.System.Name);
    }

    private void NoBusinessWrites() =>
        _fixture.Writes.BusinessChanges.Should().BeEmpty("reads and policy denials must not attempt business persistence");

    private static Dictionary<string, object?> Target(CatalogSeed seed) => new() { ["system_id"] = seed.System.Id };

    private static Dictionary<string, object?> ReadArguments(string tool, CatalogSeed seed)
    {
        var args = Target(seed);
        switch (tool)
        {
            case "inventory_get": args["item_id"] = seed.Inventory.Id; break;
            case "compliance_get_import_summary": args["import_id"] = seed.Import.Id; break;
            case "compliance_get_sap": args["sap_id"] = seed.Sap.Id; break;
            case "compliance_get_control_validation": args["control_id"] = "AC-1"; break;
            case "compliance_list_boundary_components":
            case "compliance_boundary_gap_analysis": args["boundary_id"] = seed.Boundary.Id; break;
            case "compliance_poam_by_component": args["component_id"] = seed.Component.Id; break;
            case "compliance_generate_sar":
            case "compliance_generate_rar": args["assessment_id"] = seed.Assessment.Id; args["format"] = "markdown"; break;
            case "compliance_prisma_trend":
                args["import_ids"] = JsonSerializer.Serialize(new[] { seed.Import.Id, seed.LaterImport.Id });
                break;
            case "compliance_generate_ssp": args["format"] = "markdown"; break;
        }
        return args;
    }

    private static string ResourceId(CatalogSeed seed, string key) => key switch
    {
        "item_id" => seed.Inventory.Id,
        "import_id" => seed.Import.Id,
        "sap_id" => seed.Sap.Id,
        "boundary_id" => seed.Boundary.Id,
        "component_id" => seed.Component.Id,
        "assessment_id" => seed.Assessment.Id,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private static void AssertNativeRead(string tool, JsonElement payload, CatalogSeed seed)
    {
        if (tool == "compliance_poam_metrics")
        {
            payload.GetProperty("totalOpen").GetInt32().Should().Be(1);
            payload.GetProperty("catII").GetInt32().Should().Be(1);
            return;
        }
        if (tool == "compliance_poam_trend")
        {
            payload.GetProperty("systemId").GetString().Should().Be(seed.System.Id);
            payload.GetProperty("openOverTime").GetArrayLength().Should().BeGreaterThan(0);
            return;
        }

        payload.GetProperty("status").GetString().Should().Be("success", payload.GetRawText());
        var data = payload.GetProperty("data");
        data.ValueKind.Should().Be(JsonValueKind.Object);
        switch (tool)
        {
            case "compliance_get_baseline":
                data.GetProperty("id").GetString().Should().Be(seed.Baseline.Id);
                data.GetProperty("control_ids").EnumerateArray().Select(x => x.GetString()).Should().Equal("AC-1");
                break;
            case "compliance_get_categorization": data.GetProperty("id").GetString().Should().Be(seed.Categorization.Id); break;
            case "compliance_get_system_profile":
                data.GetProperty("systemId").GetString().Should().Be(seed.System.Id);
                data.GetProperty("sections").GetArrayLength().Should().Be(6);
                break;
            case "compliance_get_profile_completeness":
                data.GetProperty("systemId").GetString().Should().Be(seed.System.Id);
                data.GetProperty("totalSections").GetInt32().Should().Be(5);
                break;
            case "compliance_get_roadmap":
            case "compliance_get_roadmap_progress": data.GetProperty("roadmap_id").GetString().Should().Be(seed.Roadmap.Id); break;
            case "compliance_get_control_validation": data.GetProperty("links")[0].GetProperty("id").GetString().Should().Be(seed.Validation.Id); break;
            case "compliance_list_imports": ArrayIds(data, "imports", "id").Should().BeEquivalentTo(seed.Import.Id, seed.LaterImport.Id, seed.Nessus.Id); break;
            case "compliance_get_import_summary": data.GetProperty("id").GetString().Should().Be(seed.Import.Id); break;
            case "compliance_list_nessus_imports": ArrayIds(data, "imports", "id").Should().Equal(seed.Nessus.Id); break;
            case "compliance_list_prisma_policies":
                data.GetProperty("policies")[0].GetProperty("policy_name").GetString().Should().Be(seed.System.Name);
                break;
            case "compliance_prisma_trend": ArrayIds(data, "imports", "import_id").Should().Equal(seed.Import.Id, seed.LaterImport.Id); break;
            case "compliance_list_boundary_definitions": ArrayIds(data, "boundaries", "id").Should().Equal(seed.Boundary.Id); break;
            case "compliance_list_boundary_components": ArrayIds(data, "components", "component_id").Should().Equal(seed.Component.Id); break;
            case "compliance_boundary_gap_analysis": data.GetProperty("boundary_filter").GetString().Should().Be(seed.Boundary.Id); break;
            case "compliance_list_rmf_roles": data.GetProperty("total_roles").GetInt32().Should().Be(0); break;
            case "inventory_get": data.GetProperty("id").GetString().Should().Be(seed.Inventory.Id); break;
            case "inventory_list": ArrayIds(data, "items", "id").Should().Equal(seed.Inventory.Id); break;
            case "inventory_completeness":
                data.GetProperty("total_items").GetInt32().Should().Be(1);
                data.GetProperty("is_complete").GetBoolean().Should().BeTrue();
                break;
            case "compliance_list_interconnections": data.GetProperty("totalInterconnections").GetInt32().Should().Be(0); break;
            case "compliance_check_privacy_compliance": data.GetProperty("overall_status").GetString().Should().Be("Compliant"); break;
            case "compliance_list_poam": ArrayIds(data, "items", "id").Should().Equal(seed.Poam.Id); break;
            case "compliance_poam_by_component":
                data.GetProperty("component_id").GetString().Should().Be(seed.Component.Id);
                ArrayIds(data, "items", "id").Should().Equal(seed.Poam.Id);
                break;
            case "compliance_list_saps": ArrayIds(data, "saps", "sap_id").Should().Equal(seed.Sap.Id); break;
            case "compliance_get_sap":
                data.GetProperty("sap_id").GetString().Should().Be(seed.Sap.Id);
                data.GetProperty("content").GetString().Should().Be(seed.Sap.Content);
                break;
            case "compliance_narrative_progress": data.GetProperty("total_controls").GetInt32().Should().Be(1); break;
            case "compliance_narrative_approval_progress": data.GetProperty("overall").GetProperty("total_controls").GetInt32().Should().Be(1); break;
            case "compliance_ssp_completeness":
                data.GetProperty("system_name").GetString().Should().Be(seed.System.Name);
                data.GetProperty("total_sections").GetInt32().Should().Be(13);
                break;
            case "compliance_generate_ssp":
            case "compliance_generate_sar":
            case "compliance_generate_rar":
                data.GetProperty("system_id").GetString().Should().Be(seed.System.Id);
                data.GetProperty("content").GetString().Should().Contain(seed.System.Name);
                if (tool != "compliance_generate_ssp")
                    data.GetProperty("assessment_id").GetString().Should().Be(seed.Assessment.Id);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(tool), tool, "Missing native payload assertion");
        }
    }

    private static IEnumerable<string?> ArrayIds(JsonElement data, string collection, string key) =>
        data.GetProperty(collection).EnumerateArray().Select(x => x.GetProperty(key).GetString());

    private Dictionary<string, object?> IssueArguments(string[] findings, bool camelCase)
    {
        var args = Target(_own);
        args["decision_type"] = "ATO";
        args["expiration_date"] = DateTime.UtcNow.AddYears(1).ToString("O");
        args["residual_risk_level"] = "Low";
        args["risk_acceptances"] = JsonSerializer.Serialize(findings.Select(id => new RiskAcceptanceInput
        {
            FindingId = id, ControlId = "AC-1", CatSeverity = "CatII",
            Justification = "Synthetic native pipeline test", ExpirationDate = DateTime.UtcNow.AddMonths(3)
        }), new JsonSerializerOptions { PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null });
        args["user_id"] = "forged-actor";
        args["user_role"] = "Compliance.AuthorizingOfficial";
        return args;
    }

    private Dictionary<string, object?> AcceptArguments(string finding)
    {
        var args = Target(_own);
        args["finding_id"] = finding;
        args["control_id"] = "AC-1";
        args["cat_severity"] = "CatII";
        args["justification"] = "Synthetic native pipeline test";
        args["expiration_date"] = DateTime.UtcNow.AddMonths(3).ToString("O");
        args["user_id"] = "forged-actor";
        args["user_role"] = "Compliance.AuthorizingOfficial";
        return args;
    }

    private string FindingFor(string owner) => owner switch
    {
        "other" => _other.Finding.Id, "foreign" => _foreign.Finding.Id,
        "unowned" => _unownedFinding, "missing" => Guid.NewGuid().ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(owner))
    };

    private Task AssertFindingDenial(HttpResponseMessage response, string owner) =>
        Denied(response, owner == "other" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound,
            owner == "other" ? "WORKSPACE_TOOL_TARGET_MISMATCH" : "WORKSPACE_RESOURCE_NOT_FOUND");

    private async Task AssertDecisionUnchanged()
    {
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var decisions = await db.AuthorizationDecisions.Where(d => d.RegisteredSystemId == _own.System.Id).ToListAsync();
        decisions.Should().ContainSingle().Which.Id.Should().Be(_own.Decision.Id);
        decisions.Single().IsActive.Should().BeTrue();
        decisions.Single().SupersededById.Should().BeNull();
        (await db.RiskAcceptances.CountAsync(r => r.AuthorizationDecisionId == _own.Decision.Id)).Should().Be(0);
        (await db.RegisteredSystems.SingleAsync(s => s.Id == _own.System.Id)).CurrentRmfStep.Should().Be(RmfPhase.Authorize);
    }

    private static ComplianceFinding NewFinding(ComplianceAssessment assessment, string title) => new()
    {
        TenantId = assessment.TenantId, AssessmentId = assessment.Id, ControlId = "AC-1",
        ControlFamily = "AC", Title = title, Status = FindingStatus.Open, Severity = FindingSeverity.Medium
    };

    private static CatalogSeed SeedSystem(AtoCopilotContext db, Guid tenant, string label)
    {
        var system = new RegisteredSystem
        {
            TenantId = tenant, Name = $"native-{label}-{Guid.NewGuid():N}", CreatedBy = "native-fixture",
            IsActive = true, CurrentRmfStep = RmfPhase.Authorize, HasNoExternalInterconnections = true
        };
        var assessment = new ComplianceAssessment { TenantId = tenant, RegisteredSystemId = system.Id, InitiatedBy = "native-fixture" };
        var finding = NewFinding(assessment, system.Name);
        var baseline = new ControlBaseline
        {
            TenantId = tenant, RegisteredSystemId = system.Id, BaselineLevel = "Low",
            ControlIds = ["AC-1"], TotalControls = 1, CustomerControls = 1, CreatedBy = "native-fixture"
        };
        var categorization = new SecurityCategorization
        {
            TenantId = tenant, RegisteredSystemId = system.Id, CategorizedBy = "native-fixture"
        };
        var implementation = new ControlImplementation
        {
            TenantId = tenant, RegisteredSystemId = system.Id, ControlId = "AC-1",
            PolicyNarrative = "Synthetic policy", TechnicalNarrative = "Synthetic implementation", AuthoredBy = "native-fixture"
        };
        var validation = new ControlValidationLink
        {
            TenantId = tenant, ControlImplementationId = implementation.Id, LinkType = ControlValidationLinkType.ExternalUrl,
            LinkTarget = "https://example.invalid/native-validation", AddedBy = "native-fixture"
        };
        var inventory = new InventoryItem
        {
            TenantId = tenant, RegisteredSystemId = system.Id, ItemName = system.Name, Type = InventoryItemType.Software,
            Vendor = "Synthetic", Version = "1", CreatedBy = "native-fixture"
        };
        var boundary = new AuthorizationBoundaryDefinition
        {
            TenantId = tenant, RegisteredSystemId = system.Id, Name = system.Name, IsPrimary = true, CreatedBy = "native-fixture"
        };
        var component = new SystemComponent
        {
            TenantId = tenant, RegisteredSystemId = system.Id, Name = system.Name,
            ComponentType = ComponentType.Thing, CreatedBy = "native-fixture"
        };
        var import = new ScanImportRecord
        {
            TenantId = tenant, RegisteredSystemId = system.Id, AssessmentId = assessment.Id,
            ImportType = ScanImportType.PrismaCsv, FileName = $"{label}-earlier.csv", FileHash = Guid.NewGuid().ToString("N"),
            ImportedBy = "native-fixture", ImportedAt = DateTime.UtcNow.AddDays(-2), TotalEntries = 1, OpenCount = 1
        };
        var laterImport = new ScanImportRecord
        {
            TenantId = tenant, RegisteredSystemId = system.Id, AssessmentId = assessment.Id,
            ImportType = ScanImportType.PrismaCsv, FileName = $"{label}-later.csv", FileHash = Guid.NewGuid().ToString("N"),
            ImportedBy = "native-fixture", ImportedAt = DateTime.UtcNow.AddDays(-1)
        };
        var nessus = new ScanImportRecord
        {
            TenantId = tenant, RegisteredSystemId = system.Id, AssessmentId = assessment.Id,
            ImportType = ScanImportType.NessusXml, FileName = $"{label}.nessus",
            FileHash = Guid.NewGuid().ToString("N"), ImportedBy = "native-fixture"
        };
        var sap = new SecurityAssessmentPlan
        {
            TenantId = tenant, RegisteredSystemId = system.Id, AssessmentId = assessment.Id,
            Title = system.Name, Content = $"# SAP for {system.Name}", BaselineLevel = "Low", GeneratedBy = "native-fixture"
        };
        var roadmap = new ImplementationRoadmap
        {
            TenantId = tenant, SystemId = system.Id, Name = system.Name, Status = RoadmapStatus.Active,
            BaselineLevel = "Low", GenerationMethod = "Manual", CreatedBy = "native-fixture"
        };
        var poam = new PoamItem
        {
            TenantId = tenant, RegisteredSystemId = system.Id, FindingId = finding.Id,
            Weakness = system.Name, WeaknessSource = "Manual", SecurityControlNumber = "AC-1",
            PointOfContact = "native-fixture", CatSeverity = CatSeverity.CatII,
            ScheduledCompletionDate = DateTime.UtcNow.AddDays(60)
        };
        var decision = new AuthorizationDecision
        {
            TenantId = tenant, RegisteredSystemId = system.Id, DecisionType = AuthorizationDecisionType.Ato,
            IssuedBy = "native-fixture", IssuedByName = "Native fixture", ExpirationDate = DateTime.UtcNow.AddYears(1)
        };
        db.AddRange(system, assessment, finding, baseline, categorization, implementation, validation, inventory,
            boundary, component, import, laterImport, nessus, sap, roadmap, poam, decision,
            new PrivacyThresholdAnalysis
            {
                TenantId = tenant, RegisteredSystemId = system.Id,
                Determination = PtaDetermination.PiaNotRequired, AnalyzedBy = "native-fixture"
            },
            new BoundaryComponentAssignment
            {
                TenantId = tenant, AuthorizationBoundaryDefinitionId = boundary.Id,
                SystemComponentId = component.Id, IsInScope = true, CreatedBy = "native-fixture"
            },
            new PoamComponentLink { TenantId = tenant, PoamItemId = poam.Id, SystemComponentId = component.Id },
            new ScanImportFinding
            {
                TenantId = tenant, ScanImportRecordId = import.Id, ComplianceFindingId = finding.Id,
                PrismaPolicyName = system.Name, PrismaAlertId = Guid.NewGuid().ToString(), ResolvedNistControlIds = ["AC-1"]
            });
        return new(system, assessment, finding, baseline, categorization, implementation, validation, inventory,
            boundary, component, import, laterImport, nessus, sap, roadmap, poam, decision);
    }

    private sealed record CatalogSeed(
        RegisteredSystem System, ComplianceAssessment Assessment, ComplianceFinding Finding,
        ControlBaseline Baseline, SecurityCategorization Categorization, ControlImplementation Implementation,
        ControlValidationLink Validation, InventoryItem Inventory, AuthorizationBoundaryDefinition Boundary,
        SystemComponent Component, ScanImportRecord Import, ScanImportRecord LaterImport, ScanImportRecord Nessus,
        SecurityAssessmentPlan Sap, ImplementationRoadmap Roadmap, PoamItem Poam, AuthorizationDecision Decision);

    public sealed class NativeCatalogFixture : IDisposable
    {
        private readonly WorkspaceMembershipFactory _factory = new();
        private readonly string _database = Path.Combine(AppContext.BaseDirectory, $"native-catalog-{Guid.NewGuid():N}.db");
        public BusinessWriteObserver Writes { get; } = new();
        public WebApplicationFactory<McpProgram> Host { get; }

        public NativeCatalogFixture()
        {
            Host = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "Sqlite",
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={_database};Mode=ReadWriteCreate"
                }));
                builder.ConfigureServices(services =>
                {
                    services.ConfigureDbContext<AtoCopilotContext>(options => options.AddInterceptors(Writes),
                        ServiceLifetime.Singleton);
                    for (var index = services.Count - 1; index >= 0; index--)
                    {
                        var registration = services[index];
                        var type = registration.ImplementationType ?? registration.ImplementationInstance?.GetType();
                        if (registration.ServiceType == typeof(IHostedService)
                            && type != typeof(TenancySeedHostedService)
                            && type?.Namespace?.StartsWith("Microsoft.", StringComparison.Ordinal) != true)
                            services.RemoveAt(index);
                    }
                    services.Configure<HostOptions>(options =>
                        options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
                });
            });
        }

        public void Dispose()
        {
            Host.Dispose();
            _factory.Dispose();
            foreach (var path in new[] { _database, _database + "-wal", _database + "-shm" })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    public sealed class BusinessWriteObserver : SaveChangesInterceptor
    {
        private readonly ConcurrentQueue<string> _changes = new();
        public IReadOnlyCollection<string> BusinessChanges => _changes.ToArray();
        public void Clear() => _changes.Clear();

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Observe(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Observe(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private void Observe(DbContext? context)
        {
            if (context is null) throw new InvalidOperationException("Missing context for native catalog save observation.");
            context.ChangeTracker.DetectChanges();
            foreach (var entry in context.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                if (entry.Entity is not AuditLogEntry)
                    _changes.Enqueue($"{entry.Metadata.ClrType.Name}:{entry.State}");
            }
        }
    }
}
