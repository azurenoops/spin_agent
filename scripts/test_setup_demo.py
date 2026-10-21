import importlib.util
import io
import json
from pathlib import Path
import runpy
import unittest
from unittest.mock import Mock, patch
from urllib.error import HTTPError, URLError


SPEC = importlib.util.spec_from_file_location(
    "setup_demo", Path(__file__).with_name("setup-demo.py")
)
demo = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(demo)


class DemoSetupTests(unittest.TestCase):
    def setUp(self):
        output = patch.object(demo, "print", create=True)
        output.start()
        self.addCleanup(output.stop)

    def test_only_loopback_origins_are_allowed(self):
        # Arrange
        valid = ["http://localhost:5173", "http://127.0.0.1:3002", "http://[::1]:5173/"]
        invalid = [
            "https://example.com", "http://localhost.example.com", "file:///tmp/demo",
            "http://user:password@localhost", "http://localhost/api",
            "http://localhost?target=remote", "http://localhost#fragment",
        ]
        # Act / Assert
        for url in valid:
            self.assertEqual(demo.local_origin(url), url.rstrip("/"))
        for url in invalid:
            with self.subTest(url=url), self.assertRaises(demo.DemoError):
                demo.local_origin(url)

    def test_demo_identities_match_checked_in_configuration(self):
        # Arrange
        path = Path(__file__).resolve().parents[1] / "src/Ato.Copilot.Mcp/appsettings.Development.json"
        config = json.loads("\n".join(
            line for line in path.read_text().splitlines()
            if not line.lstrip().startswith("//")
        ))
        # Act
        identities = config["CacAuth"]["SimulatedIdentities"]
        # Assert
        self.assertEqual({i["IdentityId"] for i in identities}, set(demo.IDENTITIES))
        for identity in identities:
            expected = demo.IDENTITIES[identity["IdentityId"]]
            self.assertEqual(identity["Oid"], expected["oid"])
            self.assertEqual(identity["Tid"], demo.DIRECTORY_ID)
            self.assertEqual(identity["Persona"], expected["persona"])
            self.assertEqual(identity["Roles"], expected["claims"])

    def test_disabled_simulation_fails_before_any_write(self):
        # Arrange
        api = FakeApi()
        api.simulation = None
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "simulation"):
            demo.setup(api)
        self.assertEqual(api.writes, [])

    def test_incomplete_identity_roster_fails_before_any_write(self):
        # Arrange
        api = FakeApi()
        api.simulation["identities"].pop()
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "identit"):
            demo.setup(api)
        self.assertEqual(api.writes, [])

    def test_provisions_and_verifies_all_nine_without_admin_escalation(self):
        # Arrange
        api = FakeApi()
        # Act
        result = demo.setup(api)
        # Assert
        self.assertEqual(result, ("demo-tenant", "demo-system"))
        self.assertEqual(len(api.memberships), 8)
        self.assertEqual(len(api.roles), 7)
        self.assertEqual(api.roles["dev-orgadmin"], "Administrator")
        self.assertEqual(api.roles["dev-sca"], "Assessor")
        self.assertNotIn("dev-soc-analyst", api.roles)
        self.assertNotIn("dev-cspadmin", api.roles)
        self.assertEqual(set(api.verified), set(demo.IDENTITIES))
        self.assertEqual(set(api.system_checked), {
            key for key, value in demo.IDENTITIES.items()
            if value["role"] not in (None, "Administrator")
        })
        self.assertEqual(api.system_creator, "dev-issm")

    def test_rerun_reuses_dataset_without_duplicate_or_replacement_writes(self):
        # Arrange
        api = FakeApi()
        demo.setup(api)
        api.writes.clear()
        # Act
        demo.setup(api)
        # Assert
        self.assertEqual(api.writes, [])

    def test_collision_with_existing_tenant_fails_before_data_changes(self):
        # Arrange
        api = FakeApi()
        api.tenant = {"id": "existing", "entraTenantId": demo.DEMO_DIRECTORY_ID,
                      "displayName": "Not the demo", "status": "Active"}
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "demo"):
            demo.setup(api)
        self.assertEqual(api.writes, [])

    def test_revoked_membership_is_not_silently_restored(self):
        # Arrange
        api = FakeApi()
        demo.setup(api)
        api.memberships["dev-isso"]["revokedAt"] = "2026-09-25"
        api.writes.clear()
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "revoked"):
            demo.setup(api)
        self.assertEqual(api.writes, [])

    def test_wrong_identity_fails_verification(self):
        # Arrange
        api = FakeApi()
        api.wrong_identity = True
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "identity"):
            demo.setup(api)
        self.assertEqual(api.writes, [])

    def test_api_failure_stops_setup(self):
        # Arrange
        api = FakeApi()
        original = api.request

        def fail_person_creation(method, path, **kwargs):
            if path.endswith("/membership-persons") and method == "POST":
                raise demo.DemoError("HTTP 500")
            return original(method, path, **kwargs)

        api.request = fail_person_creation
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "HTTP 500"):
            demo.setup(api)
        self.assertEqual(api.roles, {})
        self.assertIsNone(api.system)

    def test_client_rejects_success_status_with_failure_envelope(self):
        # Arrange
        response = Mock()
        response.status = 200
        response.read.return_value = b'{"ok":false,"message":"Role denied"}'
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=False)
        api = demo.Api("http://localhost:5173")
        # Act / Assert
        with patch.object(api.opener, "open", return_value=response):
            with self.assertRaisesRegex(demo.DemoError, "Role denied"):
                api.request("POST", "/api/roles/organization", body={})

    def test_client_rejects_html_instead_of_json(self):
        # Arrange
        response = Mock()
        response.status = 200
        response.read.return_value = b"<html>SPA fallback</html>"
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=False)
        api = demo.Api("http://localhost:5173")
        # Act / Assert
        with patch.object(api.opener, "open", return_value=response):
            with self.assertRaisesRegex(demo.DemoError, "JSON"):
                api.request("GET", "/api/auth/me")

    def test_client_scopes_identity_and_decodes_success_envelopes(self):
        # Arrange
        response = Mock(status=200)
        response.read.return_value = b'{"status":"success","data":{"id":"sample"}}'
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=False)
        api = demo.Api("http://localhost:5173")
        # Act
        with patch.object(api.opener, "open", return_value=response) as send:
            result = api.request("POST", "/api/roles/organization", identity="dev-orgadmin",
                                 tenant="demo-tenant", body={"role": "Isso"})
            request = send.call_args.args[0]
            api.request("GET", "/api/tenants", identity="dev-cspadmin")
            csp_request = send.call_args.args[0]
            response.status = 204
            empty = api.request("POST", "/api/auth/simulate?identityId=dev-isso")
        # Assert
        self.assertEqual(result, {"id": "sample"})
        self.assertEqual(request.get_header("Cookie"), "ato-simulation=dev-orgadmin")
        self.assertEqual(request.get_header("X-workspace-tenant-id"), "demo-tenant")
        self.assertEqual(request.get_header("X-workspace-mode"), "ordinary")
        self.assertEqual(csp_request.get_header("X-workspace-kind"), "csp")
        self.assertIsNone(empty)

    def test_client_surfaces_transport_and_json_shape_errors(self):
        # Arrange
        api = demo.Api("http://localhost:5173")
        errors = [
            HTTPError(api.base_url, 403, "Forbidden", {}, io.BytesIO(b"Denied")),
            URLError("Connection refused"),
            TimeoutError("Timed out"),
        ]
        # Act / Assert
        for error in errors:
            with patch.object(api.opener, "open", side_effect=error):
                with self.assertRaises(demo.DemoError):
                    api.request("GET", "/api/auth/me")
        response = Mock(status=200)
        response.read.return_value = b'[]'
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=False)
        with patch.object(api.opener, "open", return_value=response):
            with self.assertRaisesRegex(demo.DemoError, "JSON object"):
                api.request("GET", "/api/auth/me")

    def test_client_refuses_redirects(self):
        # Arrange
        handler = demo.NoRedirects()
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "redirect"):
            handler.redirect_request(None, None, 302, "", {}, "https://example.com")

    def test_changed_roster_claims_are_rejected_before_writes(self):
        # Arrange
        api = FakeApi()
        api.simulation["identities"][1]["roles"] = ["CSP.Admin"]
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "differs"):
            demo.setup(api)
        self.assertEqual(api.writes, [])

    def test_matching_name_with_different_directory_is_not_adopted(self):
        # Arrange
        api = FakeApi()
        api.tenant = {"id": "existing", "entraTenantId": "other",
                      "displayName": demo.TENANT_NAME, "status": "Active"}
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "different tenant"):
            demo.setup(api)
        self.assertEqual(api.writes, [])

    def test_membership_person_conflicts_stop_setup(self):
        # Arrange
        for conflict in ("duplicate", "missing", "different"):
            api = FakeApi()
            demo.setup(api)
            original = api.request

            def conflicting_response(method, path, **kwargs):
                result = original(method, path, **kwargs)
                if method == "GET" and path.endswith("/membership-persons"):
                    if conflict == "duplicate":
                        result.append(dict(result[0]))
                    elif conflict == "missing":
                        result.pop(0)
                    else:
                        result[0]["id"] = "another-person"
                return result

            api.request = conflicting_response
            api.writes.clear()
            # Act / Assert
            with self.subTest(conflict=conflict), self.assertRaises(demo.DemoError):
                demo.setup(api)
            self.assertEqual(api.writes, [])

    def test_existing_other_role_holder_is_not_replaced(self):
        # Arrange
        api = FakeApi()
        demo.setup(api)
        api.roles["another-person"] = api.roles.pop("dev-isso")
        api.writes.clear()
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "another Person"):
            demo.setup(api)
        self.assertEqual(api.writes, [])

    def test_existing_other_system_is_not_adopted(self):
        # Arrange
        api = FakeApi()
        demo.setup(api)
        api.system["description"] = "User-managed data"
        api.writes.clear()
        # Act / Assert
        with self.assertRaisesRegex(demo.DemoError, "existing system"):
            demo.setup(api)
        self.assertEqual(api.writes, [])

    def test_verification_fails_for_wrong_workspace_role_or_system_access(self):
        # Arrange
        for failure in ("workspace", "role", "system", "soc"):
            api = FakeApi()
            demo.setup(api)
            original = api.request

            def wrong_access(method, path, **kwargs):
                result = original(method, path, **kwargs)
                if path == "/api/auth/me" and kwargs["identity"] == "dev-soc-analyst" and failure == "soc":
                    result["isSocAnalyst"] = False
                if kwargs.get("identity") == "dev-isso":
                    if path == "/api/auth/me" and failure == "workspace":
                        result["workspace"]["tenantId"] = "other"
                    elif path == "/api/auth/me" and failure == "role":
                        result["workspace"]["roles"] = []
                    elif path.endswith("/workspace-access") and failure == "system":
                        result["permissions"]["canRead"] = False
                return result

            api.request = wrong_access
            # Act / Assert
            with self.subTest(failure=failure), self.assertRaises(demo.DemoError):
                demo.verify(api, "demo-tenant", "demo-system")

    def test_command_reports_success_or_failure_with_exit_code(self):
        # Arrange
        for error in (None, demo.DemoError("HTTP 500")):
            # Act
            with patch("sys.argv", ["setup-demo.py"]), patch.object(demo, "Api"), \
                    patch.object(demo, "setup", return_value=("tenant", "system"), side_effect=error):
                result = demo.main()
            # Assert
            self.assertEqual(result, 1 if error else 0)

    def test_entrypoint_refuses_remote_origin_without_network_access(self):
        # Arrange
        path = Path(__file__).with_name("setup-demo.py")
        # Act / Assert
        with patch("sys.argv", [str(path), "--base-url", "http://example.com"]), \
                patch("sys.stderr", io.StringIO()), self.assertRaises(SystemExit) as exit:
            runpy.run_path(str(path), run_name="__main__")
        self.assertEqual(exit.exception.code, 1)


class FakeApi:
    """Stateful supported-API fixture; unexpected requests fail the test."""

    def __init__(self):
        self.simulation = {"identities": [
            {"id": key, "persona": value["persona"], "roles": value["claims"],
             "tenantId": demo.DIRECTORY_ID}
            for key, value in demo.IDENTITIES.items()
        ]}
        self.tenant = None
        self.persons = {}
        self.memberships = {}
        self.roles = {}
        self.system = None
        self.active = False
        self.writes = []
        self.verified = []
        self.system_checked = []
        self.system_creator = None
        self.wrong_identity = False

    def request(self, method, path, *, identity=None, tenant=None, body=None):
        if path == "/api/auth/login-config":
            return {"simulation": self.simulation}
        if path.startswith("/api/auth/simulate?"):
            return None
        if path == "/api/auth/me":
            if identity != "dev-cspadmin" and identity not in self.memberships:
                raise demo.DemoError("HTTP 403 NO_TENANT_ASSIGNMENT")
            self.verified.append(identity)
            return {
                "oid": "wrong" if self.wrong_identity else demo.IDENTITIES[identity]["oid"],
                "directoryTenantId": demo.DIRECTORY_ID,
                "isSocAnalyst": identity == "dev-soc-analyst",
                "workspace": {"kind": "csp" if identity == "dev-cspadmin" else "organization",
                              "tenantId": tenant, "mode": "ordinary",
                              "roles": [self.roles[identity]] if identity in self.roles else []},
            }
        if method == "GET" and path.startswith("/api/tenants?"):
            # Deliberately force pagination before finding a pre-existing demo.
            items = [self.tenant] if "page=2&" in path and self.tenant else []
            return {"items": items, "total": 201 if self.tenant else 0}
        if method == "POST" and path == "/api/tenants":
            self.tenant = {"id": "demo-tenant", "status": "Active", **body}
            self.writes.append(path)
            return self.tenant
        if "/membership-persons" in path:
            if method == "GET":
                return [{"id": key, "email": f"{key}@demo.invalid"} for key in self.persons]
            key = body["email"].split("@")[0]
            self.persons[key] = body
            self.writes.append(path)
            return {"id": key, **body}
        if "/memberships" in path:
            if method == "GET":
                return {"items": list(self.memberships.values()), "total": len(self.memberships)}
            key = body["personId"]
            self.memberships[key] = {"id": key, "revokedAt": None, **body}
            self.writes.append(path)
            return self.memberships[key]
        if path.endswith("/administrator-assignments"):
            self.roles[body["personId"]] = "Administrator"
            self.writes.append(path)
            return {}
        if path == "/api/onboarding/role-assignments/":
            if not self.active:
                raise demo.DemoError("HTTP 403 TENANT_ONBOARDING_INCOMPLETE")
            return [{"role": role, "personId": person} for person, role in self.roles.items()]
        if path == "/api/roles/organization":
            assert self.roles[identity] == "Administrator"
            self.roles[body["personId"]] = "Assessor" if body["role"] == "Sca" else body["role"]
            self.writes.append(path)
            return {}
        if path.startswith("/api/onboarding/tenant/"):
            assert self.roles.get(identity) == "Administrator"
            if path.endswith("/state"):
                return {"onboardingState": "Active" if self.active else "Pending"}
            if path.endswith("/submit"):
                self.active = True
            self.writes.append(path)
            return {}
        if path.startswith("/api/dashboard/portfolio?"):
            items = [{**self.system, "systemId": self.system["id"]}] if self.system else []
            return {"items": items, "totalCount": len(items)}
        if path == "/api/dashboard/systems" and method == "POST":
            assert identity == "dev-issm"
            self.system_creator = identity
            self.system = {"id": "demo-system", **body}
            self.writes.append(path)
            return self.system
        if path.endswith("/workspace-access"):
            self.system_checked.append(identity)
            role = "Sca" if self.roles[identity] == "Assessor" else self.roles[identity]
            return {"systemId": "demo-system", "roles": [role],
                    "permissions": {"canRead": True}}
        if path == "/api/auth/events?page=1&pageSize=1":
            assert identity == "dev-soc-analyst"
            return {"items": [], "total": 0}
        raise AssertionError(f"Unexpected request: {method} {path}")


if __name__ == "__main__":
    unittest.main()
