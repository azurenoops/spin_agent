using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Ato.Copilot.Chat.Services.Auth;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Chat;

public class ChatCacAuthenticationParityTests
{
    private const string Issuer = "https://issuer.example.mil/";
    private const string Audience = "api://ato-copilot-chat";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("issue-838-test-signing-key-must-be-at-least-32-bytes"));

    [Fact]
    public async Task PasswordOnlyToken_IsRejectedByChatAndMcp()
    {
        // Arrange
        var token = CreateToken([new Claim("amr", "pwd")]);

        // Act
        var chatStatus = await SendToChatAsync(token);
        var mcpStatus = await SendToMcpAsync(token);

        // Assert
        chatStatus.Should().Be(HttpStatusCode.Unauthorized);
        mcpStatus.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task CacToken_IsAcceptedByChat()
    {
        // Arrange
        var token = CreateToken([
            new Claim("amr", "mfa"),
            new Claim("amr", "rsa")
        ]);

        // Act
        var status = await SendToChatAsync(token);

        // Assert
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PasswordOnlyToken_IsAcceptedByChat_WhenCacIsNotRequired()
    {
        // Arrange
        var token = CreateToken([new Claim("amr", "pwd")]);

        // Act
        var status = await SendToChatAsync(token, requireCac: false);

        // Assert
        status.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<HttpStatusCode> SendToChatAsync(
        string token,
        bool requireCac = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = SigningKey
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = requireCac
                        ? ChatCacTokenValidator.ValidateAsync
                        : _ => Task.CompletedTask
                };
            });
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/api/conversations", () => Results.Ok()).RequireAuthorization();
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/conversations");
        await app.StopAsync();
        return response.StatusCode;
    }

    private static async Task<int> SendToMcpAsync(string token)
    {
        var middleware = new CacAuthenticationMiddleware(
            _ => Task.CompletedTask,
            Options.Create(new AzureAdOptions { RequireCac = true }),
            Options.Create(new CacAuthOptions()),
            Options.Create(new RoleClaimMappingsOptions()),
            new TestHostEnvironment(),
            NullLogger<CacAuthenticationMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers.Authorization = $"Bearer {token}";

        await middleware.InvokeAsync(context);

        return context.Response.StatusCode;
    }

    private static string CreateToken(IEnumerable<Claim> authenticationMethodClaims)
    {
        var claims = new List<Claim>
        {
            new("sub", "issue-838-user"),
            new("oid", "issue-838-user")
        };
        claims.AddRange(authenticationMethodClaims);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = nameof(ChatCacAuthenticationParityTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}