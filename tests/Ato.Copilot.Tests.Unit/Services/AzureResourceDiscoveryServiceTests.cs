using Ato.Copilot.Agents.Compliance.Services;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for AzureResourceDiscoveryService static helpers and query building.
/// </summary>
public class AzureResourceDiscoveryServiceTests
{
    // ─── Query Building ──────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_BasicSubscription_ReturnsValidKql()
    {
        var query = AzureResourceDiscoveryService.BuildQuery("sub-123", null, null, null);

        query.Should().Contain("subscriptionId == 'sub-123'");
        query.Should().Contain("| project id, name, type, resourceGroup, location");
    }

    [Fact]
    public void BuildQuery_WithResourceGroupFilter_IncludesFilter()
    {
        var query = AzureResourceDiscoveryService.BuildQuery("sub-1", "rg-prod", null, null);

        query.Should().Contain("resourceGroup =~ 'rg-prod'");
    }

    [Fact]
    public void BuildQuery_WithResourceTypeFilter_IncludesFilter()
    {
        var query = AzureResourceDiscoveryService.BuildQuery("sub-1", null, "Microsoft.Compute/virtualMachines", null);

        query.Should().Contain("type =~ 'Microsoft.Compute/virtualMachines'");
    }

    [Fact]
    public void BuildQuery_WithSearchFilter_IncludesContains()
    {
        var query = AzureResourceDiscoveryService.BuildQuery("sub-1", null, null, "web-server");

        query.Should().Contain("name contains 'web-server'");
    }

    [Fact]
    public void BuildQuery_WithAllFilters_IncludesAll()
    {
        var query = AzureResourceDiscoveryService.BuildQuery("sub-1", "rg-dev", "Microsoft.Network/virtualNetworks", "vnet");

        query.Should().Contain("subscriptionId == 'sub-1'");
        query.Should().Contain("resourceGroup =~ 'rg-dev'");
        query.Should().Contain("type =~ 'Microsoft.Network/virtualNetworks'");
        query.Should().Contain("name contains 'vnet'");
    }

    // ─── KQL Injection Prevention ────────────────────────────────────────────

    [Fact]
    public void BuildQuery_EscapesSingleQuotes_PreventsInjection()
    {
        var query = AzureResourceDiscoveryService.BuildQuery("sub'; drop table--", null, null, null);

        query.Should().Contain("sub\\'; drop table--");
        query.Should().NotContain("sub'");
    }

    [Fact]
    public void EscapeKql_EscapesSingleQuotes()
    {
        AzureResourceDiscoveryService.EscapeKql("O'Brien").Should().Be("O\\'Brien");
    }

    [Fact]
    public void EscapeKql_PassthroughSafeStrings()
    {
        AzureResourceDiscoveryService.EscapeKql("safe-string-123").Should().Be("safe-string-123");
    }

    // ─── Resource Group Extraction ───────────────────────────────────────────

    [Fact]
    public void ExtractResourceGroup_StandardArmId_ReturnsGroupName()
    {
        var resourceId = "/subscriptions/sub-1/resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/vm-01";
        AzureResourceDiscoveryService.ExtractResourceGroup(resourceId).Should().Be("rg-prod");
    }

    [Fact]
    public void ExtractResourceGroup_NoResourceGroups_ReturnsEmpty()
    {
        AzureResourceDiscoveryService.ExtractResourceGroup("/subscriptions/sub-1").Should().BeEmpty();
    }

    [Fact]
    public void ExtractResourceGroup_CaseInsensitive_ReturnsGroup()
    {
        var resourceId = "/subscriptions/sub-1/RESOURCEGROUPS/My-RG/providers/Microsoft.Compute/vm";
        AzureResourceDiscoveryService.ExtractResourceGroup(resourceId).Should().Be("My-RG");
    }

    [Fact]
    public void ExtractResourceGroup_TrailingSegment_ReturnsGroupOnly()
    {
        var resourceId = "/subscriptions/sub-1/resourceGroups/rg-dev/providers/Microsoft.Network/nsg/nsg-01";
        AzureResourceDiscoveryService.ExtractResourceGroup(resourceId).Should().Be("rg-dev");
    }

    // ─── Dedup Logic ─────────────────────────────────────────────────────────

    [Fact]
    public void MaxPages_IsReasonable()
    {
        AzureResourceDiscoveryService.MaxPages.Should().Be(10);
    }
}
