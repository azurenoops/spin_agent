using System.Net;
using Ato.Copilot.Chat.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Chat;

public class ChatUnconfiguredAuthenticationTests
{
    [Theory]
    [InlineData("/health", HttpStatusCode.OK, false)]
    [InlineData("/api/info", HttpStatusCode.OK, false)]
    [InlineData("/api/conversations", HttpStatusCode.Unauthorized, false)]
    [InlineData("/api/conversations", HttpStatusCode.Unauthorized, true)]
    public async Task DevelopmentWithoutEntra_PublicEndpointsWork_ProtectedEndpointsDeny(
        string path, HttpStatusCode expected, bool invalidToken)
    {
        // Arrange
        await using var factory = new ChatFactory();
        using var client = factory.CreateClient();
        if (invalidToken)
            client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-test-token");

        // Act
        var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(expected);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        options.RequireHttpsMetadata.Should().BeTrue();
        options.TokenValidationParameters.ValidateIssuer.Should().BeTrue();
        options.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        options.TokenValidationParameters.ValidateLifetime.Should().BeTrue();
        options.TokenValidationParameters.ValidateIssuerSigningKey.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("AzureAd:Instance")]
    [InlineData("AzureAd:TenantId")]
    [InlineData("AzureAd:ClientId")]
    public void Authority_IsOnlyConfiguredWhenAllRequiredSettingsArePresent(string? missingSetting)
    {
        // Arrange
        using var factory = new ChatFactory(configured: true, missingSetting);
        using var client = factory.CreateClient();

        // Act
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        // Assert
        options.Authority.Should().Be(missingSetting is null
            ? "https://login.microsoftonline.us/11111111-1111-1111-1111-111111111111/v2.0"
            : null);
        options.RequireHttpsMetadata.Should().BeTrue();
    }

    private sealed class ChatFactory(bool configured = false, string? missingSetting = null)
        : WebApplicationFactory<ChatService>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            var settings = new Dictionary<string, string>
            {
                ["ConnectionStrings:ChatDb"] = "Data Source=:memory:",
                ["AzureAd:Instance"] = configured ? "https://login.microsoftonline.us/" : "",
                ["AzureAd:TenantId"] = configured ? "11111111-1111-1111-1111-111111111111" : "",
                ["AzureAd:ClientId"] = configured ? "22222222-2222-2222-2222-222222222222" : "",
            };
            if (missingSetting is not null)
                settings[missingSetting] = "";
            // Program consumes these values before the deferred app-configuration callbacks.
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        }
    }
}
