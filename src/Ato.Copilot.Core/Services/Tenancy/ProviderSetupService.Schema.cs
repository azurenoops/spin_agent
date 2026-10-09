using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ato.Copilot.Core.Services.Tenancy;

public sealed partial class ProviderSetupService
{
    internal const int CurrentSchemaVersion = 2;

    internal static JsonElement NormalizeDraftForRead(JsonElement stored, int schemaVersion)
    {
        if (stored.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Stored provider setup draft is unavailable.");

        var root = JsonNode.Parse(stored.GetRawText())!.AsObject();
        CopyAlias(root, "details", "providerIdentity", "providerDetails");
        CopyAlias(root, "securityContact", "contacts", "accessAndContacts");
        CopyAlias(root, "firstOffering", "offering", "serviceOffering");
        CopyAlias(root, "authorizationStartingPoint", "authorization", "authorizationIntent");
        CopyAlias(root, "sources", "sourceMaterial", "documents");

        EnsureObject(root, "details");
        if (root["operationalContact"] is null)
        {
            var details = root["details"]!.AsObject();
            var email = details["serviceContactEmail"]?.GetValue<string?>();
            root["operationalContact"] = string.IsNullOrWhiteSpace(email)
                ? new JsonObject { ["choice"] = "Unspecified" }
                : new JsonObject
                {
                    ["choice"] = "ContactOnly",
                    ["displayName"] = details["serviceContactName"]?.DeepClone(),
                    ["email"] = details["serviceContactEmail"]?.DeepClone(),
                    ["phone"] = details["supportPhone"]?.DeepClone()
                };
        }
        EnsureChoice(root, "securityContact", "Unspecified");
        EnsureChoice(root, "portfolio", "Unspecified");
        EnsureChoice(root, "firstOffering", "Unspecified");
        EnsureChoice(root, "authorizationStartingPoint", "DetermineLater");
        EnsureChoice(root, "sources", "Unspecified");
        EnsureObject(root, "review");

        var sources = root["sources"]!.AsObject();
        sources["intentIds"] ??= new JsonArray();
        var authorization = root["authorizationStartingPoint"]!.AsObject();
        authorization["existingDecision"] ??= new JsonObject { ["confirmed"] = false };
        authorization["unresolvedFields"] ??= new JsonArray();
        authorization["sources"] ??= new JsonArray();
        root["currentScreen"] = NormalizeScreen(root["currentScreen"]?.GetValue<string?>());
        root["schemaVersion"] = CurrentSchemaVersion;
        return JsonSerializer.SerializeToElement(root, JsonOptions);

        static void CopyAlias(JsonObject value, string canonical, params string[] aliases)
        {
            if (value[canonical] is not null) return;
            foreach (var alias in aliases)
            {
                if (value[alias] is null) continue;
                value[canonical] = value[alias]!.DeepClone();
                return;
            }
        }

        static void EnsureObject(JsonObject value, string property) =>
            value[property] ??= new JsonObject();

        static void EnsureChoice(JsonObject value, string property, string choice)
        {
            EnsureObject(value, property);
            value[property]!.AsObject()["choice"] ??= choice;
        }
    }

    private static string NormalizeScreen(string? screen) => screen switch
    {
        "provider-details" or "identity" => "p-details",
        "contacts" or "access" => "p-access",
        "portfolio" or "offering" => "p-offering",
        "authorization" or "authorization-starting-point" => "p-authorization",
        "documents" or "source-material" => "p-sources",
        "review" => "p-review",
        "ready" or "complete" => "p-ready",
        "p-details" or "p-access" or "p-offering" or "p-authorization" or "p-sources"
            or "p-uncertain" or "p-review" or "p-ready" => screen,
        null or "" => "p-details",
        _ => screen
    };
}
