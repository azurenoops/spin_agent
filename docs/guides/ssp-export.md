# SSP Document Export

Export your System Security Plan (SSP) as a Word document, PDF, or OSCAL JSON with a single click. Exports run asynchronously in the background and notify you when ready for download.

## Supported Formats

| Format | Extension | Use Case |
|--------|-----------|----------|
| **Word** | `.docx` | Editable SSP for review, markup, and submission |
| **PDF** | `.pdf` | Read-only SSP with headers, footers, and page numbers |
| **OSCAL JSON** | `.json` | Machine-readable NIST OSCAL SSP for automated processing |

## Exporting an SSP

1. Navigate to **Documents** for your system
2. Click **Export SSP** in the Exports section
3. Choose a format:
   - **Word (.docx)** — optionally select a custom template
   - **PDF (.pdf)** — generates a formatted PDF with cover page
   - **OSCAL JSON (.json)** — produces NIST-compliant OSCAL output
4. Click **Start Export**
5. A progress bar shows generation milestones (gathering data → generating content → writing file)
6. When complete, click **Download** to save the file

> **Note:** You must have a baseline selected before exporting. The Export SSP button is disabled until a baseline is in place.

## Export History

All exports are listed in the **Export History** table on the Documents page. Each entry shows:

- Format icon and type
- Generation status (Pending, Processing, Completed, Failed)
- File size
- Who requested it and when
- Download link (for completed exports)

## Custom Templates

Use custom Word templates to apply your organization's branding, headers, and layout to exported SSP documents.

### Managing Templates

1. Click **Manage Templates** in the Exports section header
2. From the template management dialog you can:
   - **Upload** a new `.docx` template (max 10 MB)
   - **Rename** an existing template (click Rename, edit inline, press Enter)
   - **Delete** a template (with confirmation)

### Template Requirements

- File must be a valid `.docx` (Office Open XML)
- Maximum file size: 10 MB
- Templates use `{{FieldName}}` merge field placeholders

### Available Merge Fields

| Field | Description |
|-------|-------------|
| `{{SystemName}}` | Registered system name |
| `{{SecurityCategorization}}` | FIPS 199 categorization |
| `{{AuthorizationBoundary}}` | Boundary description |
| `{{Components}}` | System component inventory |
| `{{ControlNarratives}}` | All control implementation narratives |
| `{{InformationTypes}}` | Categorized information types |
| `{{SystemDescription}}` | System purpose and function |
| `{{SystemEnvironment}}` | Operating environment details |
| `{{ResponsibleRoles}}` | Key personnel and roles |
| `{{SystemInterconnections}}` | ISA/MOU connections |
| `{{ContingencyPlan}}` | Contingency planning narrative |
| `{{IncidentResponse}}` | IR procedures |
| `{{MaintenanceProcedures}}` | System maintenance details |
| `{{MediaProtection}}` | Media handling procedures |
| `{{PhysicalSecurity}}` | Physical security controls |
| `{{PersonnelSecurity}}` | Personnel security procedures |
| `{{RiskAssessment}}` | Risk assessment summary |

When a custom template is selected during export, the system substitutes each `{{FieldName}}` placeholder with the corresponding data from your system.

## Retention Policy

Exported files are automatically cleaned up after **30 days** (configurable). The retention service runs daily and removes both the file and the database record for expired exports.

To download an export before it expires, use the Download link in the Export History table.

## API Reference

For programmatic access to exports and templates, see the [API documentation](../api/mcp-server.md).

### Key Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/dashboard/systems/{systemId}/exports?format=docx` | Start a new export |
| `GET` | `/api/dashboard/systems/{systemId}/exports` | List export history |
| `GET` | `/api/dashboard/systems/{systemId}/exports/{exportId}` | Get export details |
| `GET` | `/api/dashboard/systems/{systemId}/exports/{exportId}/download` | Download file |
| `POST` | `/api/dashboard/templates` | Upload a template |
| `GET` | `/api/dashboard/templates` | List templates |
| `PUT` | `/api/dashboard/templates/{templateId}` | Rename template |
| `DELETE` | `/api/dashboard/templates/{templateId}` | Delete template |
