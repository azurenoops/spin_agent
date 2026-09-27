using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderServiceScopeTests
{
    [Fact]
    public void LegacyAzureScope_RoundTripsWithoutChangingStoredFingerprint()
    {
        // Arrange
        const string json = """[{"cloud":"AzureCloud","directoryTenantId":"11111111-1111-4111-8111-111111111111","subscriptionId":"22222222-2222-4222-8222-222222222222","resourceId":"/subscriptions/22222222-2222-4222-8222-222222222222"}]""";

        // Act
        var scopes = ProviderAuthorizationStore.Read<ProviderScope[]>(json);

        // Assert
        scopes.Single().Should().BeOfType<ProviderAzureScope>();
        ProviderAuthorizationStore.Json(scopes).Should().Be(json);
        ProviderAuthorizationStore.Normalize(scopes.Single()).Should().BeOfType<ProviderAzureScope>();
    }

    [Fact]
    public void ServiceScope_HasExplicitKindAndNoAzureIdentifierFields()
    {
        // Arrange
        ProviderScope scope = new ProviderServiceScope("m365-collaboration", "Synthetic M365 collaboration",
            "Microsoft365DoD", "Synthetic service tenant");

        // Act
        var json = ProviderAuthorizationStore.Json(new[] { scope });
        var restored = ProviderAuthorizationStore.Read<ProviderScope[]>(json).Single();

        // Assert
        json.Should().Contain("\"kind\":\"Service\"");
        json.Should().NotContain("subscriptionId").And.NotContain("directoryTenantId").And.NotContain("resourceId");
        restored.Should().Be(scope);
    }

    [Fact]
    public void ServiceScope_RequiresExactServiceEnvironmentAndTenant_NotAnAzureOrPrefixMatch()
    {
        // Arrange
        var source = new ProviderServiceScope("m365-collaboration", "Synthetic M365 collaboration", "Microsoft365DoD", "tenant-a");
        var subscription = Guid.NewGuid();
        var azure = new ProviderAzureScope("AzureCloud", Guid.NewGuid(), subscription, $"/subscriptions/{subscription}");

        // Act / Assert
        ProviderAuthorizationStore.Contains(source, source with { ServiceName = "Reviewed display name" }).Should().BeTrue();
        ProviderAuthorizationStore.Contains(source, source with { ServiceId = "m365-collaboration/another" }).Should().BeFalse();
        ProviderAuthorizationStore.Contains(source, source with { TenantReference = "tenant-b" }).Should().BeFalse();
        ProviderAuthorizationStore.Contains(source, source with { Environment = "ManualService" }).Should().BeFalse();
        ProviderAuthorizationStore.Contains(source, azure).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"kind":"Service","serviceId":"m365","serviceName":"M365","environment":"Microsoft365DoD","subscriptionId":"22222222-2222-4222-8222-222222222222"}""")]
    [InlineData("""{"kind":"UnverifiedCloud","serviceId":"m365"}""")]
    [InlineData("""{"kind":"Service","kind":"Azure","serviceId":"m365"}""")]
    [InlineData("""{"cloud":"AzureCloud","directoryTenantId":"11111111111141118111111111111111","subscriptionId":"22222222-2222-4222-8222-222222222222","resourceId":"/subscriptions/22222222-2222-4222-8222-222222222222"}""")]
    public void ScopeContract_RejectsAmbiguousOrInvalidScopeMaterial(string json)
    {
        // Arrange / Act
        var parse = () => ProviderAuthorizationStore.Read<ProviderScope>(json);

        // Assert
        parse.Should().Throw<JsonException>();
    }
}
