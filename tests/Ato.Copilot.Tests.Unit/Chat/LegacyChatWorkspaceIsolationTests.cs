using Ato.Copilot.Chat.Data;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Chat.Services;
using Ato.Copilot.Core.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using System.Net;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Chat.Services.Auth;
using Microsoft.Data.Sqlite;
using System.Security.Claims;

namespace Ato.Copilot.Tests.Unit.Chat;

public class LegacyChatWorkspaceIsolationTests
{
    // /me must echo the complete validated (directory, object) identity before history is touched.
    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("zero")]
    [InlineData("malformed")]
    [InlineData("number")]
    [InlineData("different-directory-same-oid")]
    public async Task InvalidUpstreamDirectory_DeniesBeforeDatabaseHistoryOrForwarding(string invalid)
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        var data = new Dictionary<string, object?>
        {
            ["oid"] = scope.ObjectId,
            ["workspace"] = new
            {
                kind = scope.Kind, mode = scope.Mode, tenantId = scope.TenantId, personId = scope.PersonId,
                roles = new[] { "ISSO" }, permissions = new { canRead = true }
            }
        };
        if (invalid != "missing")
            data["directoryTenantId"] = invalid switch
            {
                "null" => null,
                "empty" => "",
                "zero" => Guid.Empty,
                "malformed" => "not-a-guid",
                "number" => 123,
                _ => Guid.Parse("99999999-9999-9999-9999-999999999999")
            };
        scope.ResponseOverride = JsonSerializer.Serialize(new { status = "success", data });
        using var db = Database();
        db.Dispose();
        using var upstream = new ChatHandler();
        var service = Service(db, scope, upstream);
        using var stream = new MemoryStream("data"u8.ToArray());
        Func<Task>[] operations =
        [
            () => service.CreateConversationAsync(new() { UserId = "default-user" }),
            () => service.GetConversationAsync("legacy"),
            () => service.GetConversationsAsync(),
            () => service.SearchConversationsAsync("secret"),
            () => service.GetMessagesAsync("legacy"),
            () => service.GetConversationHistoryAsync("legacy"),
            () => service.SendMessageAsync(new() { ConversationId = "legacy", Message = "test" }),
            () => service.CreateOrUpdateContextAsync(new() { ConversationId = "legacy" }),
            () => service.SaveAttachmentAsync("message", "file.txt", "text/plain", stream),
            () => service.DeleteConversationAsync("legacy")
        ];

        // Act
        foreach (var operation in operations)
        {
            // Assert
            await operation.Should().ThrowAsync<ChatWorkspaceException>()
                .Where(e => e.StatusCode == 403 && e.Code == "WORKSPACE_IDENTITY_MISMATCH");
        }
        scope.Requests.Should().HaveCount(operations.Length)
            .And.OnlyContain(request => request.Path == "/api/auth/me");
        upstream.Bodies.Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("error-envelope")]
    [InlineData("array-envelope")]
    [InlineData("malformed-person")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("missing-identity")]
    [InlineData("unknown-kind")]
    [InlineData("duplicate-kind")]
    [InlineData("empty-mode")]
    [InlineData("invalid-tenant")]
    [InlineData("mismatched-tenant")]
    [InlineData("csp-with-tenant")]
    [InlineData("support-without-cookie")]
    [InlineData("duplicate-bearer")]
    [InlineData("conflicting-query-token")]
    public async Task InvalidSelectorsOrAuthoritativeResponses_FailClosed(string invalid)
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        switch (invalid)
        {
            case "invalid-json": scope.ResponseOverride = "not json"; break;
            case "error-envelope": scope.ResponseOverride = "{\"status\":\"error\",\"data\":{}}"; break;
            case "array-envelope": scope.ResponseOverride = "[]"; break;
            case "malformed-person":
                scope.ResponseOverride = JsonSerializer.Serialize(new
                {
                    status = "success",
                    data = new
                    {
                        oid = scope.ObjectId,
                        directoryTenantId = scope.DirectoryId,
                        workspace = new { kind = scope.Kind, mode = scope.Mode, tenantId = scope.TenantId,
                            personId = "not-a-guid", roles = Array.Empty<string>(), permissions = new { } }
                    }
                });
                break;
            case "network": scope.UpstreamException = new HttpRequestException(); break;
            case "timeout": scope.UpstreamException = new TaskCanceledException(); break;
            case "missing-identity": scope.Http.User = new(new ClaimsIdentity(Array.Empty<Claim>(), "Bearer")); break;
            case "unknown-kind": scope.Http.Request.Headers["X-Workspace-Kind"] = "forged"; break;
            case "duplicate-kind": scope.Http.Request.Headers["X-Workspace-Kind"] = new[] { "organization", "csp" }; break;
            case "empty-mode": scope.Http.Request.Headers["X-Workspace-Mode"] = ""; break;
            case "invalid-tenant": scope.Http.Request.Headers["X-Workspace-Tenant-Id"] = "directory-not-organization"; break;
            case "mismatched-tenant": scope.Http.Request.Headers["X-Workspace-Tenant-Id"] = Guid.NewGuid().ToString(); break;
            case "csp-with-tenant": scope.Http.Request.Headers["X-Workspace-Kind"] = "csp"; break;
            case "support-without-cookie":
                scope.Http.Request.Headers["X-Workspace-Mode"] = "support";
                scope.Http.Request.Headers.Remove("Cookie");
                break;
            case "duplicate-bearer": scope.Http.Request.Headers.Authorization = new[] { "Bearer one", "Bearer two" }; break;
            case "conflicting-query-token":
                scope.Http.Request.Path = "/hubs/chat";
                scope.Http.Request.QueryString = new("?access_token=another-token");
                break;
        }
        using var db = Database();
        db.Dispose();
        using var handler = new ChatHandler();
        var service = Service(db, scope, handler);

        // Act
        var action = () => service.GetConversationsAsync();

        // Assert
        await action.Should().ThrowAsync<ChatWorkspaceException>();
        handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task SignalRQueryBearer_IsValidatedAndForwardedAsUserBearer()
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        scope.Http.Request.Path = "/hubs/chat";
        scope.Http.Request.Headers.Remove("Authorization");
        scope.Http.Request.QueryString = new("?access_token=test-user-token");

        // Act
        var workspace = await scope.Resolver.ResolveAsync();

        // Assert
        workspace.ActorId.Should().Be(LegacyChatTestScope.ActorId);
        scope.Requests.Should().ContainSingle().Which.Token.Should().Be(scope.Token);
    }

    [Fact]
    public async Task CreateWithoutValidatedUser_DeniesBeforePersistence()
    {
        // Arrange
        using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var scope = new LegacyChatTestScope { Authenticated = false };
        scope.Apply();
        var service = new ChatService(db, Mock.Of<IHttpClientFactory>(),
            NullLogger<ChatService>.Instance, Mock.Of<IPathSanitizationService>(), scope.Resolver);

        // Act
        var action = () => service.CreateConversationAsync(new CreateConversationRequest { UserId = "forged" });

        // Assert
        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task OwnerlessLegacyConversation_CannotBeReadByIdentifier()
    {
        // Arrange
        using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Conversations.Add(new Conversation { Id = "legacy", UserId = "default-user" });
        await db.SaveChangesAsync();
        using var scope = new LegacyChatTestScope();
        var service = new ChatService(db, Mock.Of<IHttpClientFactory>(),
            NullLogger<ChatService>.Instance, Mock.Of<IPathSanitizationService>(), scope.Resolver);

        // Act
        var result = await service.GetConversationAsync("legacy");

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("subject")]
    [InlineData("organization")]
    [InlineData("person")]
    [InlineData("support")]
    [InlineData("csp")]
    public async Task DifferentQualifiedOwner_CannotReadSearchMutateOrForward(string dimension)
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        using var db = Database();
        using var upstream = new ChatHandler();
        var service = Service(db, scope, upstream);
        var owner = await service.CreateConversationAsync(new() { Title = "private-title", UserId = "forged-user" });
        db.Messages.Add(new ChatMessage { Id = "private-message", ConversationId = owner.Id, Content = "private-content" });
        db.Attachments.Add(new MessageAttachment { Id = "private-attachment", MessageId = "private-message", FileName = "private.txt" });
        await db.SaveChangesAsync();
        switch (dimension)
        {
            case "directory": scope.DirectoryId = Guid.NewGuid(); break;
            case "subject": scope.ObjectId = Guid.NewGuid(); break;
            case "organization": scope.TenantId = Guid.NewGuid(); break;
            case "person": scope.PersonId = Guid.NewGuid(); break;
            case "support": scope.Mode = "support"; scope.PersonId = null; break;
            case "csp": scope.Kind = "csp"; scope.TenantId = null; scope.PersonId = null; break;
        }
        scope.Apply();
        using var file = new MemoryStream("payload"u8.ToArray());

        // Act
        var read = await service.GetConversationAsync(owner.Id);
        var list = await service.GetConversationsAsync(owner.UserId);
        var search = await service.SearchConversationsAsync("private", owner.UserId);
        Func<Task>[] denied =
        [
            () => service.GetMessagesAsync(owner.Id),
            () => service.GetConversationHistoryAsync(owner.Id),
            () => service.SendMessageAsync(new() { ConversationId = owner.Id, Message = "forged", Context = new() { ["userId"] = owner.UserId } }),
            () => service.CreateOrUpdateContextAsync(new() { ConversationId = owner.Id, Conversation = owner }),
            () => service.SaveAttachmentAsync("private-message", "x.txt", "text/plain", file),
            () => service.DeleteConversationAsync(owner.Id)
        ];

        // Assert
        read.Should().BeNull();
        list.Should().BeEmpty();
        search.Should().BeEmpty();
        foreach (var action in denied) await action.Should().ThrowAsync<ChatWorkspaceException>();
        db.Messages.Should().ContainSingle().Which.Content.Should().Be("private-content");
        db.Attachments.Should().ContainSingle();
        db.ConversationContexts.Should().BeEmpty();
        upstream.Bodies.Should().BeEmpty();
        owner.UserId.Should().Be(LegacyChatTestScope.ActorId);
        owner.OwnerKey.Should().Be(LegacyChatTestScope.DefaultOwnerKey);
    }

    [Theory]
    [InlineData("unauthenticated")]
    [InlineData("local-token")]
    [InlineData("upstream-token")]
    [InlineData("upstream-outage")]
    [InlineData("mismatched-oid")]
    [InlineData("missing-bearer")]
    [InlineData("wrong-bearer")]
    [InlineData("missing-person")]
    [InlineData("missing-workspace")]
    public async Task UnvalidatedRequests_FailBeforeTouchingEvenDisposedDatabase(string rejection)
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        switch (rejection)
        {
            case "unauthenticated": scope.Authenticated = false; break;
            case "local-token": scope.LocalTokenValid = false; break;
            case "upstream-token": scope.UpstreamStatus = HttpStatusCode.Unauthorized; break;
            case "upstream-outage": scope.UpstreamStatus = HttpStatusCode.ServiceUnavailable; break;
            case "mismatched-oid": scope.UpstreamObjectId = Guid.NewGuid(); break;
            case "missing-person": scope.PersonId = null; break;
        }
        scope.Apply();
        if (rejection == "missing-bearer") scope.Http.Request.Headers.Remove("Authorization");
        if (rejection == "wrong-bearer") scope.Http.Request.Headers.Authorization = "Bearer unvalidated-other-token";
        if (rejection == "missing-workspace") scope.Http.Request.Headers.Remove("X-Workspace-Kind");
        using var db = Database();
        db.Dispose();
        using var upstream = new ChatHandler();
        var service = Service(db, scope, upstream);
        using var stream = new MemoryStream("data"u8.ToArray());
        Func<Task>[] operations =
        [
            () => service.CreateConversationAsync(new() { UserId = "default-user" }),
            () => service.GetConversationAsync("legacy"),
            () => service.GetConversationsAsync(),
            () => service.SearchConversationsAsync("secret"),
            () => service.GetMessagesAsync("legacy"),
            () => service.GetConversationHistoryAsync("legacy"),
            () => service.SendMessageAsync(new() { ConversationId = "legacy", Message = "test" }),
            () => service.CreateOrUpdateContextAsync(new() { ConversationId = "legacy" }),
            () => service.SaveAttachmentAsync("message", "file.txt", "text/plain", stream),
            () => service.DeleteConversationAsync("legacy")
        ];

        // Act
        foreach (var operation in operations)
        {
            // Assert
            await operation.Should().ThrowAsync<ChatWorkspaceException>();
        }
        upstream.Bodies.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAndContinue_ForwardsOnlyOwnHistoryAndExactToken(bool fallback)
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        using var db = Database();
        using var handler = new ChatHandler { UseFallback = fallback };
        var service = Service(db, scope, handler);
        var conversation = await service.CreateConversationAsync(new() { UserId = "forged-user" });
        db.Conversations.Add(new Conversation { Id = "other", OwnerKey = "other" });
        db.Messages.Add(new ChatMessage { ConversationId = "other", Content = "another owner's secret" });
        await db.SaveChangesAsync();

        // Act
        var first = await service.SendMessageAsync(new()
        {
            ConversationId = conversation.Id, Message = "first", Context = new()
            {
                ["userId"] = "spoof", ["user_role"] = "AO", ["tenantId"] = "spoof",
                ["conversationHistory"] = "forged", ["conversationId"] = "other"
            }
        });
        var second = await service.SendMessageAsync(new() { ConversationId = conversation.Id, Message = "second" });

        // Assert
        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        (await service.GetMessagesAsync(conversation.Id)).Should().HaveCount(4);
        foreach (var body in handler.Bodies)
        {
            body.Should().NotContain("another owner's secret").And.NotContain("spoof").And.NotContain("forged");
            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("conversationId").GetString().Should().Be(conversation.Id);
            json.RootElement.GetProperty("context").GetProperty("userId").GetString().Should().Be(LegacyChatTestScope.ActorId);
        }
        handler.Headers.Should().OnlyContain(h => h.Token == scope.Token && h.Kind == "organization"
            && h.Tenant == scope.TenantId.ToString() && h.Mode == "ordinary" && h.Cookie == null);
        scope.Requests.Should().OnlyContain(r => r.Token == scope.Token && r.Cookie == null);
    }

    [Fact]
    public async Task SystemScope_CannotBeReboundAndIsRevalidatedOnHistoryRead()
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        using var db = Database();
        using var handler = new ChatHandler();
        var service = Service(db, scope, handler);
        var conversation = await service.CreateConversationAsync(new());
        await service.SendMessageAsync(new()
        {
            ConversationId = conversation.Id, Message = "private-system-history", Context = new() { ["systemId"] = "system-a" }
        });

        // Act
        var overwrite = () => service.SendMessageAsync(new()
        {
            ConversationId = conversation.Id, Message = "wrong-system", Context = new() { ["system_id"] = "system-b" }
        });
        var contextOverwrite = () => service.CreateOrUpdateContextAsync(new()
        {
            ConversationId = conversation.Id, Data = new() { ["systemId"] = "system-b" }
        });

        // Assert
        await overwrite.Should().ThrowAsync<ChatWorkspaceException>().Where(e => e.Code == "CONVERSATION_SYSTEM_MISMATCH");
        await contextOverwrite.Should().ThrowAsync<ChatWorkspaceException>();
        scope.SystemReadable = false;
        var history = () => service.GetConversationHistoryAsync(conversation.Id);
        await history.Should().ThrowAsync<ChatWorkspaceException>().Where(e => e.Code == "SYSTEM_ACCESS_DENIED");
        handler.Bodies.Should().ContainSingle();
        conversation.SystemId.Should().Be("system-a");
    }

    [Fact]
    public async Task ContextAndAttachmentIdentifiers_CannotAttachForeignGraphsOrFiles()
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        using var db = Database();
        using var handler = new ChatHandler();
        var service = Service(db, scope, handler);
        var own = await service.CreateConversationAsync(new());
        var foreign = new Conversation { Id = "foreign", OwnerKey = "different-owner" };
        db.Conversations.Add(foreign);
        db.Messages.Add(new() { Id = "foreign-message", ConversationId = foreign.Id });
        db.Attachments.Add(new() { Id = "foreign-file", MessageId = "foreign-message" });
        await db.SaveChangesAsync();

        // Act
        var context = await service.CreateOrUpdateContextAsync(new()
        {
            Id = "client-context", ConversationId = own.Id, Conversation = foreign,
            Data = new() { ["roles"] = new[] { "AO" }, ["personId"] = "spoof" }
        });
        var send = () => service.SendMessageAsync(new()
        {
            ConversationId = own.Id, Message = "test", AttachmentIds = ["foreign-file"]
        });

        // Assert
        context.Id.Should().NotBe("client-context");
        context.ConversationId.Should().Be(own.Id);
        context.Data.Should().NotContainKey("roles").And.NotContainKey("personId");
        foreign.OwnerKey.Should().Be("different-owner");
        await send.Should().ThrowAsync<ChatWorkspaceException>();
        handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task SupportCookie_IsForwardedOnlyForValidatedSupport()
    {
        // Arrange
        using var scope = new LegacyChatTestScope { Mode = "support", PersonId = null };
        scope.Apply();
        using var db = Database();
        using var handler = new ChatHandler();
        var service = Service(db, scope, handler);

        // Act
        var conversation = await service.CreateConversationAsync(new());
        await service.SendMessageAsync(new() { ConversationId = conversation.Id, Message = "test" });

        // Assert
        scope.Requests.Should().OnlyContain(r => r.Mode == "support" && r.Cookie == "ato-impersonate=support-ticket");
        handler.Headers.Should().OnlyContain(r => r.Mode == "support" && r.Cookie == "ato-impersonate=support-ticket");
    }

    [Fact]
    public async Task Cancellation_PropagatesWithoutSavingOrForwarding()
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        using var db = Database();
        using var handler = new ChatHandler();
        var service = Service(db, scope, handler);
        var conversation = await service.CreateConversationAsync(new());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var send = () => service.SendMessageAsync(new() { ConversationId = conversation.Id, Message = "test" }, null, cancellation.Token);
        scope.Http.RequestAborted = cancellation.Token;
        var read = () => service.GetConversationAsync(conversation.Id);

        // Assert
        await send.Should().ThrowAsync<OperationCanceledException>();
        await read.Should().ThrowAsync<OperationCanceledException>();
        db.Messages.Should().BeEmpty();
        handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationDuringForwarding_StopsWithoutFallbackOrAssistantWrite()
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        using var db = Database();
        using var cancellation = new CancellationTokenSource();
        using var handler = new ChatHandler { CancelDuringSend = cancellation };
        var service = Service(db, scope, handler);
        var conversation = await service.CreateConversationAsync(new());

        // Act
        var action = () => service.SendMessageAsync(new()
        {
            ConversationId = conversation.Id, Message = "cancel-in-flight"
        }, null, cancellation.Token);

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        handler.Bodies.Should().ContainSingle();
        db.Messages.Should().ContainSingle().Which.Role.Should().Be(MessageRole.User);
    }

    [Fact]
    public async Task UpstreamChatDenial_DoesNotRetryWithDefaultCredentials()
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        using var db = Database();
        using var handler = new ChatHandler { DenyChat = true };
        var service = Service(db, scope, handler);
        var conversation = await service.CreateConversationAsync(new());

        // Act
        var action = () => service.SendMessageAsync(new() { ConversationId = conversation.Id, Message = "denied" });

        // Assert
        await action.Should().ThrowAsync<ChatWorkspaceException>();
        handler.Bodies.Should().ContainSingle();
        handler.Headers.Should().OnlyContain(h => h.Token == scope.Token);
        db.Messages.Should().ContainSingle().Which.Role.Should().Be(MessageRole.User);
    }

    [Fact]
    public async Task SqliteSchemaUpgrade_AddsNullableOwnershipWithoutAdoptingLegacyRows()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Conversations (Id TEXT PRIMARY KEY, UserId TEXT)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Conversations (Id, UserId) VALUES ('legacy', 'default-user')");

        // Act
        await ChatWorkspaceSchema.EnsureAsync(db, default);
        await ChatWorkspaceSchema.EnsureAsync(db, default);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Conversations WHERE OwnerKey IS NULL AND SystemId IS NULL";
        var legacyCount = (long)(await command.ExecuteScalarAsync())!;

        // Assert
        legacyCount.Should().Be(1);
    }

    [Fact]
    public async Task OwnedHistory_CanBeSerializedWithoutOwnershipOrParentGraphs()
    {
        // Arrange
        using var scope = new LegacyChatTestScope();
        using var db = Database();
        using var handler = new ChatHandler();
        var service = Service(db, scope, handler);
        var conversation = await service.CreateConversationAsync(new());
        await service.SendMessageAsync(new() { ConversationId = conversation.Id, Message = "hello" });
        await service.CreateOrUpdateContextAsync(new() { ConversationId = conversation.Id, Title = "context" });
        var messages = await service.GetMessagesAsync(conversation.Id);
        var detail = await service.GetConversationAsync(conversation.Id);

        // Act
        var serialize = () => JsonSerializer.Serialize(new { messages, detail });

        // Assert
        serialize.Should().NotThrow().Which.Should().NotContain("OwnerKey").And.NotContain("v1:");
    }

    private static ChatDbContext Database() => new(new DbContextOptionsBuilder<ChatDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ChatService Service(ChatDbContext db, LegacyChatTestScope scope, ChatHandler handler)
    {
        var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://mcp.test") };
        client.DefaultRequestHeaders.Authorization = new("Bearer", "default-service-credential-must-not-be-used");
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("McpServer")).Returns(client);
        return new ChatService(db, factory.Object, NullLogger<ChatService>.Instance,
            Mock.Of<IPathSanitizationService>(), scope.Resolver);
    }

    private sealed class ChatHandler : HttpMessageHandler
    {
        public bool UseFallback { get; init; }
        public bool DenyChat { get; init; }
        public CancellationTokenSource? CancelDuringSend { get; init; }
        public List<string> Bodies { get; } = new();
        public List<(string? Token, string? Kind, string? Mode, string? Tenant, string? Cookie)> Headers { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? Header(string name) => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
            Headers.Add((request.Headers.Authorization?.Parameter, Header("X-Workspace-Kind"), Header("X-Workspace-Mode"),
                Header("X-Workspace-Tenant-Id"), Header("Cookie")));
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            if (CancelDuringSend is not null)
            {
                CancelDuringSend.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            if (DenyChat) return new(HttpStatusCode.Forbidden);
            if (request.RequestUri!.AbsolutePath.EndsWith("/stream"))
                return UseFallback ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK)
                {
                    Content = new StringContent("data: {\"type\":\"result\",\"data\":{\"response\":\"own-answer\",\"success\":true}}\n\n",
                        Encoding.UTF8, "text/event-stream")
                };
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"response\":\"own-answer\",\"success\":true}") };
        }
    }
}
