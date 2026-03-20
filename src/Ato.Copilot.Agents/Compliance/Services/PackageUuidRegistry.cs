using System.Security.Cryptography;
using System.Text;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Generates deterministic UUID v5 identifiers for OSCAL cross-artifact references.
/// Ensures the same component/party/role UUID appears in SSP, POA&amp;M, AR, and SAP
/// within a single authorization package generation.
/// </summary>
public class PackageUuidRegistry
{
    /// <summary>RFC 4122 URL namespace UUID used as the base namespace for all generated UUIDs.</summary>
    private static readonly Guid NamespaceUuid = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    private readonly string _packageId;
    private readonly Dictionary<string, Guid> _cache = new();

    public PackageUuidRegistry(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId, nameof(packageId));
        _packageId = packageId;
    }

    /// <summary>Get a deterministic UUID for an SSP document reference.</summary>
    public Guid SspUuid => GetOrCreate("ssp", "document");

    /// <summary>Get a deterministic UUID for a SAP document reference.</summary>
    public Guid SapUuid => GetOrCreate("assessment-plan", "document");

    /// <summary>Get a deterministic UUID for an Assessment Results document reference.</summary>
    public Guid AssessmentResultsUuid => GetOrCreate("assessment-results", "document");

    /// <summary>Get a deterministic UUID for a POA&amp;M document reference.</summary>
    public Guid PoamUuid => GetOrCreate("poam", "document");

    /// <summary>
    /// Get a deterministic UUID for a component within this package.
    /// </summary>
    public Guid ComponentUuid(string componentId) => GetOrCreate("component", componentId);

    /// <summary>
    /// Get a deterministic UUID for a party (person/organization) within this package.
    /// </summary>
    public Guid PartyUuid(string partyId) => GetOrCreate("party", partyId);

    /// <summary>
    /// Get a deterministic UUID for a responsible-role within this package.
    /// </summary>
    public Guid ResponsibleRoleUuid(string roleId) => GetOrCreate("responsible-role", roleId);

    /// <summary>
    /// Get a deterministic UUID for a control-implementation within this package.
    /// </summary>
    public Guid ControlImplementationUuid(string controlId) => GetOrCreate("control-implementation", controlId);

    /// <summary>
    /// Get a deterministic UUID for any entity type within this package.
    /// </summary>
    public Guid GetOrCreate(string entityType, string entityId)
    {
        var key = $"{_packageId}:{entityType}:{entityId}";

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var uuid = GenerateUuidV5(NamespaceUuid, key);
        _cache[key] = uuid;
        return uuid;
    }

    /// <summary>
    /// Generates a UUID v5 (name-based, SHA-1) per RFC 4122 §4.3.
    /// </summary>
    private static Guid GenerateUuidV5(Guid namespaceId, string name)
    {
        // Convert namespace UUID to bytes in network byte order (RFC 4122)
        var namespaceBytes = namespaceId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);

        byte[] hash;
        var combined = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, combined, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, combined, namespaceBytes.Length, nameBytes.Length);
        hash = SHA1.HashData(combined);

        // Set version to 5 (name-based SHA-1)
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        // Set variant to RFC 4122
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        // Convert back from network byte order to .NET Guid byte order
        var guidBytes = new byte[16];
        Buffer.BlockCopy(hash, 0, guidBytes, 0, 16);
        SwapGuidByteOrder(guidBytes);

        return new Guid(guidBytes);
    }

    /// <summary>
    /// Swaps byte order between .NET Guid representation and RFC 4122 network order.
    /// .NET stores the first three components in little-endian; RFC 4122 uses big-endian.
    /// </summary>
    private static void SwapGuidByteOrder(byte[] bytes)
    {
        // Swap first 4 bytes (Data1 - uint32)
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        // Swap bytes 4-5 (Data2 - uint16)
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        // Swap bytes 6-7 (Data3 - uint16)
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
        // bytes 8-15 are already in correct order
    }
}
