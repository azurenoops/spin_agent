using System.Text.Json;
using FluentAssertions;
using Xunit;
using Ato.Copilot.Mcp.Models;

namespace Ato.Copilot.Tests.Unit.Mcp;

/// <summary>
/// Tests for ErrorDetail model and McpChatResponse error construction (T025, FR-007).
/// </summary>
public class ErrorDetailTests
{
    [Fact]
    public void ErrorDetail_DefaultValues_AreCorrect()
    {
        var error = new ErrorDetail();

        error.ErrorCode.Should().BeEmpty();
        error.Message.Should().BeEmpty();
        error.Suggestion.Should().BeNull();
    }

    [Fact]
    public void ErrorDetail_WithAllFields_SerializesCorrectly()
    {
        var error = new ErrorDetail
        {
            ErrorCode = "AGENT_TIMEOUT",
            Message = "The agent timed out after 30 seconds.",
            Suggestion = "Try a simpler query or increase the timeout."
        };

        var json = JsonSerializer.Serialize(error, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        json.Should().Contain("\"errorCode\":\"AGENT_TIMEOUT\"");
        json.Should().Contain("\"message\":\"The agent timed out after 30 seconds.\"");
        json.Should().Contain("\"suggestion\":\"Try a simpler query or increase the timeout.\"");
    }

    [Fact]
    public void McpChatResponse_Errors_IsListOfErrorDetail()
    {
        var response = new McpChatResponse
        {
            Success = false,
            Errors = new List<ErrorDetail>
            {
                new() { ErrorCode = "TOOL_FAILURE", Message = "Tool failed", Suggestion = "Retry" },
                new() { ErrorCode = "UNAUTHORIZED", Message = "Access denied" }
            }
        };

        response.Errors.Should().HaveCount(2);
        response.Errors[0].ErrorCode.Should().Be("TOOL_FAILURE");
        response.Errors[0].Suggestion.Should().Be("Retry");
        response.Errors[1].ErrorCode.Should().Be("UNAUTHORIZED");
        response.Errors[1].Suggestion.Should().BeNull();
    }

    [Fact]
    public void McpChatResponse_Errors_DefaultIsEmptyList()
    {
        var response = new McpChatResponse();

        response.Errors.Should().NotBeNull();
        response.Errors.Should().BeEmpty();
    }

    [Fact]
    public void McpChatResponse_NewFields_HaveCorrectDefaults()
    {
        var response = new McpChatResponse();

        response.IntentType.Should().BeNull();
        response.FollowUpPrompt.Should().BeNull();
        response.MissingFields.Should().NotBeNull().And.BeEmpty();
        response.Data.Should().BeNull();
        response.SuggestedActions.Should().NotBeNull().And.BeEmpty();
        response.RequiresFollowUp.Should().BeFalse();
    }
}

// ── CorrelationId (epic/791) tests ────────────────────────────────────────────

public class ErrorDetailCorrelationIdTests
{
    [Fact]
    public void ErrorDetail_CorrelationId_DefaultsToNull()
    {
        var error = new ErrorDetail();
        error.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void ErrorDetail_WithCorrelationId_SerializesAndRoundTrips()
    {
        var traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        var error = new ErrorDetail
        {
            ErrorCode = "TENANT_UNRESOLVED",
            Message   = "No tenant context is available.",
            Suggestion = "Re-authenticate.",
            CorrelationId = traceId
        };

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(error, opts);
        json.Should().Contain($"\"correlationId\":\"{traceId}\"");

        var back = JsonSerializer.Deserialize<ErrorDetail>(json, opts)!;
        back.CorrelationId.Should().Be(traceId);
    }
}
