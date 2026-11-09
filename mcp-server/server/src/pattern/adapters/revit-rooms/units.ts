// Finding units (apartments) in a snapshot, grouping them into types, and detecting mirrored plans.
import { ModelSnapshot, SnapshotRoom } from "../../../analysis/snapshot.js";
import { OfficeConfig, roomTypeId } from "./config.js";
import { dedupeDoors, dedupeRooms, doorRooms, roomHostLevel } from "./adapter.js";

export interface ApartmentInfo {
  id: string;
  level?: string;
  roomKeys: string[];
  rooms: { key: string; number: string; name: string; type: string; areaM2: number }[];
  /** Sorted room-type multiset, e.g. "bathroom,bedroom,bedroom,kitchen,living,mamad,master_bedroom". */
  signature: string;
  areaM2: number;
  /** Type label shared by apartments with the same signature and similar area (A, B, C...). */
  type?: string;
  mirrored?: boolean;
}

/** Apartment id from a room number: "03-01-3" → "03-01", "03-01" → "03", "301.2" → "301", "0412" → "041"; else the level + number. */
export function apartmentIdOf(room: SnapshotRoom, pattern?: string): string {
  const num = (room.number ?? "").trim();
  if (pattern) {
    const m = new RegExp(pattern).exec(num);
    if (m) return m[1] ?? m[0];
  }
  const parts = num.split(/[-_./ ]+/).filter(Boolean);
  if (parts.length >= 3) return parts.slice(0, 2).join("-");
  if (parts.length === 2) return parts[0]; // "03-01" = apartment 03, room 01 (override with apartmentPattern when the office numbers differ)
  if (/^\d{3,4}$/.test(num)) return num.length === 4 ? num.slice(0, 3) : num;
  return `${room.level ?? ""}:${num || room.key}`;
}

/** Room types that connect apartments to each other and never belong to one. */
const PUBLIC_TYPES = new Set(["corridor", "circulation"]);

/**
 * Group rooms into apartments by the doors between them: rooms joined by a door are one apartment, public rooms
 * (corridors, stairs, lobbies) are left out so the entrance door does not merge the floor. Works for any numbering.
 */
export function groupRoomsByDoors(snap: ModelSnapshot, rooms: SnapshotRoom[], cfg: OfficeConfig): SnapshotRoom[][] {
  const privateRooms = rooms.filter((r) => !PUBLIC_TYPES.has(roomTypeId(r.name, cfg)));
  const parent = new Map<string, string>(privateRooms.map((r) => [r.key, r.key]));
  const find = (k: string): string => { let x = k; while (parent.get(x) !== x) x = parent.get(x)!; parent.set(k, x); return x; };
  const union = (a: string, b: string) => { const ra = find(a), rb = find(b); if (ra !== rb) parent.set(ra, rb); };
  for (const d of dedupeDoors(snap.doors)) {
    const rs = doorRooms(d, privateRooms);
    for (let i = 1; i < rs.length; i++) union(rs[0].key, rs[i].key);
  }
  // A room with no door at all (open kitchen, dining nook) belongs with the room it shares the longest wall with.
  const doored = new Set(dedupeDoors(snap.doors).flatMap((d) => doorRooms(d, privateRooms).map((r) => r.key)));
  for (const r of privateRooms) {
    if (doored.has(r.key)) continue;
    let best: { key: string; shared: number } | undefined;
    for (const o of privateRooms) {
      if (o.key === r.key) continue;
      const shared = sharedBoundaryMm(r, o);
      if (shared > 800 && (!best || shared > best.shared)) best = { key: o.key, shared };
    }
    if (best) union(r.key, best.key);
  }
  const groups = new Map<string, SnapshotRoom[]>();
  for (const r of privateRooms) { const root = find(r.key); groups.set(root, [...(groups.get(root) ?? []), r]); }
  return [...groups.values()];
}

/** Length of boundary the two rooms share: collinear, overlapping edges closer than 350 mm (a wall or an open edge). */
function sharedBoundaryMm(a: SnapshotRoom, b: SnapshotRoom): number {
  const pa = a.boundaryMm!, pb = b.boundaryMm!;
  let total = 0;
  for (let i = 0; i < pa.length; i++) {
    const a1 = pa[i], a2 = pa[(i + 1) % pa.length];
    const dx = a2[0] - a1[0], dy = a2[1] - a1[1], la = Math.hypot(dx, dy);
    if (la < 50) continue;
    const ux = dx / la, uy = dy / la;
    for (let j = 0; j < pb.length; j++) {
      const b1 = pb[j], b2 = pb[(j + 1) % pb.length];
      const ex = b2[0] - b1[0], ey = b2[1] - b1[1], lb = Math.hypot(ex, ey);
      if (lb < 50 || Math.abs(ux * ex + uy * ey) / lb < 0.97) continue;
      const off = Math.abs(-uy * (b1[0] - a1[0]) + ux * (b1[1] - a1[1]));
      if (off > 350) continue;
      const t1 = ux * (b1[0] - a1[0]) + uy * (b1[1] - a1[1]), t2 = ux * (b2[0] - a1[0]) + uy * (b2[1] - a1[1]);
      const lo = Math.max(0, Math.min(t1, t2)), hi = Math.min(la, Math.max(t1, t2));
      if (hi > lo) total += hi - lo;
    }
  }
  return total;
}

/** Numbering like "03-01-3" or "0412" encodes the apartment; a plain running number ("686", "711") does not. */
function numberingEncodesApartment(rooms: SnapshotRoom[]): boolean {
  const nums = rooms.map((r) => (r.number ?? "").trim()).filter(Boolean);
  if (!nums.length) return false;
  const structured = nums.filter((n) => /[-_./ ]/.test(n) || /^\d{4}$/.test(n)).length;
  return structured / nums.length > 0.8;
}

export function listApartments(snap: ModelSnapshot, cfg: OfficeConfig, opts: { levelName?: string; apartmentPattern?: string; minRooms?: number; groupBy?: "auto" | "numbers" | "doors" } = {}): ApartmentInfo[] {
  const minRooms = opts.minRooms ?? 2;
  const rooms = dedupeRooms(snap.rooms.filter((r) => r.kind === "Room" && r.boundaryMm && r.boundaryMm.length >= 3))
    .filter((r) => !opts.levelName || (roomHostLevel(snap, r) ?? r.level ?? "").toLowerCase().includes(opts.levelName.toLowerCase()));
  const byNumbers = opts.groupBy === "numbers" || (opts.groupBy !== "doors" && (!!opts.apartmentPattern || numberingEncodesApartment(rooms)));
  const groups = new Map<string, SnapshotRoom[]>();
  if (byNumbers) {
    for (const r of rooms) {
      const id = apartmentIdOf(r, opts.apartmentPattern);
      groups.set(id, [...(groups.get(id) ?? []), r]);
    }
  } else {
    for (const g of groupRoomsByDoors(snap, rooms, cfg)) {
      const nums = g.map((r) => r.number).filter(Boolean).sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));
      groups.set(`${g[0].level ?? ""}:${nums[0] ?? g[0].key}`, g);
    }
  }
  const out: ApartmentInfo[] = [];
  for (const [id, rs] of groups) {
    if (rs.length < minRooms) continue;
    const typed = rs.map((r) => ({ key: r.key, number: r.number, name: r.name, type: roomTypeId(r.name, cfg), areaM2: r.areaM2 }));
    out.push({ id, level: roomHostLevel(snap, rs[0]) ?? rs[0].level, roomKeys: rs.map((r) => r.key), rooms: typed, signature: typed.map((t) => t.type).sort().join(","), areaM2: Math.round(rs.reduce((s, r) => s + r.areaM2, 0) * 10) / 10 });
  }
  // type labels: same signature and area within 15 % → same type
  const types: { sig: string; area: number; label: string }[] = [];
  let next = 0;
  for (const a of out.sort((x, y) => (x.level ?? "").localeCompare(y.level ?? "") || x.id.localeCompare(y.id))) {
    let t = types.find((t) => t.sig === a.signature && Math.abs(t.area - a.areaM2) / Math.max(t.area, 1) <= 0.15);
    if (!t) { t = { sig: a.signature, area: a.areaM2, label: String.fromCharCode(65 + (next % 26)) + (next >= 26 ? Math.floor(next / 26) : "") }; next++; types.push(t); }
    a.type = t.label;
  }
  return out;
}

/** True when the target's room layout matches the template better as a mirror image (about X or Y) than as a translation. */
export function detectMirrored(template: SnapshotRoom[], target: SnapshotRoom[], cfg: OfficeConfig): { mirrored: boolean; axis?: "x" | "y"; score: { same: number; mirrorX: number; mirrorY: number } } {
  const layout = (rooms: SnapshotRoom[]) => {
    const pts = rooms.flatMap((r) => r.boundaryMm!);
    const cx = (Math.min(...pts.map((p) => p[0])) + Math.max(...pts.map((p) => p[0]))) / 2;
    const cy = (Math.min(...pts.map((p) => p[1])) + Math.max(...pts.map((p) => p[1]))) / 2;
    return rooms.map((r) => {
      const c = r.centerMm ?? [r.boundaryMm!.reduce((s, p) => s + p[0], 0) / r.boundaryMm!.length, r.boundaryMm!.reduce((s, p) => s + p[1], 0) / r.boundaryMm!.length];
      return { type: roomTypeId(r.name, cfg), x: c[0] - cx, y: c[1] - cy, area: r.areaM2 };
    });
  };
  const a = layout(template), b = layout(target);
  const cost = (fx: number, fy: number) => {
    let total = 0;
    for (const r of a) {
      const cands = b.filter((q) => q.type === r.type);
      if (!cands.length) { total += 5000; continue; }
      total += Math.min(...cands.map((q) => Math.hypot(q.x * fx - r.x, q.y * fy - r.y)));
    }
    return Math.round(total / Math.max(a.length, 1));
  };
  const score = { same: cost(1, 1), mirrorX: cost(-1, 1), mirrorY: cost(1, -1) };
  const best = Math.min(score.mirrorX, score.mirrorY);
  if (best < score.same * 0.7) return { mirrored: true, axis: score.mirrorX <= score.mirrorY ? "x" : "y", score };
  return { mirrored: false, score };
}
