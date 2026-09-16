using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools;

public sealed class GetControlValidationTool(
    IControlValidationLinkService service,
    ILogger<GetControlValidationTool> logger) : BaseTool(logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public override string Name => "compliance_get_control_validation";

    public override string Description =>
        "Get, add, or delete validation references for a system control implementation. RBAC: Compliance.Auditor.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "Registered system ID", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST control ID", Type = "string", Required = true },
        ["action"] = new() { Name = "action", Description = "get (default), add, or delete", Type = "string", Required = false },
        ["link_type"] = new() { Name = "link_type", Description = "AzureResource, ScanFinding, EvidenceArtifact, or ExternalUrl", Type = "string", Required = false },
        ["link_target"] = new() { Name = "link_target", Description = "Resource ID, finding reference, evidence ID, or URL", Type = "string", Required = false },
        ["description"] = new() { Name = "description", Description = "Optional description", Type = "string", Required = false },
        ["link_id"] = new() { Name = "link_id", Description = "Validation link ID for delete", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var action = (GetArg<string>(arguments, "action") ?? "get").Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(systemId) || string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'system_id' and 'control_id' parameters are required.");

        try
        {
            IReadOnlyList<ControlValidationLink> links;
            switch (action)
            {
                case "get":
                    links = await service.GetLinksAsync(systemId, controlId, cancellationToken);
                    break;
                case "add":
                    var linkTypeRaw = GetArg<string>(arguments, "link_type");
                    var linkTarget = GetArg<string>(arguments, "link_target");
                    if (!Enum.TryParse<ControlValidationLinkType>(linkTypeRaw, true, out var linkType)
                        || string.IsNullOrWhiteSpace(linkTarget))
                        return Error("INVALID_INPUT", "The 'link_type' and 'link_target' parameters are required for add.");
                    links =
                    [
                        await service.AddLinkAsync(
                            systemId,
                            controlId,
                            linkType,
                            linkTarget,
                            GetArg<string>(arguments, "description"),
                            "mcp-user",
                            cancellationToken),
                    ];
                    break;
                case "delete":
                    var linkId = GetArg<string>(arguments, "link_id");
                    if (string.IsNullOrWhiteSpace(linkId))
                        return Error("INVALID_INPUT", "The 'link_id' parameter is required for delete.");
                    if (!await service.DeleteLinkAsync(linkId, "mcp-user", cancellationToken))
                        return Error("NOT_FOUND", $"Validation link '{linkId}' was not found.");
                    links = [];
                    break;
                default:
                    return Error("INVALID_INPUT", $"Unsupported action '{action}'.");
            }

            stopwatch.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    links = links.Select(FormatLink),
                    total = links.Count,
                },
                metadata = new
                {
                    tool = Name,
                    duration_ms = stopwatch.ElapsedMilliseconds,
                    timestamp = DateTime.UtcNow.ToString("O"),
                },
            }, JsonOptions);
        }
        catch (ControlImplementationNotFoundException exception)
        {
            return Error("NOT_FOUND", exception.Message);
        }
        catch (DuplicateControlValidationLinkException exception)
        {
            return Error("ALREADY_EXISTS", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Error("INVALID_INPUT", exception.Message);
        }
    }

    private static object FormatLink(ControlValidationLink link) => new
    {
        id = link.Id,
        link_type = link.LinkType.ToString(),
        link_target = link.LinkTarget,
        description = link.Description,
        added_by = link.AddedBy,
        added_at = link.AddedAt.ToString("O"),
        validated_at = link.ValidatedAt?.ToString("O"),
        is_automated = link.IsAutomated,
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOptions);
}