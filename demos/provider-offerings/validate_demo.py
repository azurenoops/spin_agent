#!/usr/bin/env python3
"""Validate local fixture integrity; does not simulate successful API ingestion."""
import csv
import hashlib
import json
from pathlib import Path
import re
from uuid import UUID
from xml.etree import ElementTree
import zipfile

ROOT = Path(__file__).resolve().parent
LABEL = "SYNTHETIC DEMONSTRATION ONLY"
COLLECTIONS = {"components", "capabilities", "responsibilities", "authorizationReferences",
               "authorizationDecisionClaims", "boundaryClaims", "assessmentFindings", "poamItems"}
FIELDS = {"name", "title", "description", "id", "uuid", "kind", "type", "componentType", "controlId",
          "control-id", "controlIds", "mappedNistControlIds", "responsibility", "componentIds",
          "contributorIds", "componentId", "component-uuid", "capabilityId"}


def check_source(paths, expected):
    # Arrange: load the six native source documents without any semantic model.
    source = {}
    for path in paths:
        document = json.loads(path.read_text())
        assert set(document) <= COLLECTIONS
        assert not (set(document) & set(source))
        source.update(document)
    # Act: inspect identity, source dependencies and the structured parser field set.
    assert set(source) == COLLECTIONS
    declarations = [record for records in source.values() for record in records]
    ids = [record["id"] for record in declarations]
    assert len(ids) == len(set(ids)), paths
    component_ids = {r["id"] for r in source["components"]}
    capability_ids = {r["id"] for r in source["capabilities"]}
    for collection in ["components", "capabilities", "responsibilities"]:
        for record in source[collection]:
            assert set(record) <= FIELDS, record
            assert LABEL in record["description"], record
    for capability in source["capabilities"]:
        assert set(capability["componentIds"]) <= component_ids
        assert capability["responsibility"] == "Shared"
        assert all(re.fullmatch(r"[A-Z]{2}-\d+", control) for control in capability["controlIds"])
    for duty in source["responsibilities"]:
        assert duty["capabilityId"] in capability_ids
        assert duty["responsibility"] in {"Provider", "Shared", "Customer"}
    for capability_id in capability_ids:
        assert {r["responsibility"] for r in source["responsibilities"] if r["capabilityId"] == capability_id} == {"Provider", "Shared", "Customer"}
    for reference in source["authorizationReferences"]:
        assert set(reference) <= {"kind", "name", "title", "description", "id", "uuid", "reference", "issuer", "issuedAt", "expiresAt"}
        assert LABEL in reference["reference"] and "NOT ATO" in reference["reference"]
        assert len(reference["reference"]) <= 2000
    claim_sections = {"authorizationDecisionClaims": "authorizationDecision", "boundaryClaims": "boundary",
                      "assessmentFindings": "assessmentFinding", "poamItems": "poamItem"}
    for collection, section in claim_sections.items():
        for record in source[collection]:
            assert set(record) == {"id", "name", "claim"}
            claim = record["claim"]
            assert section in claim
            assert LABEL in " ".join(claim["qualifications"])
            for relationship in claim.get("relationships", []):
                assert relationship["targetSourceId"] in ids
    finding = source["assessmentFindings"][0]["claim"]["assessmentFinding"]
    poam = source["poamItems"][0]["claim"]["poamItem"]
    decision = source["authorizationDecisionClaims"][0]["claim"]["authorizationDecision"]
    assert decision["statusAsStated"] == "Approved"
    assert "Fictional demonstration distribution" in decision["decisionType"] and "NOT ATO" in decision["decisionType"]
    assert LABEL in decision["subject"] and decision["authority"].startswith("SYNTHETIC ")
    assert finding["severityAsStated"] == "Moderate" and finding["statusAsStated"] == "Open"
    assert poam["submittedEvidenceReferences"] == [] and len(poam["milestones"]) == 3
    actual = dict(Component=len(source["components"]), Capability=len(source["capabilities"]),
                  ControlMapping=sum(len(c["controlIds"]) for c in source["capabilities"]),
                  Responsibility=len(source["capabilities"]) + len(source["responsibilities"]),
                  AuthorizationReference=1, AuthorizationDecisionClaim=1, BoundaryClaim=1, AssessmentFinding=1, PoamItem=1)
    # Assert: deterministic predicted counts match the persisted manifest.
    assert actual == expected, (actual, expected)
    return source


def main():
    # Arrange: all files must exist before integrity checks.
    manifest = json.loads((ROOT / "internal-manifest.json").read_text())
    checked = 0
    # Act: validate bytes, distributions, cross-document references and formats.
    for entry in manifest["files"]:
        path = ROOT / entry["path"]
        content = path.read_bytes()
        assert hashlib.sha256(content).hexdigest() == entry["sha256"], path
        assert len(content) == entry["bytes"]
        if path.suffix in {".md", ".json", ".csv"}:
            assert LABEL in content.decode("utf-8"), path
        if path.suffix == ".pdf":
            assert content.startswith(b"%PDF-1.4") and content.endswith(b"%%EOF\n")
            assert b"/Encrypt" not in content and LABEL.encode() in content
            xref = int(re.search(rb"startxref\n(\d+)\n", content)[1])
            assert content[xref:xref + 4] == b"xref"
            offsets = re.findall(rb"(\d{10}) 00000 n", content[xref:])
            assert all(content[int(offset):].startswith(f"{i} 0 obj".encode()) for i, offset in enumerate(offsets, 1))
        if path.suffix in {".docx", ".zip"}:
            with zipfile.ZipFile(path) as archive:
                assert archive.testzip() is None
                if path.suffix == ".docx":
                    for name in archive.namelist():
                        ElementTree.fromstring(archive.read(name))
                    assert LABEL.encode() in archive.read("word/document.xml")
                    assert LABEL.encode() in archive.read("word/footer1.xml")
                elif path.name == "review-source.zip":
                    names = sorted(p.name for p in (path.parent / "sources").glob("*.json"))
                    assert len(names) == 6 and archive.namelist() == names
                    for name in names:
                        assert archive.read(name) == (path.parent / "sources" / name).read_bytes()
                elif path.name == "customer-bundle.zip":
                    assert len(archive.namelist()) == 20
                    assert not any(name.startswith("PRIVATE-") for name in archive.namelist())
                    for name in archive.namelist():
                        assert archive.read(name) == (path.parent / name).read_bytes()
        if path.name == "evidence-index.csv":
            with path.open(newline="") as stream:
                for row in csv.DictReader(stream):
                    assert (path.parent / row["source_file"]).is_file()
                    assert row["citation"] in (path.parent / row["source_file"]).read_text()
        checked += 1
    sources = []
    for source in manifest["sources"]:
        paths = [ROOT / filename for filename in source["sourceFiles"]]
        assert source["sourceDocumentCount"] == len(paths) == 6
        assert source["expectedEntryCountIncludingContainer"] == 7
        assert source["expectedSemanticModelCalls"] == 0
        assert len(source["sourceDocuments"]) == 6
        assert {document["archivePath"] for document in source["sourceDocuments"]} == {path.name for path in paths}
        for kind, total in source["expectedCandidates"].items():
            assert sum(document["expectedCandidates"][kind] for document in source["sourceDocuments"]) == total
        sources.append(check_source(paths, source["expectedCandidates"]))
        scope = source["hostingScopes"][0]
        boundary = sources[-1]["boundaryClaims"][0]["claim"]["boundary"]
        assert source["environments"] == [boundary["environment"]]
        assert all(str(value) in boundary["scope"] for value in scope.values())
        for citation in source["hostingScopeCitations"]:
            assert citation["quote"] in boundary["scope"]
            assert citation["quote"] in (ROOT / citation["sourceFile"]).read_text()
        if scope["kind"] == "Azure":
            assert scope["cloud"] == "AzureUSGovernment"
            assert UUID(scope["directoryTenantId"]).int and UUID(scope["subscriptionId"]).int
            assert scope["resourceId"].startswith(f"/subscriptions/{scope['subscriptionId']}/resourceGroups/")
            assert boundary["resourceIds"] == [scope["resourceId"]]
        else:
            assert scope["kind"] == "Service" and scope["environment"] == "Microsoft365DoD"
            assert scope["serviceId"].startswith("synthetic-") and scope["tenantReference"].startswith("synthetic-")
            assert not {"cloud", "subscriptionId", "resourceId", "directoryTenantId"} & set(scope)
            assert boundary["resourceIds"] == [scope["serviceId"]]
        for declaration in source["declarations"]:
            source_path = ROOT / declaration["sourceFile"]
            assert source_path.is_file()
            assert (ROOT / declaration["companionDocument"]).is_file()
            collection, index = re.fullmatch(r"\$/([A-Za-z]+)\[(\d+)]", declaration["locator"]).groups()
            assert sources[-1][collection][int(index)]["id"] == declaration["sourceId"]
            assert declaration["sourceQuote"] in source_path.read_text()
            assert declaration["archivePath"] == source_path.name
    baseline, collaboration, proposed = sources
    assert len(baseline["capabilities"]) == 8 and len(collaboration["capabilities"]) == 5
    changed = []
    for before, after in zip(baseline["capabilities"], proposed["capabilities"]):
        if before != after:
            changed.append(before["id"])
            assert {key for key in before if before[key] != after[key]} == {"description"}
    assert changed == ["AZ-capability-audit", "AZ-capability-backup"]
    assert all(baseline[key] == proposed[key] for key in baseline if key != "capabilities")
    # Assert: each source has an isolated inventory and no private archive leakage.
    assert not ({c["id"] for c in baseline["components"]} & {c["id"] for c in collaboration["components"]})
    print(f"PASS: {checked} file hashes/formats; 3 six-document native inventories; exact source quotes; exact two-update diff; private archive exclusion.")
    print("C# analyzer/API execution and manual document rendering remain operator checks.")


if __name__ == "__main__":
    main()
