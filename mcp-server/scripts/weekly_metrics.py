"""Build the weekly measurement workbook (metrics\\YYYY-WW.xlsx) from run-report.json files
plus two hand-filled CSVs. Nothing is estimated: a number nobody recorded stays empty.

    python weekly_metrics.py <REVITCLAUDE_ROOT> [--week 2026-46] [--manual manual.csv] [--before before.csv] [--out file.xlsx]
    python weekly_metrics.py --selfcheck

manual.csv (one row per proposal/floor that week, filled by the engineer; all but the first two optional):
    project,proposal,review_minutes,manual_corrections,review_errors_before,review_errors_after,
    moved,dropped,count_diff_pct,cost_ils
before.csv (the manual baseline, filled by engineers, one row per floor):
    project,proposal,minutes,cost_ils
A manual/before row is keyed by (project, proposal) = same names as in run-report.json.
"""
import csv, json, sys, tempfile
from datetime import datetime
from pathlib import Path
from openpyxl import Workbook, load_workbook

MANUAL_NUM = ["review_minutes", "manual_corrections", "review_errors_before", "review_errors_after",
              "moved", "dropped", "count_diff_pct", "cost_ils"]


def project_of(path: Path) -> str:
    p = path.parts
    return p[p.index("Projects") + 1] if "Projects" in p[:-2] else path.parent.name


def week_of(iso: str) -> str:
    y, w, _ = datetime.fromisoformat(iso.replace("Z", "+00:00")).isocalendar()
    return f"{y}-{w:02d}"


def load_runs(root: Path, week: str):
    runs = []
    for f in sorted(root.rglob("run-report.json")):
        try:
            data = json.loads(f.read_text(encoding="utf-8-sig"))
        except ValueError:
            continue
        for r in data if isinstance(data, list) else []:
            if r.get("at") and week_of(r["at"]) == week:
                runs.append({**r, "project": project_of(f)})
    return runs


def num(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None  # empty/invalid cell = not recorded


def load_csv(path):
    if not path or not Path(path).exists():
        return {}
    with open(path, encoding="utf-8-sig", newline="") as fh:
        return {(r["project"], r["proposal"]): r for r in csv.DictReader(fh)}


def per_floor(runs):
    """Fold the week's runs into one row per (project, proposal): last state, summed seconds."""
    floors = {}
    for r in sorted(runs, key=lambda r: r["at"]):
        f = floors.setdefault((r["project"], r["proposal"]), {"runs": 0, "seconds": 0})
        f["runs"] += 1
        f["seconds"] += r.get("seconds", 0)
        f.update(placed=r.get("placed", 0), failed=r.get("failed", 0), remaining=r.get("remaining", 0),
                 total=r.get("proposalTotal"), coverage=r.get("coverage"))
    return floors


def build_rows(floors, manual, before):
    rows = []
    for key, f in sorted(floors.items()):
        m, b = manual.get(key, {}), before.get(key, {})
        after_min = None
        rm, mc = num(m.get("review_minutes")), num(m.get("manual_corrections"))
        # manual_corrections is a count, not minutes: time after = Claude run + engineer review only.
        if rm is not None:
            after_min = round(f["seconds"] / 60 + rm, 1)
        bmin, bcost, acost = num(b.get("minutes")), num(b.get("cost_ils")), num(m.get("cost_ils"))
        rows.append({
            "project": key[0], "proposal": key[1], **f,
            **{c: num(m.get(c)) for c in MANUAL_NUM},
            "after_minutes": after_min, "before_minutes": bmin, "before_cost_ils": bcost,
            "saved_minutes": None if after_min is None or bmin is None else round(bmin - after_min, 1),
            "saved_pct": None if after_min is None or not bmin else round(100 * (bmin - after_min) / bmin, 1),
            "saved_cost_ils": None if acost is None or bcost is None else round(bcost - acost, 2),
        })
    return rows


def summary(rows):
    def tot(k): return sum(r[k] or 0 for r in rows)
    def have(k): return [r[k] for r in rows if r[k] is not None]
    placed, failed, remaining = tot("placed"), tot("failed"), tot("remaining")
    total = sum(r["total"] or 0 for r in rows)
    pct = lambda a, b: None if not b else round(100 * a / b, 1)
    rv = have("review_minutes")
    return [
        ("1 coverage - rule coverage of the sample", None, "not in run-report: fill from the proposal (points with a rule / sample points)"),
        ("2 placed % of proposal", pct(total - remaining, total), "run-report: (proposalTotal-remaining)/proposalTotal"),
        ("2 failed placements", failed, "run-report: failed"),
        ("2 moved / dropped (sum)", (tot("moved"), tot("dropped")) if have("moved") or have("dropped") else None, "manual.csv, from review_model"),
        ("3 review errors before -> after", (tot("review_errors_before"), tot("review_errors_after")) if have("review_errors_after") else None, "manual.csv, from review_model"),
        ("4 count diff vs engineer % (mean abs)", round(sum(abs(x) for x in have("count_diff_pct")) / len(have("count_diff_pct")), 1) if have("count_diff_pct") else None, "manual.csv, engineer count"),
        ("5 Claude run minutes", round(tot("seconds") / 60, 1), "run-report: seconds"),
        ("5 max review minutes per floor", max(rv) if rv else None, "manual.csv, stopwatch (target <= 15)"),
        ("6 cost ILS (sum)", sum(have("cost_ils")) if have("cost_ils") else None, "manual.csv, Team dashboard / API usage"),
    ]


def write(path, week, runs, rows):
    wb = Workbook()
    s = wb.active; s.title = "Summary"
    s.append(["week", week]); s.append(["floors", len(rows)]); s.append([])
    s.append(["metric", "value", "source"])
    for name, val, src in summary(rows):
        s.append([name, "" if val is None else (str(val) if isinstance(val, tuple) else val), src])
    f = wb.create_sheet("PerFloor")
    cols = list(rows[0]) if rows else ["project", "proposal"]
    f.append(cols)
    for r in rows:
        f.append(["" if r[c] is None else r[c] for c in cols])
    roi = wb.create_sheet("ROI")
    roi.append(["project", "proposal", "before_minutes", "after_minutes", "saved_minutes", "saved_pct", "before_cost_ils", "after_cost_ils", "saved_cost_ils"])
    for r in rows:
        roi.append(["" if v is None else v for v in (r["project"], r["proposal"], r["before_minutes"], r["after_minutes"],
                                                        r["saved_minutes"], r["saved_pct"], r["before_cost_ils"], r["cost_ils"], r["saved_cost_ils"])])
    rr = wb.create_sheet("Runs")
    rr.append(["at", "project", "user", "proposal", "attempted", "placed", "failed", "remaining", "coverage", "seconds"])
    for r in runs:
        rr.append([r.get(c.value, "") for c in rr[1]])
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    wb.save(path)


def run(root, week, manual_p, before_p, out):
    runs = load_runs(Path(root), week)
    rows = build_rows(per_floor(runs), load_csv(manual_p), load_csv(before_p))
    out = out or Path(root) / "metrics" / f"{week}.xlsx"
    write(out, week, runs, rows)
    return out, rows


def selfcheck():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        rep = d / "Projects" / "DEMO" / "run-report.json"
        rep.parent.mkdir(parents=True)
        # synthetic numbers, for the self-check only
        rep.write_text(json.dumps([
            {"at": "2026-11-09T08:00:00Z", "user": "u", "proposal": "p1.json", "seconds": 120, "attempted": 10, "placed": 8, "failed": 2, "proposalTotal": 10, "remaining": 2, "coverage": 0.8},
            {"at": "2026-11-10T08:00:00Z", "user": "u", "proposal": "p1.json", "seconds": 60, "attempted": 2, "placed": 2, "failed": 0, "proposalTotal": 10, "remaining": 0, "coverage": 1},
            {"at": "2026-10-01T08:00:00Z", "user": "u", "proposal": "old.json", "seconds": 5, "attempted": 1, "placed": 1, "failed": 0, "proposalTotal": 1, "remaining": 0, "coverage": 1}]))
        (d / "m.csv").write_text("project,proposal,review_minutes,cost_ils\nDEMO,p1.json,10,20\n")
        (d / "b.csv").write_text("project,proposal,minutes,cost_ils\nDEMO,p1.json,100,200\n")
        out, rows = run(d, "2026-46", d / "m.csv", d / "b.csv", None)
        assert len(rows) == 1 and rows[0]["runs"] == 2 and rows[0]["placed"] == 2, rows
        assert rows[0]["after_minutes"] == 13.0 and rows[0]["saved_minutes"] == 87.0 and rows[0]["saved_pct"] == 87.0
        assert rows[0]["saved_cost_ils"] == 180.0
        # no manual/before data -> ROI stays empty, never invented
        _, rows2 = run(d, "2026-46", None, None, d / "x.xlsx")
        assert rows2[0]["after_minutes"] is None and rows2[0]["saved_pct"] is None
        wb = load_workbook(out)
        assert wb.sheetnames == ["Summary", "PerFloor", "ROI", "Runs"] and wb["Runs"].max_row == 3
    print("selfcheck OK")


if __name__ == "__main__":
    a = sys.argv[1:]
    if not a or a == ["--selfcheck"]:
        selfcheck() if a else sys.exit(__doc__)
    else:
        opt = {a[i]: a[i + 1] for i in range(1, len(a) - 1, 2) if a[i].startswith("--")}
        week = opt.get("--week") or week_of(datetime.now().isoformat())
        out, rows = run(a[0], week, opt.get("--manual"), opt.get("--before"), opt.get("--out"))
        print(f"{len(rows)} floors -> {out}")
