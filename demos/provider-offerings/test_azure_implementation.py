"""Validate the authored Azure supplement without changing imported baselines."""
import csv
import hashlib
import json
from pathlib import Path
import unittest
from xml.etree import ElementTree
import zipfile

ROOT = Path(__file__).resolve().parent
FOLDER = ROOT / "azure-implementation-2026-09-27"


class AzureImplementationTests(unittest.TestCase):
    def test_backup_has_distinct_backup_and_key_management_contributors(self):
        # Arrange
        mapping = json.loads((FOLDER / "implementation-map.json").read_text())
        # Act
        components = {item["key"]: item for item in mapping["components"]}
        backup = next(item for item in mapping["capabilities"] if item["sourceId"] == "AZ-capability-backup")
        # Assert
        self.assertEqual(len(components), 12)
        self.assertEqual(len(mapping["capabilities"]), 8)
        self.assertEqual(backup["componentKeys"], ["backup", "keyvault"])
        self.assertEqual(components["backup"]["name"], "Azure Backup (Recovery Services vault)")
        self.assertEqual(components["keyvault"]["name"], "Azure Key Vault")
        self.assertEqual(components["keyvault"]["existingSourceId"], "AZ-component-encryption")
        for capability in mapping["capabilities"]:
            self.assertTrue(set(capability["componentKeys"]) <= components.keys())
            self.assertIn("SYNTHETIC DEMONSTRATION ONLY", capability["description"])

    def test_customer_materials_preserve_real_links_and_historical_disclosure(self):
        # Arrange
        folder = FOLDER
        # Act
        guide = (folder / "azure-implementation-guide.md").read_text()
        with (folder / "capability-component-map.csv").open() as stream:
            rows = list(csv.DictReader(stream))
        # Assert
        self.assertIn("explicitly authored", guide)
        self.assertIn("not extracted", guide)
        self.assertNotIn("Shared configuration backup vault", guide)
        backup = [row["component_name"] for row in rows if row["capability_name"] == "Backup and recovery"]
        self.assertEqual(backup, ["Azure Backup (Recovery Services vault)", "Azure Key Vault"])
        pdf = (folder / "azure-implementation-guide.pdf").read_bytes()
        self.assertTrue(pdf.startswith(b"%PDF-1.4"))
        self.assertIn(b"Azure Key Vault", pdf)
        self.assertIn(b"Azure Backup", pdf)
        with zipfile.ZipFile(folder / "azure-implementation-guide.docx") as word:
            body = ElementTree.fromstring(word.read("word/document.xml"))
            text = " ".join(body.itertext())
            self.assertIn("Azure Key Vault", text)
            self.assertIn("Azure Backup", text)
        with zipfile.ZipFile(folder / "customer-bundle.zip") as bundle:
            self.assertIn("azure-implementation-guide.pdf", bundle.namelist())
            self.assertIn("baseline/service-guide.pdf", bundle.namelist())
            self.assertFalse(any("PRIVATE" in name or "manifest" in name for name in bundle.namelist()))

    def test_supplement_hashes_and_frozen_baseline(self):
        # Arrange
        mapping = json.loads((FOLDER / "implementation-map.json").read_text())
        manifest = json.loads((FOLDER / "supplement-manifest.json").read_text())
        # Act / Assert
        self.assertEqual(hashlib.sha256((ROOT / "internal-manifest.json").read_bytes()).hexdigest(),
                         mapping["baselineManifestSha256"])
        for entry in manifest["files"]:
            content = (FOLDER / entry["path"]).read_bytes()
            self.assertEqual(hashlib.sha256(content).hexdigest(), entry["sha256"])
            self.assertEqual(len(content), entry["bytes"])
        self.assertEqual(manifest["capabilityCount"], 8)
        self.assertEqual(manifest["componentCount"], 12)


if __name__ == "__main__":
    unittest.main()
