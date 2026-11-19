// Measures one design run: what Claude placed (snapshot right after the run) against what the designer left after review
// (a later snapshot of the same model). Elements are matched by element id; Claude's are the ones whose Comments start with
// the provenance prefix that apply_electrical_design writes ("Claude: ...").
import { ModelSnapshot, SnapshotElement } from "../../../analysis/snapshot.js";
import { roleOf } from "./adapter.js";

export type Change = "kept" | "moved" | "retyped" | "deleted" | "added_by_designer";

export interface MeasureOptions {
  /** Only elements on this level (by elevation, so devices without a Level parameter still count). */
  levelName?: string;
  /** Comments prefix that marks Claude's elements. */
  prefix?: string;
  /** A move smaller than this is not a correction. */
  moveTolMm?: number;
}

export interface MeasureResult {
  level?: string;
  claudePlaced: number;
  kept: number;
  moved: number;
  retyped: number;
  deleted: number;
  addedByDesigner: number;
  /** kept / claudePlaced, 0..1 */
  keptShare: number;
  /** designer touches (moved + retyped + deleted + added) per 100 Claude elements */
  correctionsPer100: number;
  byRole: Record<string, Partial<Record<Change, number>>>;
  changes: { id: number; role: string; change: Change; movedMm?: number; fromType?: string; toType?: string }[];
}

const isClaude = (e: SnapshotElement, prefix: string) => (e.parameters?.Comments ?? "").trim().startsWith(prefix);

/** Elevation band [floor, next floor) of a level; undefined when the level is unknown. */
function band(snap: ModelSnapshot, name: string): [number, number] | undefined {
  const levels = [...snap.levels].sort((a, b) => a.elevationMm - b.elevationMm);
  const i = levels.findIndex((l) => l.name === name);
  const j = i >= 0 ? i : levels.findIndex((l) => l.name.toLowerCase().includes(name.toLowerCase()));
  if (j < 0) return undefined;
  // Levels closer than 1 m (datum levels such as "Level 2" next to "02") are not floors of their own.
  const next = levels.slice(j + 1).find((l) => l.elevationMm - levels[j].elevationMm >= 1000);
  return [levels[j].elevationMm - 50, next ? next.elevationMm - 50 : Infinity];
}

function onLevel(snap: ModelSnapshot, levelName?: string): (e: SnapshotElement) => boolean {
  if (!levelName) return (e) => !!e.xyzMm;
  const b = band(snap, levelName);
  if (!b) throw new Error(`level "${levelName}" is not in the snapshot`);
  return (e) => !!e.xyzMm && e.xyzMm[2] >= b[0] && e.xyzMm[2] < b[1];
}

export function measureRun(before: ModelSnapshot, after: ModelSnapshot, opts: MeasureOptions = {}): MeasureResult {
  const prefix = opts.prefix ?? "Claude";
  const tol = opts.moveTolMm ?? 50;
  const B = new Map(before.elements.filter(onLevel(before, opts.levelName)).map((e) => [e.id, e]));
  const A = new Map(after.elements.filter(onLevel(after, opts.levelName)).map((e) => [e.id, e]));
  // An element moved to another floor is still in the model: look it up in the whole later snapshot.
  const anyAfter = new Map(after.elements.map((e) => [e.id, e]));
  const changes: MeasureResult["changes"] = [];

  for (const b of B.values()) {
    if (!isClaude(b, prefix)) continue;
    const a = anyAfter.get(b.id);
    const role = roleOf(b);
    if (!a) { changes.push({ id: b.id, role, change: "deleted" }); continue; }
    if (a.typeId !== b.typeId) { changes.push({ id: b.id, role, change: "retyped", fromType: b.type, toType: a.type }); continue; }
    const d = a.xyzMm && b.xyzMm ? Math.hypot(a.xyzMm[0] - b.xyzMm[0], a.xyzMm[1] - b.xyzMm[1], a.xyzMm[2] - b.xyzMm[2]) : 0;
    changes.push(d > tol ? { id: b.id, role, change: "moved", movedMm: Math.round(d) } : { id: b.id, role, change: "kept" });
  }
  for (const a of A.values()) {
    if (B.has(a.id) || isClaude(a, prefix)) continue;
    changes.push({ id: a.id, role: roleOf(a), change: "added_by_designer" });
  }

  const count = (c: Change) => changes.filter((x) => x.change === c).length;
  const byRole: MeasureResult["byRole"] = {};
  for (const c of changes) { const r = (byRole[c.role] ??= {}); r[c.change] = (r[c.change] ?? 0) + 1; }
  const claudePlaced = changes.filter((c) => c.change !== "added_by_designer").length;
  const kept = count("kept");
  const touches = claudePlaced - kept + count("added_by_designer");
  return {
    level: opts.levelName,
    claudePlaced, kept, moved: count("moved"), retyped: count("retyped"), deleted: count("deleted"), addedByDesigner: count("added_by_designer"),
    keptShare: claudePlaced ? Math.round((kept / claudePlaced) * 1000) / 1000 : 0,
    correctionsPer100: claudePlaced ? Math.round((touches / claudePlaced) * 1000) / 10 : 0,
    byRole,
    changes: changes.filter((c) => c.change !== "kept"),
  };
}
