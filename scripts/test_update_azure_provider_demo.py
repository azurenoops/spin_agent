"""Offline contract/safety tests; these tests never contact a running service."""
import copy
import base64
import email
import email.message
import importlib.util
import io
import json
from pathlib import Path
import unittest
import urllib.response
from types import SimpleNamespace
from unittest.mock import Mock, patch


SPEC = importlib.util.spec_from_file_location(
    "azure_provider_demo", Path(__file__).with_name("update-azure-provider-demo.py"))
updater = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(updater)


def mapping():
    components = []
    for key, name in updater.SERVICES.items():
        primary = next((purpose for purpose, values in updater.LINKS.items() if values[0] == key), None)
        components.append({
            "key": key, "existingSourceId": f"AZ-component-{primary}" if primary else None,
            "name": name, "description": updater.NOTICE + ". Newly authored Azure implementation.",
        })
    return {
        "schemaVersion": 1, "demonstrationNotice": updater.NOTICE,
        "offeringName": "Azure IL5 · Shared services", "baselineDataset": updater.DATASET,
        "supplementId": updater.SUPPLEMENT, "baselineManifestSha256": updater.BASELINE_SHA,
        "references": [{"title": "Azure Backup", "url": "https://learn.microsoft.com/azure/backup/"}],
        "components": components,
        "capabilities": [
            {"sourceId": f"AZ-capability-{purpose}", "name": purpose,
             "description": updater.NOTICE + ". Newly authored Azure implementation.",
             "componentKeys": list(keys)}
            for purpose, keys in updater.LINKS.items()
        ],
    }


class MappingTests(unittest.TestCase):
    def test_exact_twelve_components_eight_capabilities(self):
        # Arrange
        data = mapping()
        # Act
        updater.validate_mapping(data)
        # Assert
        self.assertEqual(len(data["components"]), 12)
        self.assertEqual(len(data["capabilities"]), 8)

    def test_backup_reuses_key_vault_and_has_separate_backup_component(self):
        # Arrange
        data = mapping()
        backup = data["capabilities"][-1]
        # Act / Assert
        self.assertEqual(backup["componentKeys"], ["backup", "keyvault"])
        backup["componentKeys"] = ["keyvault"]
        with self.assertRaises(updater.Stop):
            updater.validate_mapping(data)

    def test_rejects_scope_changes_and_duplicate_products(self):
        # Arrange
        cases = []
        for field, value in (("offeringName", "another-offering"),
                             ("baselineDataset", "microsoft-365-collaboration/release-1.0"),
                             ("demonstrationNotice", "Real Azure deployment")):
            item = mapping()
            item[field] = value
            cases.append(item)
        item = mapping()
        item["components"].append(copy.deepcopy(item["components"][0]))
        cases.append(item)
        item = mapping()
        item["components"][1]["name"] = item["components"][0]["name"]
        cases.append(item)
        # Act / Assert
        for item in cases:
            with self.subTest(item=item), self.assertRaises(updater.Stop):
                updater.validate_mapping(item)

    def test_rejects_unreviewed_source_keys_and_other_supplements(self):
        # Arrange
        cases = []
        for value in ("../source.md", "/source.md", "https://example.org/source.md"):
            item = mapping()
            item["supplementId"] = value
            cases.append(item)
        item = mapping()
        item["components"][0]["existingSourceId"] = "M365-component-audit"
        cases.append(item)
        item = mapping()
        item["capabilities"][0]["sourceId"] = "AZ-capability-unknown"
        cases.append(item)
        # Act / Assert
        for item in cases:
            with self.subTest(item=item), self.assertRaises(updater.Stop):
                updater.validate_mapping(item)

    def test_requires_explicit_authored_date_and_synthetic_descriptions(self):
        # Arrange
        data = mapping()
        data["supplementId"] = "azure-implementation-not-a-date"
        # Act / Assert
        with self.assertRaises(updater.Stop):
            updater.validate_mapping(data)
        data = mapping()
        data["components"][0]["description"] = "Azure is operating this service."
        with self.assertRaises(updater.Stop):
            updater.validate_mapping(data)

    def test_actual_supplement_hash_chain_and_new_authorship_are_valid(self):
        # Arrange
        path = updater.loader.REPO / "demos/provider-offerings" / updater.SUPPLEMENT / "implementation-map.json"
        # Act
        data, sha = updater.read_mapping(path)
        # Assert
        self.assertEqual(sha, updater.loader.digest((path.parent / "supplement-manifest.json").read_bytes()))
        self.assertEqual(data["baselineManifestSha256"],
                         updater.loader.digest((path.parent.parent / "internal-manifest.json").read_bytes()))

    def test_manifest_rejects_duplicate_or_traversing_files(self):
        # Arrange
        path = updater.loader.REPO / "demos/provider-offerings" / updater.SUPPLEMENT / "implementation-map.json"
        manifest_path = path.parent / "supplement-manifest.json"
        original_read = Path.read_bytes
        manifest = json.loads(original_read(manifest_path))
        manifest["files"][0]["path"] = "../internal-manifest.json"

        def read(target):
            return json.dumps(manifest).encode() if target == manifest_path else original_read(target)

        # Act / Assert
        with patch.object(Path, "read_bytes", read):
            with self.assertRaisesRegex(updater.Stop, "six expected files"):
                updater.read_mapping(path)

    def test_manifest_rejects_changed_artifact_bytes(self):
        # Arrange
        path = updater.loader.REPO / "demos/provider-offerings" / updater.SUPPLEMENT / "implementation-map.json"
        original_read = Path.read_bytes

        def read(target):
            result = original_read(target)
            return result + b"changed" if target.name == "azure-implementation-guide.pdf" else result

        # Act / Assert
        with patch.object(Path, "read_bytes", read):
            with self.assertRaisesRegex(updater.Stop, "SHA-256 changed"):
                updater.read_mapping(path)


class ContractTests(unittest.TestCase):
    def test_old_patch_plan_cannot_be_reused_by_new_strategy(self):
        # Arrange
        api, state = Mock(), Mock()
        plan = {"manifestSha256": "a" * 64, "snapshotSha256": "b" * 64, "blockers": []}
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "STRATEGY_MISMATCH"):
            updater.apply_update(api, state, plan, "a" * 64, "b" * 64, Path("."))
        self.assertEqual(api.mock_calls, [])
        self.assertEqual(state.mock_calls, [])

    def test_transport_refuses_patch_and_delete_before_any_network(self):
        # Arrange
        api = updater.EnrichmentApi("http://127.0.0.1:3002", None, apply=True)
        # Act / Assert
        with patch.object(api.opener, "open") as opened:
            for method in ("PATCH", "DELETE"):
                with self.assertRaisesRegex(updater.Stop, "forbids PATCH/DELETE"):
                    api.request(method, "/api/csp/inherited-components/source", {})
            opened.assert_not_called()

    def test_exact_manifest_guard_precedes_contract_check(self):
        # Arrange
        api, journal = Mock(), Mock()
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "manifest SHA"):
            updater.apply_update(api, journal, {"manifestSha256": "a" * 64}, "b" * 64)
        self.assertEqual(api.mock_calls, [])
        self.assertEqual(journal.mock_calls, [])

    def test_contract_report_does_not_claim_existing_reference_was_enriched(self):
        # Arrange / Act
        advisories = updater.contract_advisories()
        # Assert
        self.assertEqual({b["code"] for b in advisories},
                         {"SUPPLEMENT_IS_SEPARATE_AUTHORED_EVIDENCE", "SOURCE_PARENT_IS_NOT_A_PRODUCT_CONTRIBUTOR"})
        serialized = json.dumps(advisories)
        self.assertIn("SourceArtifactReference", serialized)
        self.assertIn("ReusePublished", serialized)

    def test_snapshot_hash_detects_material_change_and_is_order_independent(self):
        # Arrange
        before = {"working": {"revision": 2, "contributors": ["one"]}, "boundary": "same"}
        # Act / Assert
        self.assertEqual(updater.snapshot_hash(before),
                         updater.snapshot_hash(dict(reversed(list(before.items())))))
        after = copy.deepcopy(before)
        after["working"]["contributors"] = ["one", "two"]
        self.assertNotEqual(updater.snapshot_hash(before), updater.snapshot_hash(after))

    def test_private_patch_envelope_cannot_be_replayed_as_create(self):
        # Arrange
        api = updater.EnrichmentApi("http://127.0.0.1:3002", None, apply=True)
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "cannot replay"):
            api.request("POST", "/api/csp/inherited-components",
                        {"_ifMatch": "AQIDBA==", "_body": {"name": "Azure Blob Storage"}})

    def test_if_match_cannot_be_attached_to_an_unrelated_route(self):
        # Arrange
        api = updater.EnrichmentApi("http://127.0.0.1:3002", None, apply=True)
        # Act / Assert
        with self.assertRaises(updater.Stop):
            api.request("PATCH", "/api/auth/config", {"_ifMatch": "AQIDBA==", "_body": {}})

    def test_actual_urllib_pipeline_sends_plain_create_without_etag(self):
        # Arrange
        api = updater.EnrichmentApi("http://127.0.0.1:3002", None, apply=True)
        captured = []

        class Capture(urllib.request.BaseHandler):
            handler_order = 100

            def http_open(self, request):
                captured.append({
                    "method": request.get_method(), "token": request.get_header("If-match"),
                    "body": json.loads(request.data),
                })
                response = urllib.response.addinfourl(
                    io.BytesIO(b'{"data":{"id":"component"}}'),
                    email.message.Message(), request.full_url, 200)
                response.msg = "OK"
                return response

        api.opener.add_handler(Capture())
        body = {"name": "Azure Monitor Logs (Log Analytics)", "description": updater.NOTICE,
                "componentType": "Service"}
        # Act
        result = api.request("POST", "/api/csp/inherited-components", body)
        # Assert
        self.assertEqual(result, {"id": "component"})
        self.assertEqual(captured, [{"method": "POST", "token": None, "body": body}])

    def test_apply_requires_snapshot_guard_even_with_supported_component_tokens(self):
        # Arrange
        api, state = Mock(), updater.loader.Journal.memory()
        plan = {"manifestSha256": "a" * 64, "snapshotSha256": "b" * 64, "blockers": []}
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "snapshot SHA"):
            updater.apply_update(api, state, plan, "a" * 64)
        self.assertEqual(api.mock_calls, [])


def planning_fixture():
    prefix = updater.DATASET + "/"
    context = {"expectedOfferingRevision": 4, "boundaryRevisionId": "boundary",
               "hostingScopeRevisionId": "hosting"}
    records, operations, plans, components = [], {}, [], []
    for purpose in updater.LINKS:
        for kind in ("Component", "Capability"):
            record_id = kind.lower() + "-" + purpose
            row = {"type": kind, "recordId": record_id, "candidateId": record_id}
            records.append(row)
            operations[prefix + "review/" + record_id] = {
                "result": {"id": "AZ-" + record_id}}
            if kind == "Component":
                operations[prefix + "review/" + record_id]["result"].update(
                    name="Original " + purpose, description="Original description " + purpose)
                components.append({
                    "id": record_id, "name": "Original " + purpose, "cspProfileId": "provider",
                    "status": "Published", "sourceFormat": "Package",
                    "sourceArtifactReference": "package:original-package/artifact:" + purpose,
                    "componentType": "Service", "description": "Original description " + purpose,
                    "capabilities": [{
                        "id": "capability-" + purpose, "cspInheritedComponentId": record_id,
                        "name": purpose, "description": "Original capability " + purpose,
                        "mappedNistControlIds": ["AC-1"], "rowVersion": "AQIDBA==",
                    }],
                })
            else:
                plans.append({
                    "capabilityId": record_id, "originalWorking": {"revision": 2, "snapshotHash": "original-" + purpose},
                    "expected": {"controlDuties": {"AC-1": "Shared"}, "contributors": ["component-" + purpose],
                                 "classification": "Shared", "serviceCategory": "Test"},
                })
    state = SimpleNamespace(data={
        "binding": {"base": "http://127.0.0.1:3002", "manifest": "baseline-hash",
                    "identity": updater.loader.IDENTITY},
        "ids": {prefix + "offering": updater.OFFERING, prefix + "package": "original-package",
                prefix + "publishedRecords": records, prefix + "finding": "finding"}, "operations": operations,
    })
    manifest = SimpleNamespace(sha="baseline-hash", sources={updater.DATASET: {}})
    baseline = {"plans": plans, "context": context}
    tool = Mock()
    tool.refresh_context_plan.return_value = baseline
    tool.candidate_source.side_effect = lambda source, reviewed: reviewed
    current = {"revision": 4, "currentBoundaryRevisionId": "boundary",
               "currentHostingScopeRevisionId": "hosting"}

    def get(method, path):
        if method != "GET":
            raise AssertionError("Planning attempted a write")
        if path.startswith("/api/csp/inherited-components/"):
            return copy.deepcopy(next(c for c in components if c["id"] == path.rsplit("/", 1)[-1]))
        if path == "/api/csp/offerings/" + updater.OFFERING:
            return current
        raise AssertionError("Unexpected API scope: " + path)

    api = Mock(base="http://127.0.0.1:3002", apply=False)
    api.request.side_effect = get
    api.pages.return_value = components
    return api, state, manifest, tool, baseline, current


class PlanningTests(unittest.TestCase):
    def test_plan_reads_only_and_preserves_baseline_references_and_ids(self):
        # Arrange
        api, state, manifest, tool, baseline, current = planning_fixture()
        before = copy.deepcopy(state.data)
        # Act
        with patch.object(updater.loader, "Loader", return_value=tool):
            result = updater.build_plan(api, state, manifest, mapping(), "a" * 64)
        # Assert
        self.assertEqual(result["status"], "ready")
        self.assertEqual(len(result["capabilities"]), 8)
        self.assertEqual(len(result["components"]), 12)
        self.assertEqual(sum(c["componentId"] is None for c in result["components"]), 12)
        self.assertEqual(len(result["sourceComponents"]), 8)
        self.assertEqual(state.data, before)
        self.assertTrue(all(call.args[0] == "GET" for call in api.request.call_args_list))
        tool.refresh_context_plan.assert_called_once_with(updater.DATASET)
        backup = next(c for c in result["capabilities"] if c["capabilityId"] == "capability-backup")
        self.assertEqual(backup["componentKeys"], ["backup", "keyvault"])
        self.assertEqual(backup["retainedWorking"]["controlDuties"], {"AC-1": "Shared"})

    def test_plan_rejects_material_revision_change(self):
        # Arrange
        api, state, manifest, tool, baseline, current = planning_fixture()
        baseline["plans"][0]["originalWorking"]["revision"] = 3
        # Act / Assert
        with patch.object(updater.loader, "Loader", return_value=tool):
            with self.assertRaisesRegex(updater.Stop, "revision-2"):
                updater.build_plan(api, state, manifest, mapping(), "a" * 64)
        api.request.assert_not_called()

    def test_plan_rejects_offering_context_race(self):
        # Arrange
        api, state, manifest, tool, baseline, current = planning_fixture()
        current["currentBoundaryRevisionId"] = "another-boundary"
        # Act / Assert
        with patch.object(updater.loader, "Loader", return_value=tool):
            with self.assertRaisesRegex(updater.Stop, "context changed"):
                updater.build_plan(api, state, manifest, mapping(), "a" * 64)

    def test_plan_refuses_to_adopt_an_existing_named_component(self):
        # Arrange
        api, state, manifest, tool, baseline, current = planning_fixture()
        api.pages.return_value = [{"id": "unrelated", "name": "Azure Blob Storage"}]
        # Act / Assert
        with patch.object(updater.loader, "Loader", return_value=tool):
            with self.assertRaisesRegex(updater.Stop, "duplicate/adoption"):
                updater.build_plan(api, state, manifest, mapping(), "a" * 64)

    def test_plan_rejects_other_source_manifest_and_mutating_api(self):
        # Arrange
        api, state, manifest, tool, baseline, current = planning_fixture()
        state.data["binding"]["manifest"] = "different"
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "binding"):
            updater.build_plan(api, state, manifest, mapping(), "a" * 64)
        state.data["binding"]["manifest"] = manifest.sha
        api.apply = True
        with self.assertRaisesRegex(updater.Stop, "Planning API"):
            updater.build_plan(api, state, manifest, mapping(), "a" * 64)
        api.request.assert_not_called()


def execution_plan():
    api, state, manifest, tool, baseline, current = planning_fixture()
    for row in api.pages.return_value:
        for capability in row["capabilities"]:
            capability.pop("rowVersion")
    data, sha = updater.read_mapping(
        updater.loader.REPO / "demos/provider-offerings" / updater.SUPPLEMENT / "implementation-map.json")
    with patch.object(updater.loader, "Loader", return_value=tool):
        plan = updater.build_plan(api, state, manifest, data, sha)
    return plan


class FakeEnrichmentApi:
    """In-memory canonical HTTP fixture, including concurrency and lost responses."""

    base = "http://127.0.0.1:3002"
    apply = True

    def __init__(self, plan):
        self.plan = plan
        self.components = {c["id"]: copy.deepcopy(c) for c in plan["sourceComponents"]}
        self.components["m365-untouched"] = {"id": "m365-untouched", "name": "M365 unchanged"}
        self.working = {
            c["capabilityId"]: dict(copy.deepcopy(c["retainedWorking"]),
                                   **copy.deepcopy(c["originalWorking"]))
            for c in plan["capabilities"]}
        self.released = {c["capabilityId"]: 2 for c in plan["capabilities"]}
        self.evidence_rows, self.contents, self.impacts, self.previews, self.idempotency = [], {}, {}, {}, {}
        self.requests = []
        self.writes = 0
        self.fail_after = None
        self.finding_revision = 1

    def pages(self, path):
        if path == "/api/csp/inherited-components":
            return [copy.deepcopy(c) for c in self.components.values()]
        if path.endswith("/findings"):
            return [{"findingId": "finding", "revision": self.finding_revision, "workflowState": "Open"}]
        if path.endswith("/evidence"):
            return copy.deepcopy(self.evidence_rows)
        raise AssertionError("Unexpected paged GET " + path)

    def target(self, cid):
        return next(c for c in self.plan["capabilities"] if c["capabilityId"] == cid)

    def capability(self, cid):
        target = self.target(cid)
        return next(c for c in self.components[target["componentId"]]["capabilities"] if c["id"] == cid)

    def detail(self, cid):
        current = copy.deepcopy(self.capability(cid))
        current["releasedRevision"] = self.released[cid]
        supporting = [dict(self.components[c]) for c in self.working[cid]["contributors"]]
        parent_id = self.target(cid)["componentId"]
        if parent_id not in self.working[cid]["contributors"]:
            supporting.append(dict(self.components[parent_id]))
        return {"capability": current, "supportingComponents": supporting, "unresolvedContributorIds": []}

    def request(self, method, path, body=None, key=None, binary=False):
        self.requests.append((method, path, copy.deepcopy(body)))
        replay_key = (method, path, key) if key else None
        if replay_key and replay_key in self.idempotency:
            return copy.deepcopy(self.idempotency[replay_key])
        result = self.dispatch(method, path, body)
        if method != "GET":
            self.writes += 1
            if replay_key:
                self.idempotency[replay_key] = copy.deepcopy(result)
            if self.writes == self.fail_after:
                self.fail_after = None
                raise updater.Stop("Simulated lost successful HTTP response")
        return copy.deepcopy(result)

    def dispatch(self, method, path, body):
        root = "/api/csp/offerings/" + updater.OFFERING
        if method == "GET" and path == root:
            context = self.plan["context"]
            return {"revision": context["expectedOfferingRevision"],
                    "currentBoundaryRevisionId": context["boundaryRevisionId"],
                    "currentHostingScopeRevisionId": context["hostingScopeRevisionId"]}
        if method == "GET" and path.endswith("/review-state"):
            return {"publication": {"records": self.plan["sourcePublication"]}}
        if method == "GET" and "/evidence/" in path and path.endswith("/content"):
            return self.contents[path.split("/")[-2]]
        if method == "POST" and path.endswith("/evidence"):
            raw = base64.b64decode(body["_multipart"])
            message = email.message_from_bytes(
                b"MIME-Version: 1.0\r\nContent-Type: " + body["contentType"].encode() + b"\r\n\r\n" + raw)
            fields = {p.get_param("name", header="content-disposition"): p for p in message.get_payload()}
            assert int(fields["expectedFindingRevision"].get_payload(decode=True)) == self.finding_revision
            self.finding_revision += 1
            content = fields["file"].get_payload(decode=True)
            eid = "evidence-" + str(self.finding_revision)
            result = {"evidenceId": eid, "fileName": fields["file"].get_filename(),
                      "description": fields["description"].get_payload(decode=True).decode(),
                      "sha256": updater.loader.digest(content), "byteLength": len(content)}
            self.evidence_rows.append(result)
            self.contents[eid] = content
            return result
        if path.startswith("/api/csp/inherited-components"):
            if method == "POST":
                assert path == "/api/csp/inherited-components", "No separate component publication is needed"
                cid = "created-" + str(len(self.components))
                result = dict(body, id=cid, cspProfileId=self.plan["providerId"], sourceFormat="Manual",
                              status="Published", capabilities=[])
                self.components[cid] = result
                return result
            parts = path.split("/")
            row = self.components[parts[4]]
            if method == "GET":
                return row
            raise updater.Stop("AUTHORIZATION_WORKFLOW_REQUIRED: linked legacy mutation is forbidden")
        if path == root + "/impact-previews":
            assert method == "POST"
            change = body["changes"][0]
            cid = change["recordId"]
            assert self.working[cid]["snapshotHash"] == change["proposedSnapshotHash"]
            assert all(w["revision"] == 3 for w in self.working.values()), "Impact before all drafts"
            rid = "impact-" + cid
            result = {"reviewId": rid, "previewId": rid + "-preview", "previewHash": rid + "-hash",
                      "revision": 1, "blockers": [], "disposition": "Pending",
                      "expiresAt": "2099-01-01T00:00:00Z"}
            self.impacts[rid] = result
            return result
        if "/impact-reviews/" in path:
            rid = path.split("/impact-reviews/")[1].split("/")[0]
            if method == "POST":
                self.impacts[rid]["disposition"] = body["disposition"]
            return self.impacts[rid]
        if path.startswith("/api/csp/catalog/capabilities/"):
            cid = path.split("/")[5]
            suffix = "/".join(path.split("/")[6:])
            working = self.working[cid]
            if method == "GET":
                return working if suffix == "working-revision" else self.detail(cid)
            if method == "PUT":
                assert working["revision"] == body["expectedRevision"] == 2
                working.update({k: v for k, v in body.items() if k != "expectedRevision"})
                working.update(revision=3, snapshotHash=updater.snapshot_hash(body))
                return working
            if suffix == "publication-previews":
                rid = body["impactReviewIds"][0]
                assert self.impacts[rid]["disposition"] == "AcceptForPublication"
                before = set(self.target(cid)["retainedWorking"]["contributors"])
                after = set(working["contributors"])
                result = {
                    "capabilityId": cid, "revision": 3, "workingSnapshotHash": working["snapshotHash"],
                    "previewId": "preview-" + cid, "previewHash": "preview-hash-" + cid,
                    "impactReviewIds": [rid], "expiresAt": "2099-01-01T00:00:00Z",
                    "dutyChanges": [], "referenceChanges": [],
                    "contributorChanges": ([{"value": c, "changeKind": "Added"} for c in sorted(after - before)]
                                           + [{"value": c, "changeKind": "Removed"} for c in sorted(before - after)]),
                }
                self.previews[cid] = result
                return result
            if suffix == "working-revision/approve":
                assert body["previewHash"] == self.previews[cid]["previewHash"]
                working.update(approvedRevision=3, approvedPreviewId=body["previewId"],
                               approvedPreviewHash=body["previewHash"])
                return working
            if suffix == "publish":
                assert body["previewId"] == working["approvedPreviewId"]
                self.released[cid] = 3
                return {"capabilityId": cid, "revision": 3, "releaseId": "release-" + cid,
                        "snapshotHash": working["snapshotHash"]}
        raise AssertionError("Unexpected HTTP request: " + method + " " + path)


class ExecutionTests(unittest.TestCase):
    def test_new_journal_reuses_retained_evidence_without_touching_old_journal(self):
        # Arrange
        plan = execution_plan()
        old_state = updater.loader.Journal.memory()
        new_state = updater.loader.Journal.memory()
        api = FakeEnrichmentApi(plan)
        enricher = updater.Enricher(
            api, old_state, plan, updater.loader.REPO / "demos/provider-offerings" / updater.SUPPLEMENT)
        enricher.evidence()
        old_snapshot = copy.deepcopy(old_state.data)
        self.assertEqual(api.writes, 7)
        # Act
        result = self.execute(api, new_state, plan)
        # Assert
        self.assertEqual(result["status"], "verified")
        self.assertEqual(api.writes, 67)
        self.assertEqual(len(api.evidence_rows), 7)
        self.assertEqual(old_state.data, old_snapshot)
        self.assertEqual(len([key for key in new_state.data["ids"] if key.startswith("evidence/")]), 7)

    def test_contributor_only_strategy_never_patches_source_records(self):
        # Arrange
        plan = execution_plan()
        state = updater.loader.Journal.memory()
        api = FakeEnrichmentApi(plan)
        originals = copy.deepcopy(api.components)
        # Act
        self.execute(api, state, plan)
        # Assert
        self.assertFalse(any(method in ("PATCH", "DELETE") for method, _, _ in api.requests))
        self.assertEqual(sum(method == "POST" and path == "/api/csp/inherited-components"
                             for method, path, _ in api.requests), 12)
        for cid, original in originals.items():
            self.assertEqual(api.components[cid], original)
        source_ids = {c["componentId"] for c in plan["capabilities"]}
        self.assertTrue(all(not source_ids.intersection(w["contributors"]) for w in api.working.values()))

    def execute(self, api, state, plan):
        return updater.apply_update(
            api, state, plan, plan["manifestSha256"], plan["snapshotSha256"],
            updater.loader.REPO / "demos/provider-offerings" / updater.SUPPLEMENT)

    def test_full_apply_preserves_ids_duties_m365_and_actual_links(self):
        # Arrange
        plan = execution_plan()
        state = updater.loader.Journal.memory()
        api = FakeEnrichmentApi(plan)
        # Act
        result = self.execute(api, state, plan)
        # Assert
        self.assertEqual(result["status"], "verified")
        self.assertEqual(result["contributorLinks"], 14)
        self.assertEqual(len(api.components), 21)  # 8 source parents + 12 Azure products + untouched M365.
        self.assertEqual(api.components["m365-untouched"], {"id": "m365-untouched", "name": "M365 unchanged"})
        self.assertEqual(len(api.evidence_rows), 7)
        for capability in plan["capabilities"]:
            self.assertEqual(api.working[capability["capabilityId"]]["controlDuties"],
                             capability["retainedWorking"]["controlDuties"])
        self.assertEqual(api.working["capability-backup"]["contributors"],
                         sorted([state.data["ids"]["component/backup"], state.data["ids"]["component/keyvault"]]))
        first_impact = next(i for i, r in enumerate(api.requests) if r[1].endswith("/impact-previews"))
        self.assertEqual(sum(r[0] == "PUT" for r in api.requests[:first_impact]), 8)
        writes = api.writes
        self.execute(api, state, plan)
        self.assertEqual(api.writes, writes, "Completed resume must perform no new domain writes")

    def test_lost_create_response_is_recovered_without_duplicate(self):
        # Arrange
        plan = execution_plan()
        state = updater.loader.Journal.memory()
        api = FakeEnrichmentApi(plan)
        api.fail_after = 8  # Seven evidence uploads, then the first authored product.
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "lost successful"):
            self.execute(api, state, plan)
        self.assertEqual(len(api.components), 10)
        self.execute(api, state, plan)
        self.assertEqual(len(api.components), 21)

    def test_lost_publication_response_replays_exact_idempotent_request(self):
        # Arrange
        plan = execution_plan()
        state = updater.loader.Journal.memory()
        api = FakeEnrichmentApi(plan)
        api.fail_after = 32  # 27 create/working/evidence writes then first full publication workflow.
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "lost successful"):
            self.execute(api, state, plan)
        self.execute(api, state, plan)
        self.assertTrue(all(revision == 3 for revision in api.released.values()))
        self.assertEqual(api.writes, 67)

    def test_uncertain_unkeyed_preview_stops_without_duplicate_preview(self):
        # Arrange
        plan = execution_plan()
        state = updater.loader.Journal.memory()
        api = FakeEnrichmentApi(plan)
        api.fail_after = 30  # First canonical publication preview.
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "lost successful"):
            self.execute(api, state, plan)
        before = api.writes
        with self.assertRaisesRegex(updater.Stop, "Uncertain non-idempotent"):
            self.execute(api, state, plan)
        self.assertEqual(api.writes, before)

    def test_component_edit_race_stops_before_overwrite(self):
        # Arrange
        plan = execution_plan()
        state = updater.loader.Journal.memory()
        api = FakeEnrichmentApi(plan)
        api.components["component-audit"]["rowVersion"] = "CQgHBg=="
        api.components["component-audit"]["description"] = "Another operator's edit"
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "Original source component"):
            self.execute(api, state, plan)
        self.assertEqual(api.components["component-audit"]["description"], "Another operator's edit")
        self.assertEqual(len(api.components), 9)

    def test_every_lost_write_response_recovers_or_stops_without_duplicates(self):
        # Arrange
        plan = execution_plan()
        uncertain_preview_writes = {30 + 5 * i for i in range(8)}
        for write_number in range(1, 68):
            with self.subTest(write_number=write_number):
                state = updater.loader.Journal.memory()
                api = FakeEnrichmentApi(plan)
                api.fail_after = write_number
                # Act / Assert
                with self.assertRaisesRegex(updater.Stop, "lost successful"):
                    self.execute(api, state, plan)
                if write_number in uncertain_preview_writes:
                    before = api.writes
                    with self.assertRaisesRegex(updater.Stop, "Uncertain non-idempotent"):
                        self.execute(api, state, plan)
                    self.assertEqual(api.writes, before)
                else:
                    result = self.execute(api, state, plan)
                    self.assertEqual(result["status"], "verified")
                    self.assertEqual(api.writes, 67)
                    self.assertEqual(len(api.components), 21)
                    self.assertEqual(len(api.evidence_rows), 7)

    def test_changed_duty_preview_cannot_be_approved_or_published(self):
        # Arrange
        plan = execution_plan()
        state = updater.loader.Journal.memory()
        api = FakeEnrichmentApi(plan)
        dispatch = api.dispatch

        def changed(method, path, body):
            result = dispatch(method, path, body)
            if path.endswith("/publication-previews"):
                result["dutyChanges"] = [{"key": "AC-1", "changeKind": "Changed", "after": "Customer"}]
            return result

        api.dispatch = changed
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "original duties/source"):
            self.execute(api, state, plan)
        self.assertTrue(all(revision == 2 for revision in api.released.values()))
        self.assertFalse(any(r[1].endswith("/working-revision/approve") for r in api.requests))

    def test_saved_plan_tampering_is_rejected_before_any_http(self):
        # Arrange
        plan = execution_plan()
        plan["capabilities"][0]["description"] = "Unreviewed content"
        state = updater.loader.Journal.memory()
        api = FakeEnrichmentApi(plan)
        # Act / Assert
        with self.assertRaisesRegex(updater.Stop, "baseline capability text"):
            self.execute(api, state, plan)
        self.assertEqual(api.requests, [])


if __name__ == "__main__":
    unittest.main()
