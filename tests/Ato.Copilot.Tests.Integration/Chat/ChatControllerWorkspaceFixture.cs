using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Chat.Services.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Tests.Integration.Chat;

internal static class ChatControllerWorkspaceFixture
{
    internal const string DirectoryId = "11111111-1111-1111-1111-111111111111";
    internal const string ObjectId = "22222222-2222-2222-2222-222222222222";
    internal const string TenantId = "33333333-3333-3333-3333-333333333333";
    internal const string Token = "synthetic-chat-controller-token";

    internal static void Register(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ChatWorkspaceResolver>();
        foreach (var name in new[] { "McpWorkspace", "McpServer" })
            services.AddHttpClient(name, client => client.BaseAddress = new Uri("https://chat-tests.invalid"))
                .ConfigurePrimaryHttpMessageHandler(() => new Handler());
    }

    internal static void Configure(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", TenantId);
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Headers.Authorization?.Parameter != Token)
                throw new InvalidOperationException("Controller test lost its authenticated bearer.");
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/me")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        status = "success",
                        data = new
                        {
                            oid = ObjectId, directoryTenantId = DirectoryId,
                            workspace = new
                            {
                                kind = "organization", tenantId = TenantId, mode = "ordinary",
                                personId = "44444444-4444-4444-4444-444444444444",
                                roles = new[] { "ISSO" }, permissions = new { canAccessCsp = false }
                            }
                        }
                    }), Encoding.UTF8, "application/json")
                });
            if (path == "/mcp/chat/stream")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "data: {\"type\":\"result\",\"data\":{\"success\":true,\"response\":\"Synthetic reply\",\"agentUsed\":\"ComplianceAgent\"}}\n\n",
                        Encoding.UTF8, "text/event-stream")
                });
            throw new InvalidOperationException($"Unexpected controller test request: {path}");
        }
    }
}
