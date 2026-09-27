using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

/// <summary>Two explicit hosting identities; legacy records without a discriminator remain Azure.</summary>
[JsonConverter(typeof(ProviderScopeJsonConverter))]
public abstract record ProviderScope
{
    [JsonIgnore] public abstract string Kind { get; }
    [JsonIgnore] public abstract string ScopeEnvironment { get; }
}

[JsonConverter(typeof(ProviderScopeJsonConverter))]
public sealed record ProviderAzureScope(string Cloud, Guid DirectoryTenantId, Guid SubscriptionId, string ResourceId) : ProviderScope
{
    [JsonIgnore] public override string Kind => "Azure";
    [JsonIgnore] public override string ScopeEnvironment => Cloud;
}

/// <summary>A manually reviewed service instance, never an assertion that a cloud connector exists.</summary>
[JsonConverter(typeof(ProviderScopeJsonConverter))]
public sealed record ProviderServiceScope(string ServiceId, string ServiceName, string Environment, string? TenantReference = null) : ProviderScope
{
    [JsonIgnore] public override string Kind => "Service";
    [JsonIgnore] public override string ScopeEnvironment => Environment;
}

public sealed class ProviderScopeJsonConverter : JsonConverter<ProviderScope>
{
    public override bool CanConvert(Type typeToConvert) => typeof(ProviderScope).IsAssignableFrom(typeToConvert);
    public override ProviderScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("A hosting scope must be an object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!fields.TryAdd(property.Name, property.Value)) throw new JsonException("Duplicate hosting scope fields are ambiguous.");

        string Required(string name) => fields.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new JsonException($"Hosting scope requires {name}.");
        Guid Identifier(string name) => fields.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.TryGetGuid(out var id) ? id : throw new JsonException($"Azure scope requires a JSON GUID for {name}.");
        var kind = fields.ContainsKey("kind") ? Required("kind") : "Azure";
        if (typeToConvert == typeof(ProviderAzureScope) && kind != "Azure"
            || typeToConvert == typeof(ProviderServiceScope) && kind != "Service")
            throw new JsonException("The scope kind does not match the requested scope type.");
        if (kind == "Service")
        {
            if (new[] { "cloud", "directoryTenantId", "subscriptionId", "resourceId" }.Any(fields.ContainsKey))
                throw new JsonException("A service relationship cannot contain Azure scope identifiers.");
            var tenant = fields.TryGetValue("tenantReference", out var value) && value.ValueKind != JsonValueKind.Null
                ? Required("tenantReference") : null;
            return new ProviderServiceScope(Required("serviceId"), Required("serviceName"), Required("environment"), tenant);
        }
        if (kind != "Azure" || new[] { "serviceId", "serviceName", "environment", "tenantReference" }.Any(fields.ContainsKey))
            throw new JsonException("Choose one supported scope kind: Azure or Service.");
        return new ProviderAzureScope(Required("cloud"), Identifier("directoryTenantId"), Identifier("subscriptionId"), Required("resourceId"));
    }

    public override void Write(Utf8JsonWriter writer, ProviderScope value, JsonSerializerOptions options)
    {
        string Name(string property) => options.PropertyNamingPolicy?.ConvertName(property) ?? property;
        writer.WriteStartObject();
        switch (value)
        {
            case ProviderAzureScope azure:
                // Keep the original Azure material byte-for-byte compatible for retained snapshot hashes.
                writer.WriteString(Name(nameof(azure.Cloud)), azure.Cloud);
                writer.WriteString(Name(nameof(azure.DirectoryTenantId)), azure.DirectoryTenantId);
                writer.WriteString(Name(nameof(azure.SubscriptionId)), azure.SubscriptionId);
                writer.WriteString(Name(nameof(azure.ResourceId)), azure.ResourceId);
                break;
            case ProviderServiceScope service:
                writer.WriteString("kind", "Service");
                writer.WriteString(Name(nameof(service.ServiceId)), service.ServiceId);
                writer.WriteString(Name(nameof(service.ServiceName)), service.ServiceName);
                writer.WriteString(Name(nameof(service.Environment)), service.Environment);
                writer.WriteString(Name(nameof(service.TenantReference)), service.TenantReference);
                break;
            default:
                throw new JsonException("Unsupported hosting scope kind.");
        }
        writer.WriteEndObject();
    }
}
