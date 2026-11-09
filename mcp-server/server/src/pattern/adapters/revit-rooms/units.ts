// Finding units (apartments) in a snapshot, grouping them into types, and detecting mirrored plans.
import { ModelSnapshot, SnapshotRoom } from "../../../analysis/snapshot.js";
import { OfficeConfig, roomTypeId } from "./config.js";

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

export function listApartments(snap: ModelSnapshot, cfg: OfficeConfig, opts: { levelName?: string; apartmentPattern?: string; minRooms?: number } = {}): ApartmentInfo[] {
  const minRooms = opts.minRooms ?? 2;
  const rooms = snap.rooms.filter((r) => r.kind === "Room" && r.boundaryMm && r.boundaryMm.length >= 3 && (!opts.levelName || (r.level ?? "").toLowerCase().includes(opts.levelName.toLowerCase())));
  const groups = new Map<string, SnapshotRoom[]>();
  for (const r of rooms) {
    const id = apartmentIdOf(r, opts.apartmentPattern);
    groups.set(id, [...(groups.get(id) ?? []), r]);
  }
  const out: ApartmentInfo[] = [];
  for (const [id, rs] of groups) {
    if (rs.length < minRooms) continue;
    const typed = rs.map((r) => ({ key: r.key, number: r.number, name: r.name, type: roomTypeId(r.name, cfg), areaM2: r.areaM2 }));
    out.push({ id, level: rs[0].level, roomKeys: rs.map((r) => r.key), rooms: typed, signature: typed.map((t) => t.type).sort().join(","), areaM2: Math.round(rs.reduce((s, r) => s + r.areaM2, 0) * 10) / 10 });
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
