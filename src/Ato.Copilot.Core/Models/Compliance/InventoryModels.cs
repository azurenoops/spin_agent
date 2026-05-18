using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ─── Enums ───────────────────────────────────────────────────────────────────

/// <summary>Inventory item type discriminator.</summary>
public enum InventoryItemType
{
    /// <summary>Physical or virtual hardware component.</summary>
    Hardware,

    /// <summary>Software product or application.</summary>
    Software
}

/// <summary>Inventory item lifecycle status.</summary>
public enum InventoryItemStatus
{
    /// <summary>Item is currently active and in use.</summary>
    Active,

    /// <summary>Item has been retired/removed from service.</summary>
    Decommissioned
}

/// <summary>Function classification for hardware inventory items.</summary>
public enum HardwareFunction
{
    /// <summary>Server (physical or virtual).</summary>
    Server,

    /// <summary>End-user workstation or laptop.</summary>
    Workstation,

    /// <summary>Network device (router, switch, firewall, load balancer).</summary>
    NetworkDevice,

    /// <summary>Storage device (SAN, NAS, disk array).</summary>
    Storage,

    /// <summary>Other hardware not classified above.</summary>
    Other
}

/// <summary>Function classification for software inventory items.</summary>
public enum SoftwareFunction
{
    /// <summary>Operating system.</summary>
    OperatingSystem,

    /// <summary>Database management system.</summary>
    Database,

    /// <summary>Middleware / application server.</summary>
    Middleware,

    /// <summary>Application software.</summary>
    Application,

    /// <summary>Security tool (AV, IDS, SIEM, etc.).</summary>
    SecurityTool,

    /// <summary>Other software not classified above.</summary>
    Other
}

// ─── Entity ──────────────────────────────────────────────────────────────────

/// <summary>
/// A single hardware or software component within a system's authorization boundary.
/// Tracks eMASS-required fields for HW/SW inventory and SSP integration.
/// </summary>
[TenantScoped]
public class InventoryItem
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the registered system this item belongs to.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Display name (e.g., "Web Server 01", "Red Hat Enterprise Linux 9").</summary>
    [Required]
    [MaxLength(300)]
    public string ItemName { get; set; } = string.Empty;

    /// <summary>Hardware or Software discriminator.</summary>
    [Required]
    public InventoryItemType Type { get; set; }

    /// <summary>Function classification for hardware items. Null for software items.</summary>
    public HardwareFunction? HardwareFunction { get; set; }

    /// <summary>Function classification for software items. Null for hardware items.</summary>
    public SoftwareFunction? SoftwareFunction { get; set; }

    /// <summary>Hardware manufacturer (e.g., "Dell", "Cisco").</summary>
    [MaxLength(300)]
    public string? Manufacturer { get; set; }

    /// <summary>Hardware model (e.g., "PowerEdge R740").</summary>
    [MaxLength(300)]
    public string? Model { get; set; }

    /// <summary>Hardware serial number.</summary>
    [MaxLength(200)]
    public string? SerialNumber { get; set; }

    /// <summary>IPv4 or IPv6 address (max length 45 covers full IPv6 notation).</summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>MAC address in colon-separated hex (e.g., 00:1A:2B:3C:4D:5E).</summary>
    [MaxLength(17)]
    public string? MacAddress { get; set; }

    /// <summary>Physical or logical location.</summary>
    [MaxLength(500)]
    public string? Location { get; set; }

    /// <summary>Software vendor (e.g., "Microsoft", "Red Hat").</summary>
    [MaxLength(300)]
    public string? Vendor { get; set; }

    /// <summary>Software version (e.g., "9.3", "2022 SP1").</summary>
    [MaxLength(100)]
    public string? Version { get; set; }

    /// <summary>Current patch level or update identifier.</summary>
    [MaxLength(200)]
    public string? PatchLevel { get; set; }

    /// <summary>License type (e.g., "Enterprise", "Open Source", "Government").</summary>
    [MaxLength(200)]
    public string? LicenseType { get; set; }

    /// <summary>Lifecycle status (Active or Decommissioned).</summary>
    [Required]
    public InventoryItemStatus Status { get; set; } = InventoryItemStatus.Active;

    /// <summary>
    /// Optional FK to parent hardware item (for software installed on hardware).
    /// Null for standalone/SaaS/PaaS software or hardware items.
    /// </summary>
    [MaxLength(36)]
    public string? ParentHardwareId { get; set; }

    /// <summary>
    /// Optional FK to AuthorizationBoundary entry for auto-seed idempotency.
    /// </summary>
    [MaxLength(36)]
    public string? BoundaryResourceId { get; set; }

    /// <summary>UTC date when the item was decommissioned.</summary>
    public DateTime? DecommissionDate { get; set; }

    /// <summary>Reason for decommissioning.</summary>
    [MaxLength(2000)]
    public string? DecommissionRationale { get; set; }

    /// <summary>User who created the item.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who last modified the item.</summary>
    [MaxLength(200)]
    public string? ModifiedBy { get; set; }

    /// <summary>Last modification timestamp (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>The registered system this item belongs to.</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }

    /// <summary>Parent hardware item (for software items installed on hardware).</summary>
    public InventoryItem? ParentHardware { get; set; }

    /// <summary>Software items installed on this hardware item (inverse of ParentHardware).</summary>
    public ICollection<InventoryItem> InstalledSoftware { get; set; } = new List<InventoryItem>();

    /// <summary>Optional linked authorization boundary resource.</summary>
    public AuthorizationBoundary? BoundaryResource { get; set; }
}

// ─── Input Types ─────────────────────────────────────────────────────────────

/// <summary>
/// Input DTO for adding or updating an inventory item.
/// For updates, null fields are not changed.
/// </summary>
public class InventoryItemInput
{
    /// <summary>Display name.</summary>
    public string? ItemName { get; set; }

    /// <summary>Hardware or Software.</summary>
    public InventoryItemType? Type { get; set; }

    /// <summary>HW function classification.</summary>
    public HardwareFunction? HardwareFunction { get; set; }

    /// <summary>SW function classification.</summary>
    public SoftwareFunction? SoftwareFunction { get; set; }

    /// <summary>HW manufacturer.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>HW model.</summary>
    public string? Model { get; set; }

    /// <summary>HW serial number.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>IPv4/IPv6 address.</summary>
    public string? IpAddress { get; set; }

    /// <summary>MAC address.</summary>
    public string? MacAddress { get; set; }

    /// <summary>Physical/logical location.</summary>
    public string? Location { get; set; }

    /// <summary>SW vendor.</summary>
    public string? Vendor { get; set; }

    /// <summary>SW version.</summary>
    public string? Version { get; set; }

    /// <summary>SW patch level.</summary>
    public string? PatchLevel { get; set; }

    /// <summary>License type.</summary>
    public string? LicenseType { get; set; }

    /// <summary>Parent HW item ID (for SW).</summary>
    public string? ParentHardwareId { get; set; }
}

/// <summary>
/// Options for listing and filtering inventory items.
/// </summary>
public class InventoryListOptions
{
    /// <summary>Filter by item type (Hardware or Software).</summary>
    public InventoryItemType? Type { get; set; }

    /// <summary>Filter by function name (matches HardwareFunction or SoftwareFunction enum name).</summary>
    public string? Function { get; set; }

    /// <summary>Filter by vendor/manufacturer (contains match).</summary>
    public string? Vendor { get; set; }

    /// <summary>Filter by status. Default is Active. Null returns all.</summary>
    public InventoryItemStatus? Status { get; set; } = InventoryItemStatus.Active;

    /// <summary>Free-text search on item name (contains match).</summary>
    public string? SearchText { get; set; }

    /// <summary>Results per page (default 50).</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Page number (1-based, default 1).</summary>
    public int PageNumber { get; set; } = 1;
}

/// <summary>
/// Options for exporting inventory to eMASS-compatible Excel.
/// </summary>
public class InventoryExportOptions
{
    /// <summary>Export type: "hardware", "software", or "all" (default).</summary>
    public string ExportType { get; set; } = "all";

    /// <summary>Include decommissioned items (default false).</summary>
    public bool IncludeDecommissioned { get; set; }
}

// ─── Computed Types (Not Persisted) ──────────────────────────────────────────

/// <summary>
/// Result of a completeness check on a system's inventory.
/// Not stored in the database — computed on demand.
/// </summary>
public class InventoryCompleteness
{
    /// <summary>The system being checked.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Total active inventory items.</summary>
    public int TotalItems { get; set; }

    /// <summary>Total active hardware items.</summary>
    public int HardwareCount { get; set; }

    /// <summary>Total active software items.</summary>
    public int SoftwareCount { get; set; }

    /// <summary>Items missing required fields per FR-018.</summary>
    public IReadOnlyList<InventoryIssue> ItemsWithMissingFields { get; set; } = [];

    /// <summary>Boundary resources with no corresponding inventory item.</summary>
    public IReadOnlyList<UnmatchedBoundaryResource> UnmatchedBoundaryResources { get; set; } = [];

    /// <summary>IDs of hardware items with no installed software children.</summary>
    public IReadOnlyList<string> HardwareWithoutSoftware { get; set; } = [];

    /// <summary>Percentage: (items without issues) / total items × 100.</summary>
    public double CompletenessScore { get; set; }

    /// <summary>True if no issues found across all three dimensions.</summary>
    public bool IsComplete { get; set; }
}

/// <summary>
/// An inventory item that has missing required fields.
/// </summary>
public class InventoryIssue
{
    /// <summary>The inventory item ID.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>The inventory item name.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>List of field names that are missing.</summary>
    public IReadOnlyList<string> MissingFields { get; set; } = [];
}

/// <summary>
/// A boundary resource with no corresponding inventory item.
/// </summary>
public class UnmatchedBoundaryResource
{
    /// <summary>The AuthorizationBoundary.Id.</summary>
    public string BoundaryResourceId { get; set; } = string.Empty;

    /// <summary>The boundary resource display name.</summary>
    public string? ResourceName { get; set; }

    /// <summary>The Azure resource type string.</summary>
    public string ResourceType { get; set; } = string.Empty;
}

/// <summary>
/// Result of an Excel import operation.
/// </summary>
public class InventoryImportResult
{
    /// <summary>Target system ID.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Whether this was a dry-run (no data persisted).</summary>
    public bool DryRun { get; set; }

    /// <summary>Number of hardware items created.</summary>
    public int HardwareCreated { get; set; }

    /// <summary>Number of software items created.</summary>
    public int SoftwareCreated { get; set; }

    /// <summary>Number of rows skipped due to errors.</summary>
    public int RowsSkipped { get; set; }

    /// <summary>Per-row error details.</summary>
    public IReadOnlyList<ImportRowError> Errors { get; set; } = [];
}

/// <summary>
/// Error detail for a single row during Excel import.
/// </summary>
public class ImportRowError
{
    /// <summary>"Hardware" or "Software" worksheet name.</summary>
    public string Worksheet { get; set; } = string.Empty;

    /// <summary>1-based row number in the worksheet.</summary>
    public int RowNumber { get; set; }

    /// <summary>Human-readable error description.</summary>
    public string Error { get; set; } = string.Empty;
}
