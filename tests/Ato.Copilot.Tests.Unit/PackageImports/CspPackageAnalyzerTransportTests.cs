using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Services;
using Azure.AI.OpenAI;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    [Theory]
    [InlineData("gpt-5.4-mini-1", true)]
    [InlineData("production-reasoning", true)]
    [InlineData("gpt-4o", false)]
    public async Task Semantic_AzureTransportOmitsToolChoiceWithoutToolsAndPreservesTokenBudget(string deployment, bool useCompletionTokens)
    {
        // Arrange
        var handler = new SemanticTransportHandler();
        using var http = new HttpClient(handler);
        var azure = new AzureOpenAIClient(new Uri("https://test.openai.azure.com/"),
            new ApiKeyCredential("unit-test-only"), new AzureOpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(http)
            });
        using var client = useCompletionTokens
            ? new AzureCompletionTokenChatClient(azure, deployment, new Uri("https://test.openai.azure.com/")).AsIChatClient()
            : azure.AsChatClient(deployment);

        // Act
        var result = await SemanticAnalyzer(client).AnalyzeAsync(
            [Input("notice.txt", Bytes("No components are declared."))]);

        // Assert
        result.NeedsAttention.Should().BeFalse();
        handler.Request.TryGetProperty("tools", out _).Should().BeFalse();
        handler.Request.TryGetProperty("tool_choice", out _).Should().BeFalse(
            "Azure rejects tool_choice when no tools are supplied");
        var completionBudget = handler.Request.TryGetProperty("max_completion_tokens", out var budget);
        completionBudget.Should().Be(useCompletionTokens);
        (completionBudget ? budget : handler.Request.GetProperty("max_tokens")).GetInt32().Should().Be(8192);
        var format = handler.Request.GetProperty("response_format");
        format.GetProperty("type").GetString().Should().Be("json_schema");
        var schema = format.GetProperty("json_schema");
        schema.GetProperty("strict").GetBoolean().Should().BeTrue();
        schema.GetProperty("schema").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("sync")]
    [InlineData("async")]
    [InlineData("stream-sync")]
    [InlineData("stream-async")]
    public async Task Semantic_AzureCompletionTokenAdapterPreservesOptionsAcrossAllSdkCallShapes(string mode)
    {
        // Arrange
        var handler = new SemanticTransportHandler();
        using var http = new HttpClient(handler);
        var endpoint = new Uri("https://test.openai.azure.com/");
        var azure = new AzureOpenAIClient(endpoint, new ApiKeyCredential("unit-test-only"),
            new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) });
        var client = new AzureCompletionTokenChatClient(azure, "production-reasoning", endpoint);
        OpenAI.Chat.ChatMessage[] messages =
            [new OpenAI.Chat.UserChatMessage("""{"segments":[{"key":"s1"}]}""")];
        var options = new OpenAI.Chat.ChatCompletionOptions
        {
            MaxOutputTokenCount = 123,
            Temperature = 0,
            ResponseFormat = OpenAI.Chat.ChatResponseFormat.CreateJsonObjectFormat(),
            ToolChoice = OpenAI.Chat.ChatToolChoice.CreateNoneChoice()
        };
        options.Tools.Add(OpenAI.Chat.ChatTool.CreateFunctionTool("test_tool", "Synthetic unit test."));
        var original = ModelReaderWriter.Write(options).ToString();

        // Act
        switch (mode)
        {
            case "sync": client.CompleteChat(messages, options); break;
            case "async": await client.CompleteChatAsync(messages, options); break;
            case "stream-sync": _ = client.CompleteChatStreaming(messages, options).ToArray(); break;
            case "stream-async":
                await foreach (var update in client.CompleteChatStreamingAsync(messages, options))
                    update.Should().NotBeNull();
                break;
        }

        // Assert
        handler.Request.GetProperty("max_completion_tokens").GetInt32().Should().Be(123);
        handler.Request.TryGetProperty("max_tokens", out _).Should().BeFalse();
        handler.Request.GetProperty("temperature").GetInt32().Should().Be(0);
        handler.Request.GetProperty("tool_choice").GetString().Should().Be("none");
        handler.Request.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString().Should().Be("test_tool");
        handler.Request.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        ModelReaderWriter.Write(options).ToString().Should().Be(original);
    }

    private sealed class SemanticTransportHandler : HttpMessageHandler
    {
        public JsonElement Request { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Request = document.RootElement.Clone();
            var content = Request.GetProperty("messages").EnumerateArray().Last().GetProperty("content");
            var text = content.ValueKind == JsonValueKind.String
                ? content.GetString()! : content.EnumerateArray().Single().GetProperty("text").GetString()!;
            using var payload = JsonDocument.Parse(text);
            var answer = JsonSerializer.Serialize(new
            {
                analyzedSegmentKeys = payload.RootElement.GetProperty("segments").EnumerateArray()
                    .Select(segment => segment.GetProperty("key").GetString()).ToArray(),
                candidates = Array.Empty<object>(),
                familyCoverage = Enum.GetValues<CspPackageClaimFamily>()
                    .Select(family => new { family = family.ToString(), status = "NoDeclarations" })
            });
            if (!Request.TryGetProperty("stream", out var streaming) || !streaming.GetBoolean())
            {
                var completion = JsonSerializer.Serialize(new
                {
                    id = "transport-test", @object = "chat.completion", created = 0, model = "test",
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = answer }, finish_reason = "stop" } }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(completion, Encoding.UTF8, "application/json")
                };
            }
            var chunk = JsonSerializer.Serialize(new
            {
                id = "transport-test", @object = "chat.completion.chunk", created = 0, model = "test",
                choices = new[] { new { index = 0, delta = new { role = "assistant", content = answer }, finish_reason = (string?)null } }
            });
            var finish = """{"id":"transport-test","object":"chat.completion.chunk","created":0,"model":"test","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"data: {chunk}\n\ndata: {finish}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
