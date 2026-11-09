// Pattern engine: learn how ONE designed example of a repeating object is laid out
// relative to its surroundings, then continue the rest of the model from it.
// Domain-free: an adapter turns a Revit apartment, a room, or a railway segment into
// Units with Features; the engine only knows points, directions and constraints.
// Units: millimeters everywhere.

export type Pt2 = [number, number];
export type Pt3 = [number, number, number];

/** A geometric anchor inside a unit: a wall, a door, a bed, a track centreline, the unit itself. */
export interface Feature {
  id: string;
  /** Domain kind: wall, door, window, bed, wardrobe, centerline, room ... */
  kind: string;
  /** Group the feature belongs to (a room key, a station id). Rules are mapped by parent type, not by parent id. */
  parent: string;
  /** Type of the parent (room type "master_bedroom", "station"), used to match template and target. */
  parentType: string;
  /** Reference point, mm. For a segment feature: its start. */
  point: Pt3;
  /** Unit direction "along" the feature. Optional: features without direction use the unit frame. */
  dir?: Pt2;
  /** Unit direction "across", pointing to the usable side (into the room, to the right of the track). */
  normal?: Pt2;
  /** Length along dir when the feature is a segment, mm. */
  length?: number;
  /** Where `point` sits on the segment: its start (walls, default) or its centre (furniture, doors, windows). */
  anchorAt?: "start" | "center";
  /** Vertical extent, absolute mm. */
  zRange?: [number, number];
  /** Free tags: "handle:left", "blocks-wall", "wet", "non-clean". */
  tags?: string[];
  meta?: Record<string, unknown>;
}

/** One designed element of the example (a socket, a pole, a signal). */
export interface Designed {
  id: string;
  role: string;
  parent: string;
  point: Pt3;
  /** How it is attached in the model (wall, ceiling, floor, level...). Passed through to the rule. */
  host?: string;
  props?: Record<string, unknown>;
}

export interface Unit {
  id: string;
  /** Unit type, e.g. "apartment", "room", "station". */
  type: string;
  /** Base elevation (finished floor / rail head), mm. Heights in rules are relative to it. */
  baseZ: number;
  /** Frame for features without a direction. */
  origin: Pt2;
  /** True when the target is a mirror image of the template (adapter-detected or engineer-said). */
  mirrored?: boolean;
  features: Feature[];
  designed: Designed[];
  /** Parent id -> parent type (room key -> room type). */
  parents: Record<string, string>;
  meta?: Record<string, unknown>;
}

export interface Rule {
  id: string;
  role: string;
  parentType: string;
  anchorKind: string;
  /** Which feature of that kind in the parent: 0 = first by along-order. */
  ordinal: number;
  /** Tags the anchor must carry (e.g. ["handle:right"]). */
  anchorTags?: string[];
  along: number;
  across: number;
  height: number;
  host?: string;
  props?: Record<string, unknown>;
  source: { designedId: string; featureId: string; distanceMm: number };
}

export interface Pattern {
  version: 1;
  unitType: string;
  templateUnitId: string;
  learnedAt: string;
  rules: Rule[];
  notes: string[];
}

export interface LearnOptions {
  /** Preferred anchor kinds, best first. Kinds not listed are still used, with a penalty. */
  anchorPriority?: string[];
  /** Beyond this distance an anchor is not plausible; the next kind is tried. */
  maxAnchorDistanceMm?: number;
  /** Tags copied from the chosen feature into rule.anchorTags, by prefix ("handle:"). */
  keepTagPrefixes?: string[];
  /** Restrict the anchor kinds per role, best first, e.g. { light: ["room"], switch: ["door", "wall"] }. */
  anchorKindsByRole?: Record<string, string[]>;
}

export interface Candidate {
  ruleId: string;
  role: string;
  parent: string;
  feature: Feature;
  point: Pt3;
  heightMm: number;
  host?: string;
  props?: Record<string, unknown>;
}

export type Verdict = { ok: true } | { ok: false; reason: string; check: string };
export type Constraint = (c: Candidate, unit: Unit) => Verdict;

export interface Placed {
  ruleId: string;
  role: string;
  parent: string;
  parentType: string;
  anchor: string;
  status: "placed" | "moved" | "dropped";
  point?: Pt3;
  heightMm?: number;
  host?: string;
  props?: Record<string, unknown>;
  movedMm?: number;
  reason?: string;
  checks: string[];
}

export interface ReplicateOptions {
  slideStepMm?: number;
  slideMaxMm?: number;
  /** Try the mirrored side of the anchor when sliding fails. */
  tryMirror?: boolean;
  /** Fallback anchor kinds when the rule's kind is missing in a parent (e.g. ["wall"]). */
  fallbackKinds?: string[];
}

// ---------- geometry ----------

export const sub = (a: Pt2, b: Pt2): Pt2 => [a[0] - b[0], a[1] - b[1]];
export const dot = (a: Pt2, b: Pt2) => a[0] * b[0] + a[1] * b[1];
export const len = (a: Pt2) => Math.hypot(a[0], a[1]);
export const unit = (a: Pt2): Pt2 => { const l = len(a) || 1; return [a[0] / l, a[1] / l]; };
export const leftOf = (d: Pt2): Pt2 => [-d[1] + 0, d[0] + 0];

/** Closest distance from p to the feature (segment when it has a length, else its point). */
export function distanceToFeature(p: Pt2, f: Feature): number {
  const a: Pt2 = [f.point[0], f.point[1]];
  if (!f.dir || !f.length) return len(sub(p, a));
  const [lo, hi] = f.anchorAt === "center" ? [-f.length / 2, f.length / 2] : [0, f.length];
  const t = Math.max(lo, Math.min(hi, dot(sub(p, a), f.dir)));
  const q: Pt2 = [a[0] + f.dir[0] * t, a[1] + f.dir[1] * t];
  return len(sub(p, q));
}

/** (along, across) of p in the feature's frame. */
export function toLocal(p: Pt2, f: Feature, u: Unit): { along: number; across: number } {
  const d = sub(p, [f.point[0], f.point[1]]);
  if (f.dir) {
    const n = f.normal ?? leftOf(f.dir);
    return { along: dot(d, f.dir), across: dot(d, n) };
  }
  return { along: d[0], across: d[1] };
}

export function fromLocal(along: number, across: number, f: Feature): Pt2 {
  if (f.dir) {
    const n = f.normal ?? leftOf(f.dir);
    return [f.point[0] + f.dir[0] * along + n[0] * across, f.point[1] + f.dir[1] * along + n[1] * across];
  }
  return [f.point[0] + along, f.point[1] + across];
}

/** Features of one kind in one parent, in a stable order (by along-position in the parent's frame). */
export function featuresOf(u: Unit, parent: string, kind: string): Feature[] {
  return u.features
    .filter((f) => f.parent === parent && f.kind === kind)
    .sort((a, b) => a.point[0] - b.point[0] || a.point[1] - b.point[1]);
}

// ---------- learn ----------

export function learn(template: Unit, opts: LearnOptions = {}): Pattern {
  const priority = opts.anchorPriority ?? [];
  const maxDist = opts.maxAnchorDistanceMm ?? 2500;
  const prefixes = opts.keepTagPrefixes ?? [];
  const rules: Rule[] = [];
  const notes: string[] = [];
  const counter = new Map<string, number>();

  for (const d of template.designed) {
    const parentType = template.parents[d.parent];
    if (!parentType) { notes.push(`${d.id}: parent ${d.parent} has no type, skipped`); continue; }
    const p: Pt2 = [d.point[0], d.point[1]];
    let best: { f: Feature; score: number; dist: number } | undefined;
    const allowed = opts.anchorKindsByRole?.[d.role];
    for (const f of template.features) {
      if (f.parent !== d.parent) continue;
      if (allowed && !allowed.includes(f.kind)) continue;
      const dist = distanceToFeature(p, f);
      if (dist > maxDist) continue;
      // Per-role kind list doubles as that role's priority; otherwise the global priority applies.
      const list = allowed ?? priority;
      const rank = list.indexOf(f.kind);
      const penalty = rank >= 0 ? rank * 150 : list.length * 150 + 1000;
      const score = dist + penalty;
      if (!best || score < best.score) best = { f, score, dist };
    }
    if (!best) { notes.push(`${d.id} (${d.role}): no anchor within ${maxDist} mm, skipped`); continue; }
    const f = best.f;
    const ordinal = featuresOf(template, d.parent, f.kind).findIndex((x) => x.id === f.id);
    const { along, across } = toLocal(p, f, template);
    const key = `${parentType}.${f.kind}.${d.role}`;
    const n = (counter.get(key) ?? 0) + 1;
    counter.set(key, n);
    const anchorTags = (f.tags ?? []).filter((t) => prefixes.some((pre) => t.startsWith(pre)));
    rules.push({
      id: `${key}#${n}`,
      role: d.role,
      parentType,
      anchorKind: f.kind,
      ordinal: Math.max(0, ordinal),
      anchorTags: anchorTags.length ? anchorTags : undefined,
      along: Math.round(along),
      across: Math.round(across),
      height: Math.round(d.point[2] - template.baseZ),
      host: d.host,
      props: d.props,
      source: { designedId: d.id, featureId: f.id, distanceMm: Math.round(best.dist) },
    });
  }
  return { version: 1, unitType: template.type, templateUnitId: template.id, learnedAt: new Date().toISOString(), rules, notes };
}

// ---------- replicate ----------

function pickFeature(u: Unit, parent: string, rule: Rule, fallbackKinds: string[]): { f?: Feature; anchorKind: string } {
  const kinds = [rule.anchorKind, ...fallbackKinds];
  for (const kind of kinds) {
    let list = featuresOf(u, parent, kind);
    if (rule.anchorTags?.length) {
      const tagged = list.filter((f) => rule.anchorTags!.every((t) => f.tags?.includes(t)));
      if (tagged.length) list = tagged;
    }
    if (!list.length) continue;
    return { f: list[Math.min(rule.ordinal, list.length - 1)], anchorKind: kind };
  }
  return { anchorKind: rule.anchorKind };
}

function runChecks(c: Candidate, u: Unit, constraints: Constraint[]): { ok: boolean; checks: string[]; reason?: string } {
  const checks: string[] = [];
  for (const k of constraints) {
    const v = k(c, u);
    if (!v.ok) { checks.push(`${v.check}: ${v.reason}`); return { ok: false, checks, reason: v.reason }; }
  }
  return { ok: true, checks };
}

export function replicate(pattern: Pattern, target: Unit, constraints: Constraint[] = [], opts: ReplicateOptions = {}): Placed[] {
  const step = opts.slideStepMm ?? 100;
  const max = opts.slideMaxMm ?? 600;
  const fallbackKinds = opts.fallbackKinds ?? [];
  const out: Placed[] = [];
  const sign = target.mirrored ? -1 : 1;

  for (const rule of pattern.rules) {
    const parents = Object.entries(target.parents).filter(([, t]) => t === rule.parentType).map(([k]) => k);
    for (const parent of parents) {
      const { f, anchorKind } = pickFeature(target, parent, rule, fallbackKinds);
      if (!f) {
        out.push({ ruleId: rule.id, role: rule.role, parent, parentType: rule.parentType, anchor: anchorKind, status: "dropped",
          reason: `no ${rule.anchorKind} in ${parent}`, checks: [] });
        continue;
      }
      const make = (along: number): Candidate => {
        const xy = fromLocal(along, rule.across, f);
        return { ruleId: rule.id, role: rule.role, parent, feature: f, point: [xy[0], xy[1], target.baseZ + rule.height],
          heightMm: rule.height, host: rule.host, props: rule.props };
      };
      const base = rule.along * sign;
      const attempts: number[] = [base];
      for (let s = step; s <= max; s += step) attempts.push(base + s, base - s);
      if (opts.tryMirror !== false && f.dir && f.length) attempts.push(f.anchorAt === "center" ? -base : f.length - base);
      let placed: Placed | undefined;
      let firstReason = "";
      const allChecks: string[] = [];
      for (let i = 0; i < attempts.length; i++) {
        const c = make(attempts[i]);
        const r = runChecks(c, target, constraints);
        allChecks.push(...r.checks);
        if (r.ok) {
          const moved = Math.abs(attempts[i] - base);
          placed = { ruleId: rule.id, role: rule.role, parent, parentType: rule.parentType, anchor: `${f.kind}#${rule.ordinal}`,
            status: moved > 0 ? "moved" : "placed", point: c.point.map((v) => Math.round(v)) as Pt3, heightMm: rule.height,
            host: rule.host, props: rule.props, movedMm: moved || undefined, reason: moved ? firstReason : undefined, checks: allChecks };
          break;
        }
        if (!firstReason) firstReason = r.reason ?? "";
      }
      out.push(placed ?? { ruleId: rule.id, role: rule.role, parent, parentType: rule.parentType, anchor: `${f.kind}#${rule.ordinal}`,
        status: "dropped", reason: firstReason || "all positions rejected", checks: allChecks });
    }
  }
  return out;
}

export function summarize(placed: Placed[]) {
  const by = (k: keyof Placed) => placed.reduce<Record<string, number>>((m, p) => { const v = String(p[k]); m[v] = (m[v] ?? 0) + 1; return m; }, {});
  return { total: placed.length, byStatus: by("status"), byRole: by("role"), byParentType: by("parentType"),
    dropped: placed.filter((p) => p.status === "dropped").map((p) => `${p.parent} ${p.ruleId}: ${p.reason}`) };
}
