using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Hubs.Notifications;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public partial class NotificationHubIsolationTests
{
    [Theory]
    [InlineData(ProgressResourceKind.Package)]
    [InlineData(ProgressResourceKind.Import)]
    public async Task LiveProgressTransport_AuthorizesSubscriptionsAndStopsDeliveryAfterRevocation(ProgressResourceKind kind)
    {
        // Arrange
        await SeedAsync();
        var id = await ProgressResourceAsync(kind, _tenantA);
        var foreign = await ProgressResourceAsync(kind, _tenantB);
        using var server = HttpServer();
        if (kind == ProgressResourceKind.Import)
        {
            await using var db = await Db();
            foreach (var pair in new[] { (id, _tenantA), (foreign, _tenantB) })
            {
                var system = (await db.ComplianceAlerts.SingleAsync(a => a.Id == _alerts[pair.Item2])).RegisteredSystemId!;
                server.Services.GetRequiredService<ScanImportStatusTracker>().Register(pair.Item1.ToString(), system);
            }
        }
        var path = kind == ProgressResourceKind.Package ? "/hubs/package" : "/hubs/import-progress";
        var target = kind == ProgressResourceKind.Package ? "SubscribeToPackage" : "JoinImportGroup";
        var client = server.CreateWebSocketClient();
        using var socket = await client.ConnectAsync(new Uri(
            $"ws://localhost{path}?workspaceKind=organization&workspaceTenantId={_tenantA}&workspaceMode=ordinary&access_token=progress-test"),
            CancellationToken.None);
        await SendFrameAsync(socket, new { protocol = "json", version = 1 });
        (await ReceiveFrameAsync(socket)).Should().NotBeNull();

        // Act
        await SendFrameAsync(socket, new { type = 1, invocationId = "foreign", target, arguments = new[] { foreign.ToString() } });
        var rejected = await ReceiveFrameAsync(socket);
        await SendFrameAsync(socket, new { type = 1, invocationId = "allowed", target, arguments = new[] { id.ToString() } });
        var subscribed = await ReceiveFrameAsync(socket);
        await PublishLiveAsync(server.Services, kind, id);
        var delivered = await ReceiveFrameAsync(socket);
        await using (var db = await Db())
        {
            (await db.OrganizationMemberships.SingleAsync(m => m.TenantId == _tenantA)).RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await PublishLiveAsync(server.Services, kind, id);
        var afterRevocation = await ReceiveFrameAsync(socket);

        // Assert
        rejected!.Value.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        subscribed!.Value.TryGetProperty("error", out _).Should().BeFalse();
        delivered!.Value.GetProperty("target").GetString().Should()
            .Be(kind == ProgressResourceKind.Package ? "PackageStatusChanged" : "ImportProgress");
        (afterRevocation is null || afterRevocation.Value.GetProperty("type").GetInt32() == 7)
            .Should().BeTrue("revocation must close the transport rather than deliver another event");
    }

    private static Task PublishLiveAsync(IServiceProvider services, ProgressResourceKind kind, Guid id)
    {
        if (kind == ProgressResourceKind.Package)
        {
            IPackageExportNotifier notifier = new SignalRPackageExportNotifier(
                services.GetRequiredService<IHubContext<PackageHub>>(), NullLogger<SignalRPackageExportNotifier>.Instance);
            return notifier.SendStatusChangedAsync(id.ToString(), "Processing");
        }
        return services.GetRequiredService<IHubContext<ImportProgressHub>>().Clients.Group($"import:{id}")
            .SendAsync("ImportProgress", new { jobId = id, status = "Processing", processedCount = 1, totalCount = 2 });
    }

    private static Task SendFrameAsync(WebSocket socket, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload) + '\u001e');
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonElement?> ReceiveFrameAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[16384];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        var text = Encoding.UTF8.GetString(message.ToArray()).TrimEnd('\u001e');
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
