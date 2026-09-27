#!/usr/bin/env python3
"""Build entirely fictional, deterministic provider-review documents; no network."""
import copy
import csv
import hashlib
import io
import json
from pathlib import Path
import re
import textwrap
from xml.sax.saxutils import escape
import zipfile

ROOT = Path(__file__).resolve().parent
LABEL = "SYNTHETIC DEMONSTRATION ONLY"
DISCLAIMER = (f"{LABEL}. All services, measurements, decisions, identities and evidence "
              "in this package are fictional. Not Microsoft provider data, a DoD approval, "
              "an IL5 authorization, a customer entitlement, or operational evidence.")
STAMP = (2026, 9, 15, 12, 0, 0)
SOURCE_DOCUMENTS = [
    ("01-service-scope.json", ("components", "boundaryClaims")),
    ("02-capability-catalog.json", ("capabilities",)),
    ("03-customer-responsibilities.json", ("responsibilities",)),
    ("04-synthetic-reference-record.json", ("authorizationReferences", "authorizationDecisionClaims")),
    ("05-assessment-summary.json", ("assessmentFindings",)),
    ("06-corrective-plan.json", ("poamItems",)),
]

# key, display name, component, controls, scope, provider, shared, customer, evidence
AZURE = [
    ("audit", "Audit collection", "Central audit collector and archive", ["AU-2", "AU-6", "AU-11"],
     "Collect administrative and platform security events from the enrolled shared-service boundary; retain 90 days searchable and 365 days archived in this scenario.",
     "Operate collector health checks every 15 minutes; reconcile ingestion counts daily; protect archived events from routine operator modification.",
     "Agree event categories and triage escalation with each mission; investigate missing source intervals jointly within one business day.",
     "Enable workload audit sources, review mission-specific events each business day, and set longer retention when mission policy requires it.",
     "E01: seven-day collector reconciliation and archive retrieval sample"),
    ("network", "Network protection", "Shared hub firewall and private routing", ["SC-7"],
     "Inspect approved north-south shared-hub flows and maintain documented private routing for enrolled spokes.",
     "Maintain deny-by-default hub rules, review exceptions monthly, and record firewall changes with rollback instructions.",
     "Approve traffic purpose and route changes jointly; exercise a denied-traffic test before connecting a new spoke.",
     "Manage application ingress, workload NSGs, DNS dependencies and endpoints outside the provider hub.",
     "E02: approved rule sample, route inventory and denied-flow test"),
    ("identity", "Privileged identity", "Provider operations identity service", ["IA-2", "AC-2"],
     "Control provider operator accounts with MFA and time-limited privileged access; customer identities remain a separate administration responsibility.",
     "Require MFA for operators; review privileged membership monthly; remove provider leavers within four hours of confirmed notice.",
     "Maintain named escalation contacts and reconcile emergency access use after every activation.",
     "Approve mission accounts, enforce mission MFA, perform access reviews, and disable customer leavers.",
     "E03: synthetic access review, activation record and leaver test"),
    ("configuration", "Configuration baselines", "Shared-service configuration repository", ["CM-2", "CM-3"],
     "Version the shared-service baseline and capture approved changes; application code and mission images are excluded.",
     "Record baseline version, approver, test result and rollback plan; compare deployed shared settings weekly.",
     "Assess mission impact before changing shared defaults; agree maintenance windows and exception expiration.",
     "Maintain application baselines and approve workload changes; remediate customer-owned configuration drift.",
     "E04: baseline revision, drift review and approved change sample"),
    ("encryption", "Encryption and key handling", "Shared-service key vault and transport configuration", ["SC-12", "SC-13"],
     "Manage fictional shared-service keys and transport settings for provider-owned endpoints only.",
     "Restrict key administration, test rotation every 90 days, record custody, and enforce TLS 1.2 or higher on covered endpoints.",
     "Document key ownership and coordinate rotation windows for dependent workloads; test trust-chain changes jointly.",
     "Configure workload encryption and certificates; manage customer keys and classify data before using a covered endpoint.",
     "E05: synthetic rotation rehearsal and endpoint protocol inventory"),
    ("incident", "Incident coordination", "Provider incident coordination desk", ["IR-4", "IR-6"],
     "Coordinate incidents affecting the shared-service boundary using a fictional 24-hour on-call roster.",
     "Acknowledge severity-one tickets within 30 minutes in the scenario; preserve provider records and issue initial impact notices.",
     "Exercise incident handoffs quarterly; coordinate containment approval where provider action affects mission service.",
     "Own mission incident declarations, regulatory reporting, application containment and mission recovery decisions.",
     "E06: tabletop timeline, escalation roster and notification template"),
    ("vulnerability", "Vulnerability management", "Shared infrastructure scan and patch service", ["RA-5", "SI-2"],
     "Scan provider-maintained hosts weekly and evaluate urgent advisories daily; mission applications are excluded.",
     "Triage critical results within one business day, schedule fixes by risk, and verify remediation with a follow-up scan.",
     "Agree outage windows and compensating controls for shared infrastructure exceptions; review overdue items weekly.",
     "Scan and patch customer applications, containers and mission hosts; document mission risk acceptance separately.",
     "E07: synthetic scan sample, patch ticket and clean rescan"),
    ("backup", "Backup and recovery", "Shared configuration backup vault", ["CP-9", "CP-10"],
     "Back up shared configuration daily; exercise quarterly restore with a scenario target of 24-hour RPO and eight-hour RTO.",
     "Monitor backup jobs daily, protect recovery copies from ordinary operator deletion, and record restore results quarterly.",
     "Agree recovery sequence and validate shared dependencies during a joint recovery exercise.",
     "Back up mission data and applications, define mission RTO/RPO, and validate business transactions after recovery.",
     "E08: backup reconciliation and shared-configuration restore exercise"),
]

M365 = [
    ("exchange", "Exchange Online collaboration mail", "Demo Exchange mail and transport configuration", ["AC-2", "SC-7"],
     "Provide a fictional SaaS collaboration mail configuration for Flank Speed Demo; no Azure subscription resources are included.",
     "Maintain documented mail-flow defaults, operator access controls and service-impact notifications for the demo boundary.",
     "Review approved external mail domains and investigate delivery-policy exceptions with tenant administrators.",
     "Approve mailboxes, review distribution groups, label messages and enforce mission-specific handling restrictions.",
     "E01: synthetic mail-flow review and mailbox access sample"),
    ("teams", "Teams meetings and messaging", "Demo Teams meeting policy service", ["AC-2", "AC-4"],
     "Cover tenant-level meeting and messaging policy templates; voice carrier connectivity and endpoint configuration are excluded.",
     "Version baseline meeting policies and track changes to guest, federation and anonymous-join defaults.",
     "Review external collaboration requests and test meeting restrictions with designated customer administrators.",
     "Approve team membership, meeting organizers and guests; manage meeting content and user training.",
     "E02: synthetic meeting-policy export and guest access test"),
    ("sharepoint", "SharePoint and OneDrive content", "Demo document collaboration policy service", ["AC-3", "AC-6"],
     "Cover fictional site-sharing templates and document access policy; no customer content is imported.",
     "Maintain baseline external-sharing limits and publish tested administrative configuration guidance.",
     "Validate site exceptions and review sharing reports with customer information owners every month.",
     "Assign site owners, remove stale sharing links, classify content and approve site-level permissions.",
     "E03: synthetic site-sharing review and access revocation test"),
    ("purview", "Purview retention and audit", "Demo collaboration audit and retention service", ["AU-2", "AU-6", "AU-11"],
     "Describe configured collaboration audit categories and a fictional 365-day retention design, not a purchased license entitlement.",
     "Maintain audit export schedules and retention-policy templates; document gaps in the customer evidence handoff.",
     "Confirm licensed functionality before any real deployment and reconcile evidence delivery with customer reviewers.",
     "Select records schedules, perform legal review, investigate user activity and approve legal holds.",
     "E04: synthetic audit-delivery ledger and retention retrieval rehearsal"),
    ("identity", "Tenant access safeguards", "Demo Entra collaboration access policy", ["IA-2", "AC-2"],
     "Cover SaaS collaboration access policy templates for the demo tenant, independently of Azure shared-service operations.",
     "Version MFA and privileged-admin policy guidance and track provider support account approvals.",
     "Test emergency access procedures and review privileged roles jointly every month.",
     "Enroll users, enforce device compliance, run joiner/mover/leaver processes and approve conditional-access exceptions.",
     "E05: synthetic MFA policy review and emergency-access rehearsal"),
]

OFFERINGS = [
    dict(slug="azure-il5-shared-services", title="Azure IL5 · Shared services", release="1.2",
         model="Provider-managed shared services", environment="Azure Government (fictional scenario)",
         team="Northstar Shared Services Demo Team", email="shared-services@example.invalid",
         boundary="Only the eight listed provider-operated shared services; no mission workloads or SaaS collaboration tenant.",
         excluded="Customer application code, databases, mission data, workstation configuration, customer subscriptions not explicitly enrolled, Microsoft 365, and any actual IL5 approval.",
         hostingScopes=[dict(kind="Azure", cloud="AzureUSGovernment",
                             directoryTenantId="07900000-0000-0000-0000-000000000101",
                             subscriptionId="07900000-0000-0000-0000-000000000102",
                             resourceId="/subscriptions/07900000-0000-0000-0000-000000000102/resourceGroups/rg-synthetic-il5-shared-services")],
         rows=AZURE, audit="audit", evidence="E01", prefix="AZ"),
    dict(slug="microsoft-365-collaboration", title="Microsoft 365 · Collaboration", release="1.0",
         model="SaaS collaboration", environment="Flank Speed Demo (fictional tenant; separate service boundary)",
         team="Harbor Collaboration Demo Team", email="collaboration@example.invalid",
         boundary="Only the five listed SaaS collaboration policy services; independent of Azure shared infrastructure and its customer allocations.",
         excluded="Azure hubs and subscriptions, mission applications, endpoint compliance operations, telephony carriers, third-party apps, real tenant entitlements, and actual Flank Speed authorization.",
         hostingScopes=[dict(kind="Service", serviceId="synthetic-m365-collaboration-001",
                             serviceName="Flank Speed Demo - Collaboration (SYNTHETIC)",
                             environment="Microsoft365DoD", tenantReference="synthetic-flank-speed-demo-tenant")],
         rows=M365, audit="purview", evidence="E04", prefix="M365"),
]


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        stream.write(text)


def json_write(path, value):
    write(path, json.dumps(value, ensure_ascii=False, indent=2) + "\n")


def source_documents(folder, data):
    files = {}
    for name, collections in SOURCE_DOCUMENTS:
        path = folder / "sources" / name
        json_write(path, {collection: data[collection] for collection in collections})
        files[name] = path.read_bytes()
    zip_write(folder / "review-source.zip", files)
    legacy = folder / "review-source.json"
    if legacy.exists():
        legacy.unlink()


def zip_write(path, files):
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, content in sorted(files.items()):
            info = zipfile.ZipInfo(name, STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, content)


def paragraphs(markdown):
    return [(len(line) - len(line.lstrip("#")), line.lstrip("# ").replace("**", ""))
            for line in markdown.splitlines() if line.strip()]


def docx_bytes(markdown):
    body = []
    for level, text in paragraphs(markdown):
        style = f'<w:pStyle w:val="Heading{min(level, 2)}"/>' if level else ""
        body.append(f'<w:p><w:pPr>{style}<w:spacing w:after="140"/></w:pPr>'
                    f'<w:r><w:t xml:space="preserve">{escape(text)}</w:t></w:r></w:p>')
    body.append('<w:sectPr><w:footerReference w:type="default" r:id="rId2"/>'
                '<w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="900" w:right="1000" '
                'w:bottom="1000" w:left="1000" w:header="400" w:footer="500"/></w:sectPr>')
    ns = 'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"'
    relns = 'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"'
    content = {
        "[Content_Types].xml": '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
        '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>'
        '<Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/></Types>',
        "_rels/.rels": '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>',
        "word/_rels/document.xml.rels": '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>'
        '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/></Relationships>',
        "word/document.xml": f'<w:document {ns} {relns}><w:body>{"".join(body)}</w:body></w:document>',
        "word/styles.xml": f'<w:styles {ns}><w:docDefaults><w:rPrDefault><w:rPr>'
        '<w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="22"/><w:color w:val="24364B"/>'
        '</w:rPr></w:rPrDefault></w:docDefaults>'
        '<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/>'
        '<w:pPr><w:keepNext/><w:spacing w:before="240"/></w:pPr><w:rPr><w:b/><w:sz w:val="36"/><w:color w:val="17365D"/></w:rPr></w:style>'
        '<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/>'
        '<w:pPr><w:keepNext/><w:spacing w:before="180"/></w:pPr><w:rPr><w:b/><w:sz w:val="27"/><w:color w:val="007D8A"/></w:rPr></w:style></w:styles>',
        "word/footer1.xml": f'<w:ftr {ns}><w:p><w:r><w:rPr><w:sz w:val="16"/><w:color w:val="9B3737"/></w:rPr>'
        f'<w:t>{LABEL} | Fictional provider review package</w:t></w:r></w:p></w:ftr>',
    }
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, text in sorted(content.items()):
            info = zipfile.ZipInfo(name, STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, '<?xml version="1.0" encoding="UTF-8"?>' + text)
    return stream.getvalue()


def pdf_bytes(markdown):
    """Small unencrypted text PDF with selectable text, headings and repeated banner."""
    pages, lines, y = [], [], 730
    for level, text in paragraphs(markdown):
        size = 17 if level == 1 else 12 if level else 10
        wrapped = textwrap.wrap(text, width=57 if level == 1 else 83 if level else 93,
                                break_long_words=True, break_on_hyphens=False)
        required = len(wrapped) * (size + 4) + 7
        if y - required < 60:
            pages.append(lines)
            lines, y = [], 730
        for line in wrapped:
            lines.append((y, size, bool(level), line))
            y -= size + 4
        y -= 7
    if lines:
        pages.append(lines)
    objects = [b"", b"", b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
               b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"]

    def literal(text):
        return text.encode("cp1252", errors="replace").replace(b"\\", b"\\\\").replace(b"(", b"\\(").replace(b")", b"\\)")

    page_ids = []
    for number, page in enumerate(pages, 1):
        commands = [b"1 1 1 rg 0 0 612 792 re f", b"0.08 0.19 0.31 rg 0 756 612 36 re f",
                    b"BT /F2 10 Tf 1 1 1 rg 48 770 Td (" + literal(LABEL) + b") Tj ET"]
        for baseline, size, bold, text in page:
            commands.append(f"BT /F{2 if bold else 1} {size} Tf 0.08 0.19 0.31 rg 48 {baseline} Td (".encode()
                            + literal(text) + b") Tj ET")
        commands.append(b"BT /F1 8 Tf 0.4 0.4 0.4 rg 48 30 Td (Fictional provider package | Page "
                        + str(number).encode() + b" of " + str(len(pages)).encode() + b") Tj ET")
        stream = b"\n".join(commands)
        page_id, stream_id = len(objects) + 1, len(objects) + 2
        page_ids.append(page_id)
        objects.append(f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {stream_id} 0 R >>".encode())
        objects.append(b"<< /Length " + str(len(stream)).encode() + b" >>\nstream\n" + stream + b"\nendstream")
    objects[0] = b"<< /Type /Catalog /Pages 2 0 R >>"
    objects[1] = f"<< /Type /Pages /Count {len(page_ids)} /Kids [{' '.join(f'{i} 0 R' for i in page_ids)}] >>".encode()
    output, offsets = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n"), [0]
    for i, obj in enumerate(objects, 1):
        offsets.append(len(output))
        output.extend(f"{i} 0 obj\n".encode() + obj + b"\nendobj\n")
    start = len(output)
    output.extend(f"xref\n0 {len(objects)+1}\n0000000000 65535 f \n".encode())
    for offset in offsets[1:]:
        output.extend(f"{offset:010d} 00000 n \n".encode())
    output.extend(f"trailer\n<< /Size {len(objects)+1} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n".encode())
    return bytes(output)


def document(folder, name, text):
    write(folder / f"{name}.md", text)
    (folder / f"{name}.docx").write_bytes(docx_bytes(text))
    (folder / f"{name}.pdf").write_bytes(pdf_bytes(text))


def csv_write(path, headers, rows):
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(["demonstration_notice", *headers])
        writer.writerows([[LABEL, *row] for row in rows])


def inventory(offering):
    o, prefix = offering, offering["prefix"]
    data = {key: [] for key in ("components", "capabilities", "responsibilities", "authorizationReferences",
                               "authorizationDecisionClaims", "boundaryClaims", "assessmentFindings", "poamItems")}
    for key, name, component, controls, scope, provider, shared, customer, evidence in o["rows"]:
        component_id, capability_id = f"{prefix}-component-{key}", f"{prefix}-capability-{key}"
        data["components"].append(dict(id=component_id, type="service", name=component,
                                       description=f"{LABEL}. {scope}"))
        data["capabilities"].append(dict(id=capability_id, name=name, description=f"{LABEL}. {scope}",
                                         componentIds=[component_id], controlIds=controls, responsibility="Shared"))
        for duty, text in (("Provider", provider), ("Shared", shared), ("Customer", customer)):
            data["responsibilities"].append(dict(id=f"{capability_id}-{duty.lower()}", name=f"{name} — {duty} duties",
                                                 description=f"{LABEL}. {text} Evidence: {prefix}-{evidence}.",
                                                 capabilityId=capability_id, responsibility=duty))
    reference_id, finding_id = f"{prefix}-REF-001", f"{prefix}-F-001"
    data["authorizationReferences"] = [dict(
        id=reference_id, name=f"SYNTHETIC {o['title']} demo release reference",
        reference=f"{LABEL}; {reference_id}; fictional service-release review record, NOT ATO or approval to operate; only {o['title']}; customer duties and logging evidence-delay plan remain required.",
        issuer=f"SYNTHETIC {o['team']} Review Board", issuedAt="2026-09-01", expiresAt="2027-08-31",
        description=f"{LABEL}. Source title: {o['title']} Fictional Service Release Review. Type: demonstration reference record. Dates apply only to this scenario.")]
    data["authorizationDecisionClaims"] = [dict(id=f"{prefix}-DECISION-001", name="Synthetic demonstration distribution decision",
        claim=dict(authorizationDecision=dict(
            subjectKind="Provider", subject=f"{LABEL}: {o['title']}", reference=reference_id,
            authority=f"SYNTHETIC {o['team']} Review Board", decisionType="Fictional demonstration distribution approval; NOT ATO",
            statusAsStated="Approved", decisionDate="2026-09-01", expirationDate="2027-08-31",
            scope=o["boundary"], conditions=["Customer must complete its own authorization and allocation checks.",
                "Retain the open moderate logging evidence-delay finding and corrective milestones.",
                "PRIVATE working papers are not cleared for customer distribution."],
            exclusions=[o["excluded"]]), qualifications=[DISCLAIMER]))]
    data["boundaryClaims"] = [dict(id=f"{prefix}-BOUNDARY-001", name=f"{o['title']} synthetic boundary",
        claim=dict(boundary=dict(subject=o["title"], relationship="Included", scope=o["boundary"] + " " + hosting_quote(o),
            environment=scope_environment(o),
            resourceIds=[scope.get("resourceId", scope.get("serviceId")) for scope in o["hostingScopes"]],
            responsibilities=["Provider owns only listed shared/SaaS operations.",
                "Customer owns mission authorization, information handling and the customer duties in the matrix."],
            decisionReference=reference_id), qualifications=[DISCLAIMER],
            relationships=[dict(kind="BoundaryComponent", targetSourceId=c["id"]) for c in data["components"]]))]
    observation = ("In the fictional 2026-08-18 through 2026-08-24 seven-day sample, two of seven daily "
                   "customer evidence bundles arrived 31 and 38 hours after period close against a 24-hour target. "
                   "All 700 expected synthetic events remained present in the source ledger; no event loss was observed "
                   "in this invented sample. A single daily export worker and missing delivery-age alert delayed the handoff. "
                   "The sample does not establish production effectiveness.")
    data["assessmentFindings"] = [dict(id=finding_id, name="Moderate logging evidence collection delay",
        claim=dict(assessmentFinding=dict(sourceFindingId=finding_id, observation=observation,
            severityAsStated="Moderate", statusAsStated="Open", assessor=f"SYNTHETIC {o['team']} Assurance Cell",
            assessmentDate="2026-08-28", controlIds=["AU-6", "AU-11"],
            evidenceReferences=[f"{prefix}-{o['evidence']}", f"{prefix}-PRIVATE-WP-001"]),
            qualifications=[DISCLAIMER, "Open corrective action is not risk acceptance or finding closure."],
            relationships=[dict(kind="FindingCapability", targetSourceId=f"{prefix}-capability-{o['audit']}")]))]
    data["poamItems"] = [dict(id=f"{prefix}-POAM-001", name="Logging evidence handoff corrective plan",
        claim=dict(poamItem=dict(sourcePoamId=f"{prefix}-POAM-001",
            correctiveAction="Add a second scheduled export attempt, retry failed deliveries, alert on evidence age at 18 hours, and verify 14 consecutive days of bundles delivered within 24 hours. No finding closure is asserted.",
            ownerAsStated=f"SYNTHETIC {o['team']} Evidence Lead <{o['email']}>", statusAsStated="Planned; not completed",
            milestones=[dict(description="Review export schedule and failure-retry design", dueDate="2026-09-18"),
                        dict(description="Exercise retry and 18-hour delivery-age alert", dueDate="2026-09-25"),
                        dict(description="Complete 14-day delivery validation and independent review", dueDate="2026-10-09")],
            requiredClosureEvidence=["Fourteen-day timestamped delivery ledger meeting the 24-hour target",
                "Failed-delivery replay and alert exercise results", "Independent synthetic reviewer sign-off"],
            submittedEvidenceReferences=[]), qualifications=[DISCLAIMER],
            relationships=[dict(kind="PoamFinding", targetSourceId=finding_id)]))]
    return data


def heading(o, title):
    return f"# {o['title']}\n## {title}\n\n{DISCLAIMER}\n\nRelease {o['release']} | Scenario date: 2026-09-15 | {o['model']}\n\n"


def scope_environment(o):
    scope = o["hostingScopes"][0]
    return scope.get("cloud", scope.get("environment"))


def hosting_quote(o):
    return f"{LABEL}. Fictional hosting identity; " + "; ".join(
        f"{key}={value}" for scope in o["hostingScopes"] for key, value in scope.items()
    ) + ". No live resource, tenant entitlement, or connector is asserted."


def generate_offering(o):
    folder = ROOT / o["slug"] / f"release-{o['release']}"
    folder.mkdir(parents=True, exist_ok=True)
    data = inventory(o)
    source_documents(folder, data)
    service = heading(o, "Service guide and customer onboarding")
    service += f"## Service boundary\n\n{o['boundary']}\n\nEnvironment: {o['environment']}.\n\n## Exclusions\n\n{o['excluded']}\n\n"
    service += "## Synthetic hosting identity\n\n" + hosting_quote(o) + "\n\n"
    service += (f"## Operating team\n\n{o['team']} | {o['email']}. This address is intentionally non-routable.\n\n"
                "## Customer onboarding and acceptance\n\n"
                "1. Identify a fictional mission information owner and select only applicable capabilities.\n\n"
                "2. Review the boundary, customer duties, reference conditions, and disclosed moderate finding.\n\n"
                "3. Confirm the application records a reviewed provider release and a separate customer association; importing a file creates neither entitlement nor authority.\n\n"
                "4. Record customer-owned controls and evidence tasks before any inheritance decision.\n\n"
                "5. Obtain mission-level review through the normal authorization process. No synthetic reference substitutes for it.\n\n"
                "## Service change and exit\n\n"
                "The team provides a fictional ten-business-day notice for planned customer-impacting changes. Emergency changes require a next-business-day review. "
                "On exit, agree evidence export scope, revoke demo associations, and document customer retention obligations; this package does not implement those actions.\n\n")
    for key, name, component, controls, scope, provider, shared, customer, evidence in o["rows"]:
        service += (f"## {name}\n\nComponent: {o['prefix']}-component-{key} — {component}.\n\n"
                    f"Coverage: {', '.join(controls)}. This is partial control support, not full control satisfaction.\n\n"
                    f"{scope}\n\nProvider: {provider}\n\nShared: {shared}\n\nCustomer: {customer}\n\n"
                    f"Review evidence: {o['prefix']}-{evidence}.\n\n")
    document(folder, "service-guide", service)
    reference = data["authorizationReferences"][0]
    decision = data["authorizationDecisionClaims"][0]["claim"]["authorizationDecision"]
    record = heading(o, "Synthetic decision and reference record")
    record += (f"## Source identification\n\nSource title: {o['title']} Fictional Service Release Review.\n\n"
               f"Issuer: {reference['issuer']}.\n\nType: invented demonstration distribution decision and reference; NOT an ATO, P-ATO, accreditation, or DoD memo.\n\n"
               f"Reference ID: {reference['id']}.\n\nIssued: 2026-09-01. Scenario expiration: 2027-08-31. Scenario review: 2026-12-01.\n\n"
               "Status as stated: Approved. Decision type: fictional demonstration distribution approval; NOT ATO. "
               "Approved ONLY for fictional customer demonstration by the synthetic review board. No real signature or official seal exists.\n\n"
               f"## Scope and exclusions\n\n{o['boundary']}\n\nExcluded: {o['excluded']}\n\n## Conditions\n\n")
    record += "\n\n".join(f"- {value}" for value in decision["conditions"])
    record += "\n\n## Evidence basis\n\nThe reference cites the customer assessment summary, responsibility matrix and synthetic evidence index. Underlying samples are constructed examples, not observed provider operations.\n"
    document(folder, "synthetic-reference-record", record)
    matrix = heading(o, "Customer responsibility matrix")
    matrix_rows = []
    for key, name, component, controls, scope, provider, shared, customer, evidence in o["rows"]:
        matrix += (f"## {name} | {', '.join(controls)}\n\n"
                   f"Provider accountable: {provider}\n\nJoint handoff: {shared}\n\nCustomer accountable: {customer}\n\n"
                   f"Acceptance artifact: {o['prefix']}-{evidence}. Review cadence: monthly and after material change. "
                   "Customer sign-off is required before claiming applicability; an unresolved customer duty remains customer-owned.\n\n")
        for control in controls:
            matrix_rows.append([f"{o['prefix']}-capability-{key}", f"{o['prefix']}-component-{key}", name, control,
                                provider, shared, customer, f"{o['prefix']}-{evidence}", "Monthly and on material change", "Customer information owner"])
    document(folder, "customer-responsibility-matrix", matrix)
    csv_write(folder / "customer-responsibility-matrix.csv",
              ["capability_id", "component_id", "capability", "control_id", "provider_duty", "shared_duty", "customer_duty",
               "evidence_reference", "review_cadence", "customer_acceptance_owner"], matrix_rows)
    finding = data["assessmentFindings"][0]["claim"]["assessmentFinding"]
    summary = heading(o, "Customer-facing assessment summary — approved for demo distribution")
    summary += (f"## Distribution decision\n\nSYNTHETIC {o['team']} Review Board approved this redacted summary on 2026-09-01 for demonstration distribution only. "
                "No real independent assessment or provider attestation is represented.\n\n"
                "## Method and sampling limits\n\nThe fictional assurance cell examined a constructed seven-day record set, walked through operator/customer duties, "
                "and reviewed one configuration and one handoff example per capability. No live environment, customer information, connector or cloud account was accessed. "
                "Control IDs describe intended coverage only; samples do not prove operating effectiveness.\n\n"
                f"## Scope examined\n\n{o['boundary']}\n\n"
                f"## Open moderate finding: {finding['sourceFindingId']}\n\n{finding['observation']}\n\n"
                "Customer impact: reviewers may not receive timely evidence for daily investigation. Source event availability and customer-side audit review remain separate obligations.\n\n"
                "Interim action: the fictional evidence lead reviews delivery age each business day and notifies the customer of overdue bundles. This is an interim check, not closure.\n\n"
                "## Corrective plan\n\n2026-09-18: review export and retry design. 2026-09-25: exercise replay and the 18-hour age alert. "
                "2026-10-09: assess a 14-day validation run against the 24-hour delivery target. All dates are planned in the scenario; none asserts work completed.\n\n"
                "Closure requires a complete timestamped ledger, failed-delivery replay, alert test and independent synthetic reviewer sign-off. "
                "Residual risk remains open. Release availability does not mean every finding is closed.\n\n"
                "## Distribution exclusions\n\nThe PRIVATE working paper contains row-level invented timestamps and reviewer notes. "
                "It is not approved for customer distribution; no genuine sensitive or personal information is present.\n")
    document(folder, "customer-assessment-summary", summary)
    private = heading(o, "PRIVATE detailed assessment working paper")
    private += (f"PRIVATE | SYNTHETIC INTERNAL DEMO ONLY | NOT REAL CUI | {o['prefix']}-PRIVATE-WP-001\n\n"
                "Do not include this attachment in customer-facing exports. Its PRIVATE label exercises document-handling choices, not a claim of real sensitivity.\n\n"
                "## Synthetic sample ledger\n\nEach period contains 100 invented events; expected and retained totals are 700. "
                "Bundle delay is measured from 00:00 UTC at the end of the period to delivery.\n\n")
    for day, hours in enumerate([12, 13, 31, 11, 38, 14, 12], 18):
        private += f"- 2026-08-{day:02d}: 100 expected / 100 retained; delivery lag {hours}h; {'LATE' if hours > 24 else 'within target'}.\n\n"
    private += ("## Reviewer analysis\n\nTwo of seven bundles exceeded the target (28.6%). The invented failure sequence was a scheduled export failure "
                "followed by the next day's retry, with no delivery-age notification. No inference about real provider reliability is valid.\n\n"
                "## Corrective test script\n\nCreate a synthetic delivery failure; verify the secondary attempt preserves event IDs; trigger an 18-hour age alert; "
                "verify escalation contacts acknowledge it; reconcile 14 daily bundles and demonstrate each arrives within 24 hours. "
                "Do not disable controls or inject faults into a real service for this demonstration.\n\n"
                "## Review notes\n\nKeep the finding Open while milestones are planned. The 1.3 scenario improves documentation but does not itself close the finding. "
                "No real assessor name, signature, tenant identifier or security configuration is included.\n")
    document(folder, "PRIVATE-assessment-working-paper", private)
    evidence_rows = []
    evidence_doc = heading(o, "Evidence index")
    for key, name, component, controls, scope, provider, shared, customer, evidence in o["rows"]:
        eid, description = evidence.split(": ", 1)
        evidence_id = f"{o['prefix']}-{eid}"
        evidence_rows.append([evidence_id, name, ";".join(controls), description, "Constructed narrative example; not operational evidence",
                              "2026-08-18/2026-08-24", o["team"], "CUSTOMER-DEMO", "service-guide.md", f"## {name}"])
        evidence_doc += (f"## {evidence_id} — {name}\n\nScope: {description}. Controls: {', '.join(controls)}.\n\n"
                         f"Location: service-guide.md, heading '{name}'. This is a narrative specimen describing expected review material, not an attached real export. "
                         f"Owner: {o['team']}. Sampling window: 2026-08-18 to 2026-08-24. Distribution: CUSTOMER-DEMO.\n\n")
    evidence_rows.append([f"{o['prefix']}-PRIVATE-WP-001", "Logging delay working paper", "AU-6;AU-11",
                          "Seven-row invented timing ledger", "Constructed numeric example", "2026-08-18/2026-08-24",
                          o["team"], "PRIVATE", "PRIVATE-assessment-working-paper.md", "## Synthetic sample ledger"])
    evidence_doc += (f"## {o['prefix']}-PRIVATE-WP-001 — restricted working paper\n\n"
                     "Controls: AU-6, AU-11. Seven-row invented timing ledger. PRIVATE; unavailable in the customer download. "
                     "The customer assessment summary preserves the finding and corrective plan without the detailed working paper.\n")
    document(folder, "evidence-index", evidence_doc)
    csv_write(folder / "evidence-index.csv", ["evidence_id", "capability", "control_ids", "description", "evidence_nature",
              "sample_period", "owner", "distribution", "source_file", "citation"], evidence_rows)
    release = heading(o, "Release notes")
    release += f"## Release {o['release']} — available baseline scenario\n\nEffective scenario date: 2026-09-01. {len(o['rows'])} capabilities. All documents are readable and unencrypted. "
    release += "Availability is a scripted demo state that must be established through review and publication in the application; these files do not publish it.\n\n"
    if o["prefix"] == "AZ":
        release += ("## Release 1.3 — proposed; two updates\n\n"
                    "1. Audit collection: adds explicit 18-hour delivery-age alerting and a secondary export attempt, retaining the same event scope, controls and component identity. "
                    "Finding closure still requires the 14-day validation run.\n\n"
                    "2. Backup and recovery: clarifies quarterly restore evidence must include dependency ordering and a customer witness acknowledgment; the 24-hour RPO and eight-hour RTO remain unchanged.\n\n"
                    "No new capability, control, component, entitlement or authorization is introduced. Baseline 1.2 remains available until reviewed 1.3 is explicitly published. "
                    "Customer adoption is a separate decision; do not silently advance existing consumers.\n\n"
                    "The proposed JSON differs in exactly two capability descriptions. Stable source IDs and the unchanged reference date allow a reviewable comparison.\n")
    else:
        release += ("## Release 1.0 initial scope\n\nIntroduces Exchange, Teams, SharePoint/OneDrive, Purview and tenant access safeguards. "
                    "This is an independent SaaS release, not Azure 1.2 or 1.3. No Azure resource allocation conveys a collaboration license. "
                    "The open moderate Purview evidence handoff finding is carried forward with the stated corrective plan.\n")
    document(folder, "release-notes", release)
    customer_files = {path.name: path.read_bytes() for path in folder.iterdir()
                      if path.suffix in {".md", ".pdf", ".docx", ".csv"} and not path.name.startswith("PRIVATE-")}
    zip_write(folder / "customer-bundle.zip", customer_files)
    return folder, data


def counts(data):
    capabilities = data.get("capabilities", [])
    return dict(Component=len(data.get("components", [])), Capability=len(capabilities),
                ControlMapping=sum(len(c["controlIds"]) for c in capabilities),
                Responsibility=len(data.get("responsibilities", [])) + len(capabilities),
                AuthorizationReference=len(data.get("authorizationReferences", [])),
                AuthorizationDecisionClaim=len(data.get("authorizationDecisionClaims", [])),
                BoundaryClaim=len(data.get("boundaryClaims", [])),
                AssessmentFinding=len(data.get("assessmentFindings", [])), PoamItem=len(data.get("poamItems", [])))


def main():
    records = []
    for offering in OFFERINGS:
        folder, data = generate_offering(offering)
        records.append((offering, folder, data))
    offering, baseline, original = records[0]
    proposed = copy.deepcopy(original)
    updates = {
        "AZ-capability-audit": " Proposed 1.3: add an 18-hour evidence delivery-age alert and secondary export attempt; 14-day validation is still required before finding closure.",
        "AZ-capability-backup": " Proposed 1.3: quarterly restore evidence includes dependency ordering and a customer witness acknowledgment; RPO and RTO targets are unchanged.",
    }
    for capability in proposed["capabilities"]:
        capability["description"] += updates.get(capability["id"], "")
    proposed_folder = baseline.parent / "release-1.3-proposed"
    source_documents(proposed_folder, proposed)
    note = heading({**offering, "release": "1.3 PROPOSED"}, "Proposed two-update review")
    note += "This is a full replacement six-document review inventory, NOT an incremental patch. Compare with release-1.2/sources/ using stable source IDs.\n\n"
    for identity, update in updates.items():
        note += f"## {identity}\n\n{update.strip()}\n\n"
    note += ("Unchanged baseline documents remain in release-1.2; the two proposed descriptions supplement their scope, not their publication status. "
             "Do not present these changes as operating commitments until review and publication. PRIVATE working paper remains internal. "
             "The moderate finding stays Open and the corrective plan stays Planned.\n")
    document(proposed_folder, "proposed-release-notes", note)
    records.append(({**offering, "release": "1.3 proposed"}, proposed_folder, proposed))
    manifest = dict(demonstrationNotice=DISCLAIMER, scenarioDate="2026-09-15", format="native-structured-inventory-not-oscal",
                    hashAlgorithm="SHA-256", files=[], sources=[])
    catalog = "# Provider offering catalog\n\n" + DISCLAIMER + "\n\n"
    for o, folder, data in records:
        relative = folder.relative_to(ROOT).as_posix()
        expected = counts(data)
        catalog += (f"## {o['title']} — {o['release']}\n\n"
                    f"Directory: `{relative}/`\n\nParser input: `{relative}/review-source.zip` with exactly six JSON documents; readable originals in `sources/`.\n\n"
                    f"Expected native candidates: **{sum(expected.values())}** total; "
                    + ", ".join(f"{number} {kind}" for kind, number in expected.items()) + ".\n\n")
        manifest["sources"].append(dict(offering=o["title"], sourceReleaseLabel=o["release"], importZip=f"{relative}/review-source.zip",
                                        environments=[scope_environment(o)], hostingScopes=o["hostingScopes"],
                                        hostingScopeCitations=[dict(
                                            sourceFile=f"{relative}/sources/01-service-scope.json",
                                            locator="$/boundaryClaims[0]",
                                            quote=hosting_quote(o))],
                                        sourceDocumentCount=6,
                                        expectedEntryCountIncludingContainer=7, expectedSemanticModelCalls=0,
                                        sourceFiles=[f"{relative}/sources/{name}" for name, _ in SOURCE_DOCUMENTS],
                                        sourceDocuments=[dict(archivePath=name,
                                            expectedCandidates=counts({collection: data[collection] for collection in collections}),
                                            expectedAnalysisComplete=True)
                                            for name, collections in SOURCE_DOCUMENTS],
                                        expectedCandidates=expected, declarations=[]))
        for collection, declarations in data.items():
            source_name = next(name for name, collections in SOURCE_DOCUMENTS if collection in collections)
            source_file = f"{relative}/sources/{source_name}"
            for index, declaration in enumerate(declarations):
                controls = declaration.get("controlIds", [])
                if collection == "components":
                    controls = sorted({control for capability in data["capabilities"]
                                       if declaration["id"] in capability["componentIds"]
                                       for control in capability["controlIds"]})
                if collection == "responsibilities":
                    controls = next(c["controlIds"] for c in data["capabilities"] if c["id"] == declaration["capabilityId"])
                if collection == "assessmentFindings":
                    controls = declaration["claim"]["assessmentFinding"]["controlIds"]
                if collection == "poamItems":
                    controls = data["assessmentFindings"][0]["claim"]["assessmentFinding"]["controlIds"]
                if collection == "boundaryClaims":
                    controls = sorted({control for capability in data["capabilities"] for control in capability["controlIds"]})
                manifest["sources"][-1]["declarations"].append(dict(
                    sourceId=declaration["id"], kind=collection, locator=f"$/{collection}[{index}]",
                    controlCoverage=controls, description=declaration.get("name"),
                    sourceFile=source_file, archivePath=source_name,
                    sourceQuote=json.dumps(declaration, ensure_ascii=False, indent=2).replace("\n", "\n    "),
                    companionDocument=f"{baseline.relative_to(ROOT).as_posix() if o['release'] == '1.3 proposed' else relative}/"
                    + ("PRIVATE-assessment-working-paper.md" if collection == "assessmentFindings" else
                       "customer-assessment-summary.md" if collection == "poamItems" else
                       "synthetic-reference-record.md" if collection in {"authorizationReferences", "authorizationDecisionClaims"} else
                       "customer-responsibility-matrix.md" if collection == "responsibilities" else "service-guide.md")))
    catalog += """## Files in each baseline

- `service-guide.{md,docx,pdf}` — scope, exclusions, component coverage and onboarding.
- `synthetic-reference-record.{md,docx,pdf}` — fictional issuer, type, dates and conditions.
- `customer-responsibility-matrix.{md,docx,pdf,csv}` — per-control provider/shared/customer duties.
- `customer-assessment-summary.{md,docx,pdf}` — demo-approved summary, moderate finding and corrective plan.
- `PRIVATE-assessment-working-paper.{md,docx,pdf}` — one logical internal attachment in three formats; seven-row ledger.
- `evidence-index.{md,docx,pdf,csv}` — constructed evidence descriptions, citations and distribution handling.
- `release-notes.{md,docx,pdf}` — baseline status and release scenario.
- `customer-bundle.zip` — the six customer documents and two CSVs; excludes PRIVATE working paper and internal manifests.
- `review-source.zip` — canonical deterministic analyzer input containing exactly six JSON documents.
- `sources/*.json` — readable originals for all six import documents.

## Six-source import layout

1. `01-service-scope.json`: `components`, `boundaryClaims`.
2. `02-capability-catalog.json`: `capabilities` with supported `componentIds`, `controlIds`, and `responsibility`.
3. `03-customer-responsibilities.json`: `responsibilities` with supported `capabilityId` dependencies and explicit duties.
4. `04-synthetic-reference-record.json`: `authorizationReferences`, `authorizationDecisionClaims`.
5. `05-assessment-summary.json`: `assessmentFindings`.
6. `06-corrective-plan.json`: `poamItems`.

All six are recognized native structured documents, not unknown JSON metadata
masquerading as analyzed text. Titles, descriptions and typed claim qualifications
identify the synthetic nature. There are no PDF, DOCX, CSV, README, manifest,
encrypted entry, or private working-paper documents in the import ZIP.

## Walkthrough for the real application operator

1. Create the Azure shared-services offering with its own provider-managed boundary;
   create Microsoft 365 as an independent SaaS offering. Never reuse Azure resource
   allocations as SaaS entitlement.
   Manifest `hostingScopes` and `environments` provide explicitly fictional
   document-backed identities. Azure uses `AzureUSGovernment`; collaboration uses
   a `Service` relationship in `Microsoft365DoD`. Source document 01 and the service
   guide cite the exact identities. They are not permission to contact any cloud.
2. Upload each baseline `review-source.zip` (exactly six JSON source documents).
   Verify the native candidate counts above, source citations, dependencies,
   and finding-to-corrective-plan relationship before accepting any declaration.
3. Native `components` create service components. `capabilities` reference their
   component IDs and list `controlIds`; each emits one capability, one mapping
   per control and one Shared responsibility. `responsibilities` adds three
   separately worded Provider/Shared/Customer duties per capability.
4. `authorizationReferences` uses only `id`, `name`, `reference`, `issuer`,
   `issuedAt`, `expiresAt`, `description`. The synthetic reference must not be
   represented as a real ATO. Typed claims use `id`, `name`, `claim` with the
   corresponding lower-camel-case claim section. The analyzer binds source fields.
5. Review the synthetic distribution-decision and boundary claims, the Open
   Moderate assessment finding, and Planned POA&M item. None changes authority
   or finding workflow state merely by being parsed. Required closure evidence
   has not been submitted.
6. Attach the customer documents as approved-demo distribution materials.
   Attach ONE preferred format of the private working paper and set actual
   application visibility to PRIVATE. Do not upload internal manifests as sources.
7. Publish/review baseline versions via the application's normal controls to
   establish the requested available 1.2 and 1.0 scenario. File generation alone
   does not establish application release status. Labels 1.2, 1.3 and 1.0 are
   source-document editions only; the canonical publication revision may be an
   integer. Never invent a semantic-version API or equate edition labels with
   persisted revision numbers.
8. Upload Azure's full 1.3 proposed inventory as a separate review revision.
   Compare stable IDs: only audit and backup descriptions change. Keep 1.2
   available; do not publish or auto-adopt 1.3 merely to demonstrate a proposal.
9. Manually open the PDFs and DOCX files. Inspect source citations in the real
   review UI and confirm customer downloads exclude the private attachment.

## Audit and limitations

`internal-manifest.json` records document SHA-256 digests, source locators, control
coverage, expected native counts and companion-document locations. It intentionally
does not hash itself. Stable source IDs are fictional and are not live entity IDs.
Candidate keys are generated by the analyzer; do not substitute source IDs for
runtime candidate keys in API calls.

The generator and validator are local standard-library utilities. Candidate counts
are derived from the inspected C# native parser, not a claim that the real API was
run. Prose/PDF extraction can generate additional proposals; these companions are
not part of the deterministic six-file review ZIP. None of these artifacts is an
OSCAL conformance claim. No document asserts a real connector, license, authorization
decision, provider assessment result, or government endorsement.
"""
    write(ROOT / "CATALOG.md", catalog)
    for path in sorted(ROOT.rglob("*")):
        if path.is_file() and path.name != "internal-manifest.json" and path.suffix in {".json", ".zip", ".md", ".csv", ".pdf", ".docx", ".py"}:
            content = path.read_bytes()
            manifest["files"].append(dict(path=path.relative_to(ROOT).as_posix(), bytes=len(content),
                                          sha256=hashlib.sha256(content).hexdigest(),
                                          distribution="PRIVATE" if path.name.startswith("PRIVATE-") else
                                          "INTERNAL" if path.name in {"build_demo.py", "validate_demo.py", "CATALOG.md", "README.md"} else
                                          "REVIEW-ONLY" if path.name.startswith("review-source") or path.parent.name == "sources" else "CUSTOMER-DEMO"))
    json_write(ROOT / "internal-manifest.json", manifest)
    print(f"Generated {len(manifest['files'])} auditable files plus internal-manifest.json under {ROOT}")


if __name__ == "__main__":
    main()
