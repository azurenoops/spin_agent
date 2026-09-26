"""Render local review images; pass an output directory outside the upload bundle."""

from pathlib import Path
import json
import sys

import pymupdf


def main():
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python render_previews.py /path/to/preview-output")
    root = Path(__file__).resolve().parent
    destination = Path(sys.argv[1]).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    expected = json.loads((root / "expected-results.json").read_text(encoding="utf-8"))
    pages = []
    for name in sorted(root.glob("0*-flankspeed-*.pdf")):
        with pymupdf.open(name) as document:
            for index, page in enumerate(document):
                pixmap = page.get_pixmap(matrix=pymupdf.Matrix(0.3, 0.3), alpha=False)
                pages.append((f"{name.name[:2]} / page {index + 1}", pixmap))
    for start in range(0, len(pages), 16):
        with pymupdf.open() as document:
            sheet = document.new_page(width=800, height=1080)
            for offset, (label, image) in enumerate(pages[start:start + 16]):
                x, y = (offset % 4) * 200 + 8, (offset // 4) * 270 + 8
                sheet.insert_text((x, y + 10), label, fontsize=9)
                sheet.insert_image(pymupdf.Rect(x, y + 18, x + image.width, y + 18 + image.height),
                                   pixmap=image)
            sheet.get_pixmap().save(destination / f"contact-{start // 16 + 1:02}.png")
    for key in ("components", "capabilities", "controls", "findings", "poams"):
        citation = expected[key][0]["source"]
        with pymupdf.open(root / citation["artifact"]) as document:
            document[citation["page"] - 1].get_pixmap(
                matrix=pymupdf.Matrix(1.3, 1.3), alpha=False
            ).save(destination / f"detail-{key}.png")
    print(f"Rendered {len(pages)} pages and five detail views into {destination}")


if __name__ == "__main__":
    main()
