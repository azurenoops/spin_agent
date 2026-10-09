using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class RequirementFirstPassGeneratorTests
{
    private static RequirementFirstPassContext Context() => new("PT-2", "Authority", "Policy",
        [new("a", "a", "Determine {{ insert: param, authority }}.", null)],
        [new("authority", "{\"label\":\"Legal authority\"}")],
        [new("policy", "RetainedPolicy", "Recorded policy", "1", "hash", "Unreviewed applicability",
            "Recorded processing authority: Example recorded authority", ["Example recorded authority"])]);

    [Fact]
    public async Task Generation_UsesExistingClientAndValidatedExtractiveSourceValues()
    {
        // Arrange
        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """
                {"responses":[{"statementId":"a","response":"Recorded policy describes the processing basis; review required.","sourceIds":["policy"],"explanation":"Based on the retained policy."}],
                 "parameters":[{"parameterId":"authority","value":"Example recorded authority","sourceIds":["policy"],"explanation":"Extracted from the recorded source."}],
                 "questions":[],"conflicts":[]}
                """)));
        var generator = new NarrativeTemplateService(client.Object,
            new AzureAiOptions { Enabled = true, Endpoint = "https://example.invalid", DeploymentName = "synthetic", ApiKey = "synthetic-not-a-secret" },
            NullLogger<NarrativeTemplateService>.Instance);
        // Act
        var result = await generator.GenerateRequirementFirstPassAsync(Context());
        // Assert
        result.Parameters.Single().Value.Should().Be("Example recorded authority");
        result.Responses.Single().SourceIds.Should().Equal("policy");
    }

    [Theory]
    [InlineData("Fabricated legal authority", "policy")]
    [InlineData("Example recorded authority", "foreign")]
    public async Task Generation_RejectsInventedAuthorityAndUnknownSources(string value, string source)
    {
        // Arrange
        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                $$"""{"responses":[],"parameters":[{"parameterId":"authority","value":"{{value}}","sourceIds":["{{source}}"],"explanation":"Claim"}],"questions":[],"conflicts":[]}""")));
        var generator = new NarrativeTemplateService(client.Object,
            new AzureAiOptions { Enabled = true, Endpoint = "https://example.invalid", DeploymentName = "synthetic", ApiKey = "synthetic-not-a-secret" },
            NullLogger<NarrativeTemplateService>.Instance);
        // Act
        var generate = () => generator.GenerateRequirementFirstPassAsync(Context());
        // Assert
        await generate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unsupported*");
    }
    [Fact]
    public async Task Generation_DisabledIsAnExplicitError_NotATemplateFallback()
    {
        // Arrange
        var generator = new NarrativeTemplateService();
        // Act
        var generate = () => generator.GenerateRequirementFirstPassAsync(Context());
        // Assert
        await generate.Should().ThrowAsync<InvalidOperationException>().WithMessage("AI_NOT_AVAILABLE:*");
    }
    [Fact]
    public void Generation_RejectsAuthorityFromARegulationTagAndWrongStatementIds()
    {
        // Arrange
        var context = Context() with { Sources = [Context().Sources[0] with { Kind = "InformationType" }] };
        var parameter = new RequirementFirstPassSuggestion([], [new("authority", "Example recorded authority", ["policy"], "Claim")], [], []);
        var response = new RequirementFirstPassSuggestion([new("foreign-statement", "Unsupported draft", ["policy"], "Claim")], [], [], []);
        // Act
        var checkParameter = () => NarrativeTemplateService.ValidateRequirementSuggestion(context, parameter);
        var checkResponse = () => NarrativeTemplateService.ValidateRequirementSuggestion(context, response);
        // Assert
        checkParameter.Should().Throw<InvalidOperationException>();
        checkResponse.Should().Throw<InvalidOperationException>();
    }
}
