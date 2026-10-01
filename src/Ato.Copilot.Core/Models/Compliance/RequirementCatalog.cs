using System.Text.Json;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>A requirement using its authoritative source identifier, label and unexpanded prose.</summary>
public sealed record CatalogRequirement(string Id, string? Label, string Text, string? ParentId);

/// <summary>An organization-defined parameter; definition retains choices, constraints and references.</summary>
public sealed record CatalogParameter(string Id, string Definition);

/// <summary>A catalog control, not a system implementation or a statement part.</summary>
public sealed record CatalogRequirementControl(
    string Id, string DisplayId, string Title, string Family, string? ParentId, bool Withdrawn,
    IReadOnlyList<CatalogRequirement> Requirements, IReadOnlyList<CatalogParameter> Parameters);

/// <summary>Reads source structure without interpreting display identifier syntax.</summary>
public sealed record RequirementCatalog(string Id, string Version, IReadOnlyList<CatalogRequirementControl> Controls)
{
    /// <summary>Parses the catalog object (or OSCAL catalog envelope), rejecting ambiguous identifiers.</summary>
    public static RequirementCatalog Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("catalog", out var wrapped)) root = wrapped;
        var id = Required(root, "uuid");
        if (!root.TryGetProperty("metadata", out var metadata))
            throw new ArgumentException("Catalog metadata is missing.");
        var version = Required(metadata, "version");
        var controls = new List<CatalogRequirementControl>();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        VisitContainer(root, string.Empty, controls, identifiers);
        if (controls.Count == 0) throw new ArgumentException("Catalog has no controls.");
        if (controls.Select(x => x.DisplayId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != controls.Count)
            throw new ArgumentException("Catalog has duplicate display identifiers.");
        return new(id, version, controls);
    }

    private static void VisitContainer(JsonElement container, string family,
        List<CatalogRequirementControl> controls, HashSet<string> identifiers)
    {
        foreach (var control in Children(container, "controls"))
            VisitControl(control, family, null, controls, identifiers);
        foreach (var group in Children(container, "groups"))
            VisitContainer(group, Text(group, "id") ?? family, controls, identifiers);
    }

    private static void VisitControl(JsonElement element, string family, string? parentId,
        List<CatalogRequirementControl> controls, HashSet<string> identifiers)
    {
        var id = Required(element, "id");
        Unique(identifiers, id);
        var requirements = new List<CatalogRequirement>();
        foreach (var part in Children(element, "parts"))
            VisitPart(part, false, null, requirements, identifiers);
        var parameters = Children(element, "params")
            .Select(parameter => new CatalogParameter(Required(parameter, "id"), parameter.GetRawText())).ToArray();
        controls.Add(new(id, Property(element, "label") ?? id, Required(element, "title"), family, parentId,
            Property(element, "status") == "withdrawn", requirements, parameters));
        foreach (var child in Children(element, "controls"))
            VisitControl(child, family, id, controls, identifiers);
    }

    private static void VisitPart(JsonElement part, bool withinStatement, string? parentId,
        List<CatalogRequirement> requirements, HashSet<string> identifiers)
    {
        var isStatement = withinStatement || Text(part, "name") == "statement";
        if (!isStatement) return;
        var id = Required(part, "id");
        Unique(identifiers, id);
        var prose = Text(part, "prose");
        if (!string.IsNullOrWhiteSpace(prose))
            requirements.Add(new(id, Property(part, "label"), prose, parentId));
        foreach (var child in Children(part, "parts"))
            VisitPart(child, true, id, requirements, identifiers);
    }

    private static void Unique(HashSet<string> identifiers, string id)
    {
        if (!identifiers.Add(id)) throw new ArgumentException($"Catalog has duplicate identifier '{id}'.");
    }

    private static string? Property(JsonElement element, string name) =>
        Children(element, "props").Where(x => Text(x, "name") == name)
            .OrderBy(x => Text(x, "class") is null ? 0 : 1)
            .Select(x => Text(x, "value")).FirstOrDefault();

    private static string Required(JsonElement element, string name) =>
        Text(element, name) is { Length: > 0 } value
            ? value : throw new ArgumentException($"Catalog source is missing '{name}'.");

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<JsonElement> Children(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"Catalog '{name}' must be an array.");
        return value.EnumerateArray();
    }
}
