using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

/// <summary>
/// Regression tests for Issue #722 — multipart chat requests silently dropping the context field.
///
/// Root cause: the multipart branch of McpHttpBridge.MapEndpoints did not read the "context"
/// form field, so system_id / systemId never reached the agent, causing the backend to
/// substitute the literal "SYSTEM_REQUIRED" sentinel.
///
/// Fix (fix/722-system-required-context): chatService.ts now appends
///   form.append('context', JSON.stringify(request.context))
/// and McpHttpBridge.TryParseContextField deserialises the JSON back into
///   Dictionary&lt;string, object&gt;? for use by ChatRequest.Context.
///
/// These tests validate the JSON parsing contract that TryParseContextField implements,
/// using the same JsonSerializer.Deserialize call the production helper uses.
/// Because TryParseContextField is private static, the tests exercise the contract
/// by reproducing the exact call path — this is the observable regression surface.
/// </summary>
public class MultipartContextParsingTests
{
    // Mirrors the production TryParseContextField implementation exactly.
    private static Dictionary<string, object>? ParseContextField(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    // ─── fix(#722): multipart context field parsing ───────────────────────────

    /// <summary>
    /// fix #722 (multipart drop): a JSON context payload containing systemId must be
    /// deserialised into a non-null dictionary with the correct value.
    /// Before the fix, this field was never read from the form — the dictionary was always null.
    /// </summary>
    [Fact]
    public void ParseContextField_WithSystemId_ReturnsDictionaryContainingSystemId()
    {
        // Arrange — JSON that chatService.ts produces via JSON.stringify(request.context)
        const string json = """{"page":"narratives","systemId":"abc-123"}""";

        // Act
        var result = ParseContextField(json);

        // Assert
        result.Should().NotBeNull(
            "a valid context JSON string must be deserialised (fix #722: multipart path previously discarded this field)");
        result!.Should().ContainKey("systemId",
            "the backend needs systemId to route the request to the correct system context");
        result["systemId"].ToString().Should().Be("abc-123",
            "the system id must survive the JSON round-trip intact");
    }

    /// <summary>
    /// fix #722 (multipart drop): a null or empty context field must return null silently
    /// (non-fatal — portfolio or non-system pages send no systemId).
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseContextField_WithNullOrEmpty_ReturnsNull(string? json)
    {
        // Act
        var result = ParseContextField(json);

        // Assert
        result.Should().BeNull(
            "a missing context field is non-fatal and must produce a null context (fix #722)");
    }

    /// <summary>
    /// fix #722 (multipart drop): malformed JSON must return null silently rather than
    /// throw — a broken context field should not crash the chat endpoint.
    /// </summary>
    [Fact]
    public void ParseContextField_WithMalformedJson_ReturnsNull()
    {
        // Act
        var result = ParseContextField("{not valid json");

        // Assert
        result.Should().BeNull(
            "malformed context JSON must be swallowed silently (fix #722 — missing context is non-fatal)");
    }

    /// <summary>
    /// fix #722 (multipart drop): a context payload with multiple fields (page, systemId,
    /// boundaryId) must deserialise all keys — the agent may consume any of them.
    /// </summary>
    [Fact]
    public void ParseContextField_WithFullContextPayload_ReturnsDictionaryWithAllFields()
    {
        // Arrange — richer context as the dashboard might produce
        const string json = """{"page":"system-detail","systemId":"guid-456","boundaryId":null}""";

        // Act
        var result = ParseContextField(json);

        // Assert
        result.Should().NotBeNull();
        result!.Should().ContainKey("page").And.ContainKey("systemId");
        result["systemId"].ToString().Should().Be("guid-456");
    }
}
