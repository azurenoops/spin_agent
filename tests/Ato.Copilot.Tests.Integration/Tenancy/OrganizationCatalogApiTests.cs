using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class OrganizationCatalogApiTests : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly WorkspaceMembershipFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _directoryId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    private Guid _personId;
    private string Root => $"/api/workspaces/organizations/{_tenantId}";

    public OrganizationCatalogApiTests(WorkspaceMembershipFactory factory) => _factory = factory;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrganizationCapability_CanAcquireFirstSystemLinkWithoutAssigningOrganizationComponents(
        bool hasOrganizationComponent)
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        var catalogBody = NewBody("capability");
        if (hasOrganizationComponent)
            catalogBody["newComponents"] = new[] { ComponentBody("Organization-only support") };
        var created = await client.PostAsJsonAsync($"{Root}/catalog-additions", catalogBody);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var capabilityId = (await DataAsync(created)).GetProperty("recordId").GetString()!;
        await AssertNoSystemWritesAsync();
        var systemId = await SeedManageableSystemAsync();
        var body = new Dictionary<string, object?>
        {
            ["idempotencyKey"] = Guid.NewGuid().ToString("N"), ["source"] = "local",
            ["recordId"] = capabilityId, ["systemId"] = systemId,
            ["componentIds"] = Array.Empty<string>(), ["subscribe"] = false
        };

        // Act
        var library = await DataAsync(await client.GetAsync($"{Root}/capabilities?source=local&grouping=capability"));
        var prepared = await client.PostAsJsonAsync($"{Root}/capability-setups/prepare", body);

        // Assert
        library.GetProperty("items").EnumerateArray().Should().ContainSingle(x =>
            x.GetProperty("recordId").GetString() == capabilityId && x.GetProperty("systemCount").GetInt32() == 0);
        prepared.StatusCode.Should().Be(HttpStatusCode.Created, await prepared.Content.ReadAsStringAsync());
        var operation = await DataAsync(prepared);
        operation.GetProperty("outcomes").EnumerateArray().Should().ContainSingle(x =>
            x.GetProperty("writeKind").GetString() == "system-link" && x.GetProperty("state").GetString() == "Pending");
        await VerifyAsync(async db =>
            (await db.SystemCapabilityLinks.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse());

        // Act
        body["preparedOperationId"] = operation.GetProperty("operationId").GetGuid();
        var completed = await client.PostAsJsonAsync($"{Root}/capability-setups", body);
        var replay = await client.PostAsJsonAsync($"{Root}/capability-setups", body);

        // Assert
        completed.StatusCode.Should().Be(HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        var result = await DataAsync(completed);
        result.GetProperty("outcomes").EnumerateArray().Should().ContainSingle(x =>
            x.GetProperty("writeKind").GetString() == "system-link" && x.GetProperty("state").GetString() == "Completed");
        (await DataAsync(replay)).GetProperty("operationId").GetGuid().Should()
            .Be(result.GetProperty("operationId").GetGuid());
        var systemDetail = await client.GetAsync(
            $"{Root}/capabilities/local/{capabilityId}?recordType=capability&systemId={systemId}");
        systemDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        await VerifyAsync(async db =>
        {
            var link = await db.SystemCapabilityLinks.IgnoreQueryFilters().SingleAsync(x => x.TenantId == _tenantId);
            link.SecurityCapabilityId.Should().Be(capabilityId);
            link.RegisteredSystemId.Should().Be(systemId);
            (await db.SecurityCapabilities.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(1);
            (await db.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId))
                .Should().Be(hasOrganizationComponent ? 1 : 0);
            (await db.SystemComponents.IgnoreQueryFilters().AnyAsync(x =>
                x.TenantId == _tenantId && x.RegisteredSystemId != null)).Should().BeFalse();
            (await db.CapabilitySubscriptions.IgnoreQueryFilters().AnyAsync(x => x.RoutingTenantId == _tenantId)).Should().BeFalse();
            (await db.NarrativeProposals.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
        });
    }

    [Fact]
    public async Task SystemSetup_RejectsOrganizationOnlyComponentInPrepareAndComplete()
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        var created = await client.PostAsJsonAsync($"{Root}/catalog-additions", NewBody("capability"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var capabilityId = (await DataAsync(created)).GetProperty("recordId").GetString()!;
        var componentId = await SeedLocalComponentAsync();
        var systemId = await SeedManageableSystemAsync();
        var body = new
        {
            idempotencyKey = Guid.NewGuid().ToString("N"), source = "local", recordId = capabilityId,
            systemId, componentIds = new[] { componentId }, subscribe = false
        };

        // Act
        var prepare = await client.PostAsJsonAsync($"{Root}/capability-setups/prepare", body);
        var complete = await client.PostAsJsonAsync($"{Root}/capability-setups", body);

        // Assert
        prepare.StatusCode.Should().Be(HttpStatusCode.Forbidden, await prepare.Content.ReadAsStringAsync());
        complete.StatusCode.Should().Be(HttpStatusCode.Forbidden, await complete.Content.ReadAsStringAsync());
        await VerifyAsync(async db =>
        {
            (await db.SystemCapabilityLinks.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
            (await db.ComponentCapabilityLinks.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
            (await db.SystemComponents.IgnoreQueryFilters().SingleAsync(x => x.Id == componentId))
                .RegisteredSystemId.Should().BeNull();
        });
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("component")]
    public async Task ExistingLocalDefinition_UpdatesAtOrganizationScopeAndReplaysWithoutOverwriting(string recordType)
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        var original = NewBody(recordType);
        var first = await client.PostAsJsonAsync($"{Root}/catalog-additions", original);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await DataAsync(first)).GetProperty("recordId").GetString()!;
        var edit = NewBody(recordType);
        edit["idempotencyKey"] = Guid.NewGuid().ToString();
        edit["recordId"] = id;
        edit[recordType] = recordType == "component"
            ? ComponentBody("Updated reusable component")
            : new { name = "Updated reusable capability", provider = "Organization", category = "AC",
                description = "Updated organization definition", implementationStatus = "Implemented", owner = "New owner" };

        // Act
        var updated = await client.PostAsJsonAsync($"{Root}/catalog-additions", edit);
        var replay = await client.PostAsJsonAsync($"{Root}/catalog-additions", original);

        // Assert
        updated.StatusCode.Should().Be(HttpStatusCode.Created, await updated.Content.ReadAsStringAsync());
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await DataAsync(await client.GetAsync(first.Headers.Location));
        detail.GetProperty("capability").GetProperty("name").GetString().Should().StartWith("Updated reusable");
        await VerifyAsync(async db =>
        {
            if (recordType == "component")
            {
                var component = await db.SystemComponents.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
                component.RegisteredSystemId.Should().BeNull();
                component.ModifiedAt.Should().NotBeNull();
            }
            else
            {
                var capability = await db.SecurityCapabilities.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
                capability.ImplementationStatus.Should().Be(CapabilityStatus.Implemented);
                capability.Owner.Should().Be("New owner");
                capability.ModifiedBy.Should().NotBeNullOrEmpty();
            }
        });
        await AssertNoSystemWritesAsync();
    }

    [Fact]
    public async Task DuplicateName_IsTenantScopedAndReturnsConflictWithoutInlineRows()
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        var first = await client.PostAsJsonAsync($"{Root}/catalog-additions", NewBody("capability"));
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var duplicate = NewBody("capability");
        duplicate["idempotencyKey"] = Guid.NewGuid().ToString();
        duplicate["newComponents"] = new[] { ComponentBody("Rejected duplicate support") };

        // Act
        var response = await client.PostAsJsonAsync($"{Root}/catalog-additions", duplicate);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        await VerifyAsync(async db =>
        {
            (await db.SecurityCapabilities.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(1);
            (await db.SystemComponents.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(0);
            (await db.OrganizationCatalogAdditions.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(1);
        });
    }

    [Fact]
    public async Task OrganizationRead_ExcludesSystemComponentsAndSystemControlMappings()
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        var created = await client.PostAsJsonAsync($"{Root}/catalog-additions", NewBody("capability"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await DataAsync(created)).GetProperty("recordId").GetString()!;
        var componentId = await SeedLocalComponentAsync(systemBound: true);
        await VerifyAsync(async db =>
        {
            var systemId = (await db.SystemComponents.IgnoreQueryFilters().SingleAsync(x => x.Id == componentId)).RegisteredSystemId!;
            db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
            {
                TenantId = _tenantId, SystemComponentId = componentId, SecurityCapabilityId = id
            });
            db.CapabilityControlMappings.AddRange(
                new CapabilityControlMapping { TenantId = _tenantId, SecurityCapabilityId = id, ControlId = "AC-2", CreatedBy = "test" },
                new CapabilityControlMapping { TenantId = _tenantId, SecurityCapabilityId = id, RegisteredSystemId = systemId,
                    ControlId = "IA-2", CreatedBy = "test" });
            await db.SaveChangesAsync();
        });

        // Act
        var list = await DataAsync(await client.GetAsync($"{Root}/capabilities?grouping=component&source=local"));
        var detail = await DataAsync(await client.GetAsync(created.Headers.Location));
        var denied = await client.GetAsync($"{Root}/capabilities/local/{componentId}?recordType=component");

        // Assert
        list.GetProperty("total").GetInt32().Should().Be(0);
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        detail.GetProperty("supportingComponents").GetArrayLength().Should().Be(0);
        detail.GetProperty("controlCoverage").EnumerateArray().Should().ContainSingle(x =>
            x.GetProperty("controlId").GetString() == "AC-2"
            && x.GetProperty("designation").GetString() == "Undesignated"
            && x.GetProperty("systemId").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task RelationalSaveFailure_RollsBackAllInlineRowsAndReceipt()
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        await VerifyAsync(db => db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER CatalogTestRollback BEFORE INSERT ON OrganizationCatalogAdditions
            WHEN NEW.CreatedBy IS NOT NULL
            BEGIN SELECT RAISE(ABORT, 'Forced catalog transaction failure'); END;
            """));
        var body = NewBody("capability");
        body["newComponents"] = new[] { ComponentBody("Rolled back support") };
        try
        {
            // Act
            var response = await client.PostAsJsonAsync($"{Root}/catalog-additions", body);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            await AssertNoCatalogRecordsAsync();
            await VerifyAsync(async db =>
            {
                (await db.OrganizationCatalogEntries.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
                (await db.OrganizationCatalogAdditions.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
                (await db.ComponentCapabilityLinks.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
            });
            await AssertNoSystemWritesAsync();
        }
        finally
        {
            await VerifyAsync(db => db.Database.ExecuteSqlRawAsync("DROP TRIGGER CatalogTestRollback;"));
        }
    }

    [Theory]
    [InlineData("capability", false)]
    [InlineData("capability", true)]
    [InlineData("component", false)]
    public async Task LocalAddition_NoSystems_RoundTripsAndReplays(string recordType, bool withComponents)
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        var body = NewBody(recordType);
        if (withComponents)
        {
            var existing = await SeedLocalComponentAsync();
            body["components"] = new[] { new { source = "local", recordId = existing } };
            body["newComponents"] = new[] { ComponentBody("Staged component") };
        }

        // Act
        var access = await client.GetAsync($"{Root}/catalog-access");
        var first = await client.PostAsJsonAsync($"{Root}/catalog-additions", body);
        var replay = await client.PostAsJsonAsync($"{Root}/catalog-additions", body);

        // Assert
        access.StatusCode.Should().Be(HttpStatusCode.OK, await access.Content.ReadAsStringAsync());
        (await DataAsync(access)).GetProperty("canManageCatalog").GetBoolean().Should().BeTrue();
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        var created = await DataAsync(first);
        created.GetProperty("existing").GetBoolean().Should().BeFalse();
        var replayed = await DataAsync(replay);
        replayed.GetProperty("existing").GetBoolean().Should().BeTrue();
        replayed.GetProperty("recordId").GetString().Should().Be(created.GetProperty("recordId").GetString());
        first.Headers.Location.Should().NotBeNull();
        var detailResponse = await client.GetAsync(first.Headers.Location);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK, await detailResponse.Content.ReadAsStringAsync());
        var detail = await DataAsync(detailResponse);
        var item = detail.GetProperty("capability");
        item.GetProperty("recordType").GetString().Should().Be(recordType);
        item.GetProperty("organizationContribution").GetString().Should().Be("Organization operating duty");
        item.GetProperty("organizationOwner").GetString().Should().Be("Catalog owner");
        item.GetProperty("systemCount").GetInt32().Should().Be(0);
        item.GetProperty("isSubscribed").GetBoolean().Should().BeFalse();
        item.GetProperty("isOrganizationAdopted").GetBoolean().Should().BeFalse();
        detail.GetProperty("responsibilities").GetArrayLength().Should().Be(0);
        detail.GetProperty("narrativeReviews").GetArrayLength().Should().Be(0);
        if (withComponents)
            detail.GetProperty("supportingComponents").GetArrayLength().Should().Be(2);
        var list = await client.GetAsync($"{Root}/capabilities?source=local&grouping={recordType}");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        (await DataAsync(list)).GetProperty("items").EnumerateArray().Should()
            .ContainSingle(x => x.GetProperty("recordId").GetString() == created.GetProperty("recordId").GetString());
        await VerifyAsync(async db =>
        {
            (await db.RegisteredSystems.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(0);
            (await db.SecurityCapabilities.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId))
                .Should().Be(recordType == "capability" ? 1 : 0);
            (await db.SystemComponents.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId))
                .Should().Be(withComponents ? 2 : recordType == "component" ? 1 : 0);
            (await db.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId))
                .Should().Be(withComponents ? 2 : 0);
            (await db.SystemComponents.IgnoreQueryFilters()
                .AnyAsync(x => x.TenantId == _tenantId && x.RegisteredSystemId != null)).Should().BeFalse();
        });
        await AssertNoSystemWritesAsync();
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("component")]
    public async Task ProviderAdoption_RetainsProvenanceAndSupplementalComponentsWithoutProviderWrites(string recordType)
    {
        // Arrange
        await SeedMemberAsync();
        var (componentId, capabilityId) = await SeedProviderAsync();
        var supplement = await SeedLocalComponentAsync();
        var recordId = recordType == "capability" ? capabilityId : componentId;
        using var client = Client();
        var body = NewBody(recordType);
        body.Remove(recordType);
        body["source"] = "provider";
        body["recordId"] = recordId.ToString("D").ToUpperInvariant();
        if (recordType == "capability")
            body["components"] = new[] { new { source = "local", recordId = supplement } };

        // Act
        var response = await client.PostAsJsonAsync($"{Root}/catalog-additions", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await DataAsync(response)).GetProperty("recordId").GetString().Should().Be(recordId.ToString("D"));
        var detail = await DataAsync(await client.GetAsync(response.Headers.Location));
        var item = detail.GetProperty("capability");
        item.GetProperty("isOrganizationAdopted").GetBoolean().Should().BeTrue();
        item.GetProperty("mutationAuthority").GetString().Should().Be("provider");
        item.GetProperty("organizationContribution").GetString().Should().Be("Organization operating duty");
        if (recordType == "capability")
        {
            detail.GetProperty("supportingComponents").EnumerateArray().Should().HaveCount(2)
                .And.Contain(x => x.GetProperty("source").GetString() == "provider"
                    && x.GetProperty("id").GetString() == componentId.ToString("D"))
                .And.Contain(x => x.GetProperty("source").GetString() == "local"
                    && x.GetProperty("id").GetString() == supplement);
            detail.GetProperty("controlCoverage").EnumerateArray().Should().OnlyContain(x =>
                x.GetProperty("designation").GetString() == "Undesignated"
                && x.GetProperty("systemId").ValueKind == JsonValueKind.Null);
        }
        await VerifyAsync(async db =>
        {
            (await db.CspInheritedComponents.SingleAsync(x => x.Id == componentId)).Name.Should().Be("Provider component");
            (await db.CspInheritedCapabilities.SingleAsync(x => x.Id == capabilityId)).Name.Should().Be("Provider capability");
            (await db.SecurityCapabilities.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(0);
            (await db.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(0);
        });
        await AssertNoSystemWritesAsync();
    }

    [Fact]
    public async Task ExistingLocal_UpdatesContributionWithoutReplayingOldIntentOrChangingSystemViews()
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        var firstBody = NewBody("capability");
        var created = await client.PostAsJsonAsync($"{Root}/catalog-additions", firstBody);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await DataAsync(created)).GetProperty("recordId").GetString()!;
        var edit = NewBody("capability");
        edit.Remove("capability");
        edit["recordId"] = id;
        edit["organizationContribution"] = "Revised organization duty";
        edit["owner"] = "New accountable owner";

        // Act
        var updated = await client.PostAsJsonAsync($"{Root}/catalog-additions", edit);
        var oldReplay = await client.PostAsJsonAsync($"{Root}/catalog-additions", firstBody);
        edit["organizationContribution"] = "Changed immutable intent";
        var conflict = await client.PostAsJsonAsync($"{Root}/catalog-additions", edit);

        // Assert
        updated.StatusCode.Should().Be(HttpStatusCode.Created, await updated.Content.ReadAsStringAsync());
        oldReplay.StatusCode.Should().Be(HttpStatusCode.OK);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict, await conflict.Content.ReadAsStringAsync());
        var detail = await DataAsync(await client.GetAsync(created.Headers.Location));
        detail.GetProperty("capability").GetProperty("organizationContribution").GetString()
            .Should().Be("Revised organization duty");
        detail.GetProperty("capability").GetProperty("organizationOwner").GetString()
            .Should().Be("New accountable owner");
        await VerifyAsync(async db =>
            (await db.SecurityCapabilities.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(1));
        await AssertNoSystemWritesAsync();
    }

    [Theory]
    [InlineData("systemId", "made-up-system")]
    [InlineData("subscribe", false)]
    [InlineData("SystemId", null)]
    public async Task Addition_RejectsSystemFieldsEvenWhenEmptyOrFalse(string field, object? value)
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        var body = NewBody("capability");
        body[field] = value;

        // Act
        var response = await client.PostAsJsonAsync($"{Root}/catalog-additions", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        await AssertNoCatalogRecordsAsync();
    }

    [Theory]
    [InlineData("contribution")]
    [InlineData("owner")]
    [InlineData("component-type")]
    [InlineData("status")]
    public async Task Addition_RejectsInvalidAuthoredFieldsWithoutWrites(string invalid)
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        var body = NewBody(invalid == "component-type" ? "component" : "capability");
        if (invalid == "contribution") body["organizationContribution"] = new string('x', 2001);
        if (invalid == "owner") body["owner"] = new string('x', 201);
        if (invalid == "component-type") body["component"] = new { name = "Bad", componentType = "999", description = "Bad", owner = "Owner" };
        if (invalid == "status") body["capability"] = new { name = "Bad", provider = "Local", category = "AC", description = "Bad", implementationStatus = "999", owner = "Owner" };

        // Act
        var response = await client.PostAsJsonAsync($"{Root}/catalog-additions", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        await AssertNoCatalogRecordsAsync();
    }

    [Theory]
    [InlineData("foreign-component")]
    [InlineData("system-component")]
    [InlineData("foreign-capability")]
    [InlineData("draft-provider")]
    [InlineData("unmapped-provider")]
    public async Task Addition_RejectsIneligibleReferencesAtomically(string invalid)
    {
        // Arrange
        await SeedMemberAsync();
        using var client = Client();
        var body = NewBody("capability");
        body["newComponents"] = new[] { ComponentBody("Must not persist") };
        if (invalid is "foreign-component" or "system-component")
        {
            var component = await SeedLocalComponentAsync(invalid == "foreign-component", invalid == "system-component");
            body["components"] = new[] { new { source = "local", recordId = component } };
        }
        else if (invalid == "foreign-capability")
        {
            body.Remove("capability");
            var capability = new SecurityCapability
            {
                TenantId = WorkspaceMembershipFactory.TenantBId, Name = "Foreign", Provider = "Local",
                Category = "AC", Description = "Foreign", Owner = "Owner", CreatedBy = "test"
            };
            await VerifyAsync(async db => { db.SecurityCapabilities.Add(capability); await db.SaveChangesAsync(); });
            body["recordId"] = capability.Id;
        }
        else
        {
            var (_, capId) = await SeedProviderAsync(invalid == "draft-provider", invalid == "unmapped-provider");
            body.Remove("capability");
            body["source"] = "provider";
            body["recordId"] = capId.ToString();
        }

        // Act
        var response = await client.PostAsJsonAsync($"{Root}/catalog-additions", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        await VerifyAsync(async db =>
        {
            (await db.SecurityCapabilities.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
            (await db.SystemComponents.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId && x.Name == "Must not persist")).Should().BeFalse();
        });
        await AssertNoSystemWritesAsync();
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("system-manager")]
    [InlineData("revoked-role")]
    [InlineData("revoked-membership")]
    [InlineData("cross-tenant")]
    [InlineData("csp-admin")]
    public async Task CatalogAccessAndWrites_UseOrganizationAuthority(string scenario)
    {
        // Arrange
        await SeedMemberAsync(scenario is "revoked-role" or "revoked-membership" or "cross-tenant");
        using var client = Client();
        if (scenario == "system-manager")
        {
            await VerifyAsync(async db =>
            {
                var system = new RegisteredSystem { TenantId = _tenantId, Name = "Assigned", CreatedBy = "test" };
                db.RegisteredSystems.Add(system);
                db.SystemRoleAssignments.Add(new SystemRoleAssignment
                {
                    TenantId = _tenantId, PersonId = _personId, RegisteredSystemId = system.Id, Role = OrganizationRole.Issm
                });
                await db.SaveChangesAsync();
            });
        }
        if (scenario == "revoked-role")
            await VerifyAsync(async db =>
            {
                (await db.OrganizationRoleAssignments.IgnoreQueryFilters().SingleAsync(x => x.PersonId == _personId)).RemovedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            });
        if (scenario == "revoked-membership")
            await VerifyAsync(async db =>
            {
                (await db.OrganizationMemberships.SingleAsync(x => x.PersonId == _personId)).RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            });
        if (scenario == "csp-admin")
        {
            client.DefaultRequestHeaders.Remove("X-Workspace-Tenant-Id");
            client.DefaultRequestHeaders.Remove("X-Workspace-Kind");
            client.DefaultRequestHeaders.Add("X-Workspace-Kind", "csp");
            client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        }
        var root = scenario == "cross-tenant"
            ? $"/api/workspaces/organizations/{WorkspaceMembershipFactory.TenantBId}" : Root;

        // Act
        var access = await client.GetAsync($"{root}/catalog-access");
        var write = await client.PostAsJsonAsync($"{root}/catalog-additions", NewBody("capability"));

        // Assert
        write.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        if (scenario is "reader" or "system-manager" or "revoked-role")
        {
            access.StatusCode.Should().Be(HttpStatusCode.OK, await access.Content.ReadAsStringAsync());
            (await DataAsync(access)).GetProperty("canManageCatalog").GetBoolean().Should().BeFalse();
            var list = await client.GetAsync($"{Root}/capabilities");
            list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        }
        else access.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        await AssertNoCatalogRecordsAsync();
    }

    [Fact]
    public async Task ConcurrentSameIntent_CreatesOneAtomicAddition()
    {
        // Arrange
        await SeedMemberAsync();
        using var first = Client();
        using var second = Client();
        var body = NewBody("capability");
        body["newComponents"] = new[] { ComponentBody("One component") };

        // Act
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync($"{Root}/catalog-additions", body),
            second.PostAsJsonAsync($"{Root}/catalog-additions", body));

        // Assert
        responses.Select(x => x.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.OK });
        var ids = await Task.WhenAll(responses.Select(async x => (await DataAsync(x)).GetProperty("recordId").GetString()));
        ids.Distinct().Should().ContainSingle();
        await VerifyAsync(async db =>
        {
            (await db.SecurityCapabilities.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(1);
            (await db.SystemComponents.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(1);
            (await db.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenantId)).Should().Be(1);
        });
        await AssertNoSystemWritesAsync();
    }

    private static Dictionary<string, object?> NewBody(string recordType) => new()
    {
        ["idempotencyKey"] = Guid.NewGuid().ToString("N"), ["source"] = "local", ["recordType"] = recordType,
        [recordType] = recordType == "component" ? ComponentBody("Standalone component") : new
        {
            name = "Local capability", provider = "Organization", category = "AC",
            description = "Reusable capability", implementationStatus = "Planned", owner = "Capability owner"
        },
        ["components"] = Array.Empty<object>(), ["newComponents"] = Array.Empty<object>(),
        ["organizationContribution"] = "Organization operating duty", ["owner"] = "Catalog owner"
    };

    private static object ComponentBody(string name) =>
        new { name, componentType = "Thing", description = "Reusable component", owner = "Component owner" };

    private async Task SeedMemberAsync(bool administrator = true)
    {
        await VerifyAsync(async db =>
        {
            db.Database.IsRelational().Should().BeTrue();
            db.Tenants.Add(new Tenant
            {
                Id = _tenantId, DisplayName = $"Catalog {_tenantId:N}",
                Status = TenantStatus.Active, OnboardingState = OnboardingState.Active
            });
            var person = new Person { TenantId = _tenantId, DisplayName = "Catalog member", Email = $"{_actorId:N}@example.invalid" };
            _personId = person.Id;
            db.Persons.Add(person);
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = _tenantId, PersonId = person.Id, DirectoryTenantId = _directoryId,
                ObjectId = _actorId, GrantedBy = "test"
            });
            if (administrator)
                db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
                {
                    TenantId = _tenantId, PersonId = person.Id, Role = OrganizationRole.Administrator
                });
            await db.SaveChangesAsync();
        });
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", _directoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", _actorId.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", _tenantId.ToString());
        return client;
    }

    private async Task<string> SeedManageableSystemAsync()
    {
        var system = new RegisteredSystem { TenantId = _tenantId, Name = "Setup target", CreatedBy = "test" };
        await VerifyAsync(async db =>
        {
            db.RegisteredSystems.Add(system);
            db.SystemRoleAssignments.Add(new SystemRoleAssignment
            {
                TenantId = _tenantId, PersonId = _personId, RegisteredSystemId = system.Id, Role = OrganizationRole.Issm
            });
            await db.SaveChangesAsync();
        });
        return system.Id;
    }

    private async Task<string> SeedLocalComponentAsync(bool foreign = false, bool systemBound = false)
    {
        var component = new SystemComponent
        {
            TenantId = foreign ? WorkspaceMembershipFactory.TenantBId : _tenantId,
            Name = "Existing component", ComponentType = ComponentType.Thing, CreatedBy = "test"
        };
        await VerifyAsync(async db =>
        {
            if (systemBound)
            {
                var system = new RegisteredSystem { TenantId = _tenantId, Name = "Unrelated system", CreatedBy = "test" };
                db.RegisteredSystems.Add(system);
                component.RegisteredSystemId = system.Id;
            }
            db.SystemComponents.Add(component);
            await db.SaveChangesAsync();
        });
        return component.Id;
    }

    private async Task<(Guid ComponentId, Guid CapabilityId)> SeedProviderAsync(bool draft = false, bool unmapped = false)
    {
        var componentId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        await VerifyAsync(async db =>
        {
            var profileId = await db.CspProfiles.Select(x => x.Id).FirstAsync();
            db.CspInheritedComponents.Add(new CspInheritedComponent
            {
                Id = componentId, CspProfileId = profileId, Name = "Provider component",
                Description = "Provider source", Status = draft ? CspInheritedComponentStatus.Draft : CspInheritedComponentStatus.Published
            });
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = componentId, Name = "Provider capability",
                Description = "Provider source", MappedNistControlIds = ["AC-2"],
                Status = unmapped ? CspInheritedCapabilityStatus.NeedsReview : CspInheritedCapabilityStatus.Mapped
            });
            await db.SaveChangesAsync();
        });
        return (componentId, capabilityId);
    }

    private async Task AssertNoCatalogRecordsAsync() => await VerifyAsync(async db =>
    {
        (await db.SecurityCapabilities.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
        (await db.SystemComponents.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
    });

    private async Task AssertNoSystemWritesAsync() => await VerifyAsync(async db =>
    {
        (await db.SystemCapabilityLinks.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
        (await db.CapabilitySubscriptions.IgnoreQueryFilters().AnyAsync(x => x.RoutingTenantId == _tenantId)).Should().BeFalse();
        (await db.CapabilitySetupOperations.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
        (await db.Set<CapabilityResponsibilityConfirmation>().IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
        (await db.NarrativeProposals.IgnoreQueryFilters().AnyAsync(x => x.TenantId == _tenantId)).Should().BeFalse();
    });

    private async Task VerifyAsync(Func<AtoCopilotContext, Task> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AtoCopilotContext>());
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
}
