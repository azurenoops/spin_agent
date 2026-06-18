# MCP Tools Contracts: OSCAL Full Compliance Suite (076)

## Tool: oscal_export_ssp

**Description**: Generate and return OSCAL 1.1.2 SSP JSON for a registered system.

**Input**:
```json
{ "system_id": "string", "mode": "strict|advisory" }
```

**Output**:
```json
{
  "document_uuid": "string",
  "oscal_json": "string",
  "schema_valid": true,
  "schematron_violations": [],
  "generated_at": "ISO8601"
}
```

---

## Tool: oscal_export_poam

**Description**: Generate OSCAL 1.1.2 POA&M JSON for a system.

**Input**: `{ "system_id": "string" }`

**Output**: Same shape as `oscal_export_ssp`.

---

## Tool: oscal_export_sar

**Description**: Generate OSCAL 1.1.2 SAR JSON from assessment results.

**Input**: `{ "system_id": "string", "assessment_id": "string?" }`

**Output**: Same shape as `oscal_export_ssp`.

---

## Tool: oscal_import_ssp

**Description**: Import an OSCAL 1.1.2 SSP JSON document into a registered system.

**Input**:
```json
{ "system_id": "string", "oscal_json": "string", "mode": "preview|full" }
```

**Output**:
```json
{
  "import_run_id": "string",
  "mode": "preview|full",
  "controls_created": 0,
  "controls_updated": 0,
  "controls_skipped": 0,
  "controls_failed": 0,
  "validation_errors": []
}
```

---

## Tool: oscal_validate

**Description**: Validate an OSCAL document against NIST JSON Schema and FedRAMP Schematron.

**Input**:
```json
{ "oscal_json": "string", "document_type": "ssp|sar|poam|sap" }
```

**Output**:
```json
{
  "schema_valid": true,
  "schema_errors": [],
  "schematron_violations": [
    { "severity": "high|medium|low", "path": "string", "message": "string" }
  ]
}
```

---

## Tool: oscal_decompose_control

**Description**: AI-assisted decomposition of a control narrative into OSCAL statement-level fragments.

**Input**:
```json
{ "system_id": "string", "control_id": "string", "narrative": "string?" }
```

**Output**:
```json
{
  "draft_id": "string",
  "control_id": "string",
  "fragments": [
    {
      "statement_id": "ac-1_smt.a",
      "component_uuid": "string|null",
      "description": "string",
      "suggested_params": [{ "param_id": "string", "value": "string" }],
      "confidence_score": 0.92
    }
  ],
  "status": "pending_approval"
}
```
