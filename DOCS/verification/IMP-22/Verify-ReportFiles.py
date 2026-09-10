"""Read generated artifacts, compare canonical facts, scan markers, and render PDF pages for QA."""
import argparse
import html
import json
import re
import unicodedata
import zipfile
from pathlib import Path
from xml.etree import ElementTree

import pypdf
import pypdfium2 as pdfium
from PIL import Image, ImageDraw


def normalized(text):
    return "".join(unicodedata.normalize("NFKC", text).split())


def verify_pdf(path, canonical, marker, redacted, output):
    reader = pypdf.PdfReader(path)
    pages = [page.extract_text() or "" for page in reader.pages]
    content = "\n".join(pages)
    if redacted:
        assert marker not in content, f"PDF marker: {path}"
    # Repeated page furniture may split a wrapped canonical paragraph across pages.
    comparable = normalized("\n".join(line for line in content.splitlines()
        if not line.startswith("DiagnosticReport / ") and not re.fullmatch(r"Page\s+\d+", line.strip())))
    missing = [line for line in canonical.splitlines() if normalized(line) and normalized(line) not in comparable]
    assert not missing, f"PDF missing canonical lines: {path}: {[line[:160] for line in missing[:3]]}"
    document = pdfium.PdfDocument(path)
    previews = []
    for index in range(len(document)):
        page = document[index]
        image = page.render(scale=1.25).to_pil()
        target = output / f"{path.stem}-page-{index + 1}.png"
        image.save(target)
        thumb = image.copy()
        thumb.thumbnail((390, 552))
        previews.append((index + 1, thumb))
    for batch in range(0, len(previews), 12):
        group = previews[batch:batch + 12]
        sheet = Image.new("RGB", (1200, ((len(group) + 2) // 3) * 585), "#ddd")
        draw = ImageDraw.Draw(sheet)
        for cell, (number, thumb) in enumerate(group):
            x, y = (cell % 3) * 400, (cell // 3) * 585
            draw.text((x + 8, y + 5), f"Page {number}", fill="black")
            sheet.paste(thumb, (x + 5, y + 25))
        sheet.save(output / f"{path.stem}-contact-{batch // 12 + 1}.png")
    return len(pages)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--word-pdf", type=Path)
    args = parser.parse_args()
    directory = args.directory
    state = json.loads((directory / "verification.json").read_text(encoding="utf-8-sig"))
    marker = state["Marker"]
    output = directory / "rendered"
    output.mkdir(exist_ok=True)
    results = []
    for prefix in ("raw", "redacted"):
        canonical = (directory / f"{prefix}-canonical.txt").read_text(encoding="utf-8")
        report = json.loads((directory / f"{prefix}.json").read_text(encoding="utf-8-sig"))
        document = (directory / f"{prefix}.html").read_text(encoding="utf-8")
        extracted = html.unescape(document.split('<pre id="report-text">')[1].split("</pre>")[0])
        assert extracted.replace("\r\n", "\n") == canonical
        assert "script-src 'none'" in document and "<script" not in document
        assert report["Scope"] == "B1/S2/Q1" and report["IssueCount"] == len(report["Issues"])
        assert len(report["Facts"]) == report["FactCount"]
        assert "OTHER_STATEMENT" not in report["SourceXml"]
        if prefix == "redacted":
            for extension in ("html", "json", "svg", "sqlplan"):
                assert marker not in (directory / f"{prefix}.{extension}").read_text(encoding="utf-8")
        with zipfile.ZipFile(directory / f"{prefix}.docx") as archive:
            document_xml = archive.read("word/document.xml")
            tree = ElementTree.fromstring(document_xml)
            text = "\n".join("".join(p.itertext()) for p in tree.iter("{http://schemas.openxmlformats.org/wordprocessingml/2006/main}p"))
            for line in canonical.splitlines():
                assert normalized(line) in normalized(text), f"Word missing line: {line}"
            if prefix == "redacted":
                for entry in archive.namelist():
                    if entry.endswith(".xml"):
                        assert marker.encode() not in archive.read(entry), f"Word marker: {entry}"
            assert any(name.startswith("word/media/") for name in archive.namelist())
        page_count = verify_pdf(directory / f"{prefix}.pdf", canonical, marker, prefix == "redacted", output)
        results.append({"Mode": prefix, "Facts": report["FactCount"], "Issues": report["IssueCount"], "PdfPages": page_count, "CanonicalLines": len(canonical.splitlines()), "Passed": True})
        word_pdf = output / f"word-{prefix}.pdf"
        if word_pdf.exists():
            results.append({"Mode": f"Word-{prefix}", "Pages": verify_pdf(word_pdf, canonical, marker, prefix == "redacted", output), "Passed": True})
    if args.word_pdf:
        canonical = (directory / "redacted-canonical.txt").read_text(encoding="utf-8")
        results.append({"Mode": "WordRendering", "Pages": verify_pdf(args.word_pdf, canonical, marker, True, output), "Passed": True})
    (directory / "format-verification.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    print(json.dumps(results))


if __name__ == "__main__":
    main()
