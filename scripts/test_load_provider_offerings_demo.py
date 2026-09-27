"""Offline safety and contract tests; no HTTP server or production API is used."""
import importlib.util
import email.message
import io
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
import urllib.request
import urllib.response
from unittest.mock import Mock, patch


SPEC = importlib.util.spec_from_file_location(
    "provider_demo", Path(__file__).with_name("load-provider-offerings-demo.py"))
demo = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(demo)


class SafetyTests(unittest.TestCase):
    def test_loopback_only(self):
        # Arrange / Act / Assert
        for url in ("https://example.org", "http://127.0.0.1.evil.test",
                    "http://user@localhost", "http://localhost/api",
                    "http://localhost?x=1", "file:///etc/passwd",
                    "http://0.0.0.0", "http://[::ffff:192.0.2.1]"):
            with self.subTest(url=url), self.assertRaises(demo.Stop):
                demo.local_base(url)
        self.assertEqual(demo.local_base("http://localhost:3001/"), "http://127.0.0.1:3001")
        self.assertEqual(demo.local_base("http://[::1]:3001"), "http://[::1]:3001")

    def test_source_locator_is_exact(self):
        # Arrange
        source = {"capabilities": [{"name": "Synthetic", "controlIds": ["AU-2"]}]}
        # Act / Assert
        self.assertEqual(demo.locate(source, "$/capabilities[0]"), source["capabilities"][0])
        with self.assertRaises(demo.Stop):
            demo.locate(source, "$/capabilities[5]")

    def test_no_state_in_checkout(self):
        # Arrange / Act / Assert
        with self.assertRaises(demo.Stop):
            demo.private_directory(demo.REPO / ".demo-state", create=False)

    def test_ready_requires_complete_native_coverage(self):
        # Arrange
        expected = {"expectedEntryCountIncludingContainer": 7}
        status = {"processingState": "ReadyForReview", "lastError": None,
                  "coverage": {"total": 7, "processed": 7, "pending": 0,
                               "failed": 0, "unsupported": 0, "unreadable": 0, "excluded": 0},
                  "analysisProgress": {"modelCalls": 0}}
        # Act / Assert
        demo.check_ready(status, expected)
        for key in ("failed", "unsupported", "excluded", "pending", "unreadable"):
            altered = json.loads(json.dumps(status))
            altered["coverage"][key] = 1
            with self.subTest(key=key), self.assertRaises(demo.Stop):
                demo.check_ready(altered, expected)
        status["analysisProgress"]["modelCalls"] = 1
        with self.assertRaises(demo.Stop):
            demo.check_ready(status, expected)

    def test_pagination_does_not_drop_second_page(self):
        # Arrange
        api = demo.Api("http://127.0.0.1:3001", None, apply=False)
        api.request = Mock(side_effect=[
            {"items": [{"id": "a"}], "total": 2, "page": 1},
            {"items": [{"id": "b"}], "total": 2, "page": 2}])
        # Act
        rows = api.pages("/records")
        # Assert
        self.assertEqual(rows, [{"id": "a"}, {"id": "b"}])
        self.assertIn("page=2", api.request.call_args.args[1])

    def test_plan_refuses_mutation(self):
        # Arrange
        api = demo.Api("http://127.0.0.1:3001", None, apply=False)
        # Act / Assert
        with self.assertRaises(demo.Stop):
            api.request("POST", "/api/auth/simulate")

    def test_uncertain_non_idempotent_write_stops(self):
        # Arrange
        state = demo.Journal.memory()
        api = Mock()
        state.data["operations"]["preview"] = {
            "method": "POST", "path": "/preview", "body": {}, "key": None,
            "status": "pending"}
        # Act / Assert
        with self.assertRaises(demo.Stop):
            state.perform(api, "preview", "POST", "/preview", {},
                          reconcile=lambda: None, idempotent=False)
        api.request.assert_not_called()

    def test_uncertain_write_reconciles_without_post(self):
        # Arrange
        state = demo.Journal.memory()
        api = Mock()
        state.data["operations"]["record"] = {
            "method": "POST", "path": "/record", "body": {}, "key": None,
            "status": "pending"}
        result = {"recordId": "record", "metadataReviewState": "Recorded"}
        # Act
        actual = state.perform(api, "record", "POST", "/record", {},
                               reconcile=lambda: result, idempotent=False)
        # Assert
        self.assertEqual(actual, result)
        self.assertEqual(state.data["operations"]["record"]["status"], "done")
        api.request.assert_not_called()

    def test_keyed_retry_reuses_original_revision_and_key(self):
        # Arrange
        state = demo.Journal.memory()
        api = Mock()
        api.request.return_value = {"id": "retained"}
        state.data["operations"]["create"] = {
            "method": "POST", "path": "/create", "body": {"expectedRevision": 1},
            "key": "stable-key", "status": "pending"}
        # Act
        state.perform(api, "create", "POST", "/create", {"expectedRevision": 9},
                      reconcile=lambda: None)
        # Assert
        self.assertEqual(api.request.call_args.kwargs["body"], {"expectedRevision": 1})
        self.assertEqual(api.request.call_args.kwargs["key"], "stable-key")

    def test_failed_send_is_journaled_before_network(self):
        # Arrange
        state = demo.Journal.memory()
        api = Mock()
        def lose_response(*args, **kwargs):
            self.assertEqual(state.data["operations"]["create"]["status"], "pending")
            raise demo.Stop("response lost")
        api.request.side_effect = lose_response
        # Act / Assert
        with self.assertRaises(demo.Stop):
            state.perform(api, "create", "POST", "/create", {"value": 1},
                          reconcile=lambda: None)
        self.assertEqual(state.data["operations"]["create"]["body"], {"value": 1})

    def test_repeated_page_cannot_fake_complete_inventory(self):
        # Arrange
        api = demo.Api("http://127.0.0.1:3001", None, apply=False)
        api.request = Mock(side_effect=[
            {"items": [{"id": "a"}], "total": 2, "page": 1},
            {"items": [{"id": "a"}], "total": 2, "page": 1}])
        # Act / Assert
        with self.assertRaises(demo.Stop):
            api.pages("/api/records")

    def test_authentication_checks_gate_before_post(self):
        # Arrange
        api = demo.Api("http://127.0.0.1:3001", None, apply=True)
        api.request = Mock(return_value={"simulation": None})
        # Act / Assert
        with self.assertRaises(demo.Stop):
            api.authenticate()
        api.request.assert_called_once_with("GET", "/api/auth/login-config")

    def test_redirects_are_never_followed(self):
        # Arrange / Act / Assert
        with self.assertRaises(demo.Stop):
            demo.NoRedirect().redirect_request(None, None, 302, "", {}, "https://external.invalid")

    def test_manifest_hash_tampering_stops_before_http(self):
        # Arrange
        root = demo.REPO / "demos/provider-offerings"
        real_read = Path.read_bytes
        def read(path):
            content = real_read(path)
            return content + b"tampered" if path.name == "CATALOG.md" else content
        # Act / Assert
        with patch.object(Path, "read_bytes", read), self.assertRaises(demo.Stop):
            demo.Manifest(root)

    def test_manifest_and_source_scopes_are_verified_offline(self):
        # Arrange / Act
        manifest = demo.Manifest(demo.REPO / "demos/provider-offerings")
        # Assert
        self.assertEqual(manifest.hosting_scopes(demo.DATASETS[0])[0]["cloud"], "AzureUSGovernment")
        self.assertEqual(manifest.hosting_scopes(demo.DATASETS[1])[0]["kind"], "Service")
        manifest.sources[demo.DATASETS[0]]["hostingScopes"][0]["subscriptionId"] = "invented"
        with self.assertRaises(demo.Stop):
            manifest.hosting_scopes(demo.DATASETS[0])

    def test_real_simulation_secure_cookies_are_sent_only_to_loopback(self):
        # Arrange: actual POST /simulate cookie attributes, no fabricated request identities.
        api = demo.Api("http://127.0.0.1:3001", None, apply=True)
        headers = email.message.Message()
        headers.add_header("Set-Cookie", "ato-simulation=dev-cspadmin; path=/; secure; httponly; samesite=strict")
        headers.add_header("Set-Cookie", "X-Simulated=true; path=/; secure; httponly; samesite=strict")
        url = "http://127.0.0.1:3001/api/auth/simulate"
        response = urllib.response.addinfourl(io.BytesIO(b""), headers, url, 204)
        api.jar.extract_cookies(response, urllib.request.Request(url))
        # Act
        request = urllib.request.Request("http://127.0.0.1:3001/api/auth/me")
        api.jar.add_cookie_header(request)
        # Assert
        self.assertIn("ato-simulation=dev-cspadmin", request.get_header("Cookie", ""))
        self.assertIn("X-Simulated=true", request.get_header("Cookie", ""))
        outside = urllib.request.Request("http://example.invalid/api/auth/me")
        api.jar.add_cookie_header(outside)
        self.assertIsNone(outside.get_header("Cookie"))

    def test_plan_prints_inventory_without_authentication_or_local_writes(self):
        # Arrange
        api = Mock()
        api.request.return_value = {"simulation": {"identities": [{"id": demo.IDENTITY}]}}
        api.pages.return_value = []
        output = io.StringIO()
        state = demo.Journal.memory()
        # Act
        with patch.object(demo, "Api", return_value=api), \
                patch.object(demo, "Journal", return_value=state) as journal, \
                patch("sys.stdout", output):
            result = demo.main(["--plan", "--base-url", "http://127.0.0.1:3001",
                                "--state-dir", str(Path.home() / ".local/state/provider-demo-offline-test")])
        # Assert
        self.assertEqual(result, 0)
        self.assertFalse(journal.call_args.kwargs["apply"])
        api.authenticate.assert_not_called()
        api.request.assert_called_once_with("GET", "/api/auth/login-config")
        plan = json.loads(output.getvalue())
        self.assertEqual([p["publishCapabilities"] for p in plan["proposed"]], [8, 5])
        self.assertEqual(state.data["operations"], {})

    def test_dotnet_seven_digit_timestamp_preserves_expiry_check(self):
        # Arrange / Act
        actual = demo.parse_api_timestamp("2026-09-26T23:26:14.7816509+00:00")
        # Assert
        self.assertEqual(actual.microsecond, 781650)
        self.assertEqual(actual.isoformat(), "2026-09-26T23:26:14.781650+00:00")
        demo.Loader.unexpired({"expiresAt": "2099-01-01T00:00:00.1234567Z"})
        with self.assertRaisesRegex(demo.Stop, "expired"):
            demo.Loader.unexpired({"expiresAt": "2000-01-01T00:00:00.1234567+00:00"})

    def test_api_timestamp_rejects_invalid_or_missing_timezone(self):
        # Arrange / Act / Assert
        for value in ("2026-09-26T23:26:14", "not-a-date", "2026-13-26T23:26:14Z",
                      "2026-09-26T23:26:14.12345678Z", "2026-09-26T23:26:14+25:00"):
            with self.subTest(value=value), self.assertRaises(demo.Stop):
                demo.parse_api_timestamp(value)
        for fraction in ("", ".1", ".123", ".123456", ".1234567"):
            with self.subTest(fraction=fraction):
                self.assertIsNotNone(demo.parse_api_timestamp("2099-01-01T00:00:00" + fraction + "-04:00").tzinfo)


class ContractApi:
    """Small in-memory HTTP-contract double, not an application/auth bypass."""
    def __init__(self, source, files):
        self.source = source
        self.calls = []
        self.offering = None
        self.boundaries, self.hosting, self.versions = [], [], []
        self.decisions, self.findings, self.poams, self.artifacts = [], [], [], []
        self.impacts = {}
        self.package = None
        self.preview = self.publication = None
        self.entries = [
            {"entryId": name, "artifactId": name, "fileName": name, "archivePath": name,
             "sha256": row["sha256"], "status": "Processed", "reason": None, "exclusionReason": None}
            for name, row in files.items()
        ]
        self.candidates = []
        kinds = {"components": "Component", "capabilities": "Capability", "responsibilities": "Responsibility",
                 "boundaryClaims": "BoundaryClaim", "authorizationReferences": "AuthorizationReference",
                 "authorizationDecisionClaims": "AuthorizationDecisionClaim", "assessmentFindings": "AssessmentFinding",
                 "poamItems": "PoamItem"}
        for name, doc in source["documents"].items():
            for collection, records in doc.items():
                for index, record in enumerate(records):
                    citation = {"artifactId": name, "archivePath": name,
                                "locator": f"$/{collection}[{index}]", "quote": json.dumps(record)}
                    candidate = {
                        "candidateId": record["id"], "type": kinds[collection], "name": record["name"],
                        "description": record.get("description", ""), "componentType": "Service",
                        "classification": "", "serviceCategory": "", "controlDuties": {},
                        "contributorIds": record.get("componentIds", []) or ([record["capabilityId"]] if "capabilityId" in record else []),
                        "citations": [citation], "duplicateMatches": [], "reviewState": "NeedsReview", "revision": 1,
                        "unresolvedDependencies": [], "claim": record.get("claim"), "publishedRecordId": None,
                        "authorizationReference": ({"reference": record["reference"], "issuer": record["issuer"],
                                                    "issuedAt": record.get("issuedAt"), "expiresAt": record.get("expiresAt")}
                                                   if collection == "authorizationReferences" else None),
                    }
                    self.candidates.append(candidate)
                    if collection == "capabilities":
                        for control in record["controlIds"]:
                            self.candidates.append(dict(candidate, candidateId=record["id"] + control,
                                                        type="ControlMapping", name=control, description="",
                                                        contributorIds=[record["id"]]))
                        self.candidates.append(dict(candidate, candidateId=record["id"] + "-duty",
                                                    type="Responsibility", name=record["responsibility"], description="",
                                                    contributorIds=[record["id"]]))

    def pages(self, path):
        if path == "/api/csp/offerings":
            return [dict(self.offering)] if self.offering else []
        suffix = path.rsplit("/", 1)[1]
        return {
            "boundary-revisions": self.boundaries, "hosting-scope-revisions": self.hosting,
            "package-versions": self.versions, "entries": self.entries, "candidates": self.candidates,
            "authorization-records": self.decisions, "findings": self.findings, "poam-items": self.poams,
            "evidence": self.artifacts, "impact-reviews": list(self.impacts.values()),
        }[suffix]

    def request(self, method, path, body=None, key=None, binary=False):
        self.calls.append((method, path, body, key))
        suffix = path.rsplit("/", 1)[1]
        if method == "GET":
            if binary:
                require_name = path.rsplit("/", 2)[1]
                artifact = next(a for a in self.artifacts if a["evidenceId"] == require_name)
                return (demo.REPO / "demos/provider-offerings" /
                        Path(self.source["importZip"]).parent / artifact["fileName"]).read_bytes()
            if path == "/api/csp/offerings/offering":
                return dict(self.offering)
            if path == "/api/csp/package-imports/package":
                return dict(self.package)
            if suffix == "review-state":
                return {"preview": self.preview, "publication": self.publication, "previewIsStale": False}
            if "/impact-options/" in path:
                candidate = next(c for c in self.candidates if c["candidateId"] == suffix)
                return {"change": {"kind": candidate["type"], "recordId": suffix,
                                   "expectedRevision": candidate["revision"], "proposedSnapshotHash": "a" * 64}}
            if "/impact-reviews/" in path:
                return dict(self.impacts[suffix])
            if "/authorization-records/" in path:
                return dict(self.decisions[0])
            if suffix == "overview":
                return {"capabilities": {"published": self.source["expectedCandidates"]["Capability"]}}
        if method == "PATCH":
            if path == "/api/csp/offerings/offering":
                if body["expectedRevision"] != self.offering["revision"]:
                    raise AssertionError("stale offering update")
                self.offering.update({k: v for k, v in body.items() if k != "expectedRevision"})
                self.offering["revision"] += 1
                return dict(self.offering)
            candidate = next(c for c in self.candidates if c["candidateId"] == suffix)
            if body["expectedRevision"] != candidate["revision"]:
                raise AssertionError("stale review")
            candidate.update({k: v for k, v in body.items() if k not in ("expectedRevision", "reviewAction")})
            candidate["revision"] += 1
            candidate["reviewState"] = body["reviewAction"]
            self.package["revision"] += 1
            return dict(candidate)
        if method == "POST":
            if suffix == "offerings":
                self.offering = dict(body, offeringId="offering", revision=1,
                                     currentBoundaryRevisionId=None, currentHostingScopeRevisionId=None)
                return dict(self.offering)
            if suffix == "boundary-revisions":
                boundary = dict(body, boundaryRevisionId="boundary", offeringRevision=2)
                self.boundaries.append(boundary)
                self.offering.update(revision=2, currentBoundaryRevisionId="boundary")
                return dict(boundary)
            if suffix == "hosting-scope-revisions":
                hosting = dict(body, snapshot={"revisionId": "hosting"}, offeringRevision=3)
                self.hosting.append(hosting)
                self.offering.update(revision=3, currentHostingScopeRevisionId="hosting")
                return dict(hosting)
            if suffix == "package-versions":
                self.package = {
                    "name": body["fields"]["name"], "packageId": "package", "revision": 1, "processingState": "ReadyForReview",
                    "lastError": None, "publicationState": "Unpublished", "analysisProgress": {"modelCalls": 0},
                    "coverage": {"total": 7, "processed": 7, "pending": 0, "unsupported": 0,
                                 "unreadable": 0, "failed": 0, "excluded": 0},
                }
                version = {"packageId": "package", "packageVersionId": "version", "boundaryRevisionId": "boundary"}
                self.versions.append(version)
                return {"package": dict(self.package), "packageVersion": dict(version)}
            if suffix == "authorization-records":
                decision = dict(body, recordId="decision", revisionId="decision-revision", revision=1,
                                snapshotHash="b" * 64, metadataReviewState="Unconfirmed")
                self.decisions.append(decision)
                return dict(decision)
            if suffix == "record":
                self.decisions[0].update(metadataReviewState="Recorded", revision=2, currentStanding="CurrentAsRecorded")
                return dict(self.decisions[0])
            if suffix == "findings":
                result = dict(body, findingId="finding", revision=1, workflowState="Open")
                self.findings.append(result)
                return dict(result)
            if suffix == "poam-items":
                result = dict(body, poamId="poam", revision=1, workflowState="Open")
                self.poams.append(result)
                return dict(result)
            if suffix == "evidence":
                file = Path(body["file"])
                content = file.read_bytes()
                result = {"evidenceId": file.name, "fileName": file.name, "sha256": demo.digest(content),
                          "byteLength": len(content), "description": body["fields"]["description"]}
                self.artifacts.append(result)
                self.findings[0]["revision"] += 1
                return result
            if suffix == "impact-previews":
                result = {"reviewId": "impact", "revision": 1, "previewId": "impact-preview",
                          "previewHash": "c" * 64, "blockers": []}
                self.impacts["impact"] = dict(result, disposition="PendingReview", stale=False)
                return result
            if suffix == "review":
                self.impacts["impact"].update(disposition="AcceptForPublication", revision=2)
                return dict(self.impacts["impact"])
            if suffix == "approval-previews":
                self.preview = dict(body, previewId="preview", previewHash="d" * 64,
                                    revision=self.package["revision"], state="Preview", blockers=[])
                return dict(self.preview)
            if suffix == "approve":
                self.preview["state"] = "Approved"
                return dict(self.preview)
            if suffix == "publish":
                records = []
                for c in self.candidates:
                    if c["type"] in ("Component", "Capability"):
                        c["publishedRecordId"] = c["candidateId"] + "-record"
                        c["reviewState"] = "Published"
                        records.append({"type": c["type"], "recordId": c["publishedRecordId"],
                                        "candidateId": c["candidateId"],
                                        "releaseId": c["candidateId"] + "-release" if c["type"] == "Capability" else None})
                self.publication = {"publicationState": "Published", "records": records}
                return self.publication
        raise AssertionError(f"Unexpected contract operation: {method} {path}")


class WorkflowContractTests(unittest.TestCase):
    def setUp(self):
        self.configure(demo.DATASETS[0])

    def configure(self, dataset):
        # Arrange: read only the authorized synthetic fixtures; use in-memory API.
        self.root = demo.REPO / "demos/provider-offerings"
        metadata = json.loads((self.root / "internal-manifest.json").read_text())
        self.dataset = dataset
        source = next(s for s in metadata["sources"] if s["importZip"] == self.dataset + "/review-source.zip")
        source = json.loads(json.dumps(source))
        source["documents"] = {Path(p).name: json.loads((self.root / p).read_text()) for p in source["sourceFiles"]}
        source["documents"]["04-synthetic-reference-record.json"]["authorizationDecisionClaims"][0]["claim"]["authorizationDecision"]["statusAsStated"] = "Approved"
        files = {row["path"]: row for row in metadata["files"]}
        self.manifest = SimpleNamespace(root=self.root, sha="f" * 64, data=metadata,
                                        files=files, sources={self.dataset: source})
        self.manifest.hosting_scopes = lambda dataset: [{
            "cloud": "AzureUSGovernment", "directoryTenantId": "10000000-0000-0000-0000-000000000001",
            "subscriptionId": "10000000-0000-0000-0000-000000000002",
            "resourceId": "/subscriptions/10000000-0000-0000-0000-000000000002/resourceGroups/synthetic"}]
        entry_files = {Path(p).name: files[p] for p in [*source["sourceFiles"], source["importZip"]]}
        self.api = ContractApi(source, entry_files)
        self.state = demo.Journal.memory()
        self.args = SimpleNamespace(azure_offering_id=None, stage="all", wait_seconds=1)
        self.loader = demo.Loader(self.api, self.state, self.manifest, self.args)
        self.multipart = patch.object(demo, "multipart", side_effect=lambda fields, field, file, key:
                                      {"fields": fields, "file": str(file)})
        self.multipart.start()
        self.addCleanup(self.multipart.stop)

    def test_full_pipeline_and_second_run_create_no_duplicates(self):
        # Act
        self.loader.run(self.dataset)
        first_calls = len(self.api.calls)
        self.loader.run(self.dataset)
        # Assert
        self.assertEqual(len(self.api.boundaries), 1)
        self.assertEqual(len(self.api.decisions), 1)
        self.assertEqual(len(self.api.findings), 1)
        self.assertEqual(len(self.api.poams), 1)
        self.assertEqual(len([r for r in self.api.publication["records"] if r["type"] == "Capability"]), 8)
        self.assertFalse(any(call[0] != "GET" for call in self.api.calls[first_calls:]))
        capabilities = [c for c in self.api.candidates if c["type"] == "Capability"]
        self.assertTrue(all(c["controlDuties"] and c["rationale"] for c in capabilities))
        self.assertTrue(all(r["workflowState"] == "Open" for r in self.api.findings + self.api.poams))
        self.assertEqual(len([a for a in self.api.artifacts if "PRIVATE" in a["fileName"]]), 1)
        self.assertTrue(any(a["fileName"].endswith(".csv") for a in self.api.artifacts))
        self.assertTrue(any(a["fileName"].endswith(".docx") for a in self.api.artifacts))
        self.assertFalse(any("1.3" in path or "/systems" in path or "/shares" in path
                             for _, path, _, _ in self.api.calls))

    def test_capability_duties_are_taken_from_exact_source(self):
        # Act
        self.loader.run(self.dataset)
        # Assert
        for c in self.api.candidates:
            if c["type"] == "Capability":
                record = self.loader.candidate_source(self.manifest.sources[self.dataset], c)
                self.assertEqual(c["controlDuties"], {control: "Shared" for control in record["controlIds"]})

    def test_unapproved_source_never_reaches_publication(self):
        # Arrange
        source = self.manifest.sources[self.dataset]
        source["documents"]["04-synthetic-reference-record.json"]["authorizationDecisionClaims"][0]["claim"]["authorizationDecision"]["statusAsStated"] = "Approved for synthetic demonstration only"
        # Act / Assert
        with self.assertRaises(demo.Stop):
            self.loader.run(self.dataset)
        self.assertIsNone(self.api.publication)

    def test_citation_tampering_is_rejected(self):
        # Arrange
        self.api.candidates[0]["citations"][0]["quote"] = '{"name": "invented"}'
        # Act / Assert
        with self.assertRaises(demo.Stop):
            self.loader.run(self.dataset)
        self.assertFalse(any(method == "PATCH" for method, _, _, _ in self.api.calls))

    def test_existing_azure_requires_explicit_id(self):
        # Arrange
        self.api.offering = {"name": self.manifest.sources[self.dataset]["offering"], "offeringId": "unapproved-existing"}
        # Act / Assert
        with self.assertRaises(demo.Stop):
            self.loader.run(self.dataset)
        self.assertFalse(any(method == "POST" for method, _, _, _ in self.api.calls))

    def test_partial_analysis_is_not_marked_reviewed(self):
        # Arrange
        original = self.api.request
        def request(method, path, **kwargs):
            value = original(method, path, **kwargs)
            if path == "/api/csp/package-imports/package":
                value["coverage"]["unsupported"] = 1
            return value
        self.api.request = request
        # Act / Assert
        with self.assertRaises(demo.Stop):
            self.loader.run(self.dataset)
        self.assertFalse(any(method == "PATCH" for method, _, _, _ in self.api.calls))

    def test_m365_has_five_capabilities_and_service_scope(self):
        # Arrange
        self.multipart.stop()
        self.configure(demo.DATASETS[1])
        self.manifest.hosting_scopes = lambda dataset: [{"kind": "Service", "serviceId": "synthetic-service",
                                                        "serviceName": "Synthetic collaboration",
                                                        "environment": "Microsoft365DoD"}]
        # Act
        self.loader.run(self.dataset)
        # Assert
        self.assertEqual(len([r for r in self.api.publication["records"] if r["type"] == "Capability"]), 5)
        self.assertEqual(self.api.hosting[0]["permittedScopes"][0]["kind"], "Service")
        self.assertEqual(self.api.offering["serviceModel"], "SoftwareAsAService")

    def test_source_backed_quote_does_not_authorize_changed_candidate_text(self):
        # Arrange
        self.api.candidates[0]["description"] = "Invented capability prose"
        # Act / Assert
        with self.assertRaises(demo.Stop):
            self.loader.run(self.dataset)
        self.assertFalse(any(method == "PATCH" for method, _, _, _ in self.api.calls))

    def test_explicit_azure_reuse_updates_only_known_offering_without_duplicate(self):
        # Arrange
        self.args.azure_offering_id = "offering"
        self.api.offering = {
            "offeringId": "offering", "name": "Azure IL5", "description": "Old draft",
            "environments": ["AzureCloud"], "revision": 3, "currentBoundaryRevisionId": "old-boundary",
            "currentHostingScopeRevisionId": None, "serviceModel": None, "managementArrangement": None,
            "providerId": "unchanged-provider", "serviceOwner": "Existing owner", "securityContact": "Existing contact",
        }
        # Act
        self.loader.run(self.dataset)
        count = len(self.api.calls)
        self.loader.run(self.dataset)
        # Assert
        self.assertFalse(any(method == "POST" and path == "/api/csp/offerings"
                             for method, path, _, _ in self.api.calls))
        updates = [body for method, path, body, _ in self.api.calls
                   if method == "PATCH" and path == "/api/csp/offerings/offering"]
        self.assertEqual(len(updates), 1)
        self.assertEqual(updates[0]["expectedRevision"], 3)
        self.assertEqual(self.api.offering["environments"], ["AzureUSGovernment"])
        self.assertEqual(self.api.offering["serviceOwner"], "Existing owner")
        self.assertEqual(self.api.offering["securityContact"], "Existing contact")
        self.assertEqual(self.api.offering["providerId"], "unchanged-provider")
        self.assertEqual(self.api.boundaries[0]["predecessorRevisionId"], "old-boundary")
        self.assertFalse(any(method != "GET" for method, _, _, _ in self.api.calls[count:]))
        self.assertFalse(any("/systems" in path or "/organizations" in path or method == "DELETE"
                             for method, path, _, _ in self.api.calls))

    def test_refresh_context_keeps_content_and_uses_single_change_reviews(self):
        # Arrange
        self.loader.run(self.dataset)
        api = RenewalApi(self.api, self.state, self.dataset)
        self.loader.api = api
        self.api.offering["revision"] = 10
        self.api.offering["serviceOwner"] = "SYNTHETIC Northstar Shared Services Demo Team"
        self.api.offering["securityContact"] = "shared-services@example.invalid"
        original = json.loads(json.dumps(api.working))
        # Act
        self.loader.refresh_context(self.dataset)
        count = len(api.calls)
        self.loader.refresh_context(self.dataset)
        # Assert
        self.assertEqual(len(api.releases), 8)
        writes = [(m, p, b) for m, p, b in api.calls if m != "GET"]
        self.assertEqual(sum(m == "PUT" for m, _, _ in writes), 8)
        self.assertTrue(all(m == "PUT" for m, _, _ in writes[:8]))
        impacts = [b for _, p, b in writes if p.endswith("/impact-previews")]
        self.assertEqual(len(impacts), 8)
        self.assertTrue(all(len(b["changes"]) == 1 and b["changes"][0]["kind"] == "Capability" for b in impacts))
        self.assertTrue(all(b["packageVersionIds"] == ["version"] for b in impacts))
        for capability, working in api.working.items():
            for field in ("classification", "serviceCategory", "contributors", "controlDuties", "snapshotHash"):
                self.assertEqual(working[field], original[capability][field])
            self.assertEqual(working["revision"], 2)
        self.assertFalse(any(m != "GET" for m, _, _ in api.calls[count:]))
        self.assertEqual(self.api.offering["securityContact"], "shared-services@example.invalid")

    def test_refresh_context_rejects_changed_duties_before_any_write(self):
        # Arrange
        self.loader.run(self.dataset)
        api = RenewalApi(self.api, self.state, self.dataset)
        self.loader.api = api
        next(iter(api.working.values()))["controlDuties"] = {"AU-2": "Provider"}
        # Act / Assert
        with self.assertRaises(demo.Stop):
            self.loader.refresh_context(self.dataset)
        self.assertFalse(any(method != "GET" for method, _, _ in api.calls))

    def test_refresh_context_recovers_put_response_loss_without_duplicate_draft(self):
        # Arrange
        self.loader.run(self.dataset)
        api = RenewalApi(self.api, self.state, self.dataset)
        self.loader.api = api
        original = api.request
        lost = False
        def request(method, path, **kwargs):
            nonlocal lost
            result = original(method, path, **kwargs)
            if method == "PUT" and not lost:
                lost = True
                raise demo.Stop("PUT response lost after commit")
            return result
        api.request = request
        with self.assertRaises(demo.Stop):
            self.loader.refresh_context(self.dataset)
        # Act
        api.request = original
        self.loader.refresh_context(self.dataset)
        # Assert
        self.assertEqual(sum(m == "PUT" for m, _, _ in api.calls), 8)
        self.assertTrue(all(w["revision"] == 2 for w in api.working.values()))

    def test_refresh_context_recovers_published_response_with_same_body_key(self):
        # Arrange
        self.loader.run(self.dataset)
        api = RenewalApi(self.api, self.state, self.dataset)
        self.loader.api = api
        original = api.request
        lost = False
        def request(method, path, **kwargs):
            nonlocal lost
            result = original(method, path, **kwargs)
            if method == "POST" and path.endswith("/publish") and not lost:
                lost = True
                raise demo.Stop("publication response lost after commit")
            return result
        api.request = request
        with self.assertRaises(demo.Stop):
            self.loader.refresh_context(self.dataset)
        # Act
        api.request = original
        self.loader.refresh_context(self.dataset)
        # Assert
        attempts = [body for method, path, body in api.calls if method == "POST" and path.endswith("/publish")]
        self.assertEqual(attempts[0], attempts[1])
        self.assertEqual(len(api.releases), 8)
        self.assertEqual(sum(method == "PUT" for method, _, _ in api.calls), 8)

    def test_refresh_context_does_not_repeat_uncertain_non_idempotent_preview(self):
        # Arrange
        self.loader.run(self.dataset)
        api = RenewalApi(self.api, self.state, self.dataset)
        self.loader.api = api
        original = api.request
        def request(method, path, **kwargs):
            result = original(method, path, **kwargs)
            if method == "POST" and path.endswith("/publication-previews"):
                raise demo.Stop("preview response lost after commit")
            return result
        api.request = request
        with self.assertRaises(demo.Stop):
            self.loader.refresh_context(self.dataset)
        # Act / Assert
        api.request = original
        with self.assertRaisesRegex(demo.Stop, "Uncertain non-idempotent"):
            self.loader.refresh_context(self.dataset)
        self.assertEqual(sum(method == "POST" and path.endswith("/publication-previews")
                             for method, path, _ in api.calls), 1)
        self.assertEqual(len(api.releases), 0)

    def test_refresh_context_m365_publishes_five_unchanged_releases(self):
        # Arrange
        self.multipart.stop()
        self.configure(demo.DATASETS[1])
        self.manifest.hosting_scopes = lambda dataset: [{"kind": "Service", "serviceId": "synthetic-service",
                                                        "serviceName": "Synthetic collaboration", "environment": "Microsoft365DoD"}]
        self.loader.run(self.dataset)
        api = RenewalApi(self.api, self.state, self.dataset)
        self.loader.api = api
        # Act
        self.loader.refresh_context(self.dataset)
        # Assert
        self.assertEqual(len(api.releases), 5)
        self.assertTrue(all(release["revision"] == 2 for release in api.releases.values()))
        self.assertFalse(any("package-versions" in path or "/hosting-assignments" in path or "1.3" in path
                             for method, path, _ in api.calls if method != "GET"))


class RenewalApi:
    """Current catalog working-revision/publication DTOs with strict impact sets."""
    def __init__(self, baseline, state, dataset):
        self.baseline = baseline
        self.calls = []
        self.working, self.releases, self.previews, self.impacts = {}, {}, {}, {}
        for record in state.data["ids"][dataset + "/publishedRecords"]:
            if record["type"] != "Capability":
                continue
            candidate = next(c for c in baseline.candidates if c["candidateId"] == record["candidateId"])
            self.working[record["recordId"]] = {
                "capabilityId": record["recordId"], "revision": 1, "snapshotHash": "f" * 64,
                "classification": candidate["classification"], "serviceCategory": candidate["serviceCategory"],
                "contributors": [c + "-record" for c in candidate["contributorIds"]],
                "controlDuties": candidate["controlDuties"], "approvedRevision": 1,
                "approvalState": "Approved", "approvedPreviewId": "baseline", "approvedPreviewHash": "baseline",
            }

    def pages(self, path):
        return self.baseline.pages(path)

    def request(self, method, path, body=None, key=None, **kwargs):
        self.calls.append((method, path, body))
        suffix = path.rsplit("/", 1)[1]
        if method == "GET" and path == "/api/csp/offerings/offering":
            return dict(self.baseline.offering)
        if method == "GET" and "/boundary-revisions/" in path:
            return self.baseline.boundaries[0]
        if method == "GET" and "/hosting-scope-revisions/" in path:
            return self.baseline.hosting[0]
        if method == "GET" and "/authorization-records/" in path:
            return dict(self.baseline.decisions[0])
        if "/api/csp/catalog/capabilities/" in path:
            capability_id = path.split("/capabilities/")[1].split("/")[0]
            working = self.working[capability_id]
            if method == "GET" and suffix == "working-revision":
                return dict(working)
            if method == "GET" and suffix == capability_id:
                candidate = next(c for c in self.baseline.candidates if c["publishedRecordId"] == capability_id)
                return {"capability": {"name": candidate["name"], "description": candidate["description"],
                                        "releasedRevision": 2 if capability_id in self.releases else 1}}
            if method == "PUT":
                if body["expectedRevision"] != working["revision"]:
                    raise AssertionError("stale working revision")
                working.update({k: v for k, v in body.items() if k != "expectedRevision"})
                working.update(revision=working["revision"] + 1, approvedRevision=None,
                               approvalState="NotApproved", approvedPreviewId=None, approvedPreviewHash=None)
                return dict(working)
            if method == "POST" and suffix == "publication-previews":
                impact = self.impacts[body["impactReviewIds"][0]]
                assert impact["changeIds"] == [capability_id]
                assert impact["disposition"] == "AcceptForPublication"
                preview = {"previewId": capability_id + "-preview", "capabilityId": capability_id,
                           "revision": working["revision"], "workingSnapshotHash": working["snapshotHash"],
                           "previewHash": "d" * 64, "isStale": False, "expiresAt": "2099-01-01T00:00:00Z",
                           "contributorChanges": [], "dutyChanges": [], "referenceChanges": [],
                           "impactReviewIds": body["impactReviewIds"]}
                self.previews[capability_id] = preview
                return dict(preview)
            if method == "POST" and suffix == "approve":
                assert body["previewId"] == self.previews[capability_id]["previewId"]
                working.update(approvedRevision=working["revision"], approvalState="Approved",
                               approvedPreviewId=body["previewId"], approvedPreviewHash=body["previewHash"])
                return dict(working)
            if method == "POST" and suffix == "publish":
                assert body["approvedRevision"] == working["approvedRevision"] == body["revision"]
                assert body["idempotencyKey"]
                release = {"releaseId": capability_id + "-release-v2", "capabilityId": capability_id,
                           "revision": working["revision"], "snapshotHash": working["snapshotHash"], "existing": False}
                self.releases[capability_id] = release
                return dict(release)
        if method == "POST" and suffix == "impact-previews":
            assert len(body["changes"]) == 1
            capability = body["changes"][0]["recordId"]
            assert body["changes"][0]["proposedSnapshotHash"] == self.working[capability]["snapshotHash"]
            result = {"reviewId": capability + "-impact", "revision": 1, "previewId": capability + "-impact-preview",
                      "previewHash": "c" * 64, "blockers": [], "stale": False, "expiresAt": "2099-01-01T00:00:00Z",
                      "changeIds": [capability], "disposition": "PendingReview"}
            self.impacts[result["reviewId"]] = result
            return dict(result)
        if "/impact-reviews/" in path:
            review_id = path.split("/impact-reviews/")[1].split("/")[0]
            result = self.impacts[review_id]
            if method == "POST":
                result.update(disposition=body["disposition"], revision=2)
            return dict(result)
        return self.baseline.request(method, path, body=body, key=key, **kwargs)


if __name__ == "__main__":
    unittest.main()
