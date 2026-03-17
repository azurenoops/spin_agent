using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Tools;

public class DeviationToolsTests
{
    private readonly Mock<IDeviationService> _svc = new();

    // ─── RequestDeviationTool ────────────────────────────────────────────────

    [Fact]
    public async Task Request_ValidInput_ReturnsSuccess()
    {
        var deviation = MakeDeviation();
        _svc.Setup(s => s.CreateDeviationAsync(
                "sys-1", It.IsAny<CreateDeviationRequest>(), "mcp-user", default))
            .ReturnsAsync(deviation);

        var tool = new RequestDeviationTool(_svc.Object, Mock.Of<ILogger<RequestDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["finding_id"] = "f-1",
            ["control_id"] = "AC-2",
            ["deviation_type"] = "FalsePositive",
            ["cat_severity"] = "CatII",
            ["justification"] = "Not applicable",
            ["expiration_date"] = "2025-12-31",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("controlId").GetString().Should().Be("AC-2");
    }

    [Fact]
    public async Task Request_MissingSystemId_ReturnsError()
    {
        var tool = new RequestDeviationTool(_svc.Object, Mock.Of<ILogger<RequestDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["finding_id"] = "f-1",
            ["control_id"] = "AC-2",
            ["deviation_type"] = "FalsePositive",
            ["cat_severity"] = "CatII",
            ["justification"] = "Test",
            ["expiration_date"] = "2025-12-31",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task Request_InvalidDate_ReturnsError()
    {
        var tool = new RequestDeviationTool(_svc.Object, Mock.Of<ILogger<RequestDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["finding_id"] = "f-1",
            ["control_id"] = "AC-2",
            ["deviation_type"] = "FalsePositive",
            ["cat_severity"] = "CatII",
            ["justification"] = "Test",
            ["expiration_date"] = "not-a-date",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task Request_DuplicateDeviation_ReturnsError()
    {
        _svc.Setup(s => s.CreateDeviationAsync(
                "sys-1", It.IsAny<CreateDeviationRequest>(), "mcp-user", default))
            .ThrowsAsync(new InvalidOperationException("DUPLICATE_DEVIATION"));

        var tool = new RequestDeviationTool(_svc.Object, Mock.Of<ILogger<RequestDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["finding_id"] = "f-1",
            ["control_id"] = "AC-2",
            ["deviation_type"] = "FalsePositive",
            ["cat_severity"] = "CatII",
            ["justification"] = "Test",
            ["expiration_date"] = "2025-12-31",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("message").GetString().Should().Contain("DUPLICATE_DEVIATION");
    }

    // ─── ReviewDeviationTool ─────────────────────────────────────────────────

    [Fact]
    public async Task Review_Approve_ReturnsSuccess()
    {
        var deviation = MakeDeviation(DeviationStatus.Approved);
        _svc.Setup(s => s.ReviewDeviationAsync(
                "dev-1", It.IsAny<ReviewDeviationRequest>(), "mcp-user", "AO", default))
            .ReturnsAsync(deviation);

        var tool = new ReviewDeviationTool(_svc.Object, Mock.Of<ILogger<ReviewDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["deviation_id"] = "dev-1",
            ["decision"] = "Approved",
            ["reviewer_role"] = "AO",
            ["comments"] = "Looks good",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("newStatus").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task Review_NotFound_ReturnsDeviationNotFound()
    {
        _svc.Setup(s => s.ReviewDeviationAsync(
                "dev-99", It.IsAny<ReviewDeviationRequest>(), "mcp-user", "AO", default))
            .ThrowsAsync(new InvalidOperationException("Deviation not found"));

        var tool = new ReviewDeviationTool(_svc.Object, Mock.Of<ILogger<ReviewDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["deviation_id"] = "dev-99",
            ["decision"] = "Approved",
            ["reviewer_role"] = "AO",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("DEVIATION_NOT_FOUND");
    }

    [Fact]
    public async Task Review_NotPending_ReturnsNotPending()
    {
        _svc.Setup(s => s.ReviewDeviationAsync(
                "dev-1", It.IsAny<ReviewDeviationRequest>(), "mcp-user", "AO", default))
            .ThrowsAsync(new InvalidOperationException("Deviation is not in Pending status"));

        var tool = new ReviewDeviationTool(_svc.Object, Mock.Of<ILogger<ReviewDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["deviation_id"] = "dev-1",
            ["decision"] = "Approved",
            ["reviewer_role"] = "AO",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("NOT_PENDING");
    }

    [Fact]
    public async Task Review_MissingDecision_ReturnsInvalidInput()
    {
        var tool = new ReviewDeviationTool(_svc.Object, Mock.Of<ILogger<ReviewDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["deviation_id"] = "dev-1",
            ["reviewer_role"] = "AO",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    // ─── ListDeviationsTool ──────────────────────────────────────────────────

    [Fact]
    public async Task List_ValidInput_ReturnsItems()
    {
        var response = new DeviationListResponse
        {
            TotalCount = 1,
            Items =
            [
                new DeviationListItem
                {
                    Id = "dev-1",
                    ControlId = "AC-2",
                    DeviationType = "FalsePositive",
                    CatSeverity = 1,
                    Status = "Approved",
                    Justification = "Test",
                    ExpirationDate = DateTime.UtcNow.AddDays(30),
                    DaysUntilExpiration = 30,
                    RequestedBy = "user",
                    RequestedAt = DateTime.UtcNow,
                    EvidenceCount = 2,
                },
            ],
        };

        _svc.Setup(s => s.ListDeviationsAsync(
                "sys-1", null, null, null, null, null, 1, 20, default))
            .ReturnsAsync(response);

        var tool = new ListDeviationsTool(_svc.Object, Mock.Of<ILogger<ListDeviationsTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task List_MissingSystemId_ReturnsError()
    {
        var tool = new ListDeviationsTool(_svc.Object, Mock.Of<ILogger<ListDeviationsTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    // ─── RevokeDeviationTool ─────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_ValidInput_ReturnsSuccess()
    {
        var deviation = MakeDeviation(DeviationStatus.Revoked);
        _svc.Setup(s => s.RevokeDeviationAsync(
                "dev-1", It.IsAny<RevokeDeviationRequest>(), "mcp-user", default))
            .ReturnsAsync(deviation);

        var tool = new RevokeDeviationTool(_svc.Object, Mock.Of<ILogger<RevokeDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["deviation_id"] = "dev-1",
            ["reason"] = "No longer needed",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("newStatus").GetString().Should().Be("Revoked");
    }

    [Fact]
    public async Task Revoke_NotFound_ReturnsDeviationNotFound()
    {
        _svc.Setup(s => s.RevokeDeviationAsync(
                "dev-99", It.IsAny<RevokeDeviationRequest>(), "mcp-user", default))
            .ThrowsAsync(new InvalidOperationException("Deviation not found"));

        var tool = new RevokeDeviationTool(_svc.Object, Mock.Of<ILogger<RevokeDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["deviation_id"] = "dev-99",
            ["reason"] = "Test",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("DEVIATION_NOT_FOUND");
    }

    [Fact]
    public async Task Revoke_MissingReason_ReturnsError()
    {
        var tool = new RevokeDeviationTool(_svc.Object, Mock.Of<ILogger<RevokeDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["deviation_id"] = "dev-1",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    // ─── ExtendDeviationTool ─────────────────────────────────────────────────

    [Fact]
    public async Task Extend_ValidInput_ReturnsSuccess()
    {
        var deviation = MakeDeviation(DeviationStatus.Approved);
        deviation.ExpirationDate = DateTime.UtcNow.AddDays(180);
        _svc.Setup(s => s.ExtendDeviationAsync(
                "dev-1", It.IsAny<ExtendDeviationRequest>(), "mcp-user", default))
            .ReturnsAsync(deviation);

        var tool = new ExtendDeviationTool(_svc.Object, Mock.Of<ILogger<ExtendDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["deviation_id"] = "dev-1",
            ["new_expiration_date"] = "2026-06-30",
            ["justification"] = "Still needed",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("id").GetString().Should().Be("dev-1");
    }

    [Fact]
    public async Task Extend_InvalidDate_ReturnsError()
    {
        var tool = new ExtendDeviationTool(_svc.Object, Mock.Of<ILogger<ExtendDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["deviation_id"] = "dev-1",
            ["new_expiration_date"] = "bad-date",
            ["justification"] = "Test",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task Extend_NotFound_ReturnsDeviationNotFound()
    {
        _svc.Setup(s => s.ExtendDeviationAsync(
                "dev-99", It.IsAny<ExtendDeviationRequest>(), "mcp-user", default))
            .ThrowsAsync(new InvalidOperationException("Deviation not found"));

        var tool = new ExtendDeviationTool(_svc.Object, Mock.Of<ILogger<ExtendDeviationTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["deviation_id"] = "dev-99",
            ["new_expiration_date"] = "2026-06-30",
            ["justification"] = "Test",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("DEVIATION_NOT_FOUND");
    }

    // ─── Tool Metadata ──────────────────────────────────────────────────────

    [Fact]
    public void Request_ToolMetadata_IsCorrect()
    {
        var tool = new RequestDeviationTool(_svc.Object, Mock.Of<ILogger<RequestDeviationTool>>());
        tool.Name.Should().Be("compliance_request_deviation");
        tool.Parameters.Should().ContainKey("system_id");
        tool.Parameters["system_id"].Required.Should().BeTrue();
        tool.Parameters.Should().ContainKey("finding_id");
        tool.Parameters.Should().ContainKey("deviation_type");
    }

    [Fact]
    public void Review_ToolMetadata_IsCorrect()
    {
        var tool = new ReviewDeviationTool(_svc.Object, Mock.Of<ILogger<ReviewDeviationTool>>());
        tool.Name.Should().Be("compliance_review_deviation");
        tool.Parameters.Should().ContainKey("deviation_id");
        tool.Parameters.Should().ContainKey("decision");
        tool.Parameters.Should().ContainKey("reviewer_role");
    }

    [Fact]
    public void List_ToolMetadata_IsCorrect()
    {
        var tool = new ListDeviationsTool(_svc.Object, Mock.Of<ILogger<ListDeviationsTool>>());
        tool.Name.Should().Be("compliance_list_deviations");
        tool.Parameters.Should().ContainKey("system_id");
        tool.Parameters["system_id"].Required.Should().BeTrue();
    }

    [Fact]
    public void Revoke_ToolMetadata_IsCorrect()
    {
        var tool = new RevokeDeviationTool(_svc.Object, Mock.Of<ILogger<RevokeDeviationTool>>());
        tool.Name.Should().Be("compliance_revoke_deviation");
        tool.Parameters.Should().ContainKey("deviation_id");
        tool.Parameters.Should().ContainKey("reason");
    }

    [Fact]
    public void Extend_ToolMetadata_IsCorrect()
    {
        var tool = new ExtendDeviationTool(_svc.Object, Mock.Of<ILogger<ExtendDeviationTool>>());
        tool.Name.Should().Be("compliance_extend_deviation");
        tool.Parameters.Should().ContainKey("deviation_id");
        tool.Parameters.Should().ContainKey("new_expiration_date");
        tool.Parameters.Should().ContainKey("justification");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Deviation MakeDeviation(DeviationStatus status = DeviationStatus.Pending) => new()
    {
        Id = "dev-1",
        RegisteredSystemId = "sys-1",
        DeviationType = DeviationType.FalsePositive,
        ControlId = "AC-2",
        CatSeverity = CatSeverity.CatII,
        Status = status,
        Justification = "Not applicable",
        ExpirationDate = DateTime.UtcNow.AddDays(90),
        ReviewCycle = "180d",
        RequestedBy = "mcp-user",
        CreatedAt = DateTime.UtcNow,
    };
}
