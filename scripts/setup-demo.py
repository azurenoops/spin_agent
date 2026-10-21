#!/usr/bin/env python3
"""Explicitly provision the checked-in simulation personas through local APIs."""

import argparse
import json
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener


DIRECTORY_ID = "00000000-0000-0000-0000-000000000001"
DEMO_DIRECTORY_ID = "de000000-0000-4000-8000-000000000051"
TENANT_NAME = "SPIN Demo Organization"
SYSTEM_NAME = "SPIN Demo System"
SYSTEM_DESCRIPTION = "Synthetic local demo system. Not a production authorization."
IDENTITIES = {
    key: {"oid": f"10000000-0000-0000-0000-{number:012d}",
          "persona": persona, "role": role, "claims": claims}
    for key, number, persona, role, claims in [
        ("dev-cspadmin", 1, "CspAdmin", None, ["CSP.Admin", "Global Reader"]),
        ("dev-isso", 2, "ISSO", "Isso", ["ISSO"]),
        ("dev-soc-analyst", 3, "SocAnalyst", None, ["Auth.SocAnalyst"]),
        ("dev-orgadmin", 4, "OrganizationAdmin", "Administrator", []),
        ("dev-mission-owner", 5, "MissionOwner", "MissionOwner", []),
        ("dev-system-owner", 6, "SystemOwner", "SystemOwner", []),
        ("dev-issm", 7, "ISSM", "Issm", []),
        ("dev-sca", 8, "SCA", "Sca", []),
        ("dev-ao", 9, "AuthorizingOfficial", "AuthorizingOfficial", []),
    ]
}


class DemoError(Exception):
    pass


def local_origin(value):
    parsed = urlsplit(value)
    if (parsed.scheme != "http" or parsed.hostname not in ("localhost", "127.0.0.1", "::1")
            or parsed.username is not None or parsed.password is not None
            or parsed.path not in ("", "/") or parsed.query or parsed.fragment):
        raise DemoError("Use an HTTP loopback origin only, e.g. http://localhost:5173.")
    return value.rstrip("/")


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise DemoError(f"Refusing HTTP {code} redirect from the local demo API.")


class Api:
    def __init__(self, base_url):
        self.base_url = local_origin(base_url)
        self.opener = build_opener(ProxyHandler({}), NoRedirects())

    def request(self, method, path, *, identity=None, tenant=None, body=None):
        headers = {"Accept": "application/json"}
        if identity:
            headers["Cookie"] = f"ato-simulation={identity}"
            if identity == "dev-cspadmin":
                headers["X-Workspace-Kind"] = "csp"
            elif tenant:
                headers.update({"X-Workspace-Kind": "organization",
                                "X-Workspace-Tenant-Id": tenant,
                                "X-Workspace-Mode": "ordinary"})
        data = None
        if body is not None:
            data = json.dumps(body).encode()
            headers["Content-Type"] = "application/json"
        request = Request(self.base_url + path, data=data, headers=headers, method=method)
        try:
            with self.opener.open(request, timeout=60) as response:
                if response.status == 204:
                    return None
                raw = response.read().decode()
        except HTTPError as error:
            raise DemoError(f"{method} {path}: HTTP {error.code}: "
                            f"{error.read().decode(errors='replace')}") from error
        except (URLError, TimeoutError) as error:
            raise DemoError(f"{method} {path}: {error}") from error
        try:
            result = json.loads(raw)
        except json.JSONDecodeError as error:
            raise DemoError(f"{method} {path}: expected JSON, not an HTML/SPA response.") from error
        if not isinstance(result, dict):
            raise DemoError(f"{method} {path}: expected a JSON object.")
        if result.get("status") == "error" or result.get("ok") is False:
            raise DemoError(f"{method} {path}: {json.dumps(result)}")
        return result["data"] if "data" in result else result


def paged(api, path, *, identity, tenant=None, total_key="total"):
    items = []
    page = 1
    while True:
        result = api.request("GET", f"{path}?page={page}&pageSize=200",
                             identity=identity, tenant=tenant)
        items.extend(result["items"])
        if page * 200 >= result[total_key]:
            return items
        page += 1


def check_roster(api):
    simulation = api.request("GET", "/api/auth/login-config").get("simulation")
    if not simulation:
        raise DemoError("Development simulation must be enabled before demo setup.")
    actual = {item["id"]: item for item in simulation["identities"]}
    if set(actual) != set(IDENTITIES):
        raise DemoError("Demo setup requires the nine checked-in development identities.")
    for key, expected in IDENTITIES.items():
        item = actual[key]
        if (item["persona"] != expected["persona"] or item["roles"] != expected["claims"]
                or item["tenantId"] != DIRECTORY_ID):
            raise DemoError(f"Configured identity {key} differs from the checked-in demo defaults.")


def sign_in(api, identity, tenant=None):
    api.request("POST", "/api/auth/simulate?" + urlencode({"identityId": identity}))
    me = api.request("GET", "/api/auth/me", identity=identity, tenant=tenant)
    if me["oid"] != IDENTITIES[identity]["oid"] or me["directoryTenantId"] != DIRECTORY_ID:
        raise DemoError(f"The selected identity {identity} did not match the demo configuration.")
    return me


def ensure_tenant(api):
    tenants = paged(api, "/api/tenants", identity="dev-cspadmin")
    matches = [t for t in tenants if t["entraTenantId"] == DEMO_DIRECTORY_ID]
    if matches:
        tenant = matches[0]
        if len(matches) != 1 or tenant["displayName"] != TENANT_NAME or tenant["status"] != "Active":
            raise DemoError("The dedicated demo directory belongs to a different or inactive tenant.")
        return tenant["id"]
    if any(t["displayName"] == TENANT_NAME for t in tenants):
        raise DemoError("A different tenant already uses the demo name; refusing to change it.")
    tenant = api.request("POST", "/api/tenants", identity="dev-cspadmin",
                         body={"entraTenantId": DEMO_DIRECTORY_ID, "displayName": TENANT_NAME})
    print(f"Created organization: {tenant['id']}")
    return tenant["id"]


def ensure_memberships(api, tenant):
    root = f"/api/tenants/{tenant}"
    memberships = paged(api, root + "/memberships", identity="dev-cspadmin")
    persons = api.request("GET", root + "/membership-persons", identity="dev-cspadmin")
    ids = {}
    for key, descriptor in IDENTITIES.items():
        if key == "dev-cspadmin":
            continue
        matches = [m for m in memberships if m["directoryTenantId"] == DIRECTORY_ID
                   and m["objectId"] == descriptor["oid"]]
        if any(m["revokedAt"] is not None for m in matches):
            raise DemoError(f"Demo membership for {key} was revoked; restore it explicitly if intended.")
        email = f"{key}@demo.invalid"
        contacts = [p for p in persons if p["email"] == email]
        if len(contacts) > 1 or len(matches) > 1:
            raise DemoError(f"Ambiguous demo Person or membership for {key}.")
        if contacts:
            person = contacts[0]
        elif matches:
            raise DemoError(f"Existing membership for {key} is not associated with its demo Person.")
        else:
            person = api.request("POST", root + "/membership-persons", identity="dev-cspadmin",
                                 body={"displayName": f"Demo {descriptor['persona']}", "email": email})
        ids[key] = person["id"]
        if matches and matches[0]["personId"] != person["id"]:
            raise DemoError(f"Existing membership for {key} belongs to another Person.")
        if not matches:
            api.request("POST", root + "/memberships", identity="dev-cspadmin",
                        body={"directoryTenantId": DIRECTORY_ID, "objectId": descriptor["oid"],
                              "personId": person["id"]})
    return ids


def ensure_administrator(api, tenant, persons):
    admin = api.request("GET", "/api/auth/me", identity="dev-orgadmin", tenant=tenant)
    if "Administrator" not in admin["workspace"]["roles"]:
        api.request("POST", f"/api/tenants/{tenant}/administrator-assignments",
                    identity="dev-cspadmin", body={"personId": persons["dev-orgadmin"]})


def ensure_roles(api, tenant, persons):
    assignments = api.request("GET", "/api/onboarding/role-assignments/",
                              identity="dev-orgadmin", tenant=tenant)
    for key, person in persons.items():
        role = IDENTITIES[key]["role"]
        organization_role = "Assessor" if role == "Sca" else role
        if role and not any(a["role"] == organization_role and a["personId"] == person for a in assignments):
            if any(a["role"] == organization_role for a in assignments):
                raise DemoError(f"Demo role {role} already belongs to another Person.")
            api.request("POST", "/api/roles/organization", identity="dev-orgadmin", tenant=tenant,
                        body={"role": role, "personId": person})


def ensure_onboarding(api, tenant):
    root = "/api/onboarding/tenant"
    context = {"identity": "dev-orgadmin", "tenant": tenant}
    state = api.request("GET", root + "/state", **context)
    # This API serializes OnboardingState as a number unless enum conversion is configured.
    if state["onboardingState"] in ("Active", 2):
        return
    steps = [
        ("legal-entity", {"legalEntityName": TENANT_NAME, "doDComponent": "Synthetic Demo", "timeZone": "UTC"}),
        ("hq-address", {"hqAddressLine1": "1 Demo Way", "hqCity": "Demo",
                        "hqStateOrProvince": "VA", "hqPostalCode": "00000", "hqCountry": "US"}),
        ("classification", {"defaultClassificationLevel": "Unclassified"}),
        ("ao", {"authorizingOfficialName": "Demo Authorizing Official",
                "authorizingOfficialEmail": "dev-ao@demo.invalid"}),
        ("primary-poc", {"primaryPocName": "Demo Organization Admin",
                         "primaryPocEmail": "dev-orgadmin@demo.invalid", "primaryPocPhone": None}),
        ("org-profile", {"name": TENANT_NAME, "description": "Synthetic local demonstration data only."}),
    ]
    for step, body in steps:
        api.request("POST", root + "/" + step, body=body, **context)
    api.request("POST", root + "/submit", **context)


def ensure_system(api, tenant):
    systems = paged(api, "/api/dashboard/portfolio", identity="dev-issm",
                    tenant=tenant, total_key="totalCount")
    matches = [s for s in systems if s["name"] == SYSTEM_NAME]
    if matches:
        if len(matches) != 1 or matches[0]["description"] != SYSTEM_DESCRIPTION:
            raise DemoError("An existing system conflicts with the dedicated demo system.")
        return matches[0]["systemId"]
    system = api.request("POST", "/api/dashboard/systems", identity="dev-issm", tenant=tenant,
                         body={"name": SYSTEM_NAME, "acronym": "SPIN-DEMO",
                               "description": SYSTEM_DESCRIPTION, "systemType": "MajorApplication",
                               "missionCriticality": "MissionSupport", "hostingEnvironment": "Local Demo"})
    print(f"Created sample system: {system['id']}")
    return system["id"]


def verify(api, tenant, system):
    for key, descriptor in IDENTITIES.items():
        me = sign_in(api, key, None if key == "dev-cspadmin" else tenant)
        workspace = me["workspace"]
        expected_kind = "csp" if key == "dev-cspadmin" else "organization"
        if (workspace is None or workspace["kind"] != expected_kind or workspace["mode"] != "ordinary"
                or (expected_kind == "organization" and workspace["tenantId"] != tenant)):
            raise DemoError(f"{key} did not reach the expected ordinary demo workspace.")
        role = descriptor["role"]
        organization_role = "Assessor" if role == "Sca" else role
        if role and organization_role not in workspace["roles"]:
            raise DemoError(f"{key} is missing its expected {role} role.")
        if role and role != "Administrator":
            access = api.request("GET", f"/api/dashboard/systems/{system}/workspace-access",
                                 identity=key, tenant=tenant)
            if (access["systemId"] != system or access["permissions"]["canRead"] is not True
                    or role not in access["roles"]):
                raise DemoError(f"{key} cannot access the demo system with its expected role.")
        if key == "dev-soc-analyst":
            if me["isSocAnalyst"] is not True:
                raise DemoError("The SOC Analyst audit claim is missing.")
            api.request("GET", "/api/auth/events?page=1&pageSize=1", identity=key, tenant=tenant)
        print(f"PASS {key}: {expected_kind}, {role or descriptor['persona']}")


def setup(api):
    check_roster(api)
    sign_in(api, "dev-cspadmin")
    tenant = ensure_tenant(api)
    persons = ensure_memberships(api, tenant)
    ensure_administrator(api, tenant, persons)
    ensure_onboarding(api, tenant)
    ensure_roles(api, tenant, persons)
    system = ensure_system(api, tenant)
    verify(api, tenant, system)
    return tenant, system


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:5173",
                        help="Local Dashboard or MCP origin (default: http://localhost:5173)")
    args = parser.parse_args()
    try:
        tenant, system = setup(Api(args.base_url))
    except (DemoError, KeyError, TypeError, ValueError) as error:
        print(f"Demo setup FAILED: {error}\nNo rollback was attempted. Fix the reported "
              "problem and rerun to resume the dedicated dataset.", file=sys.stderr)
        return 1
    print(f"\nVerified all nine demo identities. Organization: {tenant}; system: {system}")
    print("Open /login on the development Dashboard and manually test each identity.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
