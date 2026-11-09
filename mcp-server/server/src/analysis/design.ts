import fs from "fs";
import path from "path";
import { ModelSnapshot, SnapshotDoor, SnapshotElement, SnapshotRoom, loadSnapshot, roomLabel } from "./snapshot.js";
import {
  DesignRules,
  Pt2,
  RoomTypeRule,
  distanceToPolygonEdge,
  distanceToSegment,
  isEmergencyLight,
  isLighting,
  isSocket,
  isSwitch,
  loadRules,
  lumensOf,
  pointInPolygon,
  roomTypeOf,
  signedArea,
  simplify,
  wattsOf,
} from "./rules.js";

// Rule-driven electrical layout per room: lighting by the lumen method on a grid inside the room
// outline, sockets spread along the walls clear of doors and corners, a switch and an emergency
// light by each door. Revit-free: it reads a snapshot and writes a proposal that
// apply_electrical_design later sends to place_electrical_element, after the engineer approves it.

export interface TypeChoice {
  typeId?: number;
  familyName?: string;
  typeName?: string;
  /** Fixtures only: luminous flux per fixture, lm. */
  lumens?: number;
  watts?: number;
}

export type Role = "lighting" | "socket" | "switch" | "emergency";

export interface Placement {
  name: string;
  role: Role;
  roomKey: string;
  category: string;
  typeId?: number;
  familyName?: string;
  typeName?: string;
  location: { x: number; y: number; z: number };
  hostMode: "ceiling" | "wall";
  levelName?: string;
}

export interface RoomProposal {
  key: string;
  room: string;
  level?: string;
  roomType: string;
  areaM2: number;
  targetLux: number;
  lumensPerFixture?: number;
  fixturesByLumenMethod?: number;
  estimatedLux?: number;
  existing: { lights: number; sockets: number; switches: number; emergency: number };
  proposed: Record<Role, number>;
  notes: string[];
}

export interface DesignOptions {
  snapshotPath: string;
  rulesPath?: string;
  outputPath?: string;
  /** Rooms whose number, name or key contains this text. */
  roomFilter?: string;
  levelName?: string;
  roles?: Role[];
  /** Leave a role alone in a room that already has elements of that role. Default true. */
  skipExisting?: boolean;
  fixture?: TypeChoice;
  socket?: TypeChoice;
  switchType?: TypeChoice;
  emergencyFixture?: TypeChoice;
}

export interface DesignProposal {
  schemaVersion: 1;
  snapshot: string;
  model: string;
  createdAt: string;
  types: Record<Role, TypeChoice & { source: string }>;
  rooms: RoomProposal[];
  placements: Placement[];
}

export interface DesignResult {
  proposalPath: string;
  previewPath: string;
  rooms: number;
  roomsSkipped: number;
  placements: Record<Role, number>;
  types: Record<Role, string>;
  notes: string[];
}

const CATEGORY: Record<Role, string> = {
  lighting: "OST_LightingFixtures",
  emergency: "OST_LightingFixtures",
  socket: "OST_ElectricalFixtures",
  switch: "OST_LightingDevices",
};

const DEFAULT_LUMENS = 3600;
const DEFAULT_NAMES: Record<Role, TypeChoice> = {
  lighting: { familyName: "Light", lumens: DEFAULT_LUMENS },
  emergency: { familyName: "Emergency" },
  socket: { familyName: "Receptacle" },
  switch: { familyName: "Switch" },
};

/** The type the model already uses most for a role, so a proposal matches the project's own families. */
function mostUsed(elements: SnapshotElement[], test: (e: SnapshotElement) => boolean): (TypeChoice & { count: number }) | undefined {
  const counts = new Map<number, { e: SnapshotElement; n: number }>();
  for (const e of elements.filter(test)) {
    const c = counts.get(e.typeId);
    if (c) c.n++;
    else counts.set(e.typeId, { e, n: 1 });
  }
  const best = [...counts.values()].sort((a, b) => b.n - a.n)[0];
  if (!best) return undefined;
  return {
    typeId: best.e.typeId,
    familyName: best.e.family,
    typeName: best.e.type,
    lumens: lumensOf(best.e),
    watts: wattsOf(best.e),
    count: best.n,
  };
}

function resolveTypes(snap: ModelSnapshot, opts: DesignOptions): Record<Role, TypeChoice & { source: string }> {
  const pick = (role: Role, given: TypeChoice | undefined, test: (e: SnapshotElement) => boolean) => {
    if (given && (given.typeId || given.familyName || given.typeName)) return { ...given, source: "given" };
    const used = mostUsed(snap.elements, test);
    if (used) return { typeId: used.typeId, familyName: used.familyName, typeName: used.typeName, lumens: given?.lumens ?? used.lumens, watts: used.watts, source: `most used in model (${used.count})` };
    return { ...DEFAULT_NAMES[role], ...(given ?? {}), source: "default name search" };
  };
  return {
    lighting: pick("lighting", opts.fixture, (e) => isLighting(e) && !isEmergencyLight(e)),
    emergency: pick("emergency", opts.emergencyFixture, isEmergencyLight),
    socket: pick("socket", opts.socket, isSocket),
    switch: pick("switch", opts.switchType, isSwitch),
  };
}

function polygonOf(room: SnapshotRoom): Pt2[] | undefined {
  if (!room.boundaryMm || room.boundaryMm.length < 3) return undefined;
  const poly = simplify(room.boundaryMm.map((p) => [p[0], p[1]] as Pt2));
  return poly.length >= 3 ? poly : undefined;
}

function doorsOf(room: SnapshotRoom, poly: Pt2[], doors: SnapshotDoor[]): SnapshotDoor[] {
  const keyed = doors.filter((d) => d.fromRoomKey === room.key || d.toRoomKey === room.key);
  if (keyed.length) return keyed;
  // No room keys on the doors (e.g. phases differ): take doors standing on this room's outline.
  return doors.filter(
    (d) => Math.abs(d.xyzMm[2] - room.baseElevationMm) < 1000 && distanceToPolygonEdge([d.xyzMm[0], d.xyzMm[1]], poly) < (d.wallThicknessMm ?? 200) / 2 + 150
  );
}

function nearDoor(p: Pt2, doors: SnapshotDoor[], clearMm: number): boolean {
  return doors.some((d) => {
    const c: Pt2 = [d.xyzMm[0], d.xyzMm[1]];
    const half = d.widthMm / 2 + clearMm;
    const a: Pt2 = [c[0] - d.alongWall[0] * half, c[1] - d.alongWall[1] * half];
    const b: Pt2 = [c[0] + d.alongWall[0] * half, c[1] + d.alongWall[1] * half];
    return distanceToSegment(p, a, b) < (d.wallThicknessMm ?? 200) / 2 + 200;
  });
}

/** Even lighting layout: a regular grid for rectangular rooms, balanced clusters (k-means) for any other outline. */
function lightingLayout(poly: Pt2[], count: number, maxSpacingMm: number): Pt2[] {
  const xs = poly.map((p) => p[0]);
  const ys = poly.map((p) => p[1]);
  const bboxArea = (Math.max(...xs) - Math.min(...xs)) * (Math.max(...ys) - Math.min(...ys));
  const rectangular = poly.length === 4 && Math.abs(Math.abs(signedArea(poly)) - bboxArea) < bboxArea * 0.02;
  if (rectangular) {
    // Enough rows and columns that no two neighbours are further apart than the spacing limit.
    const W = Math.max(...xs) - Math.min(...xs), H = Math.max(...ys) - Math.min(...ys);
    return lightingGrid(poly, Math.max(count, Math.ceil(W / maxSpacingMm) * Math.ceil(H / maxSpacingMm)));
  }
  // Irregular outline: add fixtures until every point of the room is within reach of one.
  let n = Math.max(1, count);
  let pts = lightingClusters(poly, n);
  for (let guard = 0; guard < 40 && uncovered(poly, pts, maxSpacingMm * 0.6); guard++) pts = lightingClusters(poly, ++n);
  return pts;
}

function uncovered(poly: Pt2[], centres: Pt2[], radiusMm: number): boolean {
  const xs = poly.map((p) => p[0]);
  const ys = poly.map((p) => p[1]);
  for (let x = Math.min(...xs) + 250; x < Math.max(...xs); x += 500)
    for (let y = Math.min(...ys) + 250; y < Math.max(...ys); y += 500)
      if (pointInPolygon([x, y], poly) && distanceToPolygonEdge([x, y], poly) > 300 && Math.min(...centres.map((c) => Math.hypot(x - c[0], y - c[1]))) > radiusMm) return true;
  return false;
}

function lightingClusters(poly: Pt2[], count: number): Pt2[] {
  const xs = poly.map((p) => p[0]);
  const ys = poly.map((p) => p[1]);
  const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
  const step = Math.max(100, Math.sqrt(Math.abs(signedArea(poly)) / Math.max(1, count)) / 4);
  const samples: Pt2[] = [];
  for (let x = minX + step / 2; x < maxX; x += step)
    for (let y = minY + step / 2; y < maxY; y += step) if (pointInPolygon([x, y], poly) && distanceToPolygonEdge([x, y], poly) > 300) samples.push([x, y]);
  if (!samples.length) return [];
  if (samples.length <= count) return samples;
  // Farthest-point start, then Lloyd iterations; each centre snaps to its nearest sample so it stays inside the room.
  const centres: Pt2[] = [samples[0]];
  while (centres.length < count) {
    let far = samples[0], farD = -1;
    for (const s of samples) {
      const d = Math.min(...centres.map((c) => Math.hypot(s[0] - c[0], s[1] - c[1])));
      if (d > farD) { farD = d; far = s; }
    }
    centres.push(far);
  }
  for (let it = 0; it < 25; it++) {
    const sum = centres.map(() => [0, 0, 0]);
    for (const s of samples) {
      let k = 0, kd = Infinity;
      centres.forEach((c, i) => { const d = Math.hypot(s[0] - c[0], s[1] - c[1]); if (d < kd) { kd = d; k = i; } });
      sum[k][0] += s[0]; sum[k][1] += s[1]; sum[k][2]++;
    }
    centres.forEach((c, i) => {
      if (!sum[i][2]) return;
      const m: Pt2 = [sum[i][0] / sum[i][2], sum[i][1] / sum[i][2]];
      let best = samples[0], bd = Infinity;
      for (const s of samples) { const d = Math.hypot(s[0] - m[0], s[1] - m[1]); if (d < bd) { bd = d; best = s; } }
      centres[i] = best;
    });
  }
  return centres;
}

function lightingGrid(poly: Pt2[], count: number): Pt2[] {
  const xs = poly.map((p) => p[0]);
  const ys = poly.map((p) => p[1]);
  const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
  const W = Math.max(1, maxX - minX), H = Math.max(1, maxY - minY);
  let cols = Math.max(1, Math.round(Math.sqrt((count * W) / H)));
  let rows = Math.max(1, Math.ceil(count / cols));
  let best: Pt2[] = [];
  for (let attempt = 0; attempt < 12; attempt++) {
    const pts: Pt2[] = [];
    for (let i = 0; i < cols; i++)
      for (let j = 0; j < rows; j++) {
        const p: Pt2 = [minX + ((i + 0.5) * W) / cols, minY + ((j + 0.5) * H) / rows];
        if (pointInPolygon(p, poly) && distanceToPolygonEdge(p, poly) > 300) pts.push(p);
      }
    best = pts;
    if (pts.length >= count) break;
    // An L-shaped or irregular room loses grid points outside its outline: refine the grid.
    if (W / cols >= H / rows) cols++;
    else rows++;
  }
  return best;
}

/** Points along the walls, spread evenly, clear of corners and doors, nudged 50 mm into the room. */
function wallPoints(poly: Pt2[], count: number, doors: SnapshotDoor[]): Pt2[] {
  if (count <= 0) return [];
  const ccw = signedArea(poly) > 0;
  const candidates: { s: number; p: Pt2 }[] = [];
  let s0 = 0;
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i], b = poly[(i + 1) % poly.length];
    const len = Math.hypot(b[0] - a[0], b[1] - a[1]);
    const d: Pt2 = [(b[0] - a[0]) / len, (b[1] - a[1]) / len];
    const n: Pt2 = ccw ? [-d[1], d[0]] : [d[1], -d[0]];
    if (len >= 800) {
      for (let t = 300; t <= len - 300; t += 100) {
        const p: Pt2 = [a[0] + d[0] * t + n[0] * 50, a[1] + d[1] * t + n[1] * 50];
        if (!nearDoor(p, doors, 300)) candidates.push({ s: s0 + t, p });
      }
    }
    s0 += len;
  }
  if (!candidates.length) return [];
  const chosen: Pt2[] = [];
  const used = new Set<number>();
  for (let k = 0; k < count; k++) {
    const target = ((k + 0.5) * s0) / count;
    let bestI = -1, bestD = Infinity;
    candidates.forEach((c, i) => {
      if (used.has(i)) return;
      const dist = Math.min(Math.abs(c.s - target), s0 - Math.abs(c.s - target));
      if (dist < bestD) { bestD = dist; bestI = i; }
    });
    if (bestI < 0) break;
    // Keep chosen points at least 600 mm apart along the wall.
    for (let i = 0; i < candidates.length; i++) if (Math.abs(candidates[i].s - candidates[bestI].s) < 600) used.add(i);
    chosen.push(candidates[bestI].p);
  }
  return chosen;
}

function besideDoor(d: SnapshotDoor, poly: Pt2[], doors: SnapshotDoor[], alongMm: number, inMm: number): Pt2 | undefined {
  const c: Pt2 = [d.xyzMm[0], d.xyzMm[1]];
  const half = d.widthMm / 2;
  const t = (d.wallThicknessMm ?? 200) / 2;
  for (const side of [1, -1])
    for (const face of [1, -1]) {
      const p: Pt2 = [
        c[0] + d.alongWall[0] * side * (half + alongMm) + d.facing[0] * face * (t + inMm),
        c[1] + d.alongWall[1] * side * (half + alongMm) + d.facing[1] * face * (t + inMm),
      ];
      if (pointInPolygon(p, poly) && !nearDoor(p, doors.filter((x) => x !== d), 100)) return p;
    }
  return undefined;
}

function insideDoor(d: SnapshotDoor, poly: Pt2[], inMm: number): Pt2 | undefined {
  for (const face of [1, -1]) {
    const p: Pt2 = [d.xyzMm[0] + d.facing[0] * face * inMm, d.xyzMm[1] + d.facing[1] * face * inMm];
    if (pointInPolygon(p, poly)) return p;
  }
  return undefined;
}

export function designRooms(snap: ModelSnapshot, rules: DesignRules, opts: DesignOptions): DesignProposal {
  const types = resolveTypes(snap, opts);
  const roles = new Set<Role>(opts.roles?.length ? opts.roles : ["lighting", "socket", "switch", "emergency"]);
  const skipExisting = opts.skipExisting !== false;
  const inRoom = new Map<string, SnapshotElement[]>();
  for (const e of snap.elements) {
    const k = e.roomKey ?? e.spaceKey;
    if (k) inRoom.set(k, [...(inRoom.get(k) ?? []), e]);
  }
  const filter = opts.roomFilter?.toLowerCase();
  const level = opts.levelName?.toLowerCase();
  const candidates = (snap.rooms.some((r) => r.kind === "Room") ? snap.rooms.filter((r) => r.kind === "Room") : snap.rooms).filter(
    (r) =>
      r.areaM2 >= rules.minRoomAreaM2 &&
      (!filter || `${r.number} ${r.name} ${r.key}`.toLowerCase().includes(filter)) &&
      (!level || (r.level ?? "").toLowerCase().includes(level))
  );

  const proposal: DesignProposal = { schemaVersion: 1, snapshot: "", model: snap.document.title, createdAt: new Date().toISOString(), types, rooms: [], placements: [] };
  const typeFields = (role: Role) => {
    const t = types[role];
    return t.typeId && t.typeId > 0 ? { typeId: t.typeId } : { familyName: t.familyName, typeName: t.typeName };
  };

  for (const room of candidates) {
    const rt: RoomTypeRule = roomTypeOf(room, rules);
    const els = inRoom.get(room.key) ?? [];
    const existing = {
      lights: els.filter((e) => isLighting(e) && !isEmergencyLight(e)).length,
      sockets: els.filter(isSocket).length,
      switches: els.filter(isSwitch).length,
      emergency: els.filter(isEmergencyLight).length,
    };
    const rp: RoomProposal = {
      key: room.key,
      room: roomLabel(room),
      level: room.level,
      roomType: rt.id,
      areaM2: room.areaM2,
      targetLux: rt.lux,
      existing,
      proposed: { lighting: 0, socket: 0, switch: 0, emergency: 0 },
      notes: [],
    };
    proposal.rooms.push(rp);
    const poly = polygonOf(room);
    if (!poly) {
      rp.notes.push("אין קו מתאר לחדר בייצוא, לא ניתן למקם");
      continue;
    }
    const doors = doorsOf(room, poly, snap.doors);
    const base = room.baseElevationMm;
    const ceiling = base + Math.min(rules.ceilingHeightMm, room.heightMm > 0 ? room.heightMm : Infinity);
    const push = (role: Role, p: Pt2, z: number, hostMode: "ceiling" | "wall", i: number) =>
      proposal.placements.push({
        name: `${roomLabel(room)} ${role} ${i + 1}`,
        role,
        roomKey: room.key,
        category: CATEGORY[role],
        ...typeFields(role),
        location: { x: Math.round(p[0]), y: Math.round(p[1]), z: Math.round(z) },
        hostMode,
        levelName: room.level,
      });

    if (roles.has("lighting")) {
      if (skipExisting && existing.lights > 0) rp.notes.push(`יש כבר ${existing.lights} גופי תאורה, תאורה לא הוצעה`);
      else {
        const lm = types.lighting.lumens ?? DEFAULT_LUMENS;
        const n = Math.max(1, Math.ceil((rt.lux * room.areaM2) / (lm * rules.utilisationFactor * rules.maintenanceFactor)));
        const pts = lightingLayout(poly, n, rules.maxFixtureSpacingM * 1000);
        pts.forEach((p, i) => push("lighting", p, ceiling, "ceiling", i));
        rp.lumensPerFixture = lm;
        rp.fixturesByLumenMethod = n;
        rp.proposed.lighting = pts.length;
        rp.estimatedLux = Math.round((pts.length * lm * rules.utilisationFactor * rules.maintenanceFactor) / room.areaM2);
        if (!types.lighting.lumens) rp.notes.push(`שטף הגוף לא ידוע, חושב לפי ${DEFAULT_LUMENS} לומן`);
      }
    }
    if (roles.has("socket")) {
      const need = Math.max(rt.minSockets, rt.socketSpacingM > 0 ? Math.floor(room.perimeterM / rt.socketSpacingM) : 0);
      if (skipExisting && existing.sockets > 0) rp.notes.push(`יש כבר ${existing.sockets} בתי תקע, לא הוצעו נוספים`);
      else if (need > 0) {
        const pts = wallPoints(poly, need, doors);
        pts.forEach((p, i) => push("socket", p, base + rules.socketHeightMm, "wall", i));
        rp.proposed.socket = pts.length;
        if (pts.length < need) rp.notes.push(`נמצא מקום ל-${pts.length} מתוך ${need} בתי תקע`);
      }
    }
    const lit = existing.lights > 0 || rp.proposed.lighting > 0;
    if (roles.has("switch") && rt.switches && lit) {
      if (skipExisting && existing.switches > 0) rp.notes.push("יש כבר מפסק");
      else if (!doors.length) rp.notes.push("לא נמצאה דלת לחדר, מפסק לא הוצע");
      else
        doors.slice(0, 2).forEach((d, i) => {
          const p = besideDoor(d, poly, doors, 200, 50);
          if (p) { push("switch", p, base + rules.switchHeightMm, "wall", i); rp.proposed.switch++; }
          else rp.notes.push(`אין מקום למפסק ליד דלת ${d.id}`);
        });
      if (rp.proposed.switch > 0) rp.notes.push("צד המפסק ליד הדלת נבחר אוטומטית, לבדוק מול כיוון פתיחה");
    }
    if (roles.has("emergency") && rt.emergency) {
      if (skipExisting && existing.emergency > 0) rp.notes.push("יש כבר תאורת חירום");
      else {
        const pts = doors.map((d) => insideDoor(d, poly, 800)).filter((p): p is Pt2 => !!p);
        if (!pts.length) {
          const c = lightingGrid(poly, 1)[0];
          if (c) pts.push(c);
        }
        pts.forEach((p, i) => push("emergency", p, ceiling, "ceiling", i));
        rp.proposed.emergency = pts.length;
        if (rt.id === "corridor" && room.perimeterM / 2 > 15) rp.notes.push("מסדרון ארוך: לבדוק מרווח גופי חירום לאורכו");
      }
    }
  }
  return proposal;
}

// ---- preview ----

const COLORS = { room: "#5b6b7f", door: "#8a5a2b", existing: "#9aa3ad", lighting: "#e8a317", socket: "#2f6fd6", switch: "#2a9d55", emergency: "#d43d3d" };

function levelSvg(levelName: string, rooms: SnapshotRoom[], snap: ModelSnapshot, placements: Placement[]): string {
  const pts = rooms.flatMap((r) => r.boundaryMm ?? []);
  if (!pts.length) return "";
  const minX = Math.min(...pts.map((p) => p[0])), maxX = Math.max(...pts.map((p) => p[0]));
  const minY = Math.min(...pts.map((p) => p[1])), maxY = Math.max(...pts.map((p) => p[1]));
  const pad = 1000;
  const w = maxX - minX + 2 * pad, h = maxY - minY + 2 * pad;
  const X = (x: number) => (x - minX + pad).toFixed(0);
  const Y = (y: number) => (maxY + pad - y).toFixed(0);
  const unit = Math.max(w, h) / 250;
  const keys = new Set(rooms.map((r) => r.key));
  const out: string[] = [`<svg viewBox="0 0 ${w.toFixed(0)} ${h.toFixed(0)}" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="${esc(levelName)}">`];
  for (const r of rooms) {
    if (!r.boundaryMm?.length) continue;
    out.push(`<polygon points="${r.boundaryMm.map((p) => `${X(p[0])},${Y(p[1])}`).join(" ")}" fill="none" stroke="${COLORS.room}" stroke-width="${unit * 0.4}"/>`);
    const c = r.centerMm ?? r.boundaryMm[0];
    out.push(`<text x="${X(c[0])}" y="${Y(c[1])}" font-size="${unit * 2.2}" text-anchor="middle" fill="${COLORS.room}">${esc(roomLabel(r))}</text>`);
  }
  for (const d of snap.doors.filter((d) => (d.fromRoomKey && keys.has(d.fromRoomKey)) || (d.toRoomKey && keys.has(d.toRoomKey)))) {
    const h2 = d.widthMm / 2;
    out.push(`<line x1="${X(d.xyzMm[0] - d.alongWall[0] * h2)}" y1="${Y(d.xyzMm[1] - d.alongWall[1] * h2)}" x2="${X(d.xyzMm[0] + d.alongWall[0] * h2)}" y2="${Y(d.xyzMm[1] + d.alongWall[1] * h2)}" stroke="${COLORS.door}" stroke-width="${unit * 0.9}"/>`);
  }
  for (const e of snap.elements.filter((e) => e.xyzMm && keys.has(e.roomKey ?? e.spaceKey ?? ""))) {
    out.push(`<circle cx="${X(e.xyzMm![0])}" cy="${Y(e.xyzMm![1])}" r="${unit * 0.9}" fill="${COLORS.existing}"/>`);
  }
  for (const p of placements.filter((p) => keys.has(p.roomKey))) {
    const x = X(p.location.x), y = Y(p.location.y), s = unit * 1.4;
    if (p.role === "lighting") out.push(`<circle cx="${x}" cy="${y}" r="${s}" fill="${COLORS.lighting}" stroke="#000" stroke-width="${unit * 0.15}"/>`);
    else if (p.role === "emergency") out.push(`<rect x="${+x - s}" y="${+y - s}" width="${2 * s}" height="${2 * s}" fill="${COLORS.emergency}"/>`);
    else if (p.role === "socket") out.push(`<polygon points="${x},${+y - s} ${+x - s},${+y + s} ${+x + s},${+y + s}" fill="${COLORS.socket}"/>`);
    else out.push(`<text x="${x}" y="${+y + s * 0.7}" font-size="${s * 2}" font-weight="bold" text-anchor="middle" fill="${COLORS.switch}">S</text>`);
  }
  out.push("</svg>");
  return out.join("");
}

function esc(s: string): string {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

export function previewHtml(snap: ModelSnapshot, proposal: DesignProposal): string {
  const byKey = new Map(snap.rooms.map((r) => [r.key, r]));
  const levels = new Map<string, SnapshotRoom[]>();
  for (const rp of proposal.rooms) {
    const r = byKey.get(rp.key);
    if (!r) continue;
    const lv = r.level ?? r.source;
    levels.set(lv, [...(levels.get(lv) ?? []), r]);
  }
  const typeLine = (role: Role, he: string) => {
    const t = proposal.types[role];
    return `<li><b>${he}:</b> ${esc([t.familyName, t.typeName].filter(Boolean).join(" : ") || "?")}${t.typeId ? ` (id ${t.typeId})` : ""}${role === "lighting" ? `, ${t.lumens ?? DEFAULT_LUMENS} lm` : ""} <span class="muted">(${esc(t.source)})</span></li>`;
  };
  const rows = proposal.rooms
    .map(
      (r) =>
        `<tr><td>${esc(r.room)}</td><td>${esc(r.level ?? "")}</td><td>${esc(r.roomType)}</td><td>${r.areaM2}</td><td>${r.targetLux}</td><td>${r.estimatedLux ?? ""}</td><td>${r.proposed.lighting}</td><td>${r.proposed.socket}</td><td>${r.proposed.switch}</td><td>${r.proposed.emergency}</td><td>${esc(r.notes.join("; "))}</td></tr>`
    )
    .join("");
  const total = (role: Role) => proposal.placements.filter((p) => p.role === role).length;
  return `<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>הצעת תכנון חשמל</title>
<style>
:root{--bg:#fff;--fg:#1d2733;--muted:#66717d;--line:#d9dee4}
@media (prefers-color-scheme: dark){:root{--bg:#14181d;--fg:#e6eaee;--muted:#9aa5b1;--line:#2c343d}}
body{background:var(--bg);color:var(--fg);font-family:system-ui,"Segoe UI",Arial,sans-serif;margin:0;padding:16px;max-width:1400px}
h1{font-size:22px;margin:0 0 4px} h2{font-size:18px;margin:28px 0 8px} .muted{color:var(--muted)}
svg{width:100%;height:auto;max-height:80vh;border:1px solid var(--line);border-radius:6px;background:#fff}
table{border-collapse:collapse;width:100%;font-size:13px} th,td{border-bottom:1px solid var(--line);padding:6px;text-align:right;vertical-align:top}
.wrap{overflow-x:auto} .legend span{display:inline-block;margin-left:16px}
.dot{display:inline-block;width:10px;height:10px;border-radius:50%;margin-left:4px;vertical-align:middle}
</style></head><body>
<h1>הצעת תכנון חשמל: ${esc(proposal.model)}</h1>
<p class="muted">נוצר ${esc(proposal.createdAt)}. שום דבר לא שורטט עדיין: זו הצעה לאישור. ${total("lighting")} גופי תאורה, ${total("socket")} בתי תקע, ${total("switch")} מפסקים, ${total("emergency")} גופי חירום.</p>
<ul>${typeLine("lighting", "גוף תאורה")}${typeLine("socket", "בית תקע")}${typeLine("switch", "מפסק")}${typeLine("emergency", "חירום")}</ul>
<p class="legend"><span><i class="dot" style="background:${COLORS.lighting}"></i>תאורה מוצעת</span><span><i class="dot" style="background:${COLORS.socket}"></i>בית תקע מוצע</span><span><i class="dot" style="background:${COLORS.switch}"></i>מפסק (S)</span><span><i class="dot" style="background:${COLORS.emergency}"></i>חירום</span><span><i class="dot" style="background:${COLORS.existing}"></i>קיים במודל</span></p>
${[...levels].map(([lv, rooms]) => `<h2>${esc(lv)}</h2>${levelSvg(lv, rooms, snap, proposal.placements)}`).join("")}
<h2>לפי חדר</h2><div class="wrap"><table><thead><tr><th>חדר</th><th>קומה</th><th>סוג</th><th>מ"ר</th><th>יעד לוקס</th><th>לוקס משוער</th><th>תאורה</th><th>בתי תקע</th><th>מפסקים</th><th>חירום</th><th>הערות</th></tr></thead><tbody>${rows}</tbody></table></div>
</body></html>`;
}

export async function proposeDesign(opts: DesignOptions): Promise<DesignResult> {
  const snap = loadSnapshot(opts.snapshotPath);
  const rules = loadRules(opts.rulesPath);
  const proposal = designRooms(snap, rules, opts);
  proposal.snapshot = opts.snapshotPath;
  const base = opts.outputPath ? opts.outputPath.replace(/\.json$/i, "") : opts.snapshotPath.replace(/\.json$/i, "") + "_DESIGN";
  const proposalPath = base + ".json";
  const previewPath = base + ".html";
  fs.mkdirSync(path.dirname(proposalPath), { recursive: true });
  fs.writeFileSync(proposalPath, JSON.stringify(proposal, null, 1), "utf8");
  fs.writeFileSync(previewPath, previewHtml(snap, proposal), "utf8");
  const count = (role: Role) => proposal.placements.filter((p) => p.role === role).length;
  const describe = (role: Role) => {
    const t = proposal.types[role];
    return `${[t.familyName, t.typeName].filter(Boolean).join(" : ")}${t.typeId ? ` (id ${t.typeId})` : ""} [${t.source}]`;
  };
  const notes: string[] = [];
  if (!snap.doors) notes.push("Snapshot has no doors (older export): switches and emergency lights by doors were not proposed.");
  if (!proposal.types.lighting.lumens) notes.push(`Fixture flux unknown; lighting counts assume ${DEFAULT_LUMENS} lm. Pass fixture.lumens for real numbers.`);
  return {
    proposalPath,
    previewPath,
    rooms: proposal.rooms.length,
    roomsSkipped: proposal.rooms.filter((r) => Object.values(r.proposed).every((n) => n === 0)).length,
    placements: { lighting: count("lighting"), socket: count("socket"), switch: count("switch"), emergency: count("emergency") },
    types: { lighting: describe("lighting"), socket: describe("socket"), switch: describe("switch"), emergency: describe("emergency") },
    notes,
  };
}

export function loadProposal(proposalPath: string): DesignProposal {
  const p = JSON.parse(fs.readFileSync(proposalPath, "utf8").replace(/^﻿/, ""));
  if (!p || !Array.isArray(p.placements)) throw new Error(`${proposalPath} is not a design proposal (run propose_electrical_design first)`);
  return p as DesignProposal;
}
