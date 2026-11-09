// Revit rooms adapter: turns a model snapshot into pattern-engine Units (an apartment = a group
// of rooms, or a single room), supplies the hosting constraints, and turns the engine's output
// into placements for place_electrical_element.
import { ModelSnapshot, SnapshotRoom, SnapshotElement, SnapshotFurniture, SnapshotDoor, SnapshotWindow, SnapshotWall } from "../../../analysis/snapshot.js";
import { pointInPolygon, Pt2 as P2 } from "../../../analysis/rules.js";
import { Candidate, Constraint, Designed, Feature, Placed, Pt2, Pt3, Unit, dot, leftOf, len, sub, unit as unitVec } from "../../core.js";
import { OfficeConfig, anchorKind, roomTypeId } from "./config.js";

export interface UnitSelector {
  /** Room keys of the unit. */
  roomKeys?: string[];
  /** Rooms whose number/name/key contains this text (e.g. apartment "03-01"). */
  roomFilter?: string;
  /** Rooms on levels whose name contains this text. */
  levelName?: string;
  /** Unit id; default from the filter. */
  id?: string;
  mirrored?: boolean;
}

// ---- role classification of the engineer's devices ----
const RX = {
  switch: /switch|מפסק|dimmer|עמעם|לחצן/i,
  shutter: /shutter|תריס/i,
  tv: /\btv\b|טלוויזיה|coax|אנטנה/i,
  data: /data|rj45|תקשורת|network|lan|telephone|טלפון/i,
  ac: /\bac\b|מזגן|hvac|מעבה|fcu/i,
  threePhase: /3[- ]?ph|תלת|3p\b|25a|32a|three/i,
  panel: /panel|לוח/i,
  bell: /bell|פעמון|intercom|אינטרקום/i,
  emergency: /emerg|חירום|exit|יציאה/i,
  light: /light|תאורה|גוף|downlight|spot|שקוע/i,
};

export function roleOf(e: SnapshotElement): string {
  const t = `${e.family ?? ""} ${e.type ?? ""}`;
  switch (e.builtInCategory) {
    case "OST_LightingFixtures": return RX.emergency.test(t) ? "emergency" : "light";
    case "OST_LightingDevices": return RX.shutter.test(t) ? "shutter" : "switch";
    case "OST_ElectricalEquipment": return "panel";
    case "OST_DataDevices": case "OST_CommunicationDevices": return RX.tv.test(t) ? "tv" : "data";
    case "OST_FireAlarmDevices": return "fire";
    case "OST_SecurityDevices": return "security";
    case "OST_ElectricalFixtures":
      if (RX.switch.test(t)) return RX.shutter.test(t) ? "shutter" : "switch";
      if (RX.tv.test(t)) return "tv";
      if (RX.data.test(t)) return "data";
      if (RX.ac.test(t)) return "ac";
      if (RX.threePhase.test(t)) return "socket_3ph";
      if (RX.bell.test(t)) return "bell";
      return "socket";
    default: return e.category?.toLowerCase().replace(/\s+/g, "_") ?? "other";
  }
}

// ---- unit building ----
const p2 = (a: number[]): Pt2 => [a[0], a[1]];
const p3 = (a: number[], z = 0): Pt3 => [a[0], a[1], a.length > 2 ? a[2] : z];

/** Architect models sometimes hold the same room twice at one spot (design options, duplicate link instances): keep one. */
export function dedupeRooms(rooms: SnapshotRoom[]): SnapshotRoom[] {
  const kept: SnapshotRoom[] = [];
  const centroid = (r: SnapshotRoom): Pt2 => r.centerMm ? p2(r.centerMm) : [r.boundaryMm!.reduce((s, p) => s + p[0], 0) / r.boundaryMm!.length, r.boundaryMm!.reduce((s, p) => s + p[1], 0) / r.boundaryMm!.length];
  for (const r of rooms) {
    const c = centroid(r);
    const dup = kept.some((k) => Math.abs(k.areaM2 - r.areaM2) <= Math.max(0.3, 0.05 * r.areaM2) && len(sub(centroid(k), c)) < 300 && Math.abs((k.baseElevationMm ?? 0) - (r.baseElevationMm ?? 0)) < 1000);
    if (!dup) kept.push(r);
  }
  return kept;
}

/** Rooms a door opens into: the model's room keys when they match, else the rooms just inside each face of its wall. */
export function doorRooms(d: SnapshotDoor, rooms: SnapshotRoom[]): SnapshotRoom[] {
  const dp = p2(d.xyzMm);
  const off = (d.wallThicknessMm ?? 200) / 2 + 60;
  const fx = d.facing?.[0] ?? 0, fy = d.facing?.[1] ?? 0;
  const probes: Pt2[] = [dp, [dp[0] + fx * off, dp[1] + fy * off], [dp[0] - fx * off, dp[1] - fy * off]];
  return rooms.filter((r) => d.fromRoomKey === r.key || d.toRoomKey === r.key || probes.some((q) => pointInPolygonLoose(q, r)));
}

function selectRooms(snap: ModelSnapshot, sel: UnitSelector): SnapshotRoom[] {
  let rooms = dedupeRooms(snap.rooms.filter((r) => r.kind === "Room" && r.boundaryMm && r.boundaryMm.length >= 3));
  if (sel.roomKeys?.length) rooms = rooms.filter((r) => sel.roomKeys!.includes(r.key));
  if (sel.roomFilter) { const f = sel.roomFilter.toLowerCase(); rooms = rooms.filter((r) => `${r.number} ${r.name} ${r.key}`.toLowerCase().includes(f)); }
  if (sel.levelName) { const f = sel.levelName.toLowerCase(); rooms = rooms.filter((r) => (r.level ?? "").toLowerCase().includes(f)); }
  return rooms;
}

interface WallSeg { a: Pt2; b: Pt2; dir: Pt2; inward: Pt2; length: number; thickness?: number; wallType?: string; wallId?: number; backing?: { thickness?: number; wallType?: string } }

/** Room boundary -> wall segments with the normal pointing into the room. */
export function roomWalls(room: SnapshotRoom, walls: SnapshotWall[] = [], minThicknessMm = 70): WallSeg[] {
  const poly = room.boundaryMm!.map(p2);
  // signed area: positive = counter-clockwise, then the left of each edge is inside
  let area = 0;
  for (let i = 0; i < poly.length; i++) { const [x1, y1] = poly[i]; const [x2, y2] = poly[(i + 1) % poly.length]; area += x1 * y2 - x2 * y1; }
  const ccw = area > 0;
  const segs: WallSeg[] = [];
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i], b = poly[(i + 1) % poly.length];
    const length = len(sub(b, a));
    if (length < 50) continue;
    const dir = unitVec(sub(b, a));
    const l = leftOf(dir);
    const inward: Pt2 = ccw ? l : [-l[0], -l[1]];
    const seg: WallSeg = { a, b, dir, inward, length };
    // Match a model wall: centre line parallel and within half a thickness + tolerance.
    const mid: Pt2 = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
    let best: { w: SnapshotWall; d: number } | undefined;
    for (const w of walls) {
      const wa = p2(w.startMm), wb = p2(w.endMm);
      const wd = unitVec(sub(wb, wa));
      if (Math.abs(dot(wd, dir)) < 0.97) continue;
      const d = Math.abs(dot(sub(mid, wa), leftOf(wd)));
      if (d <= (w.thicknessMm ?? 0) / 2 + 60 && (!best || d < best.d)) best = { w, d };
    }
    if (best) {
      seg.thickness = best.w.thicknessMm; seg.wallType = best.w.type; seg.wallId = best.w.id;
      // A thin finish layer (plaster, dry lining) in front of a real wall is a fine host: look for a backing wall behind it.
      if ((best.w.thicknessMm ?? 0) < minThicknessMm) {
        for (const w of walls) {
          if (w === best.w || (w.thicknessMm ?? 0) < minThicknessMm) continue;
          const wa = p2(w.startMm), wb = p2(w.endMm);
          const wd = unitVec(sub(wb, wa));
          if (Math.abs(dot(wd, dir)) < 0.97) continue;
          const behind = -dot(sub([(wa[0] + wb[0]) / 2, (wa[1] + wb[1]) / 2], mid), inward); // distance outward from the room
          const overlap = dot(sub(wa, mid), dir) < length / 2 && dot(sub(wb, mid), dir) > -length / 2 || dot(sub(wb, mid), dir) < length / 2 && dot(sub(wa, mid), dir) > -length / 2;
          if (behind > 0 && behind < 400 && overlap) { seg.backing = { thickness: w.thicknessMm, wallType: w.type }; break; }
        }
      }
    }
    segs.push(seg);
  }
  return segs;
}

function nearestWall(p: Pt2, segs: WallSeg[]): { seg: WallSeg; index: number; along: number; dist: number } | undefined {
  let best: { seg: WallSeg; index: number; along: number; dist: number } | undefined;
  segs.forEach((seg, index) => {
    const t = Math.max(0, Math.min(seg.length, dot(sub(p, seg.a), seg.dir)));
    const q: Pt2 = [seg.a[0] + seg.dir[0] * t, seg.a[1] + seg.dir[1] * t];
    const dist = len(sub(p, q));
    if (!best || dist < best.dist) best = { seg, index, along: t, dist };
  });
  return best;
}

function roomOf(p: Pt2, rooms: SnapshotRoom[]): SnapshotRoom | undefined {
  return rooms.find((r) => pointInPolygon(p as P2, r.boundaryMm!.map(p2) as P2[]));
}

export interface BuiltUnit { unit: Unit; rooms: SnapshotRoom[]; roomTypes: Record<string, string>; levelName?: string; warnings: string[] }

/** Build one unit (apartment or room) from a snapshot. */
export function buildUnit(snap: ModelSnapshot, sel: UnitSelector, cfg: OfficeConfig): BuiltUnit {
  const rooms = selectRooms(snap, sel);
  if (!rooms.length) throw new Error("No rooms with boundaries match the selector. Rooms need a closed boundary in the snapshot.");
  const warnings: string[] = [];
  // Floor elevation. Linked rooms carry the architect's level names and elevations ("09" at 9140 where the host says
  // "02" at 7580), so never trust the name: the floor is where the apartment's doors (and furniture) stand in host
  // coordinates; the host level is the one nearest that elevation.
  const median = (v: number[]) => { const a = [...v].sort((x, y) => x - y); return a.length ? a[Math.floor(a.length / 2)] : undefined; };
  const doorZ = median(snap.doors.filter((d) => doorRooms(d, rooms).length).map((d) => d.xyzMm[2]));
  const furnZ = median((snap.furniture ?? []).filter((f) => f.roomKey ? rooms.some((r) => r.key === f.roomKey) : roomOf(p2(f.xyzMm), rooms)).map((f) => f.bboxMinMm[2]));
  const floorZ = doorZ ?? furnZ ?? rooms[0].baseElevationMm;
  let level = floorZ === undefined ? undefined : [...snap.levels].sort((a, b) => Math.abs(a.elevationMm - floorZ) - Math.abs(b.elevationMm - floorZ))[0];
  if (level && floorZ !== undefined && Math.abs(level.elevationMm - floorZ) > 1500) level = undefined;
  if (!level) level = snap.levels.find((l) => l.name === rooms[0].level);
  const levelName = level?.name ?? rooms[0].level;
  const baseZ = level?.elevationMm ?? floorZ ?? 0;
  if (level && level.name !== rooms[0].level) warnings.push(`rooms carry level "${rooms[0].level}" (link); floor at ${Math.round(floorZ ?? baseZ)} mm → host level "${level.name}" (${level.elevationMm})`);
  const features: Feature[] = [];
  const parents: Record<string, string> = {};
  const roomTypes: Record<string, string> = {};
  const wallsByRoom = new Map<string, WallSeg[]>();

  for (const r of rooms) {
    const type = roomTypeId(r.name, cfg);
    parents[r.key] = type; roomTypes[r.key] = type;
    if (type === "other") warnings.push(`room ${r.number} "${r.name}" has no known type`);
    const segs = roomWalls(r, snap.walls ?? []);
    wallsByRoom.set(r.key, segs);
    const cx = r.centerMm ? r.centerMm[0] : r.boundaryMm!.reduce((s, p) => s + p[0], 0) / r.boundaryMm!.length;
    const cy = r.centerMm ? r.centerMm[1] : r.boundaryMm!.reduce((s, p) => s + p[1], 0) / r.boundaryMm!.length;
    features.push({ id: `room:${r.key}`, kind: "room", parent: r.key, parentType: type, point: [cx, cy, baseZ], meta: { areaM2: r.areaM2, heightMm: r.heightMm, polygon: r.boundaryMm!.map(p2) } });
    segs.forEach((s, i) => {
      const tags: string[] = [];
      const isNonClean = (t?: string) => !!t && cfg.hosting.nonCleanWallKeywords.some((k) => t.toLowerCase().includes(k.toLowerCase()));
      const thin = s.thickness !== undefined && s.thickness < cfg.hosting.minWallThickness && !(s.backing && !isNonClean(s.backing.wallType));
      if (thin || isNonClean(s.wallType)) tags.push("non-clean");
      if (s.backing) tags.push("finish-layer");
      features.push({ id: `wall:${r.key}:${i}`, kind: "wall", parent: r.key, parentType: type, point: [s.a[0], s.a[1], baseZ], dir: s.dir, normal: s.inward,
        length: s.length, tags, meta: { thicknessMm: s.thickness, wallType: s.wallType, wallId: s.wallId, backingMm: s.backing?.thickness, backingType: s.backing?.wallType } });
    });
  }

  // doors: anchored on the wall they sit in, tagged with the handle side as seen from inside the room
  for (const d of snap.doors) {
    const dp = p2(d.xyzMm);
    for (const r of doorRooms(d, rooms)) {
      const nw = nearestWall(dp, wallsByRoom.get(r.key)!);
      if (!nw || nw.dist > 400) continue;
      const seg = nw.seg;
      const along = unitVec(p2(d.alongWall ?? [seg.dir[0], seg.dir[1]]));
      const sameDir = dot(along, seg.dir) >= 0;
      // door start = hinge side in Revit's along-wall convention; handle side is the opposite end
      const half = (d.widthMm || 900) / 2;
      const hinge: Pt2 = [dp[0] - along[0] * half, dp[1] - along[1] * half];
      const handle: Pt2 = [dp[0] + along[0] * half, dp[1] + along[1] * half];
      features.push({ id: `door:${d.id}:${r.key}`, kind: "door", parent: r.key, parentType: parents[r.key], point: [dp[0], dp[1], baseZ], dir: seg.dir, normal: seg.inward,
        length: d.widthMm, anchorAt: "center", tags: [sameDir ? "handle:right" : "handle:left", "opening"], meta: { widthMm: d.widthMm, hinge, handle, wallIndex: nw.index } });
    }
  }
  for (const w of snap.windows ?? []) {
    const wp = p2(w.xyzMm);
    for (const r of rooms) {
      const nw = nearestWall(wp, wallsByRoom.get(r.key)!);
      if (!nw || nw.dist > 400) continue;
      if (w.roomKeys?.length && !w.roomKeys.includes(r.key)) continue;
      features.push({ id: `window:${w.id}:${r.key}`, kind: "window", parent: r.key, parentType: parents[r.key], point: [wp[0], wp[1], baseZ + w.sillMm], dir: nw.seg.dir,
        normal: nw.seg.inward, length: w.widthMm, anchorAt: "center", zRange: [baseZ + w.sillMm, baseZ + w.headMm], tags: ["opening"], meta: { widthMm: w.widthMm, sillMm: w.sillMm, wallIndex: nw.index } });
    }
  }
  // furniture and fixtures -> anchors with the wall they stand against
  const seen: { kind: string; c: Pt2 }[] = [];
  for (const f of snap.furniture ?? []) {
    const a = anchorKind(`${f.family ?? ""} ${f.type ?? ""}`, cfg);
    if (!a) continue;
    const c = p2(f.xyzMm);
    const r = (f.roomKey && rooms.find((x) => x.key === f.roomKey)) || roomOf(c, rooms) || roomOf(bboxCenter(f), rooms);
    if (!r) continue;
    // Architect models often hold the same piece twice (design options, nested shared families, copies): one anchor per spot.
    const bc0 = bboxCenter(f);
    if (seen.some((s) => s.kind === a.kind && Math.hypot(s.c[0] - bc0[0], s.c[1] - bc0[1]) < 80)) { warnings.push(`duplicate ${a.kind} ${f.id} skipped`); continue; }
    seen.push({ kind: a.kind, c: bc0 });
    const segs = wallsByRoom.get(r.key)!;
    const bc = bboxCenter(f);
    const nw = nearestWall(bc, segs);
    const tags: string[] = [];
    if (a.blocksWall) tags.push("blocks-wall");
    if (a.wet) tags.push("wet");
    const zr: [number, number] = a.blockHeight ? [baseZ + a.blockHeight[0], baseZ + a.blockHeight[1]] : [f.bboxMinMm[2], f.bboxMaxMm[2]];
    // Frame: along the wall it stands against (so "left/right of the bed" is stable), normal into the room.
    const feat: Feature = { id: `${a.kind}:${f.id}`, kind: a.kind, parent: r.key, parentType: parents[r.key], point: [bc[0], bc[1], baseZ], tags, zRange: zr, anchorAt: "center",
      meta: { family: f.family, type: f.type, bboxMin: f.bboxMinMm, bboxMax: f.bboxMaxMm, wallIndex: nw?.index, wallDistMm: nw ? Math.round(nw.dist) : undefined } };
    if (nw) { feat.dir = nw.seg.dir; feat.normal = nw.seg.inward; feat.length = footprintAlong(f, nw.seg); }
    features.push(feat);
  }

  // the engineer's devices
  const designed: Designed[] = [];
  for (const e of snap.elements) {
    if (!e.xyzMm) continue;
    const ep = p2(e.xyzMm);
    const r = (e.roomKey && rooms.find((x) => x.key === e.roomKey)) || roomOf(ep, rooms);
    if (!r) continue;
    const z = e.xyzMm[2];
    const nw = nearestWall(ep, wallsByRoom.get(r.key)!);
    const onWall = nw && nw.dist <= 250;
    const host = e.hostCategory?.toLowerCase().includes("ceiling") || (!onWall && z - baseZ > 2000) ? "ceiling" : onWall ? "wall" : "level";
    // The engineer put it inside a blocking footprint on purpose (appliance socket behind the counter / fridge): remember that.
    const behindAppliance = features.some((f) => f.parent === r.key && f.tags?.includes("blocks-wall") && inFootprint([ep[0], ep[1], z], f));
    designed.push({ id: String(e.id), role: roleOf(e), parent: r.key, point: [ep[0], ep[1], z], host,
      props: { category: e.category, builtInCategory: e.builtInCategory, familyName: e.family, typeName: e.type, typeId: e.typeId, behindAppliance: behindAppliance || undefined } });
  }
  // stable parent order: by type, then largest area first, then position
  const parentOrder = [...rooms].sort((a, b) => roomTypes[a.key].localeCompare(roomTypes[b.key]) || b.areaM2 - a.areaM2 || (a.centerMm?.[0] ?? 0) - (b.centerMm?.[0] ?? 0) || (a.centerMm?.[1] ?? 0) - (b.centerMm?.[1] ?? 0)).map((r) => r.key);

  const unit: Unit = { id: sel.id ?? (sel.roomFilter || sel.levelName || rooms.map((r) => r.number).join(",")), type: rooms.length > 1 ? "apartment" : "room",
    baseZ, origin: [0, 0], mirrored: sel.mirrored, features, designed, parents, parentOrder, meta: { levelName, roomCount: rooms.length, floorZ } };
  return { unit, rooms, roomTypes, levelName, warnings };
}

function bboxCenter(f: SnapshotFurniture): Pt2 { return [(f.bboxMinMm[0] + f.bboxMaxMm[0]) / 2, (f.bboxMinMm[1] + f.bboxMaxMm[1]) / 2]; }
function footprintAlong(f: SnapshotFurniture, seg: WallSeg): number {
  const dx = f.bboxMaxMm[0] - f.bboxMinMm[0], dy = f.bboxMaxMm[1] - f.bboxMinMm[1];
  return Math.abs(dx * seg.dir[0]) + Math.abs(dy * seg.dir[1]);
}
function pointInPolygonLoose(p: Pt2, r: SnapshotRoom): boolean { return pointInPolygon(p as P2, r.boundaryMm!.map(p2) as P2[]); }

// ---- constraints (the hosting rules) ----

/** Half-length interval of a segment feature around its point, along its dir. */
function interval(f: Feature): [number, number] {
  const half = (f.length ?? 0) / 2;
  return [-half, half];
}
function alongAcross(p: Pt2, f: Feature): { along: number; across: number } {
  const d = sub(p, [f.point[0], f.point[1]]);
  return { along: dot(d, f.dir!), across: dot(d, f.normal ?? leftOf(f.dir!)) };
}
function sameWall(c: Feature, f: Feature): boolean {
  return c.meta?.wallIndex !== undefined && f.meta?.wallIndex !== undefined && c.meta.wallIndex === f.meta.wallIndex;
}

/** True when a point lies within a blocking feature's plan footprint (± tolerance) and its blocking height range. */
function inFootprint(p: Pt3, f: Feature): boolean {
  if (!f.dir) return false;
  const { along, across } = alongAcross([p[0], p[1]], f);
  const [lo, hi] = interval(f);
  const bmin = f.meta?.bboxMin as number[] | undefined, bmax = f.meta?.bboxMax as number[] | undefined;
  const depth = bmin && bmax ? Math.min(Math.abs(bmax[0] - bmin[0]), Math.abs(bmax[1] - bmin[1])) : 600;
  const inPlan = along >= lo - 50 && along <= hi + 50 && Math.abs(across) <= depth / 2 + 150;
  const inHeight = !f.zRange || (p[2] >= f.zRange[0] - 50 && p[2] <= f.zRange[1] + 50);
  return inPlan && inHeight;
}

function distToPolygon(p: Pt2, poly: Pt2[]): number {
  let best = Infinity;
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i], b = poly[(i + 1) % poly.length];
    const ab = sub(b, a), l = len(ab);
    const t = l ? Math.max(0, Math.min(1, dot(sub(p, a), ab) / (l * l))) : 0;
    best = Math.min(best, len(sub(p, [a[0] + ab[0] * t, a[1] + ab[1] * t])));
  }
  return best;
}

export function hostingConstraints(cfg: OfficeConfig, existing: Pt3[] = []): Constraint[] {
  const h = cfg.hosting;
  // Wall rules apply to wall-mounted devices only: ceiling and floor/level devices (detectors, luminaires) never need a wall.
  const wallDevice = (c: Candidate) => c.host === "wall" || (c.host === undefined && !["light", "emergency", "fire"].includes(c.role));
  return [
    // 0. inside the parent room (wall devices sit on its boundary, so allow a small margin)
    (c, u) => {
      const room = u.features.find((f) => f.kind === "room" && f.parent === c.parent);
      const poly = room?.meta?.polygon as Pt2[] | undefined;
      if (!poly || poly.length < 3) return { ok: true };
      const p: Pt2 = [c.point[0], c.point[1]];
      if (pointInPolygon(p as P2, poly as P2[]) || distToPolygon(p, poly) <= 150) return { ok: true };
      return { ok: false, check: "room", reason: `outside room ${c.parent}` };
    },
    // 1. non-clean host wall
    (c, u) => {
      if (!wallDevice(c)) return { ok: true };
      const wall = hostWall(c, u);
      if (!wall) return { ok: false, check: "wall", reason: "no wall within 300 mm of the point" };
      if (wall.tags?.includes("non-clean")) return { ok: false, check: "wall", reason: `wall ${wall.id} is non-clean (${wall.meta?.wallType ?? "thin"})` };
      return { ok: true };
    },
    // 2. behind furniture that blocks the wall, at that furniture's height range (unless the engineer meant it: appliance socket)
    (c, u) => {
      if (!wallDevice(c)) return { ok: true };
      for (const f of u.features) {
        if (f.parent !== c.parent || !f.tags?.includes("blocks-wall") || !f.dir) continue;
        if (c.props?.behindAppliance && (c.feature.id === f.id || c.feature.kind === f.kind)) continue;
        if (inFootprint(c.point, f)) return { ok: false, check: "behind-furniture", reason: `behind ${f.kind} ${f.id}` };
      }
      return { ok: true };
    },
    // 3. door frame / leaf and window openings
    (c, u) => {
      if (!wallDevice(c)) return { ok: true };
      const p: Pt2 = [c.point[0], c.point[1]];
      const wall = hostWall(c, u);
      for (const f of u.features) {
        if (f.parent !== c.parent || !f.tags?.includes("opening") || !f.dir) continue;
        if (wall && f.meta?.wallIndex !== undefined && wall.meta?.wallIndex !== undefined && f.meta.wallIndex !== wall.meta.wallIndex) continue;
        const { along, across } = alongAcross(p, f);
        const [lo, hi] = interval(f);
        const clear = f.kind === "door" ? (c.role === "switch" ? h.clearFromDoorFrame : h.clearFromDoorFrame) : h.clearFromWindow;
        if (Math.abs(across) <= 300 && along > lo - clear && along < hi + clear) {
          if (f.kind === "window" && f.zRange && (c.point[2] < f.zRange[0] - 50)) continue; // below the sill is fine
          return { ok: false, check: "opening", reason: `inside ${f.kind} ${f.id} ± ${clear} mm` };
        }
        if (f.kind === "door" && across > 0 && across <= (f.length ?? 900) && along > lo && along < hi && c.point[2] - u.baseZ < 2100)
          return { ok: false, check: "door-swing", reason: `in the swing of door ${f.id}` };
      }
      return { ok: true };
    },
    // 4. wet zones
    (c, u) => {
      if (!["socket", "socket_3ph", "data", "tv"].includes(c.role)) return { ok: true };
      const p: Pt2 = [c.point[0], c.point[1]];
      for (const f of u.features) {
        if (f.parent !== c.parent || !f.tags?.includes("wet")) continue;
        const bmin = f.meta?.bboxMin as number[] | undefined, bmax = f.meta?.bboxMax as number[] | undefined;
        const d = bmin && bmax ? distToBox(p, bmin, bmax) : len(sub(p, [f.point[0], f.point[1]]));
        if (d < h.wetZones.socketMinFromShowerEdge) return { ok: false, check: "wet-zone", reason: `${Math.round(d)} mm from ${f.kind} (< ${h.wetZones.socketMinFromShowerEdge})` };
        if (d < h.wetZones.socketMinFromShowerEdge + 600 && c.heightMm < h.wetZones.socketMinHeightZone2)
          return { ok: false, check: "wet-zone", reason: `zone 2 of ${f.kind}: height ${c.heightMm} < ${h.wetZones.socketMinHeightZone2}` };
      }
      return { ok: true };
    },
    // 5. collisions with existing devices (and with what this run already placed: the caller appends)
    (c) => {
      for (const e of existing) {
        if (Math.hypot(e[0] - c.point[0], e[1] - c.point[1], e[2] - c.point[2]) < h.clearBetweenDevices)
          return { ok: false, check: "collision", reason: `${h.clearBetweenDevices} mm from an existing device` };
      }
      return { ok: true };
    },
  ];
}

function hostWall(c: { point: Pt3; parent: string }, u: Unit): Feature | undefined {
  const p: Pt2 = [c.point[0], c.point[1]];
  let best: { f: Feature; d: number } | undefined;
  for (const f of u.features) {
    if (f.kind !== "wall" || f.parent !== c.parent || !f.dir) continue;
    const t = Math.max(0, Math.min(f.length ?? 0, dot(sub(p, [f.point[0], f.point[1]]), f.dir)));
    const q: Pt2 = [f.point[0] + f.dir[0] * t, f.point[1] + f.dir[1] * t];
    const d = len(sub(p, q));
    if (!best || d < best.d) best = { f, d };
  }
  return best && best.d <= 300 ? best.f : undefined;
}
function distToBox(p: Pt2, min: number[], max: number[]): number {
  const dx = Math.max(min[0] - p[0], 0, p[0] - max[0]);
  const dy = Math.max(min[1] - p[1], 0, p[1] - max[1]);
  return Math.hypot(dx, dy);
}

// ---- output ----

export interface RoomPlacement {
  name: string;
  role: string;
  roomKey: string;
  category: string;
  typeId?: number;
  familyName?: string;
  typeName?: string;
  location: { x: number; y: number; z: number };
  hostMode: "ceiling" | "wall" | "level";
  levelName?: string;
  scheduleLevel?: string;
}

/** Engine output -> placements in the shape apply_electrical_design / place_electrical_element expect. */
export function toPlacements(placed: Placed[], levelName: string | undefined, unitId: string): RoomPlacement[] {
  return placed
    .filter((p) => p.status !== "dropped" && p.point)
    .map((p, i) => ({
      name: `${unitId} ${p.parent} ${p.ruleId} #${i + 1}`,
      role: p.role,
      roomKey: p.parent,
      category: String(p.props?.category ?? categoryForRole(p.role)),
      typeId: p.props?.typeId as number | undefined,
      familyName: p.props?.familyName as string | undefined,
      typeName: p.props?.typeName as string | undefined,
      location: { x: p.point![0], y: p.point![1], z: p.point![2] },
      hostMode: p.host === "ceiling" ? "ceiling" : p.host === "level" ? "level" : "wall",
      levelName,
      scheduleLevel: levelName,
    }));
}

function categoryForRole(role: string): string {
  switch (role) {
    case "light": case "emergency": return "Lighting Fixtures";
    case "switch": case "shutter": return "Lighting Devices";
    case "panel": return "Electrical Equipment";
    case "data": case "tv": return "Data Devices";
    default: return "Electrical Fixtures";
  }
}
