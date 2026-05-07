#!/usr/bin/env python3
"""
Generates wizard quickstart fixtures (T138) using only the Python stdlib.
Produces minimal but structurally valid files that:

* sample-emass-5-systems.zip — controls.json that the eMASS importer can read
* sample-ssp.pdf             — small digital PDF with extractable text
* sample-ssp-encrypted.pdf   — same content but flagged with /Encrypt → import
                               must reject as PASSWORD_PROTECTED (manual)
* template-ssp.docx          — minimal Word OOXML
* template-sar.docx          — minimal Word OOXML
* template-sap.docx          — minimal Word OOXML
* template-crm.xlsx          — minimal Excel OOXML
* template-hwsw.xlsx         — minimal Excel OOXML
* policy-acme-cybersecurity.pdf — small policy text PDF (narrative seed input)

These exist so the manual walkthrough in `specs/047-onboarding-wizard/quickstart.md`
can run end-to-end without having to ship internal documents.
"""
from __future__ import annotations

import io
import json
import sys
import zipfile
from pathlib import Path

OUT = Path(__file__).resolve().parent

# ---------------------------------------------------------------------------
# PDF helpers
# ---------------------------------------------------------------------------

def _build_pdf(text: str, encrypted_marker: bool = False) -> bytes:
    """
    Returns a minimal-but-valid 1-page PDF that contains `text`. When
    `encrypted_marker` is True, an /Encrypt entry is added to the trailer
    so PDF readers (and our SSP-PDF importer) must treat the file as
    password-protected.
    """
    # Build an extremely simple Type 1 Helvetica PDF.
    # Reference: Adobe ISO 32000-1, sec. G.6 minimal example.
    body = io.BytesIO()
    offsets: list[int] = []

    def add(obj_bytes: bytes) -> None:
        offsets.append(body.tell())
        body.write(obj_bytes)

    # Header
    body.write(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")

    # 1: catalog
    add(b"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
    # 2: pages
    add(b"2 0 obj\n<< /Type /Pages /Count 1 /Kids [3 0 R] >>\nendobj\n")
    # 3: page
    add(
        b"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
        b"/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n"
    )
    # 4: contents (BT/ET text)
    safe = (text.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)"))
    stream_text = (
        f"BT /F1 12 Tf 72 720 Td ({safe}) Tj ET".encode("latin-1", "replace")
    )
    contents = (
        b"4 0 obj\n<< /Length "
        + str(len(stream_text)).encode()
        + b" >>\nstream\n"
        + stream_text
        + b"\nendstream\nendobj\n"
    )
    add(contents)
    # 5: font
    add(
        b"5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"
    )

    # xref
    xref_offset = body.tell()
    body.write(b"xref\n0 6\n0000000000 65535 f \n")
    for o in offsets:
        body.write(f"{o:010d} 00000 n \n".encode())
    # trailer
    if encrypted_marker:
        body.write(
            b"trailer\n<< /Size 6 /Root 1 0 R "
            b"/Encrypt << /Filter /Standard /V 1 /R 2 /P -4 >> >>\n"
        )
    else:
        body.write(b"trailer\n<< /Size 6 /Root 1 0 R >>\n")
    body.write(b"startxref\n")
    body.write(str(xref_offset).encode() + b"\n%%EOF\n")
    return body.getvalue()


# ---------------------------------------------------------------------------
# DOCX helpers — write a minimal Word 2007 OOXML package.
# ---------------------------------------------------------------------------

DOCX_CONTENT_TYPES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml"
            ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
</Types>
"""

DOCX_PKG_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                Target="word/document.xml"/>
</Relationships>
"""

def _build_docx(title: str) -> bytes:
    document_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
        '<w:body>'
        f'<w:p><w:r><w:t>{title}</w:t></w:r></w:p>'
        '<w:p><w:r><w:t>{{controlId}}</w:t></w:r></w:p>'
        '<w:p><w:r><w:t>{{controlTitle}}</w:t></w:r></w:p>'
        '<w:p><w:r><w:t>{{narrative}}</w:t></w:r></w:p>'
        '</w:body></w:document>'
    )
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", DOCX_CONTENT_TYPES)
        z.writestr("_rels/.rels", DOCX_PKG_RELS)
        z.writestr("word/document.xml", document_xml)
    return buf.getvalue()


# ---------------------------------------------------------------------------
# XLSX helpers — minimal Excel package with one worksheet.
# ---------------------------------------------------------------------------

XLSX_CONTENT_TYPES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml"
            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml"
            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
</Types>
"""

XLSX_PKG_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                Target="xl/workbook.xml"/>
</Relationships>
"""

XLSX_WORKBOOK = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>
"""

XLSX_WORKBOOK_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet1.xml"/>
</Relationships>
"""

def _build_xlsx(headers: list[str]) -> bytes:
    cells = "".join(
        f'<c r="{chr(ord("A")+i)}1" t="inlineStr"><is><t>{h}</t></is></c>'
        for i, h in enumerate(headers)
    )
    sheet_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
        f'<sheetData><row r="1">{cells}</row></sheetData>'
        '</worksheet>'
    )
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", XLSX_CONTENT_TYPES)
        z.writestr("_rels/.rels", XLSX_PKG_RELS)
        z.writestr("xl/workbook.xml", XLSX_WORKBOOK)
        z.writestr("xl/_rels/workbook.xml.rels", XLSX_WORKBOOK_RELS)
        z.writestr("xl/worksheets/sheet1.xml", sheet_xml)
    return buf.getvalue()


# ---------------------------------------------------------------------------
# eMASS zip
# ---------------------------------------------------------------------------

def _build_emass_zip() -> bytes:
    systems = [
        {"id": f"sys-{i:02d}", "name": f"Sample System {i}", "fismaId": f"FISMA-{1000+i}"}
        for i in range(1, 6)
    ]
    controls = []
    for ctrl in ("AC-1", "AC-2", "AU-2", "CM-3", "SI-4"):
        for sys_ in systems:
            controls.append(
                {
                    "systemId": sys_["id"],
                    "controlId": ctrl,
                    "implementationStatus": "Implemented",
                    "responsibleEntities": ["acme-cyber"],
                    "narrative": f"{ctrl} narrative for {sys_['name']}.",
                }
            )
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("manifest.json", json.dumps({"format": "emass", "version": "1.0"}, indent=2))
        z.writestr("systems.json", json.dumps(systems, indent=2))
        z.writestr("controls.json", json.dumps(controls, indent=2))
    return buf.getvalue()


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------

FIXTURES: dict[str, bytes] = {
    "sample-emass-5-systems.zip": _build_emass_zip(),
    "sample-ssp.pdf": _build_pdf(
        "Acme Corp System Security Plan — sample digital PDF for the onboarding wizard."
    ),
    "sample-ssp-encrypted.pdf": _build_pdf(
        "Acme Corp System Security Plan — encrypted variant.",
        encrypted_marker=True,
    ),
    "template-ssp.docx": _build_docx("System Security Plan Template"),
    "template-sar.docx": _build_docx("Security Assessment Report Template"),
    "template-sap.docx": _build_docx("Security Assessment Plan Template"),
    "template-crm.xlsx": _build_xlsx([
        "ControlId", "Description", "ResponsibleEntity", "ImplementationGuidance",
    ]),
    "template-hwsw.xlsx": _build_xlsx([
        "AssetId", "Type", "Vendor", "Model", "Version", "Owner", "Environment",
    ]),
    "policy-acme-cybersecurity.pdf": _build_pdf(
        "Acme Corp Cybersecurity Policy — sample narrative-seed reference document."
    ),
}


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    for name, payload in FIXTURES.items():
        path = OUT / name
        path.write_bytes(payload)
        print(f"wrote {path.relative_to(Path.cwd())} ({len(payload)} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
