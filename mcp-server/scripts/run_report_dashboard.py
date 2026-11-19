"""Collect every run-report.json under a folder into one Excel (Excel / Power BI source).

    python run_report_dashboard.py <REVITCLAUDE_ROOT or any folder> [out.xlsx]

Sheets: Runs (one row per apply run), PerProject, PerUser. The project is the folder name
under Projects (or the proposal's parent folder).
"""
import json, sys
from collections import defaultdict
from pathlib import Path
from openpyxl import Workbook

COLS = ["at", "project", "user", "proposal", "attempted", "placed", "failed", "remaining", "coverage", "seconds"]


def project_of(path: Path) -> str:
    parts = path.parts
    return parts[parts.index("Projects") + 1] if "Projects" in parts[:-2] else path.parent.name


def collect(root: Path):
    rows = []
    for f in sorted(root.rglob("run-report.json")):
        try:
            data = json.loads(f.read_text(encoding="utf-8-sig"))
        except ValueError:
            continue
        for r in data if isinstance(data, list) else []:
            rows.append({**r, "project": project_of(f), "user": r.get("user", "")})
    return rows


def summarize(rows, key):
    agg = defaultdict(lambda: [0, 0, 0, 0, 0])  # runs, attempted, placed, failed, seconds
    for r in rows:
        a = agg[r[key]]
        a[0] += 1; a[1] += r.get("attempted", 0); a[2] += r.get("placed", 0); a[3] += r.get("failed", 0); a[4] += r.get("seconds", 0)
    return [[k, *v] for k, v in sorted(agg.items())]


def main(argv):
    if len(argv) < 2:
        sys.exit(__doc__)
    rows = collect(Path(argv[1]))
    wb = Workbook()
    ws = wb.active; ws.title = "Runs"
    ws.append(COLS)
    for r in rows:
        ws.append([r.get(c, "") for c in COLS])
    for name, key in (("PerProject", "project"), ("PerUser", "user")):
        s = wb.create_sheet(name)
        s.append([key, "runs", "attempted", "placed", "failed", "seconds"])
        for row in summarize(rows, key):
            s.append(row)
    out = argv[2] if len(argv) > 2 else "run-report-dashboard.xlsx"
    wb.save(out)
    print(f"{len(rows)} runs -> {out}")


if __name__ == "__main__":
    main(sys.argv)
