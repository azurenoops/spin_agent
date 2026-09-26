"""Structural acceptance tests, not a claim of SPIN import or authorization."""

import hashlib
import json
import unittest
import zipfile
from pathlib import Path

import openpyxl
import pymupdf
from pypdf import PdfReader


ROOT = Path(__file__).resolve().parent
NOTICE = "SYNTHETIC TEST DATA"
ARTIFACTS = [
    "01-flankspeed-il5-ssp.pdf",
    "02-flankspeed-il5-assessment.pdf",
    "03-flankspeed-il5-operations.pdf",
    "04-flankspeed-il5-unsigned-decision.pdf",
    "05-flankspeed-il5-components.json",
    "06-flankspeed-il5-registers.xlsx",
]
COUNTS = {
    "components": 12, "capabilities": 24, "controls": 40, "evidence": 16,
    "findings": 6, "poams": 6,
}


def load(name):
    return json.loads((ROOT / name).read_text(encoding="utf-8"))


class FlankSpeedPackageTests(unittest.TestCase):
    def test_scenario_cross_references_and_evidence_states(self):
        # Arrange
        source = load("source-content.json")
        # Act
        ids = {key: {row["id"] for row in source[key]} for key in COUNTS}
        # Assert
        self.assertIs(source["synthetic"], True)
        self.assertEqual("SYN-FS-IL5-001", source["fixtureId"])
        for key, count in COUNTS.items():
            self.assertEqual(count, len(source[key]), key)
            self.assertEqual(count, len(ids[key]), f"duplicate {key} IDs")
        for row in source["components"]:
            self.assertTrue(set(row["capabilityIds"]) <= ids["capabilities"])
            self.assertTrue(set(row["evidenceIds"]) <= ids["evidence"])
        for row in source["capabilities"]:
            self.assertTrue(row["componentIds"])
            self.assertTrue(set(row["componentIds"]) <= ids["components"])
            self.assertTrue(set(row["controlIds"]) <= ids["controls"])
            self.assertTrue(set(row["evidenceIds"]) <= ids["evidence"])
        for row in source["controls"]:
            self.assertTrue(set(row["evidenceIds"]) <= ids["evidence"])
            self.assertTrue(set(row["gapFindings"]) <= ids["findings"])
            self.assertGreaterEqual(len(row["narrative"].split()), 75, row["id"])
            for field in ("parameters", "assessmentProcedure", "acceptanceCriterion"):
                self.assertTrue(row[field], f'{row["id"]}: {field}')
        for row in source["findings"]:
            self.assertTrue(set(row["controls"]) <= ids["controls"])
            self.assertTrue(set(row["componentIds"]) <= ids["components"])
            self.assertTrue(set(row["evidenceIds"]) <= ids["evidence"])
            self.assertEqual("Open", row["status"])
        self.assertEqual(ids["findings"], {row["findingId"] for row in source["poams"]})
        for row in source["poams"]:
            self.assertTrue(row["milestones"])
            self.assertTrue(row["closureEvidence"])
            self.assertTrue(all(source["asOf"] < step["dueDate"] <= row["scheduledCompletion"]
                                for step in row["milestones"]))
        samples = [row for row in source["evidence"] if row["state"] == "Synthetic sample included"]
        self.assertEqual(4, len(samples))
        self.assertTrue(all(len(row["sampleText"]) > 80 for row in samples))
        self.assertEqual(12, sum(row["state"] == "Not supplied" for row in source["evidence"]))
        for row in source["evidence"]:
            self.assertTrue(set(row["relatedControls"]) <= ids["controls"])
            self.assertTrue(set(row["relatedComponents"]) <= ids["components"])
        for key, minimum in [("roles", 8), ("dataFlows", 8), ("monitoring", 10), ("procedures", 6)]:
            self.assertGreaterEqual(len(source[key]), minimum, key)
        self.assertEqual("Draft - unsigned - not effective", source["decision"]["status"])

    def test_expected_ledger_has_actual_page_provenance(self):
        # Arrange
        expected = load("expected-results.json")
        # Act
        docs = {name: PdfReader(ROOT / name) for name in ARTIFACTS if name.endswith(".pdf")}
        # Assert
        self.assertEqual("SYN-FS-IL5-001", expected["fixture"])
        self.assertEqual(12, expected["uniqueComponentCount"])
        self.assertEqual(24, expected["uniqueCapabilityCount"])
        self.assertIn("not a recorded application result", expected["expectationsType"])
        for key in ("components", "capabilities", "controls", "findings", "poams"):
            self.assertEqual(COUNTS[key], len(expected[key]))
            for row in expected[key]:
                citation = row["source"]
                text = docs[citation["artifact"]].pages[citation["page"] - 1].extract_text()
                self.assertIn(row["fixtureId"], text, f"{key}: {citation}")

    def test_every_pdf_page_is_marked_readable_and_within_page_bounds(self):
        # Arrange
        names = [name for name in ARTIFACTS if name.endswith(".pdf")]
        # Act
        documents = [pymupdf.open(ROOT / name) for name in names]
        # Assert
        try:
            self.assertGreaterEqual(sum(len(doc) for doc in documents), 60)
            for document in documents:
                self.assertFalse(document.is_encrypted)
                for page in document:
                    text = page.get_text()
                    self.assertIn(NOTICE, text)
                    self.assertIn("NOT A VALID ATO", text)
                    self.assertGreater(len(text.strip()), 160)
                    for x0, y0, x1, y1, word, *_ in page.get_text("words"):
                        self.assertGreaterEqual(x0, 20, word)
                        self.assertLessEqual(x1, page.rect.width - 20, word)
                        self.assertGreaterEqual(y0, 12, word)
                        self.assertLessEqual(y1, page.rect.height - 12, word)
        finally:
            for document in documents:
                document.close()

    def test_component_sections_keep_final_responsibilities_on_same_page(self):
        # Arrange
        source = load("source-content.json")
        expected = load("expected-results.json")
        capabilities = {row["id"]: row for row in source["capabilities"]}
        document = PdfReader(ROOT / ARTIFACTS[0])
        pages = {row["fixtureId"]: row["source"]["page"] for row in expected["components"]}
        # Act
        page_text = {key: " ".join(document.pages[page - 1].extract_text().split())
                     for key, page in pages.items()}
        # Assert
        for component in source["components"]:
            final_capability = capabilities[component["capabilityIds"][-1]]
            self.assertIn(" ".join(final_capability["customerActions"].split()),
                          page_text[component["id"]], component["id"])

    def test_structured_projection_preserves_review_and_relationship_identity(self):
        # Arrange
        projection = load(ARTIFACTS[4])
        # Act
        components = projection["components"]
        capabilities = projection["capabilities"]
        ids = {row["id"] for key in ("components", "capabilities", "authorizationDecisionClaims",
                                    "boundaryClaims", "assessmentFindings", "poamItems")
               for row in projection[key]}
        # Assert
        self.assertEqual(12, len(components))
        self.assertEqual(24, len(capabilities))
        self.assertEqual(6, len(projection["assessmentFindings"]))
        self.assertEqual(6, len(projection["poamItems"]))
        for row in components + capabilities:
            self.assertIn(NOTICE, row["description"])
        for key in ("authorizationDecisionClaims", "boundaryClaims", "assessmentFindings", "poamItems"):
            for row in projection[key]:
                self.assertTrue(row["claim"]["qualifications"])
                for relation in row["claim"].get("relationships", []):
                    self.assertIn(relation["targetSourceId"], ids)
        decision = projection["authorizationDecisionClaims"][0]["claim"]["authorizationDecision"]
        self.assertEqual("Draft - unsigned - not effective", decision["statusAsStated"])
        self.assertNotIn("effectiveDate", decision)
        self.assertFalse(decision.get("decisionDate"))
        self.assertFalse(decision.get("expirationDate"))
        for row in projection["poamItems"]:
            self.assertEqual([], row["claim"]["poamItem"]["submittedEvidenceReferences"])

    def test_manifest_hashes_and_archive_entries_match_actual_bytes(self):
        # Arrange
        manifest = load("package-manifest.json")
        # Act
        files = manifest["artifacts"]
        # Assert
        self.assertEqual(set(ARTIFACTS), set(files))
        for name, entry in files.items():
            content = (ROOT / name).read_bytes()
            self.assertEqual(len(content), entry["bytes"])
            self.assertEqual(hashlib.sha256(content).hexdigest(), entry["sha256"])
        for name, expected_count in [("flankspeed-il5-ato-clean.zip", 6),
                                     ("flankspeed-il5-ato-needs-attention.zip", 9)]:
            content = (ROOT / name).read_bytes()
            self.assertEqual(hashlib.sha256(content).hexdigest(), manifest["archives"][name]["sha256"])
            with zipfile.ZipFile(ROOT / name) as archive:
                self.assertIsNone(archive.testzip())
                self.assertEqual(expected_count, len(archive.namelist()))
                self.assertEqual(archive.namelist(), manifest["archives"][name]["entries"])
                self.assertTrue(set(ARTIFACTS) <= set(archive.namelist()))
                self.assertFalse(any("harbor" in path.lower() or ".." in path for path in archive.namelist()))
                for artifact in ARTIFACTS:
                    self.assertEqual((ROOT / artifact).read_bytes(), archive.read(artifact))

    def test_attention_exceptions_are_real_and_explicit(self):
        # Arrange
        from io import BytesIO
        with zipfile.ZipFile(ROOT / "flankspeed-il5-ato-needs-attention.zip") as archive:
            encrypted_bytes = archive.read("07-encrypted-appendix.pdf")
            malformed = archive.read("08-malformed-source.json")
            unsupported = archive.read("09-unsupported-appendix.synthetic")
        # Act
        reader = PdfReader(BytesIO(encrypted_bytes))
        # Assert
        self.assertTrue(reader.is_encrypted)
        self.assertEqual(0, reader.decrypt("incorrect-fixture-password"))
        self.assertNotEqual(0, reader.decrypt("fixture-only-password"))
        self.assertIn(NOTICE, reader.pages[0].extract_text())
        with self.assertRaises(json.JSONDecodeError):
            json.loads(malformed)
        self.assertIn(NOTICE.encode(), unsupported)
        outcomes = load("expected-results.json")["attentionEntries"]
        self.assertEqual(3, len(outcomes))
        self.assertTrue(all(row["reason"] for row in outcomes))

    def test_workbook_registers_and_no_executable_formulas(self):
        # Arrange
        workbook = openpyxl.load_workbook(ROOT / ARTIFACTS[5], read_only=False, data_only=False)
        # Act
        row_counts = {"Components": 12, "Capabilities": 24, "Controls": 40,
                      "Findings": 6, "POAM": 6, "Evidence": 16}
        # Assert
        try:
            for sheet, count in row_counts.items():
                self.assertEqual(count + 1, workbook[sheet].max_row, sheet)
                self.assertEqual("A2", workbook[sheet].freeze_panes)
                self.assertTrue(workbook[sheet].auto_filter.ref)
            for sheet in workbook:
                values = []
                for row in sheet:
                    for cell in row:
                        self.assertNotEqual("f", cell.data_type)
                        if isinstance(cell.value, str):
                            values.append(cell.value)
                self.assertTrue(any(NOTICE in value for value in values), sheet.title)
        finally:
            workbook.close()


if __name__ == "__main__":
    unittest.main()
