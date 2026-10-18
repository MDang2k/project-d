"""Export docs/design/item_catalog.xlsx to one CSV per sheet in docs/design/catalog/.

The xlsx is the source of truth (the designer edits it in Excel); the CSVs are the text copy
agents read and git diffs. Re-run after every edit:

    python tools/export_catalog.py

Needs openpyxl (python -m pip install --user openpyxl).
The pre-commit hook in tools/githooks runs this automatically when the xlsx is committed.
"""
import csv
import re
from pathlib import Path

from openpyxl import load_workbook

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "design" / "item_catalog.xlsx"
OUT = ROOT / "docs" / "design" / "catalog"


def slug(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def main() -> None:
    values = load_workbook(SRC, data_only=True)  # cached results of formulas
    raw = load_workbook(SRC)  # formulas, used when a cell has no cached result
    OUT.mkdir(parents=True, exist_ok=True)
    written = []
    for ws in values.worksheets:
        if ws.title == "Summary":
            continue
        raw_ws = raw[ws.title]
        path = OUT / f"{slug(ws.title)}.csv"
        with path.open("w", newline="", encoding="utf-8") as f:
            w = csv.writer(f, lineterminator="\n")
            for row, raw_row in zip(ws.iter_rows(), raw_ws.iter_rows()):
                cells = [c.value if c.value is not None else r.value for c, r in zip(row, raw_row)]
                if all(v is None for v in cells):
                    continue
                w.writerow(["" if v is None else v for v in cells])
        written.append(path.name)
    for stale in OUT.glob("*.csv"):
        if stale.name not in written:
            stale.unlink()
    print(f"exported {len(written)} sheets to {OUT.relative_to(ROOT)}: {', '.join(written)}")


if __name__ == "__main__":
    main()
