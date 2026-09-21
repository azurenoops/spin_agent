using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Agents.Document.Tools;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tools;

public class DocumentNarrativeGenerateAdapterToolTests
{
    [Theory]
    [InlineData("model", true)]
    [InlineData("empty", false)]
    [InlineData("failure", false)]
    [InlineData("no-model", false)]
    public async Task SaveDraft_PersistsModelOrAutomaticOrigin_ManualWriteClearsIt(string responseKind, bool expectedAi)
    {
        // Arrange
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(options => options.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.RegisteredSystems.Add(new RegisteredSystem { Id = "sys-1", Name = "Synthetic system" });
            db.ControlBaselines.Add(new ControlBaseline { RegisteredSystemId = "sys-1", ControlIds = ["AC-2"] });
            await db.SaveChangesAsync();
        }
        var ssp = new SspService(provider.GetRequiredService<IServiceScopeFactory>(), Mock.Of<ILogger<SspService>>());
        await ssp.WriteNarrativeAsync("sys-1", "AC-2", "Existing manual text");
        var chat = new Mock<IChatClient>();
        var setup = chat.Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()));
        if (responseKind == "failure")
            setup.ThrowsAsync(new InvalidOperationException("Synthetic failure"));
        else
            setup.ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                responseKind == "empty" ? " " : "Source-grounded model text")));
        using var http = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("Synthetic source evidence", Encoding.UTF8, "text/plain")
        }));
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(factory => factory.CreateClient("default")).Returns(http);
        var tool = new DocumentNarrativeGenerateAdapterTool(ssp, Mock.Of<IDocumentTemplateService>(),
            httpFactory.Object, responseKind == "no-model" ? null : chat.Object, null,
            Mock.Of<ILogger<DocumentNarrativeGenerateAdapterTool>>());

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1", ["control_id"] = "AC-2", ["save_draft"] = "true",
            ["source_url"] = "https://example.test/source.txt"
        });

        // Assert
        using var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success", result);
        json.RootElement.GetProperty("data").GetProperty("ai_used").GetBoolean().Should().Be(expectedAi);
        json.RootElement.GetProperty("data").GetProperty("derivation_basis").GetString()
            .Should().Be(expectedAi ? "ModelAssessedWithEvidence" : "TemplateScaffold");
        using var verification = provider.CreateScope();
        var dbContext = verification.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var saved = await dbContext.ControlImplementations.AsNoTracking().SingleAsync();
        saved.AiSuggested.Should().Be(expectedAi);
        saved.IsAutoPopulated.Should().BeTrue();
        saved.TechnicalNarrative.Should().Contain("Synthetic source evidence");
        var manual = await ssp.WriteNarrativeAsync("sys-1", "AC-2", "Manual replacement");
        manual.AiSuggested.Should().BeFalse();
        manual.IsAutoPopulated.Should().BeFalse();
    }

    [Fact]
    public void AddComplianceAgent_RegistersGraphServiceClientSingleton_WithoutDuplicates()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:Azure:CloudEnvironment"] = "AzureGovernment"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddComplianceAgent(config);
        services.AddComplianceAgent(config);

        var descriptors = services.Where(d => d.ServiceType == typeof(GraphServiceClient)).ToList();
        descriptors.Should().HaveCount(1, "GraphServiceClient should be registered once even if AddComplianceAgent is called multiple times");
        descriptors[0].Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task ExecuteAsync_WithSharePointSource_UsesFallbackHttpEvidenceWhenGraphUnavailable()
    {
        var ssp = new Mock<ISspService>();
        ssp.Setup(s => s.SuggestNarrativeAsync("sys-1", "AC-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NarrativeSuggestion
            {
                Narrative = "Baseline narrative",
                Confidence = null, // template path — no grounding signal; must not emit fabricated constant (#705)
                DerivationBasis = NarrativeDerivationBasis.TemplateScaffold,
                RequiresHumanValidation = true,
                References = new List<string> { "NIST 800-53 AC-2 [scaffold reference — unverified]" },
                ControlId = "AC-2"
            });

        var templates = new Mock<IDocumentTemplateService>();

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>System uses CAC + PIM workflows.</body></html>", Encoding.UTF8, "text/html")
        });
        var httpClient = new HttpClient(handler);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient("default")).Returns(httpClient);

        var tool = new DocumentNarrativeGenerateAdapterTool(
            ssp.Object,
            templates.Object,
            httpFactory.Object,
            chatClient: null,
            graphClient: null,
            Mock.Of<ILogger<DocumentNarrativeGenerateAdapterTool>>());

        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["control_id"] = "AC-2",
            ["source_url"] = "https://contoso.sharepoint.com/sites/rmf/Shared%20Documents/Plans/plan.txt?file=plan.txt"
        };

        var result = await tool.ExecuteAsync(args);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("status").GetString().Should().Be("success");

        var data = json.RootElement.GetProperty("data");
        data.GetProperty("ai_used").GetBoolean().Should().BeFalse();
        data.GetProperty("source_evidence").GetArrayLength().Should().Be(1);
        data.GetProperty("suggested_narrative").GetString().Should().Contain("Source Grounding Excerpts:");
        data.GetProperty("suggested_narrative").GetString().Should().Contain("System uses CAC + PIM workflows.");
    }

    [Fact]
    public void TryParseSharePointDocumentPath_ParsesExpectedSiteAndDocumentPath()
    {
        var method = typeof(DocumentNarrativeGenerateAdapterTool)
            .GetMethod("TryParseSharePointDocumentPath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var uri = new Uri("https://contoso.sharepoint.com/sites/rmf/Shared%20Documents/Plans/plan.docx");
        object?[] parameters = [uri, null, null, null];

        var ok = (bool)method!.Invoke(null, parameters)!;
        ok.Should().BeTrue();
        parameters[1].Should().Be("contoso.sharepoint.com");
        parameters[2].Should().Be("/sites/rmf");
        parameters[3].Should().Be("Shared Documents/Plans/plan.docx");
    }

    [Fact]
    public async Task ExtractDocxTextAsync_ExtractsTextFromDocumentXml()
    {
        var method = typeof(DocumentNarrativeGenerateAdapterTool)
            .GetMethod("ExtractDocxTextAsync", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        await using var stream = CreateDocxStream("Control AC-2 implemented with MFA and CAC.");
        var task = (Task<string?>)method!.Invoke(null, new object[] { stream })!;
        var extracted = await task;

        extracted.Should().NotBeNull();
        extracted.Should().Contain("Control AC-2 implemented with MFA and CAC.");
    }

    private static MemoryStream CreateDocxStream(string bodyText)
    {
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
    <w:body><w:p><w:r><w:t>{bodyText}</w:t></w:r></w:p></w:body>
</w:document>";

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(xml);
        }

        ms.Position = 0;
        return ms;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
