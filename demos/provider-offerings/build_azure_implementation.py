#!/usr/bin/env python3
"""Build a dated Azure supplement without rewriting imported source editions."""
import hashlib
import json
from pathlib import Path
import zipfile

from build_demo import LABEL, csv_write, document, json_write, zip_write

ROOT = Path(__file__).resolve().parent
FOLDER = ROOT / "azure-implementation-2026-09-27"


def main():
    mapping = json.loads((FOLDER / "implementation-map.json").read_text())
    baseline_manifest = ROOT / "internal-manifest.json"
    if hashlib.sha256(baseline_manifest.read_bytes()).hexdigest() != mapping["baselineManifestSha256"]:
        raise ValueError("The original baseline manifest changed; review before generating the supplement.")
    baseline = ROOT / mapping["baselineDataset"]
    original = json.loads((baseline / "sources/02-capability-catalog.json").read_text())
    duties = json.loads((baseline / "sources/03-customer-responsibilities.json").read_text())["responsibilities"]
    components = {item["key"]: item for item in mapping["components"]}
    original_ids = {item["id"] for item in original["capabilities"]}
    if len(components) != 12 or {item["sourceId"] for item in mapping["capabilities"]} != original_ids:
        raise ValueError("Expected twelve distinct products and the eight original capability identities.")
    lines = [
        "# Azure IL5 shared services: Azure implementation guide", "",
        mapping["demonstrationNotice"], "",
        "## Status and provenance", "",
        "Implementation supplement dated September 27, 2026. These Azure product choices are explicitly authored "
        "for the provider demonstration; they were not extracted from the older generic source package. "
        "The original source edition 1.2 and its immutable publication history remain intact. "
        "This supplement does not itself publish a release or accept mission responsibilities.", "",
        "Use this guide as the current product-mapping document alongside the baseline responsibility matrix, "
        "assessment summary and corrective plan. In the customer bundle, those earlier documents are under "
        "baseline/ so they are not mistaken for revised Azure-specific sources.", "",
        "This enriches the Azure demonstration only. The application remains multi-cloud capable. "
        "Microsoft 365 stays a separate Microsoft SaaS offering, not an Azure subscription or resource allocation.", "",
        "## Azure service components", "",
    ]
    for component in mapping["components"]:
        lines += [f"### {component['name']}", "", component["description"], ""]
    lines += ["## Capability implementation and unchanged customer duties", ""]
    rows = []
    for capability in mapping["capabilities"]:
        keys = capability["componentKeys"]
        if not keys or len(keys) != len(set(keys)) or not set(keys) <= components.keys():
            raise ValueError(f"Invalid contributor set: {capability['sourceId']}")
        lines += [f"### {capability['name']}", "", capability["description"], "",
                  "Implementing components: " + "; ".join(components[key]["name"] for key in keys) + ".", ""]
        original_capability = next(item for item in original["capabilities"] if item["id"] == capability["sourceId"])
        lines += ["Control references: " + ", ".join(original_capability["controlIds"]) + ".", ""]
        for duty in duties:
            if duty["capabilityId"] == capability["sourceId"]:
                lines += [f"{duty['responsibility']} duty: {duty['description']}", ""]
        for key in keys:
            rows.append([capability["sourceId"], capability["name"], key, components[key]["name"],
                         ";".join(original_capability["controlIds"]), "Shared; original duty review retained"])
    lines += [
        "## Backup and key management are separate", "",
        "Azure Backup owns backup policy, protected items, recovery points and restore jobs. "
        "Azure Key Vault owns the customer-managed encryption key and its access/rotation/recovery configuration. "
        "The vault identity requires explicitly configured access to the selected key. "
        "A key vault is not a substitute for workload backups. Configuration exports in Azure Blob Storage "
        "are likewise not proof that Azure Backup protects every Azure resource.", "",
        "The scenario's 24-hour RPO and eight-hour RTO remain design targets, not measured recovery results. "
        "Keep the existing finding and corrective milestones open until reviewed evidence supports closure.", "",
        "## Verification before real deployment", "",
        "Validate region and Azure Government service/feature availability, licensing, supported workload types, "
        "network access, identity permissions, encryption compatibility, retention and restore behavior. "
        "Neither the service names nor the IL5 demonstration title certify an authorization boundary. "
        "No real Azure tenant, subscription, resource or government decision is asserted.", "",
        "## Public product references", "",
    ]
    for reference in mapping["references"]:
        lines += [f"{reference['title']}: {reference['url']}", ""]
    document(FOLDER, "azure-implementation-guide", "\n".join(lines).rstrip() + "\n")
    csv_write(FOLDER / "capability-component-map.csv",
              ["capability_source_id", "capability_name", "component_key", "component_name", "control_ids", "responsibility"],
              rows)
    customer = {
        "READ-ME-FIRST.txt": (
            f"{LABEL}\nSeptember 27 Azure implementation supplement.\n"
            "Read azure-implementation-guide first. baseline/ preserves older supporting documents unchanged.\n"
            "This bundle is not an authorization decision, published API state or evidence-sharing grant.\n"
        ).encode()
    }
    with zipfile.ZipFile(baseline / "customer-bundle.zip") as prior:
        for name in prior.namelist():
            if "PRIVATE" in name or "manifest" in name or Path(name).name != name:
                raise ValueError(f"Unexpected baseline customer entry: {name}")
            customer["baseline/" + name] = prior.read(name)
    for name in ("azure-implementation-guide.md", "azure-implementation-guide.docx",
                 "azure-implementation-guide.pdf", "capability-component-map.csv"):
        customer[name] = (FOLDER / name).read_bytes()
    zip_write(FOLDER / "customer-bundle.zip", customer)
    names = ["implementation-map.json", "azure-implementation-guide.md", "azure-implementation-guide.docx",
             "azure-implementation-guide.pdf", "capability-component-map.csv", "customer-bundle.zip"]
    manifest = {
        "supplementId": mapping["supplementId"], "demonstrationNotice": mapping["demonstrationNotice"],
        "baselineManifestSha256": mapping["baselineManifestSha256"],
        "capabilityCount": len(mapping["capabilities"]), "componentCount": len(components),
        "files": [{"path": name, "bytes": (FOLDER / name).stat().st_size,
                   "sha256": hashlib.sha256((FOLDER / name).read_bytes()).hexdigest()} for name in names],
    }
    json_write(FOLDER / "supplement-manifest.json", manifest)
    print(f"Generated Azure implementation supplement: 8 capabilities, 12 component identities, {len(rows)} contribution links.")


if __name__ == "__main__":
    main()
