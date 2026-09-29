using System.Net;
using System.Text;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services.Ticketing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public class TaskTicketProviderTests
{
    [Fact]
    public async Task Jira_Create_UsesResolvedSecretAndStableCorrelation()
    {
        // Arrange
        var handler = new RecordingHandler("""{"key":"TEST-42"}""");
        var provider = Jira(handler);
        // Act
        var result = await provider.CreateTaskAsync(new("title", "description", "stable-key"), Config(), default);
        // Assert
        result.ExternalRef.Should().Be("TEST-42");
        handler.Body.Should().Contain("stable-key").And.NotContain("secret-reference");
        handler.Authorization.Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:resolved-token")));
    }

    [Fact]
    public async Task Jira_Pull_ReportsSnapshotIncludingAssignee()
    {
        // Arrange
        var handler = new RecordingHandler("""{"fields":{"status":{"name":"Closed"},"assignee":{"displayName":"Test owner"}}}""");
        // Act
        var result = await Jira(handler).PullAsync("TEST-42", Config(), default);
        // Assert
        result.ExternalStatus.Should().Be("Closed");
        result.ExternalAssignee.Should().Be("Test owner");
    }

    [Theory]
    [InlineData("http://tickets.example")]
    [InlineData("https://other.example")]
    [InlineData("https://user:pass@tickets.example")]
    public async Task Jira_RejectsUnsafeDestinationBeforeSending(string url)
    {
        // Arrange
        var handler = new RecordingHandler("{}");
        var config = Config(); config.BaseUrl = url;
        // Act
        var result = await Jira(handler).CreateTaskAsync(new("title", "", "stable"), config, default);
        // Assert
        result.Success.Should().BeFalse();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ServiceNow_Create_UsesSysIdNotDisplayNumber()
    {
        // Arrange
        var handler = new RecordingHandler("""{"result":{"sys_id":"abcdef0123456789abcdef0123456789","number":"INC00042"}}""");
        var provider = new ServiceNowProvider(Factory(handler), NullLogger<ServiceNowProvider>.Instance, Credentials());
        var config = Config(); config.Provider = TicketingProvider.ServiceNow; config.ProjectKeyOrTableName = "incident";
        // Act
        var result = await provider.CreateTaskAsync(new("title", "", "stable"), config, default);
        // Assert
        result.ExternalRef.Should().Be("abcdef0123456789abcdef0123456789");
    }

    [Fact]
    public async Task Jira_CreateTimeout_IsReportedAsUncertainWithoutCredentialLeakage()
    {
        // Arrange
        var handler = new RecordingHandler("{}") { ThrowOnSend = true };
        // Act
        var result = await Jira(handler).CreateTaskAsync(new("title", "", "stable"), Config(), default);
        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("uncertain").And.NotContain("resolved-token");
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Jira_UnprovisionedReference_IsNeverUsedAsAuthorization()
    {
        // Arrange
        var handler = new RecordingHandler("{}");
        var config = Config(); config.KeyVaultSecretUri = "unprovisioned";
        // Act
        var result = await Jira(handler).PullAsync("TEST-42", config, default);
        // Assert
        result.Success.Should().BeFalse();
        handler.Calls.Should().Be(0);
        result.Error.Should().NotContain("unprovisioned");
    }

    [Fact]
    public async Task ServiceNow_Pull_UsesStableIdentifierAndDisplaySnapshots()
    {
        // Arrange
        var handler = new RecordingHandler("""{"result":{"state":{"display_value":"Closed","value":"7"},"assigned_to":{"display_value":"Test Owner","value":"user-id"}}}""");
        var provider = new ServiceNowProvider(Factory(handler), NullLogger<ServiceNowProvider>.Instance, Credentials());
        var config = Config(); config.ProjectKeyOrTableName = "incident";
        // Act
        var result = await provider.PullAsync("abcdef0123456789abcdef0123456789", config, default);
        // Assert
        result.ExternalStatus.Should().Be("Closed");
        result.ExternalAssignee.Should().Be("Test Owner");
        handler.Path.Should().Contain("/incident/abcdef0123456789abcdef0123456789?");
    }

    private static JiraProvider Jira(RecordingHandler handler) => new(Factory(handler), NullLogger<JiraProvider>.Instance, Credentials());
    private static IHttpClientFactory Factory(RecordingHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        return factory.Object;
    }
    private static TicketingCredentialResolver Credentials() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ticketing:AllowedHosts:0"] = "tickets.example",
            ["Ticketing:Credentials:secret-reference"] = "user:resolved-token"
        }).Build());
    private static TicketingIntegration Config() => new()
    {
        Provider = TicketingProvider.Jira, BaseUrl = "https://tickets.example",
        ProjectKeyOrTableName = "TEST", KeyVaultSecretUri = "secret-reference"
    };
    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }
        public string? Path { get; private set; }
        public bool ThrowOnSend { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri?.PathAndQuery;
            if (ThrowOnSend) throw new TaskCanceledException("synthetic timeout");
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString();
            return new(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
