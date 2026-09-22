using System.Text.Json;
using System.Threading.Channels;
using Ato.Copilot.Mcp.Resilience;

namespace Ato.Copilot.Mcp.Server;

/// <summary>Serializes progress, keepalives and results; observes writes instead of async-void callbacks.</summary>
internal sealed class ChatSseWriter : IProgress<string>, IAsyncDisposable
{
    private readonly Channel<(string? Data, string? Event)> _frames =
        Channel.CreateUnbounded<(string?, string?)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly JsonSerializerOptions _options;
    private readonly SseEventBuffer _buffer;
    private readonly string _sessionKey;
    private readonly Task _writer;

    public ChatSseWriter(HttpContext http, SseEventBuffer buffer, string key, JsonSerializerOptions options)
    {
        _options = options;
        _buffer = buffer;
        _sessionKey = key;
        _writer = WriteAsync(http, buffer, key);
    }

    public void Report(string value) => Enqueue(value.StartsWith("{") && value.Contains("\"type\"")
        ? value : JsonSerializer.Serialize(new { type = "progress", step = value }, _options));

    public void Enqueue(string? data, string? eventName = null)
    {
        if (!_frames.Writer.TryWrite((data, eventName)))
            throw new InvalidOperationException("Chat stream is already complete.");
    }

    private async Task WriteAsync(HttpContext http, SseEventBuffer buffer, string key)
    {
        await foreach (var (data, eventName) in _frames.Reader.ReadAllAsync(http.RequestAborted))
        {
            string frame;
            if (data is null)
                frame = ": keepalive\n\n";
            else
            {
                var evt = buffer.AddEvent(key, data);
                frame = $"id: {evt.Id}\n{(eventName is null ? "" : $"event: {eventName}\n")}data: {data}\n\n";
            }
            await http.Response.WriteAsync(frame, http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _frames.Writer.TryComplete();
        try
        {
            await _writer;
        }
        finally
        {
            _buffer.CompleteSession(_sessionKey);
        }
    }
}
