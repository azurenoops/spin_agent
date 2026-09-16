using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools;

/// <summary>MCP tool for authoring the policy narrative half.</summary>
public sealed class NarrativePolicyTool : BaseTool
{
    private readonly IDualNarrativeService _service;
    private readonly IServiceScopeFactory _scopeFactory;

    public NarrativePolicyTool(
        IDualNarrativeService service,
        IServiceScopeFactory scopeFactory,
        ILogger<NarrativePolicyTool> logger) : base(logger)
    {
        _service = service;
        _scopeFactory = scopeFactory;
    }

    public override string Name => "narrative_set_policy";
    public override string Description => "Author or update the policy/procedural narrative for a NIST control.";
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => NarrativeParameters("policy_narrative");

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var narrative = GetArg<string>(arguments, "policy_narrative");
        if (string.IsNullOrWhiteSpace(systemId) || string.IsNullOrWhiteSpace(controlId) || string.IsNullOrWhiteSpace(narrative))
        {
            return Error("INVALID_INPUT", "system_id, control_id, and policy_narrative are required.");
        }

        using var scope = _scopeFactory.CreateScope();
        var user = scope.ServiceProvider.GetService<IUserContext>() ?? AnonymousNarrativeUserContext.Instance;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await _service.UpdateAsync(
                systemId, controlId, narrative, true, null, false,
                user.Role, user.UserId, cancellationToken);
            return Success(Name, stopwatch, new
            {
                systemId = result.SystemId,
                controlId = result.ControlId,
                policyNarrative = result.PolicyNarrative
            });
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Error(ErrorCode(exception), exception.Message);
        }
    }

    private static IReadOnlyDictionary<string, ToolParameter> NarrativeParameters(string narrativeName) =>
        new Dictionary<string, ToolParameter>
        {
            ["system_id"] = new() { Name = "system_id", Description = "System identifier", Type = "string", Required = true },
            ["control_id"] = new() { Name = "control_id", Description = "NIST control identifier", Type = "string", Required = true },
            [narrativeName] = new() { Name = narrativeName, Description = "Narrative text (maximum 8000 characters)", Type = "string", Required = true }
        };

    internal static string Success(string tool, Stopwatch stopwatch, object data) => JsonSerializer.Serialize(new
    {
        status = "success",
        data,
        metadata = new { tool, duration_ms = stopwatch.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") }
    });

    internal static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message });

    internal static string ErrorCode(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "FORBIDDEN",
        ArgumentException => "VALIDATION_ERROR",
        _ when exception.Message.Contains(':') => exception.Message[..exception.Message.IndexOf(':')],
        _ => "OPERATION_FAILED"
    };
}

/// <summary>MCP tool for authoring the technical narrative half.</summary>
public sealed class NarrativeTechnicalTool : BaseTool
{
    private readonly IDualNarrativeService _service;
    private readonly IServiceScopeFactory _scopeFactory;

    public NarrativeTechnicalTool(
        IDualNarrativeService service,
        IServiceScopeFactory scopeFactory,
        ILogger<NarrativeTechnicalTool> logger) : base(logger)
    {
        _service = service;
        _scopeFactory = scopeFactory;
    }

    public override string Name => "narrative_set_technical";
    public override string Description => "Author or update the technical implementation narrative for a NIST control.";
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System identifier", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST control identifier", Type = "string", Required = true },
        ["technical_narrative"] = new() { Name = "technical_narrative", Description = "Narrative text (maximum 8000 characters)", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var narrative = GetArg<string>(arguments, "technical_narrative");
        if (string.IsNullOrWhiteSpace(systemId) || string.IsNullOrWhiteSpace(controlId) || string.IsNullOrWhiteSpace(narrative))
        {
            return NarrativePolicyTool.Error("INVALID_INPUT", "system_id, control_id, and technical_narrative are required.");
        }

        using var scope = _scopeFactory.CreateScope();
        var user = scope.ServiceProvider.GetService<IUserContext>() ?? AnonymousNarrativeUserContext.Instance;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await _service.UpdateAsync(
                systemId, controlId, null, false, narrative, true,
                user.Role, user.UserId, cancellationToken);
            return NarrativePolicyTool.Success(Name, stopwatch, new
            {
                systemId = result.SystemId,
                controlId = result.ControlId,
                technicalNarrative = result.TechnicalNarrative
            });
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            return NarrativePolicyTool.Error(NarrativePolicyTool.ErrorCode(exception), exception.Message);
        }
    }
}

/// <summary>MCP tool for manually classifying narrative evidence.</summary>
public sealed class EvidenceClassifyTool : BaseTool
{
    private readonly IDualNarrativeService _service;
    private readonly IServiceScopeFactory _scopeFactory;

    public EvidenceClassifyTool(
        IDualNarrativeService service,
        IServiceScopeFactory scopeFactory,
        ILogger<EvidenceClassifyTool> logger) : base(logger)
    {
        _service = service;
        _scopeFactory = scopeFactory;
    }

    public override string Name => "evidence_classify";
    public override string Description => "Classify evidence as Policy, Technical, Combined, or Unclassified.";
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["evidence_artifact_id"] = new() { Name = "evidence_artifact_id", Description = "Evidence artifact identifier", Type = "string", Required = true },
        ["narrative_type"] = new() { Name = "narrative_type", Description = "Policy, Technical, Combined, or Unclassified", Type = "string", Required = true },
        ["rationale"] = new() { Name = "rationale", Description = "Optional classification rationale", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var artifactId = GetArg<string>(arguments, "evidence_artifact_id");
        var narrativeTypeText = GetArg<string>(arguments, "narrative_type");
        var rationale = GetArg<string>(arguments, "rationale");
        if (string.IsNullOrWhiteSpace(artifactId) ||
            !Enum.TryParse<EvidenceNarrativeType>(narrativeTypeText, true, out var narrativeType) ||
            !Enum.IsDefined(narrativeType))
        {
            return NarrativePolicyTool.Error("INVALID_INPUT", "A valid evidence_artifact_id and narrative_type are required.");
        }

        using var scope = _scopeFactory.CreateScope();
        var user = scope.ServiceProvider.GetService<IUserContext>() ?? AnonymousNarrativeUserContext.Instance;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await _service.ClassifyEvidenceAsync(
                artifactId, narrativeType, rationale, user.UserId, cancellationToken);
            return NarrativePolicyTool.Success(Name, stopwatch, new
            {
                evidenceArtifactId = result.Id,
                result.FileName,
                narrativeType = result.NarrativeType.ToString(),
                result.AutoTagRationale,
                result.ManuallyTaggedBy
            });
        }
        catch (InvalidOperationException exception)
        {
            return NarrativePolicyTool.Error(NarrativePolicyTool.ErrorCode(exception), exception.Message);
        }
    }
}

internal sealed class AnonymousNarrativeUserContext : IUserContext
{
    public static readonly AnonymousNarrativeUserContext Instance = new();
    public string UserId => "anonymous";
    public string DisplayName => "anonymous";
    public string Role => "Compliance.Viewer";
    public bool IsAuthenticated => false;
}