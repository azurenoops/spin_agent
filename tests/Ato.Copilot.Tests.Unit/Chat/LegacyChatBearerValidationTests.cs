using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Ato.Copilot.Chat.Services.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Chat;

public class LegacyChatBearerValidationTests
{
    private const string Issuer = "https://chat-test.invalid/issuer";
    private const string Audience = "chat-test-audience";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes(
        "synthetic-test-key-for-chat-workspace-isolation-only"));

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("signature")]
    public async Task InvalidBearer_CannotReuseAuthenticatedPrincipalOrReachUpstream(string invalid)
    {
        // Arrange
        var claims = new[] { new Claim("tid", LegacyChatTestScope.Directory.ToString()),
            new Claim("oid", LegacyChatTestScope.Subject.ToString()) };
        var key = invalid == "signature"
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes("wrong-synthetic-signature-for-chat-test-only-key"))
            : Key;
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: invalid == "issuer" ? "https://wrong.invalid" : Issuer,
            audience: invalid == "audience" ? "wrong-audience" : Audience,
            claims: claims, notBefore: DateTime.UtcNow.AddMinutes(-20),
            expires: invalid == "expired" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
        var registrations = new ServiceCollection().AddLogging();
        registrations.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true, ValidIssuer = Issuer,
                ValidateAudience = true, ValidAudience = Audience,
                ValidateIssuerSigningKey = true, IssuerSigningKey = Key,
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero
            };
        });
        await using var services = registrations.BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "previous-handler"))
        };
        http.Request.Headers.Authorization = $"Bearer {token}";
        var clients = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var resolver = new ChatWorkspaceResolver(new HttpContextAccessor { HttpContext = http }, clients.Object);

        // Act
        var action = () => resolver.ResolveAsync();

        // Assert
        await action.Should().ThrowAsync<ChatWorkspaceException>().Where(e => e.StatusCode == 401);
        clients.VerifyNoOtherCalls();
    }
}
