using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;
using Ato.Copilot.Chat.Data;
using Ato.Copilot.Chat.Hubs;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Chat.Services;
using Ato.Copilot.Chat.Services.Auth;
using Ato.Copilot.Core.Interfaces;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Integration.Chat;

/// <summary>
/// Integration tests for conversation endpoints (US2).
/// Uses TestServer, validated synthetic JWTs, and authoritative workspace responses.
/// </summary>
public class ConversationsControllerIntegrationTests : IAsyncLifetime
{
    private const string Issuer = "https://chat-integration.invalid/issuer";
    private const string Audience = "chat-integration";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes(
        "synthetic-chat-integration-signing-key-not-for-production"));
    private static readonly Guid DirectoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherDirectoryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ObjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherObjectId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TenantId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PersonId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly ConcurrentQueue<Type> _databaseAccesses = new();
    private readonly ConcurrentQueue<string> _workspaceTokens = new();
    private readonly ConcurrentQueue<string> _chatRequests = new();
    private HttpStatusCode _workspaceStatus = HttpStatusCode.OK;
    private Guid? _upstreamDirectoryId;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        var dbName = $"ChatIntegration_Conversations_{Guid.NewGuid():N}";

        var dbOptions = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        builder.Services.AddScoped<ChatDbContext>(_ =>
            new ObservedChatDbContext(dbOptions, type => _databaseAccesses.Enqueue(type)));
        builder.Services.AddSingleton<IPathSanitizationService, PathSanitizationService>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ChatWorkspaceResolver>();
        builder.Services.AddScoped<IChatService, ChatService>();
        builder.Services.AddHttpClient("McpServer", client =>
        {
            client.BaseAddress = new Uri("https://mcp-integration.invalid");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(() => new StubHandler(request =>
        {
            _chatRequests.Enqueue(request.RequestUri!.AbsolutePath);
            throw new InvalidOperationException("Conversation tests must not forward chat history.");
        }));
        builder.Services.AddHttpClient("McpWorkspace", client =>
            client.BaseAddress = new Uri("https://mcp-integration.invalid"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(ValidateWorkspace));
        builder.Services.AddControllers()
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            })
            .AddApplicationPart(typeof(Ato.Copilot.Chat.Controllers.ConversationsController).Assembly);
        builder.Services.AddSignalR();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.SaveToken = true;
                options.TokenValidationParameters = ValidationParameters();
            });
        builder.Services.AddAuthorization();

        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseCors();
        _app.UseRouting();
        // UseAuthentication + UseAuthorization MUST be ordered between UseRouting()
        // and endpoint mapping (MapControllers). Without this, any endpoint with
        // [Authorize] metadata raises InvalidOperationException at runtime.
        _app.UseAuthentication();
        _app.UseAuthorization();
        // Match the production translation without replacing resolver or service validation.
        _app.Use(async (http, next) =>
        {
            try { await next(http); }
            catch (ChatWorkspaceException ex) when (!http.Response.HasStarted)
            {
                http.Response.StatusCode = ex.StatusCode;
                await http.Response.WriteAsJsonAsync(new ErrorResponse
                {
                    Error = ex.Code,
                    Message = ex.Message
                }, http.RequestAborted);
            }
        });
        _app.MapControllers();
        _app.MapHub<ChatHub>("/hubs/chat");

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(DirectoryId, ObjectId));
        _client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        _client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        _client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", TenantId.ToString("D"));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ─── Happy Path Tests ────────────────────────────────────────

    [Fact]
    public async Task CreateConversation_ReturnsConversationWithId()
    {
        // Arrange
        var request = new CreateConversationRequest { Title = "Integration Test Conv", UserId = "test-user" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/conversations", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var conversation = JsonSerializer.Deserialize<Conversation>(content, _jsonOptions);
        conversation.Should().NotBeNull();
        conversation!.Id.Should().NotBeNullOrEmpty();
        conversation.Title.Should().Be("Integration Test Conv");
        conversation.UserId.Should().Be($"{DirectoryId:D}/{ObjectId:D}");
        content.Should().NotContain("ownerKey");
        using var scope = _app.Services.CreateScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<ChatDbContext>()
            .Conversations.SingleAsync(c => c.Id == conversation.Id);
        persisted.OwnerKey.Should().Be(new ChatWorkspace(DirectoryId, ObjectId,
            "organization", TenantId, "ordinary", PersonId, "", null).OwnerKey);
        persisted.OwnerKey.Should().NotBe(request.UserId);
        _workspaceTokens.Should().ContainSingle()
            .Which.Should().Be(_client.DefaultRequestHeaders.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetConversations_ReturnsCreatedConversations()
    {
        // Arrange
        // Body and query user IDs cannot select the persisted workspace owner.
        await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "Conv 1", UserId = "test-user" }, _jsonOptions);
        await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "Conv 2", UserId = "test-user" }, _jsonOptions);

        // Act — userId query param is ignored post-DEF-001; identity comes from claims.
        var response = await _client.GetAsync("/api/conversations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var conversations = JsonSerializer.Deserialize<List<Conversation>>(content, _jsonOptions);
        conversations.Should().NotBeNull();
        conversations!.Should().HaveCount(2);
        conversations.Should().OnlyContain(c => c.UserId == $"{DirectoryId:D}/{ObjectId:D}");
    }

    [Fact]
    public async Task GetConversationById_ReturnsConversation()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "Detail Conv", UserId = "test-user" }, _jsonOptions);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<Conversation>(createContent, _jsonOptions)!;

        // Act
        var response = await _client.GetAsync($"/api/conversations/{created.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var conversation = JsonSerializer.Deserialize<Conversation>(content, _jsonOptions);
        conversation.Should().NotBeNull();
        conversation!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task DeleteConversation_ThenGetReturns404()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "To Delete", UserId = "test-user" }, _jsonOptions);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<Conversation>(createContent, _jsonOptions)!;

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/conversations/{created.Id}");
        var getResponse = await _client.GetAsync($"/api/conversations/{created.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Error Path Tests ────────────────────────────────────────

    [Fact]
    public async Task DeleteConversation_WithNonExistentId_Returns404()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/conversations/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetConversation_WithNonExistentId_Returns404()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/conversations/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Search Tests ────────────────────────────────────────────

    [Fact]
    public async Task SearchConversations_ReturnsMatchingResults()
    {
        // Arrange
        await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "Compliance Review", UserId = "test-user" }, _jsonOptions);
        await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "General Chat", UserId = "test-user" }, _jsonOptions);

        // Act — userId query param is ignored post-DEF-001; identity comes from claims.
        var response = await _client.GetAsync("/api/conversations/search?query=compliance");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var conversations = JsonSerializer.Deserialize<List<Conversation>>(content, _jsonOptions);
        conversations.Should().NotBeNull();
        conversations!.Should().HaveCount(1);
        conversations.First().Title.Should().Contain("Compliance");
    }

    [Fact]
    public async Task SearchConversations_WithEmptyQuery_Returns400()
    {
        // Arrange
        const string url = "/api/conversations/search?query=&userId=test-user";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("object")]
    public async Task DifferentQualifiedActor_CannotReadSearchDeleteOrSendToForgedConversation(string dimension)
    {
        // Arrange
        var created = await CreateWithHistoryAsync();
        var directory = dimension == "directory" ? OtherDirectoryId : DirectoryId;
        var subject = dimension == "object" ? OtherObjectId : ObjectId;
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(directory, subject));
        _databaseAccesses.Clear();
        var forgedUser = Uri.EscapeDataString(created.UserId);

        // Act
        using var list = await _client.GetAsync($"/api/conversations?userId={forgedUser}");
        using var search = await _client.GetAsync($"/api/conversations/search?query=private&userId={forgedUser}");
        using var read = await _client.GetAsync($"/api/conversations/{created.Id}?userId={forgedUser}");
        using var messages = await _client.GetAsync($"/api/messages?conversationId={created.Id}&userId={forgedUser}");
        using var send = await _client.PostAsJsonAsync("/api/messages", new SendMessageRequest
        {
            ConversationId = created.Id,
            Message = "forged message",
            Context = new() { ["userId"] = created.UserId }
        }, _jsonOptions);
        using var delete = await _client.DeleteAsync($"/api/conversations/{created.Id}?userId={forgedUser}");

        // Assert
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<List<Conversation>>(_jsonOptions)).Should().BeEmpty();
        search.StatusCode.Should().Be(HttpStatusCode.OK);
        (await search.Content.ReadFromJsonAsync<List<Conversation>>(_jsonOptions)).Should().BeEmpty();
        foreach (var response in new[] { read, messages, send, delete })
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _databaseAccesses.Should().NotContain(typeof(ChatMessage));
        _chatRequests.Should().BeEmpty();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(DirectoryId, ObjectId));
        using var restored = await _client.GetAsync($"/api/conversations/{created.Id}");
        restored.StatusCode.Should().Be(HttpStatusCode.OK);
        var original = await restored.Content.ReadFromJsonAsync<Conversation>(_jsonOptions);
        original!.Messages.Should().ContainSingle().Which.Content.Should().Be("private history");
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("signature")]
    [InlineData("missing")]
    [InlineData("malformed")]
    public async Task InvalidBearer_DeniesBeforeWorkspaceDatabaseOrHistory(string invalid)
    {
        // Arrange
        var created = await CreateWithHistoryAsync();
        _databaseAccesses.Clear();
        _workspaceTokens.Clear();
        _client.DefaultRequestHeaders.Authorization = invalid == "missing" ? null
            : new AuthenticationHeaderValue("Bearer",
                invalid == "malformed" ? "not-a-jwt" : Token(DirectoryId, ObjectId, invalid));

        // Act
        using var create = await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "denied", UserId = created.UserId }, _jsonOptions);
        using var read = await _client.GetAsync($"/api/conversations/{created.Id}");
        using var send = await _client.PostAsJsonAsync("/api/messages",
            new SendMessageRequest { ConversationId = created.Id, Message = "denied" }, _jsonOptions);

        // Assert
        foreach (var response in new[] { create, read, send })
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _workspaceTokens.Should().BeEmpty();
        _databaseAccesses.Should().BeEmpty();
        _chatRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task UpstreamRejectsPreviouslyValidWorkspace_DeniesBeforeDatabaseOrHistory(HttpStatusCode status)
    {
        // Arrange
        var created = await CreateWithHistoryAsync();
        _workspaceStatus = status;
        _databaseAccesses.Clear();
        _workspaceTokens.Clear();

        // Act
        using var create = await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "denied", UserId = created.UserId }, _jsonOptions);
        using var list = await _client.GetAsync("/api/conversations");
        using var search = await _client.GetAsync("/api/conversations/search?query=private");
        using var read = await _client.GetAsync($"/api/conversations/{created.Id}");
        using var messages = await _client.GetAsync($"/api/messages?conversationId={created.Id}");
        using var send = await _client.PostAsJsonAsync("/api/messages",
            new SendMessageRequest { ConversationId = created.Id, Message = "denied" }, _jsonOptions);
        using var delete = await _client.DeleteAsync($"/api/conversations/{created.Id}");

        // Assert
        foreach (var response in new[] { create, list, search, read, messages, send, delete })
        {
            response.StatusCode.Should().Be(status);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>(_jsonOptions))!
                .Error.Should().Be("WORKSPACE_VALIDATION_FAILED");
        }
        _workspaceTokens.Should().HaveCount(7);
        _databaseAccesses.Should().BeEmpty();
        _chatRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task UpstreamSameOidFromDifferentDirectory_DeniesBeforeDatabaseOrHistory()
    {
        // Arrange
        var created = await CreateWithHistoryAsync();
        _upstreamDirectoryId = OtherDirectoryId;
        _databaseAccesses.Clear();
        _workspaceTokens.Clear();

        // Act
        using var create = await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "denied", UserId = created.UserId }, _jsonOptions);
        using var list = await _client.GetAsync("/api/conversations");
        using var search = await _client.GetAsync("/api/conversations/search?query=private");
        using var read = await _client.GetAsync($"/api/conversations/{created.Id}");
        using var messages = await _client.GetAsync($"/api/messages?conversationId={created.Id}");
        using var send = await _client.PostAsJsonAsync("/api/messages",
            new SendMessageRequest { ConversationId = created.Id, Message = "denied" }, _jsonOptions);
        using var delete = await _client.DeleteAsync($"/api/conversations/{created.Id}");

        // Assert
        foreach (var response in new[] { create, list, search, read, messages, send, delete })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>(_jsonOptions))!
                .Error.Should().Be("WORKSPACE_IDENTITY_MISMATCH");
        }
        _workspaceTokens.Should().HaveCount(7)
            .And.OnlyContain(token => token == _client.DefaultRequestHeaders.Authorization!.Parameter);
        _databaseAccesses.Should().BeEmpty();
        _chatRequests.Should().BeEmpty();
    }

    private async Task<Conversation> CreateWithHistoryAsync()
    {
        using var response = await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "private title", UserId = "forged-owner" }, _jsonOptions);
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<Conversation>(_jsonOptions))!;
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        db.Messages.Add(new ChatMessage { ConversationId = created.Id, Content = "private history" });
        await db.SaveChangesAsync();
        return created;
    }

    private HttpResponseMessage ValidateWorkspace(HttpRequestMessage request)
    {
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.AbsolutePath.Should().Be("/api/auth/me");
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        var token = request.Headers.Authorization.Parameter!;
        _workspaceTokens.Enqueue(token);
        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }
            .ValidateToken(token, ValidationParameters(), out _);
        var directory = Guid.Parse(principal.FindFirst("tid")!.Value);
        var subject = Guid.Parse(principal.FindFirst("oid")!.Value);
        var member = (directory == DirectoryId && (subject == ObjectId || subject == OtherObjectId))
            || (directory == OtherDirectoryId && subject == ObjectId);
        request.Headers.GetValues("X-Workspace-Kind").Should().ContainSingle().Which.Should().Be("organization");
        request.Headers.GetValues("X-Workspace-Mode").Should().ContainSingle().Which.Should().Be("ordinary");
        request.Headers.GetValues("X-Workspace-Tenant-Id").Should().ContainSingle().Which.Should().Be(TenantId.ToString("D"));
        if (!member || _workspaceStatus != HttpStatusCode.OK)
            return new HttpResponseMessage(member ? _workspaceStatus : HttpStatusCode.Forbidden);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                status = "success",
                data = new
                {
                    oid = subject,
                    directoryTenantId = _upstreamDirectoryId ?? directory,
                    workspace = new
                    {
                        kind = "organization", mode = "ordinary", tenantId = TenantId,
                        personId = PersonId, roles = new[] { "ISSO" }, permissions = new { canRead = true }
                    }
                }
            })
        };
    }

    private static TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true, ValidIssuer = Issuer,
        ValidateAudience = true, ValidAudience = Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = SigningKey,
        RequireSignedTokens = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateLifetime = true, ClockSkew = TimeSpan.Zero
    };

    private static string Token(Guid directory, Guid subject, string? invalid = null)
    {
        var key = invalid == "signature"
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes("wrong-synthetic-chat-integration-signature-key"))
            : SigningKey;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: invalid == "issuer" ? "https://wrong.invalid" : Issuer,
            audience: invalid == "audience" ? "wrong-audience" : Audience,
            claims: [new Claim("tid", directory.ToString("D")), new Claim("oid", subject.ToString("D"))],
            notBefore: DateTime.UtcNow.AddMinutes(-20),
            expires: invalid == "expired" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ObservedChatDbContext(DbContextOptions<ChatDbContext> options, Action<Type> accessed)
        : ChatDbContext(options)
    {
        public override DbSet<TEntity> Set<TEntity>()
        {
            accessed(typeof(TEntity));
            return base.Set<TEntity>();
        }
    }
}

/// <summary>
/// Minimal authentication handler for integration tests.
/// Authenticates every request as an anonymous authenticated principal so
/// endpoints carrying [Authorize] don't throw the "middleware not found"
/// InvalidOperationException. No real identity provider is needed.
/// </summary>
internal sealed class TestPassThroughAuthHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
    Microsoft.Extensions.Logging.ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<
        Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization != $"Bearer {ChatControllerWorkspaceFixture.Token}")
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
        var claims = new[]
        {
            new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier, "test-user"),
            new System.Security.Claims.Claim("tid", ChatControllerWorkspaceFixture.DirectoryId),
            new System.Security.Claims.Claim("oid", ChatControllerWorkspaceFixture.ObjectId),
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, Scheme.Name);
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var properties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties();
        properties.StoreTokens([new Microsoft.AspNetCore.Authentication.AuthenticationToken
            { Name = "access_token", Value = ChatControllerWorkspaceFixture.Token }]);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, properties, Scheme.Name);
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
    }
}
