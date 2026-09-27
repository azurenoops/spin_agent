#!/usr/bin/env python3
"""Load verified synthetic provider examples through the production HTTP workflow.

Safety contract / operator paper trail:
  * --plan is GET-only and never creates a cookie jar, state directory or API record.
  * --apply requires the exact manifest SHA printed by --plan. This is explicit
    approval of these synthetic source declarations, NOT an assertion of an ATO.
  * Only loopback URLs, configured dev-cspadmin simulation and ordinary CSP
    workspace requests are supported. No configuration, SQL or identities change.
    The real simulation endpoint sets Secure cookies even on HTTP loopback.
    The dedicated jar permits those server-issued cookies on this exact loopback
    origin only, matching the local-development transport requirement; cookies
    and their Secure attributes are never fabricated or rewritten.
  * State and cookies must live in a private directory outside this checkout.
    Never delete state to retry: exact pending requests and idempotency keys are
    retained before transmission. Uncertain non-idempotent operations reconcile
    through GET or stop rather than blindly repeating POST.
  * Human PDF/DOCX/CSV documents are protected finding evidence, never AI input.
    No mission allocation, adoption, users, cleanup, closure or evidence sharing.
  * Editions are document labels, not canonical publication revision numbers.
    Azure 1.3 is deliberately out of scope; this script cannot publish it.
  * Explicit --azure-offering-id permits revision-checked alignment of that
    offering's name, synthetic description, environments and service model.
    It never edits the provider profile, login configuration, organizations,
    systems or old failed receipts; cleanup remains a separate operator action.
  * --stage refresh-context renews only previously published baseline capabilities
    in this journal, retaining their names, descriptions, classification, category,
    contributors and duties. All unchanged drafts are prepared before impact
    review because private edits can invalidate earlier reviews. Each capability
    gets a separate exact one-change impact review and canonical publication.
    Current offering metadata/scopes and recorded decision are read, never reset.
    Keys include offering revision and capability ID. Old releases are retained;
    no proposal import, allocation, metadata update, source edit or SQL repair.
    API expiry timestamps require an explicit timezone. .NET's seventh fractional
    digit is conservatively truncated to Python microseconds, never rounded up.

Production contracts: ProviderMissionWorkflowHttpTests; ProviderAuthorization,
ProviderHosting, ProviderImpact, CspPackageImport and ProviderFinding endpoints.
Run offline tests: python3 -m unittest discover -s scripts -p 'test_load_provider_offerings_demo.py'
Manual verification after applying: inspect the two offerings, exact retained
citations, 8/5 published capabilities, one Open Moderate finding and linked Open
POA&M per offering; download retained evidence. No actual API run is claimed by
the script's offline tests.
"""

import argparse
import base64
from collections import Counter
from datetime import datetime, timezone
import fcntl
import hashlib
import http.cookiejar
import ipaddress
import json
import mimetypes
import os
from pathlib import Path
import re
import stat
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile


REPO = Path(__file__).resolve().parents[1]
IDENTITY = "dev-cspadmin"
DATASETS = ("azure-il5-shared-services/release-1.2", "microsoft-365-collaboration/release-1.0")
NOTICE = "SYNTHETIC DEMONSTRATION ONLY"


class Stop(RuntimeError):
    """Fail closed, preserving journal and retained API objects."""


def require(condition, message):
    if not condition:
        raise Stop(message)


def digest(content):
    return hashlib.sha256(content).hexdigest()


def parse_api_timestamp(value):
    require(isinstance(value, str), "API expiry timestamp must be a timezone-qualified string.")
    match = re.fullmatch(r"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})", value)
    require(match is not None, f"Invalid timezone-qualified API expiry timestamp: {value}")
    fraction = "." + match[2][:6].ljust(6, "0") if match[2] else ""
    offset = "+00:00" if match[3] == "Z" else match[3]
    try:
        return datetime.fromisoformat(match[1] + fraction + offset)
    except ValueError as exc:
        raise Stop(f"Invalid API expiry timestamp: {value}") from exc


def local_base(value):
    try:
        url = urllib.parse.urlsplit(value)
        host = url.hostname
        require(url.scheme in ("http", "https") and host, "Use a loopback HTTP(S) URL.")
        require(not url.username and not url.password and url.path in ("", "/")
                and not url.query and not url.fragment, "Base URL must contain only scheme, loopback host and port.")
        host = "127.0.0.1" if host == "localhost" else host
        address = ipaddress.ip_address(host)
        require(address.is_loopback, "Non-loopback API URLs are forbidden.")
        authority = f"[{host}]" if address.version == 6 else host
        return f"{url.scheme}://{authority}" + (f":{url.port}" if url.port else "")
    except (ValueError, TypeError) as exc:
        raise Stop(f"Invalid loopback base URL: {value}") from exc


def private_directory(path, create):
    path = Path(path).expanduser().absolute()
    require(not path.resolve().is_relative_to(REPO), "State/cookies must be outside the repository.")
    require(not any(p.is_symlink() for p in (path, *path.parents)), "Symlinked state paths are forbidden.")
    if create:
        path.mkdir(mode=0o700, parents=True, exist_ok=True)
    if path.exists():
        info = path.stat()
        require(path.is_dir() and info.st_uid == os.getuid() and stat.S_IMODE(info.st_mode) == 0o700,
                f"State directory must be owned by you with mode 0700: {path}")
    return path


def private_file(path):
    if path.exists() or path.is_symlink():
        info = path.lstat()
        require(stat.S_ISREG(info.st_mode) and info.st_uid == os.getuid()
                and stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1,
                f"Private file must be a singly linked, owned regular file with mode 0600: {path}")


def atomic_json(path, data):
    target = path.with_suffix(".next")
    private_file(target)
    fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as out:
        json.dump(data, out, ensure_ascii=False, indent=2)
        out.flush()
        os.fsync(out.fileno())
    os.replace(target, path)
    fd = os.open(path.parent, os.O_RDONLY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def locate(document, locator):
    require(locator.startswith("$/"), f"Unsupported source locator: {locator}")
    value = document
    try:
        for part in locator[2:].split("/"):
            match = re.fullmatch(r"([^\[\]]+)(?:\[(\d+)\])?", part)
            require(match is not None, f"Unsupported source locator: {locator}")
            value = value[match[1].replace("~1", "/").replace("~0", "~")]
            if match[2] is not None:
                value = value[int(match[2])]
        return value
    except (KeyError, IndexError, TypeError) as exc:
        raise Stop(f"Source locator does not resolve: {locator}") from exc


class Manifest:
    def __init__(self, directory):
        self.root = Path(directory).resolve()
        raw = (self.root / "internal-manifest.json").read_bytes()
        self.sha = digest(raw)
        self.data = json.loads(raw)
        require(NOTICE in self.data.get("demonstrationNotice", ""), "Missing explicit synthetic notice.")
        require(self.data.get("hashAlgorithm") == "SHA-256", "Unsupported manifest hash algorithm.")
        self.files = {}
        for entry in self.data["files"]:
            name = entry["path"]
            path = self.root / name
            require(not Path(name).is_absolute() and ".." not in Path(name).parts
                    and path.resolve().is_relative_to(self.root) and not path.is_symlink(),
                    f"Unsafe manifest path: {name}")
            require(name not in self.files, f"Duplicate manifest path: {name}")
            content = path.read_bytes()
            require(len(content) == entry["bytes"] and digest(content) == entry["sha256"],
                    f"Manifest hash/size mismatch: {name}")
            self.files[name] = entry
        self.sources = {}
        for dataset in DATASETS:
            matches = [s for s in self.data["sources"] if s["importZip"] == dataset + "/review-source.zip"]
            require(len(matches) == 1, f"Expected one source manifest for {dataset}")
            source = matches[0]
            expected_edition = "1.2" if dataset.startswith("azure-") else "1.0"
            expected_capabilities = 8 if dataset.startswith("azure-") else 5
            require(source["sourceReleaseLabel"] == expected_edition,
                    "Only baseline source editions Azure 1.2 and M365 1.0 may be loaded; future proposals are not publishable here.")
            require(source["expectedCandidates"].get("Capability") == expected_capabilities
                    and source["expectedCandidates"].get("Component") == expected_capabilities,
                    "Baseline must declare exactly eight Azure or five M365 capabilities and components.")
            require(source["sourceDocumentCount"] == 6 and source["expectedSemanticModelCalls"] == 0,
                    "Only the deterministic six-JSON native source inventory is supported.")
            require(source["importZip"] in self.files, "ZIP is not hash-bound by manifest.")
            documents = {}
            with zipfile.ZipFile(self.root / source["importZip"]) as archive:
                entries = archive.infolist()
                require(len(entries) == 6 and len({e.filename for e in entries}) == 6,
                        "Review ZIP must contain exactly six unique JSON documents.")
                for entry in entries:
                    require(Path(entry.filename).name == entry.filename and entry.filename.endswith(".json")
                            and not entry.flag_bits & 1, "Unsafe/non-JSON/encrypted review entry.")
                    filename = dataset + "/sources/" + entry.filename
                    require(filename in source["sourceFiles"] and filename in self.files, "Unmanifested source entry.")
                    content = archive.read(entry)
                    require(digest(content) == self.files[filename]["sha256"]
                            and content == (self.root / filename).read_bytes(), "Review ZIP/source hash mismatch.")
                    documents[entry.filename] = json.loads(content)
            source = dict(source, documents=documents)
            require(sum(source["expectedCandidates"].values()) in (69, 46), "Unexpected source candidate inventory.")
            self.sources[dataset] = source

    def hosting_scopes(self, dataset):
        source = self.sources[dataset]
        scopes = source.get("hostingScopes")
        require(isinstance(scopes, list) and scopes, f"{dataset}: manifest must explicitly declare hostingScopes.")
        citations = source.get("hostingScopeCitations")
        require(citations, "Hosting identities require explicit synthetic source citations.")
        supported = []
        for citation in citations:
            require(citation["sourceFile"] in source["sourceFiles"], "Hosting citation is not a verified source document.")
            record = locate(source["documents"][Path(citation["sourceFile"]).name], citation["locator"])
            serialized = json.dumps(record, ensure_ascii=False)
            require(NOTICE in citation["quote"] and citation["quote"] in serialized,
                    "Hosting identity is not explicitly declared as synthetic in the cited source.")
            supported.append(citation["quote"])
        for scope in scopes:
            require(any(all(str(value) in quote for value in scope.values()) for quote in supported),
                    "Hosting scope fields are not all present in one verified synthetic source citation.")
            if dataset.startswith("azure-"):
                require(scope.get("kind", "Azure") == "Azure"
                        and scope.get("cloud") in ("AzureCloud", "AzureUSGovernment"), "Invalid synthetic Azure scope.")
                import uuid
                for field in ("directoryTenantId", "subscriptionId"):
                    require(uuid.UUID(scope[field]).int != 0, f"Missing explicit synthetic {field}.")
                require(scope["resourceId"].lower().startswith(
                    f"/subscriptions/{scope['subscriptionId']}/resourcegroups/".lower()),
                    "Synthetic Azure resource ID must match its subscription.")
            else:
                require(scope.get("kind") == "Service" and all(scope.get(k) for k in
                        ("serviceId", "serviceName", "environment"))
                        and scope["environment"] in ("Microsoft365DoD", "ManualService"),
                        "M365 requires a Service scope, not Azure entitlement.")
        return scopes


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise Stop(f"API redirect refused ({code}); no credentials forwarded.")


class LoopbackCookiePolicy(http.cookiejar.DefaultCookiePolicy):
    def __init__(self, origin):
        super().__init__()
        self.origin = local_base(origin)

    def same_origin(self, request):
        parsed = urllib.parse.urlsplit(request.full_url)
        return parsed.scheme + "://" + parsed.netloc == self.origin

    def return_ok(self, cookie, request):
        return self.same_origin(request) and super().return_ok(cookie, request)

    def return_ok_secure(self, cookie, request):
        if self.same_origin(request) and self.origin.startswith("http://"):
            return True
        return super().return_ok_secure(cookie, request)


class Api:
    def __init__(self, base, cookie_path, apply):
        self.base = local_base(base)
        self.apply = apply
        self.cookie_path = cookie_path
        self.jar = http.cookiejar.MozillaCookieJar(str(cookie_path) if cookie_path else None,
                                                  policy=LoopbackCookiePolicy(self.base))
        if cookie_path and cookie_path.exists():
            private_file(cookie_path)
            self.jar.load(ignore_discard=True, ignore_expires=False)
            require(all((c.name, c.value) in (("ato-simulation", IDENTITY), ("X-Simulated", "true")) for c in self.jar),
                    "Dedicated loader jar contains an unexpected identity/support cookie.")
        self.opener = urllib.request.build_opener(
            urllib.request.ProxyHandler({}), NoRedirect(), urllib.request.HTTPCookieProcessor(self.jar))

    def request(self, method, path, body=None, key=None, binary=False):
        require(method == "GET" or self.apply, "--plan forbids all HTTP mutations.")
        require(path.startswith("/api/") and not path.startswith("//"), "Only relative API routes are allowed.")
        headers = {"Accept": "application/json", "X-Workspace-Kind": "csp", "X-Workspace-Mode": "ordinary"}
        data = None
        if body is not None:
            if "_multipart" in body:
                data = base64.b64decode(body["_multipart"], validate=True)
                headers["Content-Type"] = body["contentType"]
            else:
                data = json.dumps(body, ensure_ascii=False).encode()
                headers["Content-Type"] = "application/json"
        if key:
            headers["Idempotency-Key"] = key
        request = urllib.request.Request(self.base + path, data=data, headers=headers, method=method)
        try:
            with self.opener.open(request, timeout=60) as response:
                content = response.read()
            if binary:
                return content
            value = json.loads(content) if content else {}
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            raise Stop(f"{method} {path}: HTTP {exc.code}: {detail}") from exc
        except (OSError, ValueError) as exc:
            raise Stop(f"{method} {path}: response uncertain/unavailable: {exc}") from exc
        if isinstance(value, dict) and "data" in value:
            require(value.get("success", True) is not False, f"{method} {path}: {value}")
            value = value["data"]
        return value

    def pages(self, path):
        result, total = [], None
        for page in range(1, 10001):
            separator = "&" if "?" in path else "?"
            data = self.request("GET", path + f"{separator}page={page}&pageSize=100")
            require(isinstance(data, dict) and isinstance(data.get("items"), list)
                    and isinstance(data.get("total"), int), f"Unexpected paged contract: {path}")
            require(data.get("page") == page, f"API repeated or skipped a page: {path}")
            require(total is None or total == data["total"], f"Inventory changed during pagination: {path}")
            total = data["total"]
            result.extend(data["items"])
            require(len(result) <= total, f"Page overrun: {path}")
            if len(result) == total:
                return result
            require(data["items"], f"Incomplete pagination: {path}")
        raise Stop(f"Pagination limit exceeded: {path}")

    def authenticate(self):
        config = self.request("GET", "/api/auth/login-config")
        identities = (config.get("simulation") or {}).get("identities", [])
        require(sum(i.get("id") == IDENTITY for i in identities) == 1,
                "Development simulation is unavailable or dev-cspadmin is not configured; config is not changed.")
        self.request("POST", "/api/auth/simulate?identityId=" + IDENTITY)
        require(any(c.name == "ato-simulation" and c.value == IDENTITY for c in self.jar),
                "Simulation did not issue the configured identity cookie.")
        require(all((c.name, c.value) in (("ato-simulation", IDENTITY), ("X-Simulated", "true")) for c in self.jar),
                "Simulation response included an unexpected authentication/support cookie.")
        probe = urllib.request.Request(self.base + "/api/auth/me")
        self.jar.add_cookie_header(probe)
        require("ato-simulation=" + IDENTITY in probe.get_header("Cookie", ""),
                "The actual simulation cookie will not be sent; refusing fallback to ambient app identity.")
        me = self.request("GET", "/api/auth/me")
        workspace = me.get("currentWorkspace") or me.get("workspace") or {}
        require(me.get("isCspAdmin") is True and not me.get("isImpersonating")
                and workspace.get("kind") == "csp" and workspace.get("mode") == "ordinary",
                "Configured identity is not an ordinary CSP administrator; refusing identity/config changes.")
        if self.cookie_path:
            private_file(self.cookie_path)
            fd = os.open(self.cookie_path, os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600)
            os.close(fd)
            self.jar.save(ignore_discard=True, ignore_expires=False)
        return me


class Journal:
    def __init__(self, directory, binding, apply):
        self.path = None
        self.lock = None
        directory = private_directory(directory, create=apply)
        path = directory / "state.json"
        private_file(path)
        if apply:
            lock = directory / "loader.lock"
            private_file(lock)
            self.lock = os.open(lock, os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600)
            try:
                fcntl.flock(self.lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError as exc:
                raise Stop("Another loader holds this state directory; no concurrent application.") from exc
            self.path = path
        self.data = json.loads(path.read_text()) if path.exists() else {"binding": binding, "operations": {}, "ids": {}}
        require(self.data["binding"] == binding, "State belongs to another API/dataset/identity; do not overwrite or discard it.")

    @classmethod
    def memory(cls):
        instance = cls.__new__(cls)
        instance.path = None
        instance.data = {"binding": {"manifest": "offline-test"}, "operations": {}, "ids": {}}
        return instance

    def save(self):
        if self.path:
            private_file(self.path)
            atomic_json(self.path, self.data)

    def remember(self, name, value):
        self.data["ids"][name] = value
        self.save()

    def key(self, name):
        return "provider-demo-" + digest(json.dumps(self.data["binding"], sort_keys=True).encode() + name.encode())[:64]

    def perform(self, api, name, method, path, body, reconcile, idempotent=True):
        operation = self.data["operations"].get(name)
        if operation:
            require(operation["method"] == method and operation["path"] == path,
                    f"Saved operation route changed: {name}")
            found = reconcile()
            if found is not None:
                operation.update(status="done", result=found)
                self.save()
                return found
            if operation["status"] == "done":
                raise Stop(f"Previously completed {name} no longer reconciles with API. Saved IDs retained.")
            fenced_patch = method in ("PATCH", "PUT") and "expectedRevision" in operation["body"]
            require(idempotent and operation.get("key") or fenced_patch,
                    f"Uncertain non-idempotent {name}; GET did not recover it. Inspect retained API/state; no duplicate POST sent.")
        else:
            found = reconcile()
            if found is not None:
                self.data["operations"][name] = dict(method=method, path=path, body=body,
                                                     key=None, status="done", result=found)
                self.save()
                return found
            key = self.key(name) if idempotent else None
            operation = dict(method=method, path=path, body=body, key=key, status="pending")
            self.data["operations"][name] = operation
            self.save()
        result = api.request(method, path, body=operation["body"], key=operation["key"])
        operation.update(status="done", result=result)
        self.save()
        return result


def multipart(fields, file_field, path, key):
    boundary = "provider-demo-" + digest(key.encode())[:40]
    chunks = []
    for name, value in fields.items():
        chunks.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode())
    mime = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
    require('"' not in path.name and "\r" not in path.name and "\n" not in path.name, "Unsafe multipart filename.")
    chunks.append((f'--{boundary}\r\nContent-Disposition: form-data; name="{file_field}"; filename="{path.name}"\r\n'
                   f'Content-Type: {mime}\r\n\r\n').encode())
    chunks.extend([path.read_bytes(), f"\r\n--{boundary}--\r\n".encode()])
    return {"_multipart": base64.b64encode(b"".join(chunks)).decode(),
            "contentType": "multipart/form-data; boundary=" + boundary}


def one(rows, predicate, label, optional=True):
    selected = [row for row in rows if predicate(row)]
    require(len(selected) <= 1, f"Ambiguous {label}: {[r for r in selected]}")
    require(optional or selected, f"Missing {label}.")
    return selected[0] if selected else None


def check_ready(status, source):
    coverage = status.get("coverage") or {}
    require(status.get("processingState") == "ReadyForReview" and not status.get("lastError"),
            f"Source is not ReadyForReview: {status}")
    expected = source["expectedEntryCountIncludingContainer"]
    require(coverage.get("total") == expected and coverage.get("processed") == expected
            and all(coverage.get(k) == 0 for k in ("pending", "unsupported", "unreadable", "failed", "excluded")),
            f"Incomplete/partial/unavailable source coverage: {coverage}")
    progress = status.get("analysisProgress")
    require(progress is not None and progress.get("modelCalls") == 0,
            "Native-only analysis must report zero modelCalls; missing telemetry or AI work requires operator review.")


class Loader:
    def __init__(self, api, journal, manifest, args):
        self.api, self.state, self.manifest, self.args = api, journal, manifest, args

    def get(self, path):
        return self.api.request("GET", path)

    def post(self, name, path, body, reconcile, keyed=True):
        return self.state.perform(self.api, name, "POST", path, body, reconcile, idempotent=keyed)

    def run(self, dataset):
        source = self.manifest.sources[dataset]
        scopes = self.manifest.hosting_scopes(dataset)
        environments = sorted({s.get("cloud") or s["environment"] for s in scopes})
        name = source["offering"]
        tag = f"{dataset}:{self.manifest.sha[:12]}"
        note = f"{NOTICE}; source edition {source['sourceReleaseLabel']}; manifest SHA-256 {self.manifest.sha}."
        metadata = {
            "name": name, "description": note + " " + self.manifest.data["demonstrationNotice"],
            "environments": environments,
            "serviceModel": "InfrastructureSharedServices" if dataset.startswith("azure-") else "SoftwareAsAService",
            "managementArrangement": "ProviderManaged",
        }
        saved_id = self.state.data["ids"].get(dataset + "/offering")
        explicit_id = self.args.azure_offering_id if dataset.startswith("azure-") else None
        require(not saved_id or not explicit_id or saved_id == explicit_id,
                "Explicit Azure ID disagrees with saved offering; refusing to cross datasets.")
        offering_id = saved_id or explicit_id
        if offering_id:
            offering = self.get("/api/csp/offerings/" + offering_id)
        else:
            matches = self.api.pages("/api/csp/offerings")
            known = one(matches, lambda r: r["name"] == name, name)
            pending_create = dataset + "/offering-create" in self.state.data["operations"]
            require(not (dataset.startswith("azure-") and known and not pending_create),
                    f"Azure offering already exists ({known['offeringId'] if known else ''}); explicitly pass --azure-offering-id.")
            if known and not pending_create:
                offering = known
            else:
                offering = self.post(dataset + "/offering-create", "/api/csp/offerings", metadata,
                                     lambda: one(self.api.pages("/api/csp/offerings"),
                                                 lambda r: r["name"] == name and r["description"].startswith(note), name))
            offering_id = offering["offeringId"]
        self.state.remember(dataset + "/offering", offering_id)
        update_key = dataset + "/explicit-offering-update"
        if explicit_id and (any(offering.get(k) != v for k, v in metadata.items())
                            or update_key in self.state.data["operations"]):
            path = "/api/csp/offerings/" + offering_id
            def aligned_offering():
                current = self.get(path)
                return current if all(current.get(k) == v for k, v in metadata.items()) else None
            offering = self.state.perform(
                self.api, update_key, "PATCH", path,
                dict(expectedRevision=offering["revision"], **metadata), aligned_offering, idempotent=False)
            require(all(offering.get(k) == v for k, v in metadata.items()),
                    "Known Azure offering update did not retain the exact requested synthetic metadata.")
        require(set(environments).issubset(offering["environments"]),
                f"Offering {offering_id} has incompatible environments; identity/configuration will not be rewritten.")
        self.state.remember(dataset + "/offering", offering_id)
        root = "/api/csp/offerings/" + offering_id
        boundary_claim = source["documents"]["01-service-scope.json"]["boundaryClaims"][0]["claim"]["boundary"]
        decision_claim = source["documents"]["04-synthetic-reference-record.json"]["authorizationDecisionClaims"][0]["claim"]["authorizationDecision"]
        require(decision_claim["statusAsStated"] == "Approved" and "NOT ATO" in decision_claim["decisionType"],
                "Synthetic source must explicitly state Approved distribution status and NOT ATO type; no source text is rewritten.")
        offering = self.get(root)
        boundary_name = "SYNTHETIC " + name + " · " + tag
        boundary_body = {
            "expectedOfferingRevision": offering["revision"],
            "predecessorRevisionId": offering["currentBoundaryRevisionId"],
            "name": boundary_name, "scopeStatement": boundary_claim["scope"],
            "services": [c["name"] for c in source["documents"]["01-service-scope.json"]["components"]],
            "componentSnapshotIds": [], "includedScopes": scopes,
            "exclusions": [{"scope": None, "description": e, "rationale": note}
                           for e in decision_claim["exclusions"]],
            "providerResponsibilities": [r for r in boundary_claim["responsibilities"] if r.startswith("Provider")],
            "customerResponsibilities": [r for r in boundary_claim["responsibilities"] if r.startswith("Customer")],
            "citations": [],
        }
        boundary = self.post(dataset + "/boundary", root + "/boundary-revisions", boundary_body,
                             lambda: one(self.api.pages(root + "/boundary-revisions"),
                                         lambda r: r["name"] == boundary_name, "source boundary"))
        require(boundary["scopeStatement"] == boundary_body["scopeStatement"]
                and boundary["services"] == boundary_body["services"], "Retained source boundary differs from manifest.")
        self.state.remember(dataset + "/boundary", boundary["boundaryRevisionId"])
        offering = self.get(root)
        hosting = self.post(dataset + "/hosting", root + "/hosting-scope-revisions", {
            "expectedOfferingRevision": offering["revision"],
            "predecessorRevisionId": offering["currentHostingScopeRevisionId"],
            "name": boundary_name, "permittedScopes": scopes, "exclusions": [], "citations": [],
            "purpose": note + " Technical demonstration scope only; no customer allocation.",
            "changeRationale": "Explicit source-manifest scope, not live infrastructure or authorization.",
        }, lambda: one(self.api.pages(root + "/hosting-scope-revisions"),
                       lambda r: r["name"] == boundary_name, "source hosting scope"))
        self.state.remember(dataset + "/hosting", hosting["snapshot"]["revisionId"])
        receipt = self.receive(dataset, root, source, boundary)
        package_id = receipt["package"]["packageId"]
        package_root = "/api/csp/package-imports/" + package_id
        self.state.remember(dataset + "/package", package_id)
        deadline = time.monotonic() + self.args.wait_seconds
        while True:
            status = self.get(package_root)
            if status["processingState"] not in ("Received", "Processing"):
                break
            require(time.monotonic() < deadline, f"Analysis timeout for {package_id}; retry with same state after inspecting API.")
            time.sleep(2)
        check_ready(status, source)
        entries = self.api.pages(package_root + "/entries")
        self.verify_entries(dataset, source, entries)
        candidates = self.api.pages(package_root + "/candidates")
        self.verify_candidates(source, entries, candidates)
        review_state = self.get(package_root + "/review-state")
        if not review_state.get("publication"):
            self.review(dataset, package_root, source, candidates)
            candidates = self.api.pages(package_root + "/candidates")
        else:
            require(all(c["reviewState"] in ("Reviewed", "Approved", "Published") for c in candidates),
                    "A published source contains declarations not explicitly reviewed.")
        decision = self.record_decision(dataset, root, package_id, boundary, candidates)
        finding = self.finding_and_poam(dataset, root, package_id, candidates)
        if self.args.stage != "publish":
            self.evidence(dataset, root, finding)
        if self.args.stage != "load-evidence":
            self.publish(dataset, root, package_root, source, boundary, hosting, receipt, decision, candidates)
        self.verify_outputs(dataset, root, package_root, source)

    def receive(self, dataset, root, source, boundary):
        package_name = f"SYNTHETIC {source['offering']} source edition {source['sourceReleaseLabel']} [{self.manifest.sha[:12]}]"
        zip_path = self.manifest.root / source["importZip"]
        def reconcile():
            matches = []
            for version in self.api.pages(root + "/package-versions"):
                package_root = "/api/csp/package-imports/" + version["packageId"]
                package = self.get(package_root)
                if package["name"] != package_name:
                    continue
                entries = self.api.pages(package_root + "/entries")
                original = one(entries, lambda e: e["fileName"] == zip_path.name
                               and e["sha256"].lower() == self.manifest.files[source["importZip"]]["sha256"],
                               "retained review ZIP")
                require(original is not None, "Matching package name has a different source digest.")
                require(version["boundaryRevisionId"] == boundary["boundaryRevisionId"], "Package is bound to another boundary.")
                matches.append({"package": package, "packageVersion": version})
            return one(matches, lambda r: True, "source receipt")
        offering = self.get(root)
        body = multipart({"name": package_name, "boundaryRevisionId": boundary["boundaryRevisionId"],
                          "expectedOfferingRevision": offering["revision"]}, "files", zip_path, dataset)
        return self.post(dataset + "/receipt", root + "/package-versions", body, reconcile)

    def verify_entries(self, dataset, source, entries):
        require(len(entries) == source["expectedEntryCountIncludingContainer"], "Unexpected retained entry count.")
        require(all(e["status"] == "Processed" and not e.get("reason") and not e.get("exclusionReason")
                    for e in entries), "Retained source includes processing exceptions or exclusions.")
        expected = {Path(p).name: self.manifest.files[p]["sha256"] for p in source["sourceFiles"]}
        expected["review-source.zip"] = self.manifest.files[source["importZip"]]["sha256"]
        require(Counter(e["fileName"] for e in entries) == Counter(expected.keys()), "Retained entry filenames differ.")
        for entry in entries:
            require(entry["sha256"].lower() == expected[entry["fileName"]], f"Retained hash mismatch: {entry['fileName']}")

    def candidate_source(self, source, candidate):
        require(candidate["citations"], f"Uncited candidate: {candidate['candidateId']}")
        objects = []
        for citation in candidate["citations"]:
            filename = Path(citation["archivePath"]).name
            require(filename in source["documents"], "Candidate cites a nonmanifested source.")
            record = locate(source["documents"][filename], citation["locator"])
            try:
                quoted = json.loads(citation["quote"])
            except ValueError as exc:
                raise Stop("Native candidate quote is not the exact structured source object.") from exc
            require(quoted == record, "Candidate quote does not equal manifest-bound source segment.")
            objects.append(record)
        require(all(r == objects[0] for r in objects), "Candidate spans inconsistent source declarations.")
        return objects[0]

    def verify_candidates(self, source, entries, candidates):
        require(len({c["candidateId"] for c in candidates}) == len(candidates), "Duplicate paged candidate IDs.")
        require(Counter(c["type"] for c in candidates) == Counter(source["expectedCandidates"]),
                f"Native candidate inventory mismatch: {dict(Counter(c['type'] for c in candidates))}")
        by_artifact = {e["artifactId"]: e for e in entries}
        source_ids = {}
        for candidate in candidates:
            if candidate["type"] in ("Component", "Capability"):
                record = self.candidate_source(source, candidate)
                require(record["id"] not in source_ids, "Duplicate primary source declaration identity.")
                source_ids[record["id"]] = candidate["candidateId"]
        for candidate in candidates:
            require(not candidate.get("unresolvedDependencies"), f"Unresolved source dependencies: {candidate['candidateId']}")
            record = self.candidate_source(source, candidate)
            kind = candidate["type"]
            auto = kind in ("ControlMapping", "Responsibility") and "componentIds" in record
            expected_name = (candidate["name"] if kind == "ControlMapping" else record["responsibility"]) if auto else record["name"]
            require(candidate["name"] == expected_name
                    and candidate["description"] == ("" if auto or "claim" in record else record.get("description", "")),
                    f"Candidate text differs from the approved source: {candidate['candidateId']}")
            if kind == "ControlMapping":
                require(candidate["name"] in record.get("controlIds", []), "Mapping proposes a control absent from source.")
            if kind in ("Component", "Capability", "ControlMapping", "Responsibility"):
                dependencies = ([record["id"]] if auto else
                                record.get("componentIds", []) or ([record["capabilityId"]] if record.get("capabilityId") else []))
                require(all(d in source_ids for d in dependencies)
                        and sorted(candidate["contributorIds"]) == sorted(source_ids[d] for d in dependencies),
                        "Candidate dependencies differ from actual source component/capability identities.")
            if "claim" in record:
                claim = candidate.get("claim") or {}
                for section, fields in record["claim"].items():
                    if section in ("qualifications", "relationships", "fieldSources", "sourceAliases"):
                        continue
                    require(isinstance(claim.get(section), dict)
                            and all(claim[section].get(k) == v for k, v in fields.items()),
                            "Typed claim fields differ from approved source.")
            for citation in candidate["citations"]:
                require(citation["artifactId"] in by_artifact
                        and citation["archivePath"] == by_artifact[citation["artifactId"]]["archivePath"],
                        "Candidate citation does not identify its exact retained artifact.")

    def review(self, dataset, package_root, source, candidates):
        for candidate in candidates:
            record = self.candidate_source(source, candidate)
            duties = candidate["controlDuties"]
            if candidate["type"] == "Capability":
                require(record.get("responsibility") in ("Provider", "Shared", "Customer"),
                        "Capability has no source-stated duty; no default is inferred.")
                controls = record.get("controlIds") or [record.get("controlId")]
                require(all(controls), "Capability has no explicit source controls.")
                duties = {control: record["responsibility"] for control in controls}
                require(candidate["contributorIds"], "Capability lacks resolved source components.")
            if candidate["type"] in ("Component", "Capability"):
                require(not any(d.get("published") for d in candidate["duplicateMatches"]),
                        "Source matches an existing publication; do not create duplicate canonical capabilities. Reconcile/clean up explicitly.")
            rationale = (f"Operator --apply review of {NOTICE}; exact manifest {self.manifest.sha}; "
                         f"source {candidate['citations'][0]['archivePath']} {candidate['citations'][0]['locator']}. "
                         "Retain source-stated duties and distinct source declarations; NOT an ATO or mission adoption.")
            body = {k: candidate[k] for k in ("name", "description", "componentType", "contributorIds")}
            body.update(expectedRevision=candidate["revision"], classification="Unclassified",
                        serviceCategory="Synthetic demonstration", controlDuties=duties, reviewAction="Reviewed",
                        rationale=rationale, duplicateResolution="KeepSeparate" if candidate["duplicateMatches"] else None,
                        authorizationReference=candidate.get("authorizationReference"))
            def reconcile(c=candidate, expected=body):
                actual = one(self.api.pages(package_root + "/candidates"),
                             lambda r: r["candidateId"] == c["candidateId"], "candidate", optional=False)
                if actual["reviewState"] not in ("Reviewed", "Approved"):
                    return None
                require(all(actual[k] == expected[k] for k in
                            ("name", "description", "componentType", "controlDuties", "contributorIds",
                             "classification", "serviceCategory", "rationale", "duplicateResolution")),
                        f"Existing candidate review differs from this exact source decision: {c['candidateId']}")
                return actual
            self.state.perform(self.api, dataset + "/review/" + candidate["candidateId"], "PATCH",
                               package_root + "/candidates/" + candidate["candidateId"], body, reconcile, idempotent=False)

    @staticmethod
    def citations(package_id, candidate):
        return [dict(packageId=package_id, **c) for c in candidate["citations"]]

    @staticmethod
    def source_ref(package_id, candidate):
        return {"packageId": package_id, "candidateId": candidate["candidateId"], "revision": candidate["revision"]}

    def record_decision(self, dataset, root, package_id, boundary, candidates):
        candidate = one(candidates, lambda c: c["type"] == "AuthorizationDecisionClaim", "typed decision", optional=False)
        claim = candidate["claim"]["authorizationDecision"]
        source = self.candidate_source(self.manifest.sources[dataset], candidate)["claim"]["authorizationDecision"]
        require(all(claim.get(k) == v for k, v in source.items()), "Retained typed decision differs from source.")
        require(claim["statusAsStated"] == "Approved" and "NOT ATO" in claim["decisionType"], "Not a synthetic distribution decision.")
        reference = claim["reference"]
        body = {
            "expectedOfferingRevision": self.get(root)["revision"], "boundaryRevisionId": boundary["boundaryRevisionId"],
            "sourceCandidateRefs": [self.source_ref(package_id, candidate)], "recordKind": "ProviderDecision",
            "reference": reference, "issuingAuthority": claim["authority"], "issuingAuthorityType": "organization",
            "decisionAsStated": claim["statusAsStated"], "issuedOn": claim["decisionDate"], "effectiveOn": None,
            "expiresOn": claim.get("expirationDate"), "expiryBasis": "DateStated" if claim.get("expirationDate") else "NoExpiryStated",
            "scopeStatement": claim["subject"] + ": " + claim["scope"],
            "conditions": [claim["decisionType"], *claim["conditions"], *claim["exclusions"]],
            "citations": self.citations(package_id, candidate),
        }
        def find():
            record = one(self.api.pages(root + "/authorization-records"),
                         lambda r: r["reference"] == reference, "synthetic decision")
            if record:
                require(all(record[k] == body[k] for k in body if k != "expectedOfferingRevision"),
                        "Existing decision differs from manifest/source revision; refusing to rewrite it.")
            return record
        draft = self.post(dataset + "/decision", root + "/authorization-records", body, find)
        self.state.remember(dataset + "/decision", draft["recordId"])
        path = root + "/authorization-records/" + draft["recordId"]
        def recorded():
            current = self.get(path)
            require(current["revisionId"] == draft["revisionId"] and current["snapshotHash"] == draft["snapshotHash"],
                    "Decision snapshot changed; no stale recording.")
            return current if current["metadataReviewState"] == "Recorded" else None
        return self.post(dataset + "/decision-record", path + "/record", {
            "expectedRevision": draft["revision"], "revisionId": draft["revisionId"], "snapshotHash": draft["snapshotHash"],
            "rationale": f"{NOTICE}: source-stated Approved means fictional distribution only; NOT ATO. Manifest {self.manifest.sha}.",
        }, recorded, keyed=False)

    def finding_and_poam(self, dataset, root, package_id, candidates):
        candidate = one(candidates, lambda c: c["type"] == "AssessmentFinding", "finding source", optional=False)
        claim = candidate["claim"]["assessmentFinding"]
        require(claim["severityAsStated"] == "Moderate" and claim["statusAsStated"] == "Open",
                "Demo requires a source-stated Open Moderate finding, not invented remediation.")
        finding_body = {"expectedOfferingRevision": self.get(root)["revision"], "title": candidate["name"],
                        "observation": claim["observation"], "severityAsStated": claim["severityAsStated"],
                        "controlIds": claim["controlIds"], "citations": self.citations(package_id, candidate),
                        "sourceCandidateRef": self.source_ref(package_id, candidate)}
        def find_finding():
            found = one(self.api.pages(root + "/findings"),
                        lambda r: (r.get("sourceCandidateRef") or {}).get("candidateId") == candidate["candidateId"],
                        "source finding")
            if found:
                require(all(found[k] == v for k, v in finding_body.items() if k != "expectedOfferingRevision")
                        and found["workflowState"] == "Open", "Existing finding changed; no automatic overwrite or reopen.")
            return found
        finding = self.post(dataset + "/finding", root + "/findings", finding_body, find_finding)
        self.state.remember(dataset + "/finding", finding["findingId"])
        poam_candidate = one(candidates, lambda c: c["type"] == "PoamItem", "POAM source", optional=False)
        poam = poam_candidate["claim"]["poamItem"]
        relationships = poam_candidate["claim"]["relationships"]
        require(any(r["kind"] == "PoamFinding" and r["targetSourceId"] == claim["sourceFindingId"]
                    for r in relationships), "POAM does not cite this source finding.")
        poam_body = {"expectedOfferingRevision": self.get(root)["revision"], "title": poam_candidate["name"],
                     "findingIds": [finding["findingId"]], "correctiveAction": poam["correctiveAction"],
                     "ownerAsStated": poam.get("ownerAsStated"), "milestones": poam["milestones"],
                     "citations": self.citations(package_id, poam_candidate),
                     "sourceCandidateRef": self.source_ref(package_id, poam_candidate)}
        def find_poam():
            found = one(self.api.pages(root + "/poam-items"),
                        lambda r: (r.get("sourceCandidateRef") or {}).get("candidateId") == poam_candidate["candidateId"],
                        "source POAM")
            if found:
                require(all(found[k] == v for k, v in poam_body.items() if k != "expectedOfferingRevision")
                        and found["workflowState"] == "Open", "Existing POAM changed; no automatic overwrite.")
            return found
        plan = self.post(dataset + "/poam", root + "/poam-items", poam_body, find_poam)
        self.state.remember(dataset + "/poam", plan["poamId"])
        return finding

    def evidence_files(self, dataset):
        return [name for name, entry in self.manifest.files.items()
                if name.startswith(dataset + "/") and Path(name).suffix in (".pdf", ".docx", ".csv")
                and (entry["distribution"] == "CUSTOMER-DEMO"
                     or entry["distribution"] == "PRIVATE" and name.endswith(".pdf"))]

    def evidence(self, dataset, root, finding):
        path = root + "/findings/" + finding["findingId"] + "/evidence"
        for name in self.evidence_files(dataset):
            entry = self.manifest.files[name]
            description = (f"{NOTICE}; {entry['distribution']}; retained source companion, NOT closure evidence. "
                           f"Manifest {self.manifest.sha}; source {name}. Provider-private; no sharing grant.")
            def reconcile(n=name, metadata=entry, text=description):
                found = one(self.api.pages(path), lambda r: r["fileName"] == Path(n).name, "evidence " + n)
                if found:
                    require(found["sha256"].lower() == metadata["sha256"] and found["byteLength"] == metadata["bytes"]
                            and found["description"] == text, "Retained evidence filename has different content/provenance.")
                return found
            current = one(self.api.pages(root + "/findings"), lambda f: f["findingId"] == finding["findingId"],
                          "finding", optional=False)
            body = multipart({"expectedFindingRevision": current["revision"], "description": description},
                             "file", self.manifest.root / name, dataset + name)
            evidence = self.post(dataset + "/evidence/" + Path(name).name, path, body, reconcile)
            require(evidence["sha256"].lower() == entry["sha256"], "Upload receipt content hash mismatch.")
            self.state.remember(dataset + "/evidence/" + Path(name).name, evidence["evidenceId"])

    def publish(self, dataset, root, package_root, source, boundary, hosting, receipt, decision, candidates):
        current = self.get(package_root + "/review-state")
        if current.get("publication"):
            self.verify_publication(current["publication"], source)
            return
        selected = [c for c in candidates if c["type"] in ("Component", "Capability")]
        generation = self.state.data["ids"].get(dataset + "/preview-generation", 1)
        previous_impact = self.state.data["ids"].get(dataset + "/impact")
        if previous_impact:
            existing = self.get(root + "/impact-reviews/" + previous_impact)
            if existing["stale"] or current.get("previewIsStale"):
                generation += 1
                self.state.remember(dataset + "/preview-generation", generation)
                self.state.remember(dataset + "/impact", None)
        prefix = dataset + f"/pipeline-{generation}"
        changes = [self.get(root + f"/impact-options/{c['type']}/{c['candidateId']}")["change"] for c in selected]
        impact_body = {"expectedOfferingRevision": self.get(root)["revision"], "changes": changes,
                       "authorizationRevisionIds": [decision["revisionId"]],
                       "boundaryRevisionId": boundary["boundaryRevisionId"],
                       "hostingScopeRevisionId": hosting["snapshot"]["revisionId"],
                       "packageVersionIds": [receipt["packageVersion"]["packageVersionId"]]}
        def find_impact():
            operation = self.state.data["operations"].get(prefix + "/impact")
            result = operation.get("result") if operation else None
            if result:
                live = self.get(root + "/impact-reviews/" + result["reviewId"])
                require(not live["stale"], "Impact review expired/changed; rerun to create a fresh exact preview.")
                return result
            # Preview creation has server-enforced idempotency. No discoverable
            # preview ID after response loss: replay the journaled exact request.
            self.api.pages(root + "/impact-reviews")
            return None
        impact = self.post(prefix + "/impact", root + "/impact-previews", impact_body, find_impact)
        self.state.remember(dataset + "/impact", impact["reviewId"])
        require(not impact["blockers"], f"Actual impact blockers: {impact['blockers']}")
        impact_path = root + "/impact-reviews/" + impact["reviewId"]
        def accepted():
            live = self.get(impact_path)
            require(not live["stale"], "Impact is stale.")
            return live if live["disposition"] == "AcceptForPublication" else None
        self.post(prefix + "/impact-review", impact_path + "/review", {
            "expectedRevision": impact["revision"], "previewId": impact["previewId"], "previewHash": impact["previewHash"],
            "disposition": "AcceptForPublication",
            "rationale": f"{NOTICE}: exact {len(selected)} source-backed records, explicit duties, manifest {self.manifest.sha}. NOT ATO.",
        }, accepted, keyed=False)
        selections = [{"candidateId": c["candidateId"], "revision": c["revision"]} for c in selected]
        def find_preview():
            review = self.get(package_root + "/review-state")
            preview = review.get("preview")
            if not preview or review["previewIsStale"]:
                return None
            if (sorted(preview["candidates"], key=lambda c: c["candidateId"]) ==
                    sorted(selections, key=lambda c: c["candidateId"])
                    and preview.get("impactReviewIds") == [impact["reviewId"]]):
                return preview
            return None
        preview = self.post(prefix + "/preview", package_root + "/approval-previews", {
            "expectedRevision": self.get(package_root)["revision"], "candidates": selections,
            "impactReviewIds": [impact["reviewId"]],
        }, find_preview, keyed=False)
        require(not preview["blockers"], f"Actual publication preview blockers: {preview['blockers']}")
        selection = {k: preview[k] for k in ("previewId", "previewHash", "revision")}
        def approved():
            live = find_preview()
            return live if live and live["state"] in ("Approved", "Published") else None
        self.post(prefix + "/approve", package_root + "/approve", selection, approved, keyed=False)
        publication = self.post(prefix + "/publish", package_root + "/publish", selection,
                                lambda: self.get(package_root + "/review-state").get("publication"))
        self.verify_publication(publication, source)
        self.state.remember(dataset + "/publishedRecords", publication["records"])

    @staticmethod
    def verify_publication(publication, source):
        require(publication["publicationState"] == "Published", "Canonical publication did not complete.")
        records = publication["records"]
        require(Counter(r["type"] for r in records) ==
                Counter({k: source["expectedCandidates"][k] for k in ("Component", "Capability")}),
                "Canonical publication has incorrect component/capability counts.")
        require(all(r.get("releaseId") for r in records if r["type"] == "Capability"),
                "Published capability is missing its canonical release.")

    def verify_outputs(self, dataset, root, package_root, source):
        if self.args.stage != "load-evidence":
            publication = self.get(package_root + "/review-state").get("publication")
            require(publication, "Missing persisted publication.")
            self.verify_publication(publication, source)
            overview = self.get(root + "/overview")
            require(overview["capabilities"]["published"] >= source["expectedCandidates"]["Capability"],
                    "Offering overview does not expose expected canonical capabilities.")
        if self.args.stage != "publish":
            finding_id = self.state.data["ids"][dataset + "/finding"]
            evidence_path = root + "/findings/" + finding_id + "/evidence"
            artifacts = self.api.pages(evidence_path)
            expected = self.evidence_files(dataset)
            require(all(any(a["fileName"] == Path(n).name
                            and a["sha256"].lower() == self.manifest.files[n]["sha256"] for a in artifacts)
                        for n in expected), "Visible retained evidence does not match manifest.")
            for name in expected:
                artifact = one(artifacts, lambda a: a["fileName"] == Path(name).name, "retained evidence", optional=False)
                content = self.api.request("GET", evidence_path + "/" + artifact["evidenceId"] + "/content", binary=True)
                require(digest(content) == self.manifest.files[name]["sha256"],
                        f"Retained evidence download failed content verification: {name}")

    @staticmethod
    def same_working(actual, expected):
        return (all(actual.get(k) == expected[k] for k in ("classification", "serviceCategory", "controlDuties"))
                and sorted(actual.get("contributors", [])) == sorted(expected["contributors"]))

    @staticmethod
    def unexpired(result):
        require(not result.get("isStale", False) and not result.get("stale", False),
                "Context-renewal preview is stale; no approval/publication attempted.")
        if result.get("expiresAt"):
            require(parse_api_timestamp(result["expiresAt"]) > datetime.now(timezone.utc),
                    "Context-renewal preview expired; retain journal and reconcile explicitly, without generic reset.")

    def refresh_context_plan(self, dataset):
        ids = self.state.data["ids"]
        require(all(dataset + "/" + key in ids for key in
                    ("offering", "package", "decision", "publishedRecords")),
                "refresh-context requires the completed baseline journal; no automatic source or offering creation.")
        require(not dataset.startswith("azure-") or not self.args.azure_offering_id
                or self.args.azure_offering_id == ids[dataset + "/offering"],
                "Explicit Azure offering ID disagrees with the baseline context-renewal journal.")
        source = self.manifest.sources[dataset]
        root = "/api/csp/offerings/" + ids[dataset + "/offering"]
        offering = self.get(root)
        require(offering["currentBoundaryRevisionId"] and offering["currentHostingScopeRevisionId"],
                "Current offering boundary and hosting scope are required.")
        package_root = "/api/csp/package-imports/" + ids[dataset + "/package"]
        check_ready(self.get(package_root), source)
        entries = self.api.pages(package_root + "/entries")
        self.verify_entries(dataset, source, entries)
        candidates = self.api.pages(package_root + "/candidates")
        self.verify_candidates(source, entries, candidates)
        publication = self.get(package_root + "/review-state").get("publication")
        require(publication, "Baseline source does not have a canonical publication.")
        self.verify_publication(publication, source)
        baseline = ids[dataset + "/publishedRecords"]
        require(sorted(baseline, key=lambda r: r["candidateId"]) ==
                sorted(publication["records"], key=lambda r: r["candidateId"]),
                "Saved baseline record/release IDs differ from the immutable package publication.")
        receipt = self.state.data["operations"].get(dataset + "/receipt", {}).get("result")
        require(receipt and receipt["package"]["packageId"] == ids[dataset + "/package"],
                "Missing exact baseline receipt; proposal or failed source substitution is forbidden.")
        version_id = receipt["packageVersion"]["packageVersionId"]
        version = one(self.api.pages(root + "/package-versions"),
                      lambda r: r["packageVersionId"] == version_id, "baseline package version", optional=False)
        require(version["packageId"] == ids[dataset + "/package"], "Package-version/source identity mismatch.")
        decision = self.get(root + "/authorization-records/" + ids[dataset + "/decision"])
        source_decision = source["documents"]["04-synthetic-reference-record.json"]["authorizationDecisionClaims"][0]["claim"]["authorizationDecision"]
        require(decision["metadataReviewState"] == "Recorded" and decision["currentStanding"] == "CurrentAsRecorded"
                and decision["reference"] == source_decision["reference"] and decision["decisionAsStated"] == "Approved"
                and decision["boundaryRevisionId"] == offering["currentBoundaryRevisionId"],
                "Current recorded synthetic decision does not support this exact offering boundary.")
        context = {
            "expectedOfferingRevision": offering["revision"],
            "authorizationRevisionIds": [decision["revisionId"]],
            "boundaryRevisionId": offering["currentBoundaryRevisionId"],
            "hostingScopeRevisionId": offering["currentHostingScopeRevisionId"],
            "packageVersionIds": [version_id],
        }
        by_candidate = {c["candidateId"]: c for c in candidates}
        record_ids = {r["candidateId"]: r["recordId"] for r in baseline}
        plans = []
        for published in baseline:
            if published["type"] != "Capability":
                continue
            candidate = by_candidate[published["candidateId"]]
            record = self.candidate_source(source, candidate)
            reviewed = self.state.data["operations"].get(dataset + "/review/" + candidate["candidateId"], {}).get("result")
            require(reviewed and reviewed["rationale"] and reviewed["controlDuties"] == candidate["controlDuties"],
                    "Original approved baseline review is absent or duties changed.")
            expected = {
                "classification": reviewed["classification"], "serviceCategory": reviewed["serviceCategory"],
                "contributors": sorted(record_ids[c] for c in reviewed["contributorIds"]),
                "controlDuties": {c: record["responsibility"] for c in record["controlIds"]},
            }
            require(reviewed["controlDuties"] == expected["controlDuties"],
                    "Original approved duties differ from the frozen source document.")
            capability_id = published["recordId"]
            catalog = "/api/csp/catalog/capabilities/" + capability_id
            detail = self.get(catalog)["capability"]
            require(detail["name"] == candidate["name"] and detail["description"] == candidate["description"],
                    "Canonical capability text differs from the approved source; refresh-context cannot edit content.")
            working = self.get(catalog + "/working-revision")
            require(self.same_working(working, expected), "Working content/duties differ from the original approved baseline.")
            prefix = dataset + f"/refresh-context/offering-{offering['revision']}/" + capability_id
            operation = self.state.data["operations"].get(prefix + "/draft")
            if not operation:
                require(working["revision"] == detail["releasedRevision"],
                        "Unjournaled working draft exists; do not replace or adopt another operator's revision.")
            plans.append({"capabilityId": capability_id, "catalog": catalog, "prefix": prefix,
                          "originalWorking": working, "expected": expected})
        require(len(plans) == source["expectedCandidates"]["Capability"], "Baseline capability inventory is incomplete.")
        return {"dataset": dataset, "root": root, "offeringRevision": offering["revision"],
                "context": context, "plans": plans}

    def refresh_context(self, dataset):
        plan = self.refresh_context_plan(dataset)
        root, context = plan["root"], plan["context"]

        def context_unchanged():
            current = self.get(root)
            require(current["revision"] == context["expectedOfferingRevision"]
                    and current["currentBoundaryRevisionId"] == context["boundaryRevisionId"]
                    and current["currentHostingScopeRevisionId"] == context["hostingScopeRevisionId"],
                    "Offering context changed during renewal; no stale review/publication attempted.")

        # Complete every private edit before any impact review; a later draft
        # can invalidate already-accepted reviews in the same offering.
        for item in plan["plans"]:
            prefix, catalog, expected = item["prefix"], item["catalog"], item["expected"]
            original = item["originalWorking"]
            operation = self.state.data["operations"].get(prefix + "/draft")
            before = operation["body"]["expectedRevision"] if operation else original["revision"]

            def draft_current(p=catalog, fields=expected, revision=before):
                current = self.get(p + "/working-revision")
                require(self.same_working(current, fields), "Working content changed while reconciling draft.")
                require(current["revision"] in (revision, revision + 1), "Unexpected working revision; no duplicate draft.")
                return current if current["revision"] == revision + 1 else None

            context_unchanged()
            draft = self.state.perform(
                self.api, prefix + "/draft", "PUT", catalog + "/working-revision",
                dict(expectedRevision=before, **expected), draft_current, idempotent=False)
            require(self.same_working(draft, expected) and draft["revision"] == before + 1
                    and draft["snapshotHash"] == original["snapshotHash"],
                    "Context-only draft changed approved content, duties or working hash.")
            item["draft"] = draft

        for item in plan["plans"]:
            prefix, catalog, draft = item["prefix"], item["catalog"], item["draft"]
            capability_id = item["capabilityId"]
            context_unchanged()
            published_op = self.state.data["operations"].get(prefix + "/publish")
            if published_op and published_op.get("result"):
                self.verify_refreshed_release(item, published_op["result"])
                continue
            impact_body = dict(context, changes=[{
                "kind": "Capability", "recordId": capability_id, "expectedRevision": draft["revision"],
                "proposedSnapshotHash": draft["snapshotHash"],
            }])

            def retained_impact(p=prefix):
                operation = self.state.data["operations"].get(p + "/impact")
                result = operation.get("result") if operation else None
                if result:
                    self.unexpired(self.get(root + "/impact-reviews/" + result["reviewId"]))
                    self.unexpired(result)
                    return result
                self.api.pages(root + "/impact-reviews")
                return None

            impact = self.post(prefix + "/impact", root + "/impact-previews", impact_body, retained_impact)
            require(not impact["blockers"], f"Context-renewal impact blockers: {impact['blockers']}")
            self.unexpired(impact)
            impact_path = root + "/impact-reviews/" + impact["reviewId"]

            def accepted(p=impact_path):
                current = self.get(p)
                self.unexpired(current)
                return current if current["disposition"] == "AcceptForPublication" else None

            self.post(prefix + "/impact-review", impact_path + "/review", {
                "expectedRevision": impact["revision"], "previewId": impact["previewId"],
                "previewHash": impact["previewHash"], "disposition": "AcceptForPublication",
                "rationale": f"{NOTICE}: renew current offering context only for {capability_id}; unchanged approved baseline. NOT ATO.",
            }, accepted, keyed=False)

            def retained_preview(p=prefix, c=catalog, d=draft):
                current = self.get(c + "/working-revision")
                require(current["revision"] == d["revision"] and current["snapshotHash"] == d["snapshotHash"],
                        "Working revision changed before canonical publication preview.")
                operation = self.state.data["operations"].get(p + "/preview")
                result = operation.get("result") if operation else None
                if result:
                    self.unexpired(result)
                    require(result["workingSnapshotHash"] == d["snapshotHash"]
                            and result["impactReviewIds"] == [impact["reviewId"]], "Saved preview has a different exact context.")
                return result

            preview = self.post(prefix + "/preview", catalog + "/publication-previews",
                                {"revision": draft["revision"], "impactReviewIds": [impact["reviewId"]]},
                                retained_preview, keyed=False)
            self.unexpired(preview)
            require(preview["capabilityId"] == capability_id and preview["revision"] == draft["revision"]
                    and preview["workingSnapshotHash"] == draft["snapshotHash"]
                    and preview["impactReviewIds"] == [impact["reviewId"]],
                    "Canonical preview does not bind the exact unchanged capability and accepted impact.")
            require(not any(row.get("changeKind") not in (None, "Unchanged")
                            for key in ("contributorChanges", "dutyChanges", "referenceChanges") for row in preview[key]),
                    "Canonical preview contains content/duty/reference changes; context-only renewal refused.")
            approval = {"revision": draft["revision"], "previewId": preview["previewId"], "previewHash": preview["previewHash"]}

            def approved(p=catalog):
                current = self.get(p + "/working-revision")
                require(current["revision"] == draft["revision"] and current["snapshotHash"] == draft["snapshotHash"],
                        "Working content/revision changed before approval.")
                return current if (current.get("approvedRevision") == approval["revision"]
                                   and current.get("approvedPreviewId") == approval["previewId"]
                                   and current.get("approvedPreviewHash") == approval["previewHash"]) else None

            self.post(prefix + "/approve", catalog + "/working-revision/approve", approval, approved, keyed=False)
            context_unchanged()

            def released(p=prefix, i=item):
                self.get(i["catalog"])
                self.get(i["catalog"] + "/working-revision")
                operation = self.state.data["operations"].get(p + "/publish")
                result = operation.get("result") if operation else None
                if result:
                    self.verify_refreshed_release(i, result)
                return result

            publication = self.post(prefix + "/publish", catalog + "/publish", {
                **approval, "approvedRevision": draft["revision"], "idempotencyKey": self.state.key(prefix + "/publish"),
            }, released)
            self.verify_refreshed_release(item, publication)
            self.state.remember(prefix + "/release", publication)

    def verify_refreshed_release(self, item, publication):
        working = self.get(item["catalog"] + "/working-revision")
        detail = self.get(item["catalog"])["capability"]
        require(publication["capabilityId"] == item["capabilityId"] and publication["releaseId"]
                and publication["revision"] == item["draft"]["revision"] == detail["releasedRevision"]
                and publication["snapshotHash"] == item["draft"]["snapshotHash"] == working["snapshotHash"]
                and self.same_working(working, item["expected"]),
                "Persisted context-renewal publication does not match the unchanged approved source.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--plan", action="store_true", help="GET-only inventory and proposed actions; no local writes.")
    action.add_argument("--apply", action="store_true", help="Explicitly approve this synthetic dataset and perform the plan.")
    parser.add_argument("--base-url", required=True, help="Loopback API root, e.g. http://127.0.0.1:3001")
    parser.add_argument("--state-dir", required=True, type=Path, help="Dedicated private 0700 directory outside repository.")
    parser.add_argument("--manifest-dir", type=Path, default=REPO / "demos/provider-offerings")
    parser.add_argument("--manifest-sha256", help="Required with --apply; exact manifest hash from --plan.")
    parser.add_argument("--azure-offering-id", help="Explicit permission to reuse this known Azure offering, never inferred by name.")
    parser.add_argument("--stage", choices=("all", "publish", "load-evidence", "refresh-context"), default="all",
                        help="all: complete workflow; publish: omit evidence; load-evidence: no publication; "
                             "refresh-context: renew only journaled baseline capabilities without content/metadata changes.")
    parser.add_argument("--wait-seconds", type=int, default=300)
    args = parser.parse_args(argv)
    state = None
    try:
        base = local_base(args.base_url)
        require(args.wait_seconds > 0, "--wait-seconds must be positive.")
        manifest = Manifest(args.manifest_dir)
        if args.azure_offering_id:
            import uuid
            args.azure_offering_id = str(uuid.UUID(args.azure_offering_id))
        if args.apply:
            require(args.manifest_sha256 == manifest.sha, "--apply requires --manifest-sha256 matching the verified --plan.")
        for dataset in DATASETS:
            manifest.hosting_scopes(dataset)
            claim = manifest.sources[dataset]["documents"]["04-synthetic-reference-record.json"]["authorizationDecisionClaims"][0]["claim"]["authorizationDecision"]
            require(claim["statusAsStated"] == "Approved" and "NOT ATO" in claim["decisionType"],
                    "Source decision must explicitly say Approved synthetic distribution / NOT ATO before any writes.")
        binding = {"base": base, "manifest": manifest.sha, "identity": IDENTITY}
        state = Journal(args.state_dir, binding, apply=args.apply)
        api = Api(base, Path(args.state_dir).expanduser().absolute() / "cookies.txt", apply=args.apply)
        if args.plan:
            config = api.request("GET", "/api/auth/login-config")
            identities = (config.get("simulation") or {}).get("identities", [])
            require(any(i["id"] == IDENTITY for i in identities),
                    "Plan preflight: dev-cspadmin simulation not configured; no writes performed.")
            if args.stage == "refresh-context":
                loader = Loader(api, state, manifest, args)
                plans = [loader.refresh_context_plan(dataset) for dataset in DATASETS]
                print(json.dumps({"mode": "plan: GET-only", "stage": args.stage, "manifestSha256": manifest.sha,
                                  "renewals": plans, "constraints": [
                                      "Prepare all unchanged drafts before impact reviews.",
                                      "One Capability change per impact; exact retained baseline package only.",
                                      "No source/proposal import, metadata/allocation change, SQL repair or history deletion."]},
                                 ensure_ascii=False, indent=2))
                return 0
            inventory = api.pages("/api/csp/offerings")
            explicit_azure = api.request("GET", "/api/csp/offerings/" + args.azure_offering_id) if args.azure_offering_id else None
            proposed = []
            for dataset in DATASETS:
                source = manifest.sources[dataset]
                proposed.append({"dataset": dataset, "offeringName": source["offering"],
                                 "sourceEdition": source["sourceReleaseLabel"],
                                 "existingNamedOfferings": [r["offeringId"] for r in inventory if r["name"] == source["offering"]],
                                 "explicitAzureOfferingId": args.azure_offering_id if dataset.startswith("azure-") else None,
                                 "explicitAzureRevision": explicit_azure["revision"] if explicit_azure and dataset.startswith("azure-") else None,
                                 "offeringMetadataAction": ("Align known offering name, synthetic description, environments, service model and management arrangement; preserve provider/profile/contacts."
                                                            if explicit_azure and dataset.startswith("azure-")
                                                            else "Create offering if absent; named reuse is permitted only for M365."),
                                 "hostingScopes": manifest.hosting_scopes(dataset),
                                 "reviewCandidates": source["expectedCandidates"],
                                 "publishCapabilities": source["expectedCandidates"]["Capability"] if args.stage != "load-evidence" else 0,
                                 "retainArtifacts": Loader(api, state, manifest, args).evidence_files(dataset) if args.stage != "publish" else [],
                                 "finding": "one Open Moderate source finding", "poam": "one linked Open source POAM"})
            print(json.dumps({"mode": "plan: GET-only", "manifestSha256": manifest.sha, "stage": args.stage,
                              "proposed": proposed, "savedIds": state.data["ids"],
                              "constraints": ["No missions/users/config/SQL/cleanup/sharing changes.",
                                              "No future Azure 1.3 publication.",
                                              "All human attachments remain provider-private.",
                                              "Initial boundary uses verified source fields; retained citations exist only after receipt.",
                                              "Canonical revisions are independent from source-edition labels."]},
                             ensure_ascii=False, indent=2))
            return 0
        me = api.authenticate()
        identity = {"oid": me["oid"], "directoryTenantId": me["directoryTenantId"]}
        saved_identity = state.data.get("authenticatedIdentity")
        require(saved_identity is None or saved_identity == identity, "Configured simulation identity changed since prior apply.")
        state.data["authenticatedIdentity"] = identity
        state.save()
        loader = Loader(api, state, manifest, args)
        for dataset in DATASETS:
            if args.stage == "refresh-context":
                loader.refresh_context(dataset)
            else:
                loader.run(dataset)
        print(json.dumps({"result": "Verified persisted API outcomes", "stage": args.stage,
                          "stateFile": str(state.path), "ids": state.data["ids"]}, ensure_ascii=False, indent=2))
        return 0
    except (Stop, OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
        print(f"STOP: {exc}", file=sys.stderr)
        if state:
            print(json.dumps({"stateFile": str(state.path) if state.path else None,
                              "ids": state.data["ids"],
                              "pending": [k for k, v in state.data["operations"].items() if v["status"] == "pending"]},
                             ensure_ascii=False, indent=2), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
