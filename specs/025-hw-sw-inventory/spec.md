# Feature Specification: Hardware/Software Inventory

**Feature Branch**: `025-hw-sw-inventory`
**Created**: 2026-03-11
**Status**: Draft
**Input**: User description: "Hardware/Software inventory - eMASS requires a complete HW/SW list with versions, Auth boundary has resources but not a formal HW/SW inventory format."

---

## Part 1: The Problem

### Background

eMASS (Enterprise Mission Assurance Support Service) requires every system package to include a complete hardware and software inventory as part of the System Security Plan (SSP). This inventory must list every component within the authorization boundary — servers, workstations, network devices, operating systems, middleware, databases, and applications — along with version numbers, vendors, and IP/MAC addresses where applicable.

Security Posture Intelligence Navigator already tracks authorization boundary resources via the `AuthorizationBoundary` entity. However, boundary resources are Azure resource identifiers (e.g., subscription IDs, resource group IDs, VM resource IDs) — they tell the system **what cloud resources are in scope** but do not capture the formal HW/SW inventory detail that DoD assessors and eMASS require:

- **Hardware**: manufacturer, model, serial number, function (server, workstation, network device), location, IP address, MAC address
- **Software**: product name, vendor, version, patch level, license type, installation location, function (OS, middleware, database, application)

### The Current Gap

| What eMASS Requires | What Security Posture Intelligence Navigator Has Today |
|----------------------|---------------------------|
| HW inventory with manufacturer, model, serial number, IP/MAC | `AuthorizationBoundary.ResourceType` + `ResourceName` — no manufacturer, model, serial, IP, or MAC |
| SW inventory with vendor, product name, version, patch level | Nothing — software is not tracked at all |
| Component function classification (server, workstation, router, etc.) | `ResourceType` stores Azure resource type strings, not DoD HW categories |
| Mapping between HW items and the SW installed on them | No relationship between hardware and software exists |
| eMASS-compatible HW/SW worksheet for import | `ExportControlsAsync` exports controls only, not inventory |
| Ports, protocols, and services per component | Not tracked (deferred — separate eMASS artifact, out of scope for this feature) |

### Who Benefits

- **ISSO/ISSM**: No longer manually maintains HW/SW spreadsheets separate from Security Posture Intelligence Navigator
- **Engineer**: Can register components as they are provisioned and keep versions current
- **SCA**: Can review the inventory for completeness before assessment
- **AO**: Gets a complete SSP package where the HW/SW sections are auto-populated

---

## Part 2: The Product

### What We're Building

A **Hardware/Software Inventory** capability that allows users to register, update, query, and export the HW/SW components of a registered system. The inventory integrates with the existing authorization boundary, enriches it with DoD-required detail, and exports in eMASS-compatible format.

### What It Is

- A **component registry** where each hardware or software item is tracked with the fields eMASS requires
- A **parent-child relationship** between hardware and the software installed on it
- **MCP tools** for adding, updating, listing, and removing inventory items
- An **eMASS-compatible export** that produces the HW/SW inventory worksheets expected by eMASS import
- A **completeness check** that identifies gaps before SSP submission
- **Auto-enrichment from boundary** — optional seeding of inventory items from existing `AuthorizationBoundary` resources

### What It Is NOT

- Not an automated discovery/scan tool — it does not probe the network or call Azure APIs to discover running software (boundary definition already handles resource discovery)
- Not a CMDB replacement — it tracks what eMASS needs, not full IT asset management fields
- Not a license management system — it records license type for the inventory record but does not track entitlements or compliance
- Not a vulnerability scanner — it records versions but does not correlate them with CVEs (that is a separate concern)
- Not a ports, protocols, and services (PPS) tracker — PPS is a separate eMASS artifact and is deferred to a future feature

---

## User Scenarios & Testing

### User Story 1 — Register & Manage HW/SW Inventory Items (Priority: P1)

As an ISSO or Engineer, I need to add hardware and software items to a system's inventory so that the SSP contains a complete HW/SW list for eMASS submission.

**Why this priority**: Without the ability to create and manage inventory items, no other feature (export, completeness check, SSP integration) can function. This is the foundational data entry capability.

**Independent Test**: Can be fully tested by adding HW and SW items via MCP tools and verifying they persist and can be retrieved.

**Acceptance Scenarios**:

1. **Given** a registered system with no inventory items, **When** an ISSO adds a hardware item with manufacturer, model, function, and IP address, **Then** the item is persisted and appears in the system's inventory list.
2. **Given** a system with a hardware item, **When** an Engineer adds a software item linked to that hardware item (e.g., an OS installed on a server), **Then** the software item is persisted with a reference to its parent hardware item.
3. **Given** a system with existing inventory items, **When** a user updates the version of a software item, **Then** the version is updated and a last-modified timestamp is recorded.
4. **Given** a system with inventory items, **When** a user removes an item, **Then** the item is marked as decommissioned (soft delete) with a decommission date and rationale.
5. **Given** a system with boundary resources, **When** an ISSO requests auto-seeding of inventory from the boundary, **Then** one hardware inventory item is created per boundary resource with available fields pre-populated from the boundary data.

---

### User Story 2 — Query & Filter Inventory (Priority: P2)

As an ISSO or SCA, I need to list and filter the HW/SW inventory for a system so I can review what is registered, find gaps, and verify completeness.

**Why this priority**: Without query capability, users cannot review what has been entered. This is required before export or completeness checks are useful.

**Independent Test**: Can be tested by seeding inventory items and querying with various filters (type, function, vendor, status).

**Acceptance Scenarios**:

1. **Given** a system with mixed HW and SW items, **When** a user lists inventory filtered by type "hardware", **Then** only hardware items are returned.
2. **Given** a system with items from multiple vendors, **When** a user filters by vendor name, **Then** only items from that vendor are returned.
3. **Given** a system with active and decommissioned items, **When** a user lists inventory with status "active", **Then** only active (non-decommissioned) items are returned; decommissioned items are excluded by default.
4. **Given** a system with SW items linked to HW items, **When** a user queries a specific hardware item, **Then** the response includes the list of software installed on that hardware item.

---

### User Story 3 — Export eMASS-Compatible HW/SW Inventory (Priority: P2)

As an ISSO, I need to export the HW/SW inventory in eMASS-compatible format so I can upload it directly to eMASS as part of the system package.

**Why this priority**: eMASS export is the primary driver for this feature. Tied with US2 as both are essential for the core value proposition, but export requires data (US1) and benefits from query (US2).

**Independent Test**: Can be tested by seeding inventory data, exporting to Excel, and verifying eMASS column headers and data mapping.

**Acceptance Scenarios**:

1. **Given** a system with hardware items, **When** an ISSO exports the HW inventory, **Then** an Excel workbook is produced with a "Hardware" worksheet containing eMASS-required columns (System Name, Hardware Name, Manufacturer, Model, Serial Number, Function, IP Address, MAC Address, Location, Status).
2. **Given** a system with software items linked to hardware, **When** an ISSO exports the SW inventory, **Then** an Excel workbook is produced with a "Software" worksheet containing eMASS-required columns (System Name, Software Name, Vendor, Version, Patch Level, Function, License Type, Installed On, Status).
3. **Given** a system with both HW and SW items, **When** an ISSO exports "all", **Then** a single Excel workbook is produced containing both "Hardware" and "Software" worksheets.
4. **Given** a system with decommissioned items, **When** an ISSO exports the inventory, **Then** decommissioned items are excluded from the export by default, with an option to include them.

---

### User Story 4 — Inventory Completeness Check (Priority: P3)

As an ISSO or SCA, I need to check inventory completeness so I know whether the HW/SW list is ready for eMASS submission.

**Why this priority**: Completeness validation prevents eMASS rejection and reduces assessment rework, but requires US1-US3 to be in place first.

**Independent Test**: Can be tested by seeding an inventory with deliberate gaps and verifying the completeness report identifies them.

**Acceptance Scenarios**:

1. **Given** a system with hardware items missing required fields (e.g., no IP address on a server), **When** a user runs the completeness check, **Then** each missing required field is reported with the item name and the field that is missing.
2. **Given** a system with boundary resources that have no corresponding inventory items, **When** a user runs the completeness check, **Then** unmatched boundary resources are flagged as "boundary resource without inventory entry."
3. **Given** a system with hardware items that have no software installed, **When** a user runs the completeness check, **Then** a warning is raised ("Hardware item with no software entries — expected at minimum an OS").
4. **Given** a system with a complete inventory (all required fields populated, all boundary resources matched, all HW items have SW), **When** a user runs the completeness check, **Then** the check returns a passing result with 100% completeness score.

---

### User Story 5 — SSP Integration (Priority: P3)

As an ISSO, I need the HW/SW inventory to be included in the generated SSP so that the SSP document is complete without manual copy-paste from separate spreadsheets.

**Why this priority**: SSP integration is the final piece that ties inventory management into the existing document generation pipeline. It depends on US1-US3 being complete.

**Independent Test**: Can be tested by seeding inventory items and generating an SSP, then verifying that the HW/SW sections contain the inventory data.

**Acceptance Scenarios**:

1. **Given** a system with HW/SW inventory items, **When** an ISSO generates the SSP, **Then** the SSP includes a "Hardware Inventory" section listing all active hardware items with key fields.
2. **Given** a system with HW/SW inventory items, **When** an ISSO generates the SSP, **Then** the SSP includes a "Software Inventory" section listing all active software items with key fields.
3. **Given** a system with no inventory items, **When** an ISSO generates the SSP, **Then** the SSP HW/SW sections contain a placeholder noting "Inventory not yet populated" rather than being omitted.

---

### User Story 6 — Import Inventory from Excel (Priority: P2)

As an ISSO, I need to import existing HW/SW inventory data from an Excel spreadsheet so I can onboard systems that already have inventories without manually adding items one-by-one.

**Why this priority**: Many DoD systems have existing HW/SW inventories in spreadsheets. Bulk import dramatically reduces onboarding friction and creates a natural round-trip with the eMASS export. Tied with US2/US3 as part of the core value proposition.

**Independent Test**: Can be tested by creating an Excel file matching the eMASS export format, importing it, and verifying all items are created with correct field mappings.

**Acceptance Scenarios**:

1. **Given** an Excel file with a "Hardware" worksheet matching the eMASS export column format, **When** an ISSO imports it for a registered system, **Then** one hardware inventory item is created per row with fields mapped from the column headers.
2. **Given** an Excel file with a "Software" worksheet matching the eMASS export column format, **When** an ISSO imports it for a registered system, **Then** one software inventory item is created per row, with the "Installed On" column used to link SW items to existing HW items by name.
3. **Given** an import file with rows that fail validation (e.g., missing required fields per FR-018), **When** the import is run, **Then** valid rows are imported and invalid rows are reported with row number and error detail.
4. **Given** an import file, **When** an ISSO runs the import in dry-run mode, **Then** the system reports what would be created/skipped without persisting any data.

---

### Edge Cases

- What happens when a user tries to add a software item linked to a hardware item that does not exist? → Rejected with a clear error identifying the missing hardware item.
- What happens when a user decommissions a hardware item that still has active software entries? → The software entries are also marked as decommissioned with the same rationale.
- What happens when auto-seeding from boundary is run a second time after new boundary resources are added? → Only new/unmatched boundary resources are seeded; existing inventory items are not duplicated.
- What happens when a user exports inventory for a system with zero items? → Export returns an error indicating no inventory data exists.
- What happens when a hardware item has duplicate IP addresses within the same system? → Rejected with a uniqueness constraint violation error.
- What happens when an import file contains rows referencing a hardware item (via "Installed On") that does not yet exist? → If the HW item exists in an earlier row of the same import, it is created first (Hardware worksheet processed before Software); otherwise the SW row is skipped with an error.
- What happens when an import file contains duplicate items (same name/type) as existing inventory? → Duplicates are skipped and reported; existing items are not overwritten.

---

## Requirements

### Functional Requirements

- **FR-001**: System MUST allow users to add hardware inventory items with the following fields: item name, manufacturer, model, serial number, function (server, workstation, network device, storage, other), IP address, MAC address, location, and status (active, decommissioned).
- **FR-002**: System MUST allow users to add software inventory items with the following fields: product name, vendor, version, patch level, function (operating system, database, middleware, application, security tool, other), license type, and an optional reference to the hardware item it is installed on. Software items for managed/SaaS/PaaS components MAY exist without a parent hardware reference.
- **FR-003**: System MUST allow users to update any field of an existing inventory item and record the last-modified timestamp and modifier identity.
- **FR-004**: System MUST support soft-deletion (decommissioning) of inventory items with a decommission date and rationale rather than hard deletion.
- **FR-005**: When a hardware item is decommissioned, all software items linked to it MUST also be decommissioned with the same rationale.
- **FR-006**: System MUST allow users to list inventory items with filtering by type (hardware/software), function, vendor, status, and free-text search on item name.
- **FR-007**: System MUST return software children when querying a specific hardware item.
- **FR-008**: System MUST export hardware inventory to an eMASS-compatible Excel worksheet with the column headers: System Name, Hardware Name, Manufacturer, Model, Serial Number, Function, IP Address, MAC Address, Location, Status.
- **FR-009**: System MUST export software inventory to an eMASS-compatible Excel worksheet with the column headers: System Name, Software Name, Vendor, Version, Patch Level, Function, License Type, Installed On (hardware reference), Status.
- **FR-010**: System MUST support combined export producing a single workbook with both Hardware and Software worksheets.
- **FR-011**: System MUST exclude decommissioned items from export by default, with an option to include them.
- **FR-012**: System MUST provide a completeness check that reports: missing required fields per item, boundary resources without inventory entries, and hardware items without any software entries.
- **FR-013**: System MUST support auto-seeding inventory items from the existing authorization boundary resources, creating one hardware item per boundary resource with pre-populated fields where available.
- **FR-014**: Auto-seeding MUST be idempotent — running it again after new boundary resources are added creates only new entries for unmatched resources. Idempotency is enforced via an optional `BoundaryResourceId` foreign key on each inventory item that references the source `AuthorizationBoundary` entry.
- **FR-015**: System MUST integrate HW/SW inventory sections into the generated SSP document.
- **FR-016**: IP addresses MUST be unique within a system's hardware inventory.
- **FR-017**: Each inventory item MUST be associated with exactly one registered system.
- **FR-018**: System MUST enforce function-based required field validation at creation time:
  - **All hardware items**: item name, function, and manufacturer are required.
  - **Hardware with function server or network device**: IP address is additionally required.
  - **All software items**: product name, vendor, and version are required.
  - All other fields are optional at creation time but flagged by the completeness check (FR-012) if missing at export time.
- **FR-019**: System MUST support importing inventory items from an Excel workbook using the same eMASS column format as the export (round-trip compatibility). The workbook may contain a "Hardware" worksheet, a "Software" worksheet, or both.
- **FR-020**: During import, rows that fail validation (per FR-018) MUST be skipped and reported with row number and error detail; valid rows MUST still be imported.
- **FR-021**: System MUST support a dry-run import mode that reports what would be created or skipped without persisting any data.

### Key Entities

- **InventoryItem**: Represents a single hardware or software component within a system's authorization boundary. Key attributes: item name, type (hardware/software), function classification, vendor/manufacturer, version (for SW), serial number (for HW), IP/MAC addresses (for HW), status (active/decommissioned), optional parent hardware reference (for SW items — not required for managed/SaaS/PaaS components), optional `BoundaryResourceId` FK linking to the source `AuthorizationBoundary` entry (used for auto-seed idempotency). Belongs to one RegisteredSystem.
- **InventoryCompleteness**: A computed result (not persisted) representing the completeness state of a system's inventory — counts of items with missing required fields, unmatched boundary resources, and hardware without software entries.

---

## Clarifications

### Session 2026-03-11

- Q: Should software items be allowed to exist without a parent hardware reference (for SaaS/PaaS/managed services)? → A: Yes — hardware parent is optional; SW items can exist independently for managed/SaaS components.
- Q: Should the spec include ports, protocols, and services (PPS) tracking per inventory item? → A: No — PPS is deferred to a separate feature; explicitly out of scope.
- Q: What level of field validation should be enforced at inventory item creation time? → A: Function-based required fields — servers/network devices require IP; all HW requires manufacturer; all SW requires vendor+version. Remaining fields are optional at creation but flagged by completeness check.
- Q: How should the inventory-to-boundary-resource linkage be maintained for auto-seed idempotency? → A: Direct FK — each inventory item has an optional `BoundaryResourceId` referencing its source `AuthorizationBoundary` entry.
- Q: Should the spec include importing inventory items from an Excel/CSV file? → A: Yes — include Excel import using the same eMASS column format as the export, enabling round-trip capability. Supports dry-run mode and partial import with row-level error reporting.

---

## Assumptions

- The eMASS HW/SW import template column headers follow the standard DoD eMASS Excel format. If a specific eMASS version requires different headers, the export can be updated without changing the data model.
- IP addresses are IPv4 or IPv6 strings; no network validation is performed beyond format checks.
- MAC addresses are stored as colon-separated hex strings (e.g., `00:1A:2B:3C:4D:5E`); no format enforcement beyond reasonable length.
- "Function" classifications for hardware (server, workstation, network device, storage, other) and software (operating system, database, middleware, application, security tool, other) are based on common DoD SSP templates and may be extended via enum additions.
- Auto-seeding from boundary maps `AuthorizationBoundary.ResourceType` to the closest hardware function and uses `ResourceName` as the inventory item name. Fields not available from the boundary (e.g., serial number, IP) are left blank for manual completion.
- Inventory data does not require the same narrative governance (version control / approval workflow) as control narratives — items are tracked with simple create/update/decommission audit fields.

---

## Success Criteria

### Measurable Outcomes

- **SC-001**: An ISSO can add a hardware item and its associated software items in under 2 minutes using MCP tools.
- **SC-002**: An ISSO can export a complete HW/SW inventory to eMASS-compatible Excel in a single tool invocation.
- **SC-003**: The completeness check identifies 100% of missing required fields and unmatched boundary resources.
- **SC-004**: Auto-seeding from boundary resources creates inventory entries for all in-scope boundary resources in a single operation.
- **SC-005**: The generated SSP includes HW/SW inventory sections that are accepted by assessors without manual supplementation for systems with complete inventory data.
- **SC-006**: 90% of ISSOs can populate a 50-item inventory and export it to eMASS in under 30 minutes.
- **SC-007**: An ISSO can import a 100-row Excel inventory file and receive a complete import report (created/skipped counts with errors) in a single tool invocation.

---

## Documentation Updates

The following documentation MUST be updated as part of this feature delivery:

- **[docs/api/mcp-server.md](docs/api/mcp-server.md)**: Add all 9 new HW/SW inventory MCP tools (add/update/decommission/get/list/export/import/completeness-check/auto-seed) with parameter descriptions, example invocations, and return types.
- **[docs/architecture/data-model.md](docs/architecture/data-model.md)**: Add the `InventoryItem` entity with all fields, relationships (RegisteredSystem FK, optional parent HW FK, optional BoundaryResourceId FK), and the computed `InventoryCompleteness` result type.
- **[docs/architecture/agent-tool-catalog.md](docs/architecture/agent-tool-catalog.md)**: Register all new inventory tools in the tool catalog with capability descriptions, required parameters, and persona access mappings.
- **[docs/guides/isso-guide.md](docs/guides/isso-guide.md)** *(new or update existing ISSO guide)*: Add a "Managing HW/SW Inventory" section covering the end-to-end workflow: auto-seed from boundary → add/edit items → run completeness check → export to eMASS → import from Excel.
- **[docs/guides/engineer-guide.md](docs/guides/engineer-guide.md)**: Add inventory registration guidance — how engineers register components as they are provisioned and keep versions current.
- **[docs/guides/sca-guide.md](docs/guides/sca-guide.md)**: Add inventory review guidance — how SCAs review inventory completeness and verify HW/SW coverage before assessment.
- **[docs/getting-started/isso.md](docs/getting-started/isso.md)**: Update the ISSO getting-started guide to mention HW/SW inventory as a core capability with a link to the detailed guide.
- **[docs/getting-started/engineer.md](docs/getting-started/engineer.md)**: Update the Engineer getting-started guide to mention component registration.
- **[docs/reference/glossary.md](docs/reference/glossary.md)**: Add terms: "Hardware Inventory", "Software Inventory", "Inventory Completeness Check", "Auto-Seed", "eMASS HW/SW Export".
- **[docs/architecture/overview.md](docs/architecture/overview.md)**: Update the architecture overview to include the inventory service in the system component diagram.
- **[docs/persona-test-cases/tool-validation.md](docs/persona-test-cases/tool-validation.md)**: Add validation test cases for all new inventory MCP tools — covering expected inputs, outputs, and error scenarios per persona (ISSO, Engineer, SCA).
- **[docs/persona-test-cases/test-data-setup.md](docs/persona-test-cases/test-data-setup.md)**: Add seed data instructions for HW/SW inventory items (sample hardware, software, parent-child relationships, boundary linkages) needed for persona test execution.
- **[docs/persona-test-cases/environment-checklist.md](docs/persona-test-cases/environment-checklist.md)**: Add checklist items verifying inventory tools are registered, inventory data is seeded, and eMASS export/import round-trip is functional.
- **[docs/persona-test-cases/results-template.md](docs/persona-test-cases/results-template.md)**: Add inventory-related rows to the results template for tracking pass/fail status of inventory tool validation tests.
- **[docs/persona-test-cases/scripts/isso-test-script.md](docs/persona-test-cases/scripts/isso-test-script.md)**: Add test scenarios for ISSO inventory workflows — add/edit items, run completeness check, export to eMASS, import from Excel.
- **[docs/persona-test-cases/scripts/engineer-test-script.md](docs/persona-test-cases/scripts/engineer-test-script.md)**: Add test scenarios for Engineer registering components and updating versions.
- **[docs/persona-test-cases/scripts/sca-test-script.md](docs/persona-test-cases/scripts/sca-test-script.md)**: Add test scenarios for SCA reviewing inventory completeness before assessment.
- **[docs/persona-test-cases/scripts/issm-test-script.md](docs/persona-test-cases/scripts/issm-test-script.md)**: Add test scenarios for ISSM reviewing inventory status across systems in the portfolio.
- **[docs/persona-test-cases/scripts/cross-persona-test-script.md](docs/persona-test-cases/scripts/cross-persona-test-script.md)**: Add cross-persona inventory scenarios — e.g., Engineer registers items → ISSO runs completeness check → SCA verifies → ISSO exports.
- **[docs/persona-test-cases/scripts/unified-rmf-test-script.md](docs/persona-test-cases/scripts/unified-rmf-test-script.md)**: Add HW/SW inventory steps to the unified RMF lifecycle test flow (inventory ties into Categorize/Implement/Assess phases).
