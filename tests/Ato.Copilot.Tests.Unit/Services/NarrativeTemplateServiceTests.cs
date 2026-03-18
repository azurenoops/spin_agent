using FluentAssertions;
using Xunit;
using Moq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Services;

public class NarrativeTemplateServiceTests
{
    private readonly NarrativeTemplateService _sut = new();

    // ─── GenerateEnrichedNarrative ───────────────────────────────────────────

    [Fact]
    public void GenerateEnrichedNarrative_WithComponents_IncludesComponentDetails()
    {
        var components = new List<ComponentContext>
        {
            new("Microsoft Entra ID", "Thing", "Cloud Team"),
            new("ISSO — John Smith", "Person", null),
            new("Azure East US Gov", "Place", "Infra Team"),
        };

        var result = _sut.GenerateEnrichedNarrative(
            "Multi-Factor Authentication", "Microsoft", "Enforces MFA for all users",
            "IA-2", "Identification and Authentication",
            components, "Production");

        result.Should().Contain("Multi-Factor Authentication");
        result.Should().Contain("Microsoft");
        result.Should().Contain("Technology: Microsoft Entra ID.");
        result.Should().Contain("Responsible personnel: ISSO — John Smith.");
        result.Should().Contain("Infrastructure: Azure East US Gov.");
        result.Should().Contain("Production authorization boundary");
    }

    [Fact]
    public void GenerateEnrichedNarrative_WithoutComponents_FallsBackToSimpleNarrative()
    {
        var result = _sut.GenerateEnrichedNarrative(
            "MFA", "Entra ID", "MFA enforcement",
            "IA-2", "Identification and Authentication",
            null, null);

        result.Should().Contain("MFA");
        result.Should().Contain("Entra ID");
        result.Should().NotContain("Technology:");
        result.Should().NotContain("Responsible personnel:");
    }

    [Fact]
    public void GenerateEnrichedNarrative_EmptyComponents_FallsBackToSimpleNarrative()
    {
        var result = _sut.GenerateEnrichedNarrative(
            "MFA", "Entra ID", "MFA enforcement",
            "IA-2", "Identification and Authentication",
            new List<ComponentContext>(), "Production");

        result.Should().NotContain("Technology:");
        result.Should().NotContain("authorization boundary");
    }

    [Fact]
    public void GenerateEnrichedNarrative_ThingsOnly_IncludesTechnologyNoPersonnel()
    {
        var components = new List<ComponentContext>
        {
            new("Azure Conditional Access", "Thing", "Cloud Team"),
            new("Microsoft Authenticator", "Thing", null),
        };

        var result = _sut.GenerateEnrichedNarrative(
            "MFA", "Microsoft", "MFA enforcement",
            "IA-2", "Identification and Authentication",
            components, null);

        result.Should().Contain("Technology: Azure Conditional Access, Microsoft Authenticator.");
        result.Should().NotContain("Responsible personnel:");
        result.Should().NotContain("Infrastructure:");
        result.Should().NotContain("authorization boundary");
    }

    // ─── GenerateCompositeNarrative with components (T006) ───────────────────

    [Fact]
    public void GenerateCompositeNarrative_SingleMappingWithComponents_UsesEnrichedNarrative()
    {
        var components = new List<ComponentContext>
        {
            new("Azure Firewall", "Thing", "Net Team"),
        };

        var mappings = new List<BoundaryMappingContext>
        {
            new("Network Segmentation", "Azure", "Network isolation", "Production", components),
        };

        var result = _sut.GenerateCompositeNarrative("SC-7", "Boundary Protection", mappings);

        result.Should().Contain("Network Segmentation");
        result.Should().Contain("Technology: Azure Firewall.");
        result.Should().Contain("Production authorization boundary");
    }

    [Fact]
    public void GenerateCompositeNarrative_MultipleMappingsWithComponents_IncludesComponentsPerSection()
    {
        var mappings = new List<BoundaryMappingContext>
        {
            new("MFA", "Entra ID", "MFA for all users", null,
                new List<ComponentContext> { new("Entra ID", "Thing", null) }),
            new("Smart Card", "PIV", "CAC-based auth", "Production",
                new List<ComponentContext>
                {
                    new("PIV Card Reader", "Thing", null),
                    new("ISSO — Jane", "Person", null),
                }),
        };

        var result = _sut.GenerateCompositeNarrative("IA-2", "Identification and Authentication", mappings);

        result.Should().Contain("Organization-Wide: MFA using Entra ID");
        result.Should().Contain("Technology: Entra ID.");
        result.Should().Contain("Within the Production boundary: Smart Card using PIV");
        result.Should().Contain("Technology: PIV Card Reader.");
        result.Should().Contain("Responsible personnel: ISSO — Jane.");
    }

    // ─── AI-Assisted Generation (T007a) ─────────────────────────────────────

    [Fact]
    public async Task GenerateNarrativeWithAiAsync_WhenAiDisabled_ReturnsNull()
    {
        // Default constructor = no AI
        var sut = new NarrativeTemplateService();

        var result = await sut.GenerateNarrativeWithAiAsync(
            "MFA", "Entra", "desc", "IA-2", "Identification", null, null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GenerateNarrativeWithAiAsync_WhenAiEnabled_ReturnsNarrative()
    {
        var mockClient = new Mock<IChatClient>();
        var aiResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "AI generated narrative text about MFA."));
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var aiOptions = new AzureAiOptions { Enabled = true, Endpoint = "https://test.openai.azure.us/" };
        var logger = Mock.Of<ILogger<NarrativeTemplateService>>();
        var sut = new NarrativeTemplateService(mockClient.Object, aiOptions, logger);

        var result = await sut.GenerateNarrativeWithAiAsync(
            "MFA", "Entra", "desc", "IA-2", "Identification", null, null);

        result.Should().Be("AI generated narrative text about MFA.");
    }

    [Fact]
    public async Task GenerateNarrativeWithAiAsync_WhenAiFails_ReturnsNull()
    {
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("AI service unavailable"));

        var aiOptions = new AzureAiOptions { Enabled = true, Endpoint = "https://test.openai.azure.us/" };
        var logger = Mock.Of<ILogger<NarrativeTemplateService>>();
        var sut = new NarrativeTemplateService(mockClient.Object, aiOptions, logger);

        var result = await sut.GenerateNarrativeWithAiAsync(
            "MFA", "Entra", "desc", "IA-2", "Identification", null, null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GenerateNarrativeWithAiAsync_WithComponents_IncludesContextInPrompt()
    {
        var mockClient = new Mock<IChatClient>();
        IEnumerable<ChatMessage>? capturedMessages = null;
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Narrative with components.")));

        var aiOptions = new AzureAiOptions { Enabled = true, Endpoint = "https://test.openai.azure.us/" };
        var logger = Mock.Of<ILogger<NarrativeTemplateService>>();
        var sut = new NarrativeTemplateService(mockClient.Object, aiOptions, logger);

        var components = new List<ComponentContext>
        {
            new("Entra ID", "Thing", "Cloud Team"),
            new("ISSO — John", "Person", null),
        };

        await sut.GenerateNarrativeWithAiAsync(
            "MFA", "Microsoft", "MFA enforcement",
            "IA-2", "Identification",
            components, "Production");

        capturedMessages.Should().NotBeNull();
        var userMessage = capturedMessages!.ElementAt(1).Text;
        userMessage.Should().Contain("Technology Components: Entra ID");
        userMessage.Should().Contain("Responsible Personnel: ISSO — John");
        userMessage.Should().Contain("Authorization Boundary: Production");
    }
}
