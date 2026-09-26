using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Ato.Copilot.Core.Services;

/// <summary>Uses Azure's completion-token option without changing legacy deployments.</summary>
public sealed class AzureCompletionTokenChatClient : ChatClient
{
    private readonly ChatClient _inner;

    public AzureCompletionTokenChatClient(AzureOpenAIClient client, string deployment, Uri endpoint)
        : base(client.Pipeline, deployment, new OpenAIClientOptions { Endpoint = endpoint })
    {
        _inner = client.GetChatClient(deployment);
    }

    public override Task<ClientResult<ChatCompletion>> CompleteChatAsync(
        IEnumerable<ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default)
        => _inner.CompleteChatAsync(messages, CompletionOptions(options), cancellationToken);

    public override ClientResult<ChatCompletion> CompleteChat(
        IEnumerable<ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default)
        => _inner.CompleteChat(messages, CompletionOptions(options), cancellationToken);

    public override AsyncCollectionResult<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
        IEnumerable<ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default)
        => _inner.CompleteChatStreamingAsync(messages, CompletionOptions(options), cancellationToken);

    public override CollectionResult<StreamingChatCompletionUpdate> CompleteChatStreaming(
        IEnumerable<ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default)
        => _inner.CompleteChatStreaming(messages, CompletionOptions(options), cancellationToken);

    private static ChatCompletionOptions CompletionOptions(ChatCompletionOptions? options)
    {
        // Deserialization initializes the SDK's extension storage, which its opt-in
        // otherwise dereferences while null. Cloning also avoids mutating caller options.
        options = ModelReaderWriter.Read<ChatCompletionOptions>(
            ModelReaderWriter.Write(options ?? new ChatCompletionOptions()))!;
#pragma warning disable AOAI001 // Azure's supported opt-in prevents its legacy max_tokens rewrite for reasoning deployments.
        options.SetNewMaxCompletionTokensPropertyEnabled();
#pragma warning restore AOAI001
        return options;
    }
}
