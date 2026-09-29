#!/usr/bin/env python3
"""Create authored Azure products and publish contributor-only capability revisions.

Live finding (2026-09-27): offering-linked component/capability text PATCH is
forbidden by SaveProviderChangesAsync -> RequireLegacyMutationAsync even with
the correct ETag. AUTHORIZATION_WORKFLOW_REQUIRED inherits DbUpdateConcurrency
and is currently masked by the endpoint's generic 412 response. Seven retained
supplement evidence artifacts succeeded; the first component PATCH did not.
Preserve that journal, those artifacts and the original pending token.

Revised strategy (2026-09-27), azure-product-contributors-v1:
  Preserve all eight original source components and baseline capability names/
  descriptions. CREATE twelve distinct, explicitly authored Azure products.
  SaveWorkingRevision does not require the provenance parent among contributors;
  replace its former contributor link with the exact product-only sets (fourteen
  links), then use ordinary impact/approval/publication for revision 3 of all
  eight existing capabilities. There are twenty Azure-associated components:
  eight retained source containers plus twelve implementing products.

  No PATCH, DELETE, ETag requirement, source import, guard bypass, or text edit.
  existingSourceId in the supplement map identifies source origin, NOT a record
  to rename/reuse. Supplement capability descriptions are authored guidance, NOT
  replacement canonical text. Old source bytes/references, duties, history,
  boundary and hosting stay intact. The projection may still show the source
  parent in supportingComponents; verification distinguishes that provenance
  container from the exact working-revision contributor IDs.

  Use a NEW strategy-specific state directory, never the failed PATCH journal.
  Previously retained supplement evidence is reconciled by exact name/hash/
  description and downloaded for verification; do not upload duplicate evidence.

  --plan validates immutable source provenance and current revision-2 baseline
  via GET only, then reports the exact proposed mapping and snapshot hash.
  --apply requires BOTH exact manifest and snapshot hashes. Fresh durable state
  is separate from the immutable original loader journal. Normal configured
  loopback simulation creates a dedicated cookie jar, never edits the original.
  Every mutation is durably recorded before transmission. Uncertain creates
  reconcile through GET or stop; no blind non-idempotent retry. Keep the same
  state directory for resume. Never delete it to retry.
  All product creation and working edits precede any impact review. Each of the eight
  preserved capability IDs gets its own accepted impact, exact approval and
  revision-3 canonical publication, with unchanged control duties. Manual
  component create already returns Published; no bypass publication is needed.
  No cloud calls, source reimport, role edits, SQL, deletion or M365 writes.

Mapping JSON schema (UTF-8; sibling supplement-manifest.json binds all bytes):
  schemaVersion: 1
  supplementId: "azure-implementation-2026-09-27"
  demonstrationNotice: "SYNTHETIC DEMONSTRATION ONLY. ..."
  offeringName: "Azure IL5 · Shared services"
  baselineDataset: "azure-il5-shared-services/release-1.2"
  baselineManifestSha256: "<exact original loader manifest SHA-256>"
  components: [{key, existingSourceId: "AZ-component-audit" | null,
                name, description}]
  capabilities: [{sourceId: "AZ-capability-audit",
                  name, description, componentKeys: ["monitor", "blob"]}]
  references: [{title, url}]
  --manifest-sha256 is the raw SHA-256 of supplement-manifest.json, which must
  bind implementation-map.json, the guide MD/PDF/DOCX, the CSV and customer ZIP.
  Component keys/names and capability links are the exact SERVICES/LINKS below.
  Every description must explicitly identify the new synthetic authorship;
  neither old source releases nor extracted claims may be rewritten.

Offline tests:
  python3 -m unittest discover -s scripts -p 'test_update_azure_provider_demo.py'
Manual plan (existing private baseline cookie jar; no writes):
  python3 scripts/update-azure-provider-demo.py --plan --base-url http://127.0.0.1:3002
    --mapping <mapping.json> --baseline-journal <original-private-state/state.json>
    --state-dir <different-new-private-state-directory>
Replace --plan with --apply --manifest-sha256 <plan hash> --snapshot-sha256 <plan
hash> after reviewing the proposed product links. Offline tests do not claim a
live publication. Inspect Backup -> Azure Backup AND Azure Key Vault locally.
"""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
from types import SimpleNamespace


SPEC = importlib.util.spec_from_file_location(
    "provider_demo_loader", Path(__file__).with_name("load-provider-offerings-demo.py"))
loader = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(loader)
Stop, require, NOTICE = loader.Stop, loader.require, loader.NOTICE
OFFERING = "5417fe07-190d-4d47-9bae-a2238b6f30d4"
DATASET = "azure-il5-shared-services/release-1.2"
SUPPLEMENT = "azure-implementation-2026-09-27"
STRATEGY = "azure-product-contributors-v1"
BASELINE_SHA = "b892d1e271911444c85ab35080b9550f4e4de62fc59de505965573bb2354ccbe"
FILES = {
    "implementation-map.json", "azure-implementation-guide.md",
    "azure-implementation-guide.pdf", "azure-implementation-guide.docx",
    "capability-component-map.csv", "customer-bundle.zip",
}
SERVICES = {
    "monitor": "Azure Monitor Logs (Log Analytics)",
    "blob": "Azure Blob Storage",
    "firewall": "Azure Firewall",
    "vnet": "Azure Virtual Network",
    "entra": "Microsoft Entra ID (Privileged Identity Management)",
    "policy": "Azure Policy",
    "keyvault": "Azure Key Vault",
    "sentinel": "Microsoft Sentinel",
    "logicapps": "Azure Logic Apps",
    "defender": "Microsoft Defender for Cloud",
    "updatemanager": "Azure Update Manager",
    "backup": "Azure Backup (Recovery Services vault)",
}
LINKS = {
    "audit": ["monitor", "blob"],
    "network": ["firewall", "vnet"],
    "identity": ["entra"],
    "configuration": ["policy", "blob"],
    "encryption": ["keyvault"],
    "incident": ["sentinel", "logicapps"],
    "vulnerability": ["defender", "updatemanager"],
    "backup": ["backup", "keyvault"],
}


def snapshot_hash(value):
    return loader.digest(json.dumps(value, sort_keys=True, separators=(",", ":"),
                                    ensure_ascii=False).encode())


class EnrichmentApi(loader.Api):
    def request(self, method, path, body=None, key=None, binary=False):
        require(method in ("GET", "POST", "PUT"),
                "Contributor-only strategy forbids PATCH/DELETE and source text edits.")
        require(not isinstance(body, dict) or "_ifMatch" not in body,
                "Contributor-only strategy cannot replay an old text PATCH envelope.")
        return super().request(method, path, body, key, binary)


def validate_mapping(data):
    require(isinstance(data, dict), "Mapping must be a JSON object.")
    require(data.get("schemaVersion") == 1 and isinstance(data.get("demonstrationNotice"), str)
            and data["demonstrationNotice"].startswith(NOTICE),
            "Mapping requires schemaVersion 1 and an explicit synthetic notice.")
    require(data.get("offeringName") == "Azure IL5 · Shared services" and data.get("baselineDataset") == DATASET
            and data.get("baselineManifestSha256") == BASELINE_SHA,
            "Only the exact current synthetic Azure offering and reviewed baseline are supported.")
    require(data.get("supplementId") == SUPPLEMENT, "Only the exact dated authored supplement is supported.")
    components, capabilities = data.get("components"), data.get("capabilities")
    require(isinstance(components, list) and len(components) == 12
            and all(isinstance(c, dict) for c in components), "Exactly twelve components are required.")
    require(isinstance(capabilities, list) and len(capabilities) == 8
            and all(isinstance(c, dict) for c in capabilities), "Exactly eight capabilities are required.")
    require(all(isinstance(c.get("key"), str) for c in components)
            and {c["key"] for c in components} == set(SERVICES),
            "Component keys must name exactly the twelve supported Azure services, without duplicates.")
    primary = {keys[0]: "AZ-component-" + purpose for purpose, keys in LINKS.items()}
    for component in components:
        key = component["key"]
        require(component.get("name") == SERVICES[key], f"Unexpected Azure product name for {key}.")
        require(component.get("existingSourceId") == primary.get(key),
                f"{key} must retain its exact baseline source key (or null for a new service).")
    require(all(isinstance(c.get("sourceId"), str) for c in capabilities)
            and {c["sourceId"] for c in capabilities}
            == {"AZ-capability-" + purpose for purpose in LINKS},
            "Exactly the eight original source capability keys are required.")
    for capability in capabilities:
        purpose = capability["sourceId"].removeprefix("AZ-capability-")
        require(capability.get("componentKeys") == LINKS[purpose],
                f"Incorrect contributor links for {purpose}; primary component must remain first.")
    for record in components + capabilities:
        require(isinstance(record.get("name"), str) and 0 < len(record["name"].strip()) <= 256,
                "Names must contain 1–256 characters.")
        description = record.get("description")
        require(isinstance(description, str) and NOTICE in description and len(description) <= 2000,
                "Descriptions must explicitly declare synthetic authorship within the 2,000-character API limit.")
    references = data.get("references")
    require(isinstance(references, list) and references
            and all(isinstance(r, dict) and isinstance(r.get("title"), str) and r["title"].strip()
                    and isinstance(r.get("url"), str) and r["url"].startswith("https://") for r in references),
            "Explicit HTTPS product references are required (never fetched by this updater).")


def read_mapping(path):
    path = Path(path).resolve()
    raw = path.read_bytes()
    data = json.loads(raw)
    validate_mapping(data)
    require(path.name == "implementation-map.json", "Use the exact implementation-map.json.")
    manifest_raw = (path.parent / "supplement-manifest.json").read_bytes()
    manifest = json.loads(manifest_raw)
    require(manifest.get("supplementId") == SUPPLEMENT
            and manifest.get("baselineManifestSha256") == BASELINE_SHA
            and manifest.get("demonstrationNotice") == data["demonstrationNotice"]
            and manifest.get("componentCount") == 12 and manifest.get("capabilityCount") == 8,
            "Supplement manifest scope differs from the verified mapping.")
    entries = manifest.get("files")
    require(isinstance(entries, list) and len(entries) == len(FILES)
            and all(isinstance(e, dict) and isinstance(e.get("path"), str) for e in entries)
            and {e["path"] for e in entries} == FILES,
            "Supplement manifest must bind exactly the six expected files without duplicates or traversal.")
    for entry in entries:
        artifact = path.parent / entry["path"]
        require(artifact.resolve().is_relative_to(path.parent), "Supplement symlink escapes its directory.")
        content = artifact.read_bytes()
        require(isinstance(entry.get("sha256"), str) and re.fullmatch(r"[a-f0-9]{64}", entry["sha256"])
                and len(content) == entry.get("bytes") and loader.digest(content) == entry["sha256"],
                "Supplement file size or SHA-256 changed: " + entry["path"])
    text = (path.parent / "azure-implementation-guide.md").read_text()
    require(NOTICE in text and ("2026-09-27" in text or "September 27, 2026" in text),
            "Supplement must contain the synthetic notice and explicit authorship date.")
    require(all(name in text for name in SERVICES.values()),
            "Supplement must explicitly name all twelve mapped services.")
    return data, loader.digest(manifest_raw)


def contract_advisories():
    return [
        {
            "code": "SUPPLEMENT_IS_SEPARATE_AUTHORED_EVIDENCE",
            "detail": (
                "Retaining separately attributed authored supplement finding evidence is "
                "supported without a new extraction/import. Canonical publication References "
                "continue to derive only from the parent component's "
                "SourceArtifactReference. Manual CRUD and working-revision requests cannot bind "
                "the supplement as a replacement for that reference. Do not claim the old "
                "extraction named these Azure products. Capability import creates candidateId "
                "records; ReusePublished only reuses components, so importing new capability "
                "candidates would violate preservation of the existing eight capability IDs."
            ),
            "contracts": [
                "src/Ato.Copilot.Core/Services/Workspaces/WorkspaceOperationsService.cs",
                "src/Ato.Copilot.Core/Services/Workspaces/WorkspaceContracts.cs",
                "src/Ato.Copilot.Core/Services/PackageImports/CspPackageService.Publication.cs",
            ],
        },
        {
            "code": "SOURCE_PARENT_IS_NOT_A_PRODUCT_CONTRIBUTOR",
            "detail": (
                "The eight original parent components remain unchanged source provenance. "
                "Existing capability text stays baseline text; supplement descriptions are "
                "authored guidance only. Twelve new product components provide the fourteen "
                "working-revision links. Current supportingComponents presentation may also "
                "include the source parent; it must not be counted as a product contributor."
            ),
            "contracts": ["src/Ato.Copilot.Core/Services/Workspaces/WorkspaceOperationsService.ProviderCatalog.cs"],
        },
    ]


def apply_update(api, journal, plan, expected_manifest, expected_snapshot=None, supplement_root=None):
    require(expected_manifest == plan["manifestSha256"],
            "--apply requires the exact manifest SHA from --plan.")
    require(expected_snapshot == plan["snapshotSha256"], "--apply requires the exact snapshot SHA from --plan.")
    require(plan.get("strategy") == STRATEGY and not plan.get("blockers"),
            "STRATEGY_MISMATCH: use a fresh contributor-only plan and separate journal; never reuse the failed PATCH plan.")
    require(supplement_root is not None, "An exact verified supplement directory is required.")
    return Enricher(api, journal, plan, Path(supplement_root)).run()


def build_plan(api, state, manifest, mapping, manifest_sha):
    require(state.data["binding"]["base"] == api.base
            and state.data["binding"]["manifest"] == manifest.sha
            and state.data["binding"]["identity"] == loader.IDENTITY,
            "Baseline journal binding does not match this API, verified source manifest or simulation identity.")
    require(state.data["ids"].get(DATASET + "/offering") == OFFERING,
            "Baseline journal does not identify the exact permitted Azure offering.")
    require(not api.apply, "Planning API must prohibit every HTTP mutation.")
    tool = loader.Loader(api, state, manifest, SimpleNamespace(azure_offering_id=OFFERING))
    baseline = tool.refresh_context_plan(DATASET)
    require(all(p["originalWorking"]["revision"] == 2 for p in baseline["plans"]),
            "Expected eight current revision-2 capabilities; live data changed, so no automatic adaptation.")
    require(len(baseline["plans"]) == 8, "Baseline must contain exactly eight capabilities.")
    source = manifest.sources[DATASET]
    published = state.data["ids"][DATASET + "/publishedRecords"]
    records, declarations = {}, {}
    for row in published:
        reviewed = state.data["operations"][DATASET + "/review/" + row["candidateId"]]["result"]
        original = tool.candidate_source(source, reviewed)
        require(original["id"] not in records, "Duplicate source key in retained publication.")
        records[original["id"]] = row
        declarations[original["id"]] = original
    components = []
    current_components = []
    for target in mapping["components"]:
        source_key = target["existingSourceId"]
        current = None
        if source_key:
            require(source_key in records and records[source_key]["type"] == "Component",
                    "Mapped primary component is not in the immutable baseline publication.")
            current = api.request("GET", "/api/csp/inherited-components/" + records[source_key]["recordId"])
            require(current["id"] == records[source_key]["recordId"], "Component identity mismatch.")
            require(current["name"] == declarations[source_key]["name"]
                    and current["description"] == declarations[source_key]["description"],
                    "Original component content differs from the reviewed frozen source.")
            require(current["status"] == "Published" and current["sourceFormat"] == "Package"
                    and current.get("sourceArtifactReference", "").startswith(
                        "package:" + state.data["ids"][DATASET + "/package"] + "/artifact:"),
                    "Baseline component no longer has its exact published package provenance.")
            current_components.append(current)
        components.append(dict(target, componentId=None, original=None,
                               sourceParentId=current["id"] if current else None,
                               action="create-authored-product"))
    inventory = api.pages("/api/csp/inherited-components")
    baseline_ids = {r["recordId"] for r in published if r["type"] == "Component"}
    collisions = [r["id"] for r in inventory if r["id"] not in baseline_ids
                  and r["name"].casefold() in {name.casefold() for name in SERVICES.values()}]
    require(not collisions, "Named Azure components already exist outside the baseline; refusing duplicate/adoption.")
    require(len({c["cspProfileId"] for c in current_components}) == 1, "Components cross provider identities.")
    proposed = []
    for target in mapping["capabilities"]:
        source_key = target["sourceId"]
        require(source_key in records and records[source_key]["type"] == "Capability",
                "Capability source identity differs from the immutable baseline.")
        capability_id = records[source_key]["recordId"]
        item = loader.one(baseline["plans"], lambda p: p["capabilityId"] == capability_id,
                          "baseline capability", optional=False)
        parent = loader.one(current_components,
                            lambda c: any(cap["id"] == capability_id for cap in c.get("capabilities", [])),
                            "original capability parent", optional=False)
        original = loader.one(parent["capabilities"], lambda c: c["id"] == capability_id,
                              "original capability", optional=False)
        proposed.append(dict(target, name=original["name"], description=original["description"],
                             supplementName=target["name"], supplementDescription=target["description"],
                             capabilityId=capability_id, currentRevision=2,
                             proposedRevision=3, retainedWorking=item["expected"], original=original,
                             componentId=parent["id"], originalWorking=item["originalWorking"]))
    root = "/api/csp/offerings/" + OFFERING
    current = api.request("GET", root)
    context = baseline["context"]
    require(current["revision"] == context["expectedOfferingRevision"]
            and current["currentBoundaryRevisionId"] == context["boundaryRevisionId"]
            and current["currentHostingScopeRevisionId"] == context["hostingScopeRevisionId"],
            "Offering context changed while planning.")
    snapshot = {"baseline": baseline, "components": current_components, "strategy": STRATEGY}
    blockers = []
    return {
        "mode": "plan: GET-only", "status": "blocked" if blockers else "ready", "manifestSha256": manifest_sha,
        "baselineManifestSha256": manifest.sha, "snapshotSha256": snapshot_hash(snapshot),
        "offeringId": OFFERING, "authoredOn": "2026-09-27",
        "strategy": STRATEGY, "sourceComponents": current_components,
        "supplementId": mapping["supplementId"], "context": context,
        "components": components, "capabilities": proposed, "blockers": blockers,
        "providerId": current_components[0]["cspProfileId"],
        "findingId": state.data["ids"][DATASET + "/finding"],
        "sourcePublication": published,
        "sourcePackageId": state.data["ids"][DATASET + "/package"],
        "advisories": contract_advisories(),
        "constraints": [
            "Eight source parents, baseline capability text, source bytes, IDs, duties and prior history remain unchanged.",
            "Create twelve distinct authored Azure products; replace working contributors with fourteen product links.",
            "Current presentation may additionally include the original source parent; it is not a working contributor.",
            "M365, AWS/GCP product support, roles, tenant configuration and mission allocations are untouched.",
            "Retain the supplement as separate authored evidence; never rewrite old extraction claims.",
            "Apply requires exact manifest/snapshot hashes and a dedicated durable resumable journal.",
        ],
    }


class Enricher:
    """Single-threaded journaled mutations; all authoring precedes impact review."""

    def __init__(self, api, state, plan, supplement_root):
        self.api, self.state, self.plan, self.root_dir = api, state, plan, supplement_root
        self.root = "/api/csp/offerings/" + OFFERING
        self.component_root = "/api/csp/inherited-components"

    def get(self, path):
        return self.api.request("GET", path)

    def context_unchanged(self):
        current = self.get(self.root)
        context = self.plan["context"]
        require(current["revision"] == context["expectedOfferingRevision"]
                and current["currentBoundaryRevisionId"] == context["boundaryRevisionId"]
                and current["currentHostingScopeRevisionId"] == context["hostingScopeRevisionId"],
                "Offering/boundary/hosting changed; retain journal and stop.")

    def perform(self, name, method, path, body, reconcile, keyed=True):
        operation = self.state.data["operations"].get(name)
        if operation:
            require(operation["body"] == body, "Journal request content changed: " + name)
        self.context_unchanged()
        return self.state.perform(self.api, name, method, path, body, reconcile, idempotent=keyed)

    @staticmethod
    def fields_equal(actual, expected, fields):
        return all(actual.get(k) == expected.get(k) for k in fields)

    def component_valid(self, current, target):
        require(current["cspProfileId"] == self.plan["providerId"]
                and current["status"] == "Published", "Component provider or publication status changed.")
        require(target["original"] is None and current["sourceFormat"] == "Manual"
                and not current.get("sourceArtifactReference") and current["componentType"] == "Service",
                "Product must be a new explicitly authored, published manual component.")

    def prepare_component(self, target):
        key = target["key"]
        name = "component/" + key
        require(target["original"] is None and target["componentId"] is None,
                "Contributor-only strategy cannot mutate or reuse a source container.")
        body = {"name": target["name"], "description": target["description"],
                "componentType": "Service"}

        def reconcile():
            found = loader.one(self.api.pages(self.component_root),
                               lambda c: c["name"].casefold() == body["name"].casefold(),
                               "new Azure service " + key)
            if found:
                require(name in self.state.data["operations"], "Unjournaled named service exists; no adoption.")
                current = self.get(self.component_root + "/" + found["id"])
                self.component_valid(current, target)
                require(self.fields_equal(current, body, ("name", "description", "componentType")),
                        "Named service differs from the saved create request.")
                return current
            return None

        # Manual create already returns Published. It has no idempotency
        # contract: uncertain responses reconcile by exact GET, never retry.
        result = self.perform(name, "POST", self.component_root, body, reconcile, keyed=False)
        self.component_valid(result, target)
        require(self.fields_equal(result, body, ("name", "description", "componentType")),
                "Component response differs from the exact authored mapping.")
        self.state.remember("component/" + key, result["id"])
        return result

    def capability_current(self, target):
        parent = self.get(self.component_root + "/" + target["componentId"])
        return loader.one(parent["capabilities"], lambda c: c["id"] == target["capabilityId"],
                          "retained original capability", optional=False)

    def capability_valid(self, current, target):
        original = target["original"]
        require(current["id"] == target["capabilityId"]
                and current["cspInheritedComponentId"] == target["componentId"]
                and sorted(current["mappedNistControlIds"]) == sorted(original["mappedNistControlIds"])
                and self.fields_equal(current, original, ("createdAt", "createdBy")),
                "Capability identity, original control mappings or provenance changed.")

    def expected_working(self, target):
        expected = dict(target["retainedWorking"])
        expected["contributors"] = sorted(self.state.data["ids"]["component/" + key]
                                          for key in target["componentKeys"])
        require(target["componentId"] not in expected["contributors"],
                "Source provenance parent must not remain an active product contributor.")
        return expected

    def prepare_working(self, target):
        catalog = "/api/csp/catalog/capabilities/" + target["capabilityId"]
        name = "capability/" + target["capabilityId"] + "/draft"
        expected = self.expected_working(target)
        body = dict(expectedRevision=2, **expected)

        def reconcile():
            working = self.get(catalog + "/working-revision")
            require(working["revision"] in (2, 3), "Unexpected working revision; no duplicate authoring.")
            if working["revision"] == 3:
                require(name in self.state.data["operations"] and loader.Loader.same_working(working, expected),
                        "Unjournaled or materially changed revision-3 draft.")
                return working
            require(working["snapshotHash"] == target["originalWorking"]["snapshotHash"]
                    and loader.Loader.same_working(working, target["retainedWorking"]),
                    "Original revision-2 content changed before draft authoring.")
            return None

        result = self.perform(name, "PUT", catalog + "/working-revision", body, reconcile, keyed=False)
        require(result["revision"] == 3 and loader.Loader.same_working(result, expected),
                "Draft did not preserve original classification/category/duties and exact new contributors.")
        self.state.remember("draft/" + target["capabilityId"], result)

    def evidence(self):
        path = self.root + "/findings/" + self.plan["findingId"] + "/evidence"
        manifest_raw = (self.root_dir / "supplement-manifest.json").read_bytes()
        require(loader.digest(manifest_raw) == self.plan["manifestSha256"], "Supplement manifest changed before upload.")
        entries = {e["path"]: e for e in json.loads(manifest_raw)["files"]}
        entries["supplement-manifest.json"] = {"sha256": self.plan["manifestSha256"], "bytes": len(manifest_raw)}
        files = sorted(FILES | {"supplement-manifest.json"})
        for file in files:
            content_path = self.root_dir / file
            content = content_path.read_bytes()
            sha = loader.digest(content)
            require(sha == entries[file]["sha256"] and len(content) == entries[file]["bytes"],
                    "Supplement bytes changed before upload: " + file)
            name = "evidence/" + file
            description = (
                f"{NOTICE}. Newly authored Azure implementation supplement {SUPPLEMENT}; "
                f"manifest {self.plan['manifestSha256']}; original baseline {BASELINE_SHA}. "
                "Not extracted from the original generic package. Design basis only, not "
                "operational, closure or authorization evidence. Provider-private; no sharing grant. "
                f"File: {file}."
            )

            def reconcile(filename=file, expected_sha=sha, size=len(content), text=description):
                found = loader.one(self.api.pages(path), lambda e: e["fileName"] == filename,
                                   "supplement evidence " + filename)
                if found:
                    require(found["sha256"].lower() == expected_sha and found["byteLength"] == size
                            and found["description"] == text, "Retained evidence has different content/provenance.")
                return found

            operation = self.state.data["operations"].get(name)
            if operation:
                body = operation["body"]
            else:
                finding = loader.one(self.api.pages(self.root + "/findings"),
                                     lambda f: f["findingId"] == self.plan["findingId"],
                                     "original open finding", optional=False)
                require(finding["workflowState"] == "Open", "Original finding changed; never reopen/close it.")
                frozen_file = SimpleNamespace(name=file, read_bytes=lambda value=content: value)
                body = loader.multipart({"expectedFindingRevision": finding["revision"], "description": description},
                                        "file", frozen_file, self.state.key(name))
            result = self.perform(name, "POST", path, body, reconcile)
            require(result["sha256"].lower() == sha and result["byteLength"] == len(content),
                    "Evidence receipt hash/size mismatch.")
            downloaded = self.api.request("GET", path + "/" + result["evidenceId"] + "/content", binary=True)
            require(loader.digest(downloaded) == sha, "Retained supplement download failed hash validation.")
            self.state.remember(name, result["evidenceId"])

    def publication_valid(self, target, publication):
        cid = target["capabilityId"]
        catalog = "/api/csp/catalog/capabilities/" + cid
        detail = self.get(catalog)
        working = self.get(catalog + "/working-revision")
        draft = self.state.data["ids"]["draft/" + cid]
        require(publication["capabilityId"] == cid and publication.get("releaseId")
                and publication["revision"] == working["revision"] == detail["capability"]["releasedRevision"] == 3
                and publication["snapshotHash"] == working["snapshotHash"] == draft["snapshotHash"]
                and loader.Loader.same_working(working, self.expected_working(target)),
                "Canonical revision-3 release does not match the exact authored working snapshot.")
        require(detail["capability"]["name"] == target["name"]
                and detail["capability"]["description"] == target["description"],
                "Canonical capability text changed after authoring.")
        expected_ids = set(self.expected_working(target)["contributors"])
        visible_ids = {c["id"] for c in detail["supportingComponents"]}
        require(visible_ids in (expected_ids, expected_ids | {target["componentId"]})
                and not detail.get("unresolvedContributorIds"),
                "Canonical product links do not resolve, or presentation includes an unexpected non-source component.")

    def publish(self, target):
        cid = target["capabilityId"]
        prefix = "capability/" + cid
        catalog = "/api/csp/catalog/capabilities/" + cid
        draft = self.state.data["ids"]["draft/" + cid]
        published = self.state.data["operations"].get(prefix + "/publish")
        if published and published.get("result"):
            self.publication_valid(target, published["result"])
            return published["result"]
        if published:
            result = self.perform(prefix + "/publish", "POST", catalog + "/publish",
                                  published["body"], lambda: None)
            self.publication_valid(target, result)
            return result
        body = dict(self.plan["context"], changes=[{
            "kind": "Capability", "recordId": cid, "expectedRevision": 3,
            "proposedSnapshotHash": draft["snapshotHash"],
        }])

        def retained_impact():
            operation = self.state.data["operations"].get(prefix + "/impact")
            result = operation.get("result") if operation else None
            if result:
                loader.Loader.unexpired(result)
                loader.Loader.unexpired(self.get(self.root + "/impact-reviews/" + result["reviewId"]))
            return result

        impact = self.perform(prefix + "/impact", "POST", self.root + "/impact-previews", body, retained_impact)
        require(not impact["blockers"], "Canonical impact preview has blockers; no approval attempted.")
        loader.Loader.unexpired(impact)
        impact_path = self.root + "/impact-reviews/" + impact["reviewId"]

        def accepted():
            current = self.get(impact_path)
            loader.Loader.unexpired(current)
            return current if current["disposition"] == "AcceptForPublication" else None

        self.perform(prefix + "/impact-review", "POST", impact_path + "/review", {
            "expectedRevision": impact["revision"], "previewId": impact["previewId"],
            "previewHash": impact["previewHash"], "disposition": "AcceptForPublication",
            "rationale": (
                f"{NOTICE}: explicitly approve newly authored Azure implementation {SUPPLEMENT}; "
                f"retained evidence manifest {self.plan['manifestSha256']}. Original source package "
                "remains the reviewed duties baseline, not the source of new Azure product claims. "
                "Original capability ID and duties retained; NOT ATO."
            ),
        }, accepted, keyed=False)

        def preview_current():
            current = self.get(catalog + "/working-revision")
            require(current["revision"] == 3 and current["snapshotHash"] == draft["snapshotHash"]
                    and loader.Loader.same_working(current, self.expected_working(target)),
                    "Working material changed before publication preview.")
            operation = self.state.data["operations"].get(prefix + "/preview")
            result = operation.get("result") if operation else None
            if result:
                loader.Loader.unexpired(result)
            return result

        preview = self.perform(prefix + "/preview", "POST", catalog + "/publication-previews",
                               {"revision": 3, "impactReviewIds": [impact["reviewId"]]}, preview_current, keyed=False)
        loader.Loader.unexpired(preview)
        require(preview["capabilityId"] == cid and preview["revision"] == 3
                and preview["workingSnapshotHash"] == draft["snapshotHash"]
                and preview["impactReviewIds"] == [impact["reviewId"]],
                "Canonical preview does not bind the exact revision/hash/impact.")
        for field in ("dutyChanges", "referenceChanges"):
            require(not any(row.get("changeKind") not in (None, "Unchanged") for row in preview[field]),
                    "Canonical preview changes original duties/source references.")
        old = set(target["retainedWorking"]["contributors"])
        new = set(self.expected_working(target)["contributors"])
        changes = preview["contributorChanges"]
        require({row["value"] for row in changes if row["changeKind"] == "Added"} == new - old
                and {row["value"] for row in changes if row["changeKind"] == "Removed"} == old - new
                and all(row["changeKind"] in ("Added", "Removed", "Unchanged") for row in changes),
                "Canonical contributor change set differs from the exact approved mapping.")
        approval = {"revision": 3, "previewId": preview["previewId"], "previewHash": preview["previewHash"]}

        def approved():
            current = self.get(catalog + "/working-revision")
            require(current["revision"] == 3 and current["snapshotHash"] == draft["snapshotHash"],
                    "Working revision changed before approval.")
            return current if (current.get("approvedRevision") == 3
                and current.get("approvedPreviewId") == preview["previewId"]
                and current.get("approvedPreviewHash") == preview["previewHash"]) else None

        self.perform(prefix + "/approve", "POST", catalog + "/working-revision/approve",
                     approval, approved, keyed=False)

        def released():
            operation = self.state.data["operations"].get(prefix + "/publish")
            result = operation.get("result") if operation else None
            if result:
                self.publication_valid(target, result)
            return result

        result = self.perform(prefix + "/publish", "POST", catalog + "/publish", {
            **approval, "approvedRevision": 3, "idempotencyKey": self.state.key(prefix + "/publish"),
        }, released)
        self.publication_valid(target, result)
        return result

    def verify(self):
        self.context_unchanged()
        actual_ids = []
        for component in self.plan["components"]:
            cid = self.state.data["ids"]["component/" + component["key"]]
            current = self.get(self.component_root + "/" + cid)
            self.component_valid(current, component)
            require(self.fields_equal(current, component, ("name", "description")), "Final component content mismatch.")
            actual_ids.append(cid)
        require(len(set(actual_ids)) == 12, "Expected twelve distinct product component IDs.")
        require(sum(len(c["componentKeys"]) for c in self.plan["capabilities"]) == 14,
                "Expected fourteen actual contributor links.")
        for capability in self.plan["capabilities"]:
            result = self.state.data["operations"]["capability/" + capability["capabilityId"] + "/publish"]["result"]
            self.publication_valid(capability, result)
        publication = self.get("/api/csp/package-imports/" + self.plan["sourcePackageId"] + "/review-state")["publication"]
        require(sorted(publication["records"], key=lambda r: r["candidateId"])
                == sorted(self.plan["sourcePublication"], key=lambda r: r["candidateId"]),
                "Immutable original source publication was changed.")
        inventory = self.api.pages(self.component_root)
        names = {c["name"].casefold() for c in self.plan["components"]}
        require({c["id"] for c in inventory if c["name"].casefold() in names} == set(actual_ids)
                and sum(c["name"].casefold() in names for c in inventory) == 12,
                "Final service inventory contains duplicate/unexpected Azure product names.")
        self.verify_originals()
        return {"status": "verified", "offeringId": OFFERING, "productComponents": 12,
                "sourceComponentsPreserved": 8, "totalAzureComponents": 20,
                "capabilities": 8, "contributorLinks": 14, "canonicalRevision": 3,
                "sourcePublicationUnchanged": True, "manifestSha256": self.plan["manifestSha256"]}

    def run(self):
        require(self.api.apply, "Apply requires a mutation-enabled loopback client.")
        require(self.plan.get("strategy") == STRATEGY,
                "STRATEGY_MISMATCH: never execute the failed text PATCH plan.")
        mapping, manifest_sha = read_mapping(self.root_dir / "implementation-map.json")
        require(manifest_sha == self.plan["manifestSha256"], "Supplement manifest changed after plan.")
        for field, identity in (("components", "key"), ("capabilities", "sourceId")):
            for desired in mapping[field]:
                saved = loader.one(self.plan[field], lambda row: row[identity] == desired[identity],
                                   "manifest-bound planned record", optional=False)
                aliases = {"name": "supplementName", "description": "supplementDescription"} if field == "capabilities" else {}
                require(all(saved.get(aliases.get(key, key)) == value for key, value in desired.items()),
                        "Saved plan content differs from the exact authored mapping.")
        require(all(self.fields_equal(c, c["original"], ("name", "description")) for c in self.plan["capabilities"]),
                "Saved plan cannot change baseline capability text.")
        self.context_unchanged()
        self.verify_originals()
        self.evidence()
        for component in self.plan["components"]:
            self.prepare_component(component)
        for capability in self.plan["capabilities"]:
            self.prepare_working(capability)
        for capability in self.plan["capabilities"]:
            self.publish(capability)
        result = self.verify()
        self.state.remember("verifiedResult", result)
        return result

    def verify_originals(self):
        require(len(self.plan["sourceComponents"]) == 8, "Exactly eight immutable source parents are required.")
        for original in self.plan["sourceComponents"]:
            current = self.get(self.component_root + "/" + original["id"])
            require(self.fields_equal(current, original, (
                "id", "name", "description", "componentType", "cspProfileId",
                "status", "sourceFormat", "sourceFileName", "sourceArtifactReference",
                "importedAt", "importedBy")),
                "Original source component content/provenance changed; no source mutation is allowed.")
        for target in self.plan["capabilities"]:
            current = self.capability_current(target)
            self.capability_valid(current, target)
            require(self.fields_equal(current, target["original"], ("name", "description")),
                    "Original capability text changed; this strategy does not edit text.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--plan", action="store_true")
    mode.add_argument("--apply", action="store_true")
    parser.add_argument("--mapping", type=Path, required=True)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--baseline-journal", type=Path, required=True)
    parser.add_argument("--state-dir", type=Path, required=True,
                        help="New private 0700 state directory; reuse this exact directory for resume.")
    parser.add_argument("--manifest-dir", type=Path, default=loader.REPO / "demos/provider-offerings")
    parser.add_argument("--manifest-sha256")
    parser.add_argument("--snapshot-sha256")
    args = parser.parse_args(argv)
    write_state = None
    try:
        mapping, sha = read_mapping(args.mapping)
        base = loader.local_base(args.base_url)
        if args.apply:
            require(args.manifest_sha256 == sha, "--apply requires the exact manifest SHA from --plan.")
        baseline_path = args.baseline_journal.expanduser().absolute()
        require(baseline_path.name == "state.json", "Use the original loader's exact state.json.")
        loader.private_file(baseline_path)
        target = loader.private_directory(args.state_dir, create=False)
        require(target != baseline_path.parent, "Never reuse or mutate the baseline journal directory.")
        manifest = loader.Manifest(args.manifest_dir)
        binding = {"base": base, "manifest": manifest.sha, "identity": loader.IDENTITY}
        baseline_state = loader.Journal(baseline_path.parent, binding, apply=False)
        update_binding = {
            "base": base, "manifest": sha, "identity": loader.IDENTITY, "supplementId": SUPPLEMENT,
            "strategy": STRATEGY,
            "baselineJournalSha256": loader.digest(baseline_path.read_bytes()),
            "baselineManifestSha256": manifest.sha, "offeringId": OFFERING,
        }
        if (target / "state.json").exists():
            saved = loader.Journal(target, update_binding, apply=False)
            plan = saved.data["ids"].get("plan")
            require(plan and plan["manifestSha256"] == sha, "Update journal has no exact saved plan.")
            require(plan.get("strategy") == STRATEGY, "Never resume the failed PATCH strategy journal.")
            read_api = loader.Api(base, target / "cookies.txt", apply=False)
            Enricher(read_api, saved, plan, args.mapping.resolve().parent).context_unchanged()
        else:
            require(not target.exists(), "Start with a fresh state directory, or reuse its complete update journal.")
            read_api = loader.Api(base, baseline_path.parent / "cookies.txt", apply=False)
            plan = build_plan(read_api, baseline_state, manifest, mapping, sha)
        original_records = baseline_state.data["ids"][DATASET + "/publishedRecords"]
        require({c["capabilityId"] for c in plan["capabilities"]}
                == {r["recordId"] for r in original_records if r["type"] == "Capability"}
                and {c["id"] for c in plan["sourceComponents"]}
                == {r["recordId"] for r in original_records if r["type"] == "Component"},
                "Saved plan no longer preserves the exact original Azure record IDs.")
        if args.plan:
            print(json.dumps(plan, ensure_ascii=False, indent=2))
            return 0
        require(not plan["blockers"] and plan.get("strategy") == STRATEGY,
                "A fresh unblocked contributor-only plan is required.")
        require(args.snapshot_sha256 == plan["snapshotSha256"],
                "--apply requires the exact snapshot SHA from --plan; live material may have changed.")
        write_state = loader.Journal(target, update_binding, apply=True)
        write_state.remember("plan", plan)
        api = EnrichmentApi(base, target / "cookies.txt", apply=True)
        me = api.authenticate()
        identity = {"oid": me["oid"], "directoryTenantId": me["directoryTenantId"]}
        require(identity == baseline_state.data.get("authenticatedIdentity"),
                "Simulation actor differs from the original baseline; no catalog writes attempted.")
        previous_identity = write_state.data.get("authenticatedIdentity")
        require(previous_identity in (None, identity), "Update journal belongs to another simulation actor.")
        write_state.data["authenticatedIdentity"] = identity
        write_state.save()
        result = apply_update(api, write_state, plan, args.manifest_sha256,
                              args.snapshot_sha256, args.mapping.resolve().parent)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0
    except (Stop, OSError, ValueError, KeyError, TypeError) as exc:
        print(f"BLOCKED: {exc}", file=sys.stderr)
        return 2
    finally:
        if write_state is not None and write_state.lock is not None:
            os.close(write_state.lock)


if __name__ == "__main__":
    sys.exit(main())
