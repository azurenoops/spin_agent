# Control Inheritance & CRM Guide

This guide explains how to use the **Control Inheritance** page in the ATO Copilot Dashboard to manage inheritance designations, generate Customer Responsibility Matrices, apply CSP profiles, and import CRM spreadsheets.

## Overview

Every control in your selected NIST 800-53 baseline must be designated as one of:

| Type | Meaning |
|------|---------|
| **Inherited** | Fully provided by the CSP (e.g., Azure Government) |
| **Shared** | Split responsibility between CSP and customer |
| **Customer** | Fully the customer's responsibility |
| **Undesignated** | Not yet classified (default) |

The Control Inheritance page surfaces summary metrics, inline editing, bulk operations, audit trails, CRM export/import, and pre-built CSP profiles.

## Navigation

1. Open a registered system from the Dashboard.
2. In the left sidebar under **Compliance Posture**, click **Control Inheritance**.

## Managing Designations

### Inline Editing

Click any row in the table to edit its inheritance type:

1. Select the **Inheritance Type** dropdown (Inherited / Shared / Customer).
2. If Inherited or Shared, enter the **Provider** name (e.g., "Azure Government").
3. Optionally fill in **Customer Responsibility** for Shared/Customer controls.
4. Click **Save** — the change is recorded with a Manual audit entry.

### Bulk Update

1. Select multiple controls using the row checkboxes (or the header checkbox for all visible).
2. The **Bulk Update Toolbar** appears above the table.
3. Choose the inheritance type, provider, and responsibility.
4. Click **Apply** — all selected controls are updated in one operation logged as a BulkUpdate source.

### Filtering & Search

- **Family filter** — Narrow to a specific NIST family (e.g., AC, AU, CM).
- **Type filter** — Show only Inherited, Shared, Customer, or Undesignated controls.
- **Search** — Free-text search by control ID.
- **Sort** — Click column headers to sort ascending/descending.
- **Pagination** — Navigate pages for large baselines (50 controls per page).

## Audit History

Click any table row to open the **Audit History Panel** on the right side. It shows:

- Who made each change and when.
- Previous → New values for inheritance type, provider, and responsibility.
- The change source (Manual, BulkUpdate, ProfileApply, CrmImport).

## Generating the CRM

1. Click **Generate CRM** in the header.
2. The CRM view shows controls grouped by family with a summary statistics row.
3. Choose an **export format** (CSV or Excel) and a **layout**:
   - **Custom** — Control ID, Family, Inheritance Type, Provider, Customer Responsibility
   - **FedRAMP** — Aligned with FedRAMP CRM template columns
   - **eMASS** — Aligned with eMASS import format
4. Click **Export** to download the file.

## Applying a CSP Profile

Pre-built CSP profiles automatically set inheritance designations for known CSP-provided controls.

1. Click **Apply CSP Profile** in the header.
2. Select a profile (e.g., "Azure Government — FedRAMP High").
3. Choose conflict resolution:
   - **Skip** — Do not overwrite controls that already have a designation.
   - **Overwrite** — Replace existing designations with profile values.
4. Click **Preview Changes** to see how many controls will be set.
5. Click **Apply Profile** to commit the changes.

## Importing a CRM

If you have an existing CRM spreadsheet, you can import it to set designations in bulk.

1. Click **Import CRM** in the header.
2. Drag and drop a CSV or Excel file (or click Browse).
3. Review the detected columns and adjust the **column mapping**:
   - Map source columns to: Control ID (required), Inheritance Type (required), Provider, Customer Responsibility.
4. Review the sample data preview.
5. Choose conflict resolution (Overwrite or Skip).
6. Click **Apply Import** — controls not found in the baseline are flagged.

## Summary Bar

The six summary cards at the top show:

| Card | Description |
|------|-------------|
| Total | Total controls in the baseline |
| Inherited | Controls fully provided by CSP |
| Shared | Shared responsibility controls |
| Customer | Customer-responsible controls |
| Undesignated | Controls not yet classified |
| Inheritance % | Percentage of controls with a designation |
