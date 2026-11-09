import fs from "fs";
import { SnapshotElement, SnapshotRoom } from "./snapshot.js";

// Design and review rules shared by review_model and propose_electrical_design.
// The defaults are a starting point (EN 12464-1 lux levels, common Israeli practice), not a standard
// anybody signed: an office overrides them with a JSON file of the same shape.

export interface RoomTypeRule {
  id: string;
  /** Hebrew and English words looked for in the room name, case-insensitive. */
  match: string[];
  /** Maintained illuminance target in lux. */
  lux: number;
  /** One socket every this many metres of wall. 0 = no sockets by spacing. */
  socketSpacingM: number;
  minSockets: number;
  /** Needs emergency lighting by every exit door. */
  emergency: boolean;
  /** Gets a light switch by each door. */
  switches: boolean;
}

export interface DesignRules {
  roomTypes: RoomTypeRule[];
  defaultRoomType: RoomTypeRule;
  /** Lumen method factors. */
  utilisationFactor: number;
  maintenanceFactor: number;
  /** Mounting heights above the room's floor, mm. */
  socketHeightMm: number;
  switchHeightMm: number;
  ceilingHeightMm: number;
  /** Largest distance between neighbouring fixtures, for uniformity (corridors, low lux rooms). */
  maxFixtureSpacingM: number;
  /** Review thresholds. */
  maxCircuitLoadRatio: number;
  maxVoltageDropLightingPct: number;
  maxVoltageDropOtherPct: number;
  maxPhaseImbalancePct: number;
  maxDevicesPerPowerCircuit: number;
  maxDevicesPerLightingCircuit: number;
  maxLightingPowerDensityWm2: number;
  /** Copper resistivity at operating temperature, ohm*mm2/m. */
  copperResistivity: number;
  /** Rooms smaller than this are ignored by room checks and design (shafts, ducts). */
  minRoomAreaM2: number;
}

const rule = (
  id: string,
  match: string[],
  lux: number,
  socketSpacingM: number,
  minSockets: number,
  emergency: boolean,
  switches = true
): RoomTypeRule => ({ id, match, lux, socketSpacingM, minSockets, emergency, switches });

export const DEFAULT_RULES: DesignRules = {
  roomTypes: [
    rule("protected-room", ['ממ"ד', "ממד", 'ממ"ק', "ממק", "מרחב מוגן", "מקלט", "shelter", "safe room"], 300, 3, 3, true),
    rule("electrical-room", ["חדר חשמל", "חשמל", "תקשורת", "ארון", "electrical", "electric", "server", "comms", "it room"], 300, 3, 2, true),
    rule("meeting", ["ישיבות", "ישיבה", "דיונים", "meeting", "conference", "board"], 500, 3, 4, false),
    rule("office", ["משרד", "עבודה", "מנהל", "office", "open space", "workspace"], 500, 3, 4, false),
    rule("classroom", ["כיתה", "class", "הדרכה", "training"], 500, 4, 4, false),
    rule("kitchen", ["מטבח", "מטבחון", "kitchen", "pantry", "kitchenette"], 300, 1.5, 4, false),
    rule("toilet", ["שירותים", "שרותים", "מקלחת", "רחצה", "wc", "toilet", "restroom", "bath", "shower"], 200, 0, 0, false),
    rule("corridor", ["מסדרון", "מעבר", "פרוזדור", "corridor", "hall", "passage"], 100, 15, 0, true),
    rule("stair", ["מדרגות", "חדר מדרגות", "stair"], 150, 0, 0, true, false),
    rule("lobby", ["לובי", "כניסה", "המתנה", "lobby", "entrance", "reception", "waiting"], 200, 8, 1, true),
    rule("parking", ["חניה", "חניון", "parking", "garage"], 75, 0, 0, true, false),
    rule("storage", ["מחסן", "אחסון", "storage", "store"], 150, 0, 1, false),
    rule("living", ["סלון", "מגורים", "living", "family room"], 200, 3, 4, false),
    rule("bedroom", ["שינה", "חדר הורים", "bedroom"], 150, 3, 3, false),
    rule("dining", ["פינת אוכל", "חדר אוכל", "dining", "cafeteria"], 200, 4, 2, false),
  ],
  defaultRoomType: rule("other", [], 300, 4, 2, false),
  utilisationFactor: 0.6,
  maintenanceFactor: 0.8,
  socketHeightMm: 300,
  switchHeightMm: 1100,
  ceilingHeightMm: 2700,
  maxFixtureSpacingM: 4.5,
  maxCircuitLoadRatio: 0.8,
  maxVoltageDropLightingPct: 3,
  maxVoltageDropOtherPct: 5,
  maxPhaseImbalancePct: 15,
  maxDevicesPerPowerCircuit: 10,
  maxDevicesPerLightingCircuit: 20,
  maxLightingPowerDensityWm2: 12,
  copperResistivity: 0.0225,
  minRoomAreaM2: 2,
};

/** Defaults, with any fields of the JSON file at rulesPath laid over them. Room types in the file replace the defaults by id or are added. */
export function loadRules(rulesPath?: string): DesignRules {
  const rules: DesignRules = JSON.parse(JSON.stringify(DEFAULT_RULES));
  if (!rulesPath) return rules;
  if (!fs.existsSync(rulesPath)) throw new Error(`Rules file not found: ${rulesPath}`);
  const custom = JSON.parse(fs.readFileSync(rulesPath, "utf8").replace(/^﻿/, ""));
  for (const [k, v] of Object.entries(custom)) {
    if (k === "roomTypes" && Array.isArray(v)) {
      for (const rt of v as RoomTypeRule[]) {
        const i = rules.roomTypes.findIndex((x) => x.id === rt.id);
        if (i >= 0) rules.roomTypes[i] = { ...rules.roomTypes[i], ...rt };
        else rules.roomTypes.unshift({ ...DEFAULT_RULES.defaultRoomType, ...rt });
      }
    } else if (k === "defaultRoomType" && typeof v === "object") {
      rules.defaultRoomType = { ...rules.defaultRoomType, ...(v as RoomTypeRule) };
    } else if (k in rules) {
      (rules as any)[k] = v;
    }
  }
  return rules;
}

export function roomTypeOf(room: SnapshotRoom, rules: DesignRules): RoomTypeRule {
  const text = `${room.name ?? ""} ${room.department ?? ""}`.toLowerCase();
  return rules.roomTypes.find((rt) => rt.match.some((m) => text.includes(m.toLowerCase()))) ?? rules.defaultRoomType;
}

// ---- element classification ----

const EMERGENCY = /emerg|חירום|exit|יציאה|מילוט|escape/i;
const SWITCH = /switch|מפסק|dimmer|עמעם|לחצן/i;

export const isLighting = (e: SnapshotElement) => e.builtInCategory === "OST_LightingFixtures";
export const isEmergencyLight = (e: SnapshotElement) =>
  isLighting(e) && EMERGENCY.test(`${e.family ?? ""} ${e.type ?? ""} ${e.parameters?.["Description"] ?? ""}`);
export const isSocket = (e: SnapshotElement) =>
  e.builtInCategory === "OST_ElectricalFixtures" && !SWITCH.test(`${e.family ?? ""} ${e.type ?? ""}`);
export const isSwitch = (e: SnapshotElement) =>
  e.builtInCategory === "OST_LightingDevices" || (e.builtInCategory === "OST_ElectricalFixtures" && SWITCH.test(`${e.family ?? ""} ${e.type ?? ""}`));

/** First number in a parameter value such as "3600 lm", "36 W", "1 200 VA". */
export function num(value: string | undefined): number | undefined {
  if (!value) return undefined;
  const m = value.replace(/(\d)[\s,](?=\d{3}\b)/g, "$1").match(/-?\d+(\.\d+)?/);
  return m ? Number(m[0]) : undefined;
}

/** Luminous flux of a fixture from its parameters, if the family carries it. */
export function lumensOf(e: SnapshotElement): number | undefined {
  for (const [k, v] of Object.entries(e.parameters ?? {})) {
    if (/lm\b|lumen/i.test(v) || /lumen|flux|שטף/i.test(k)) {
      const n = num(v);
      if (n && n > 50) return n;
    }
  }
  return undefined;
}

export function wattsOf(e: SnapshotElement): number | undefined {
  const w = num(e.parameters?.["Wattage"]);
  if (w && w > 0) return w;
  const va = num(e.parameters?.["Apparent Load"]);
  return va && va > 0 ? va : undefined;
}

/** Conductor cross-section in mm2 from a Revit wire size text: "3x2.5", "2.5 mm²", "3#12, 1#12G" (AWG). */
export function crossSectionMm2(wireSize: string | undefined): number | undefined {
  if (!wireSize) return undefined;
  const awg = wireSize.match(/#\s*(\d+)/);
  if (awg) {
    const n = Number(awg[1]);
    return Math.round(0.012668 * Math.pow(92, (36 - n) / 19.5) * 100) / 100;
  }
  const x = wireSize.match(/\d+\s*[x×*]\s*(\d+(?:\.\d+)?)/i);
  if (x) return Number(x[1]);
  const mm = wireSize.match(/(\d+(?:\.\d+)?)\s*mm/i);
  if (mm) return Number(mm[1]);
  const plain = Number(wireSize.trim());
  return isFinite(plain) && plain > 0 && plain <= 400 ? plain : undefined;
}

// ---- 2D geometry on room outlines (mm) ----

export type Pt2 = [number, number];

export function signedArea(poly: Pt2[]): number {
  let a = 0;
  for (let i = 0; i < poly.length; i++) {
    const [x1, y1] = poly[i];
    const [x2, y2] = poly[(i + 1) % poly.length];
    a += x1 * y2 - x2 * y1;
  }
  return a / 2;
}

export function pointInPolygon([x, y]: Pt2, poly: Pt2[]): boolean {
  let inside = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const [xi, yi] = poly[i];
    const [xj, yj] = poly[j];
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}

export function distanceToSegment([px, py]: Pt2, [ax, ay]: Pt2, [bx, by]: Pt2): number {
  const dx = bx - ax;
  const dy = by - ay;
  const len2 = dx * dx + dy * dy;
  const t = len2 === 0 ? 0 : Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / len2));
  return Math.hypot(px - (ax + t * dx), py - (ay + t * dy));
}

export function distanceToPolygonEdge(p: Pt2, poly: Pt2[]): number {
  let d = Infinity;
  for (let i = 0; i < poly.length; i++) d = Math.min(d, distanceToSegment(p, poly[i], poly[(i + 1) % poly.length]));
  return d;
}

/** Drops consecutive points closer than tolMm and collinear middle points, so tessellated arcs and splits don't make tiny edges. */
export function simplify(poly: Pt2[], tolMm = 20): Pt2[] {
  const out: Pt2[] = [];
  for (const p of poly) {
    const last = out[out.length - 1];
    if (!last || Math.hypot(p[0] - last[0], p[1] - last[1]) > tolMm) out.push(p);
  }
  if (out.length > 2 && Math.hypot(out[0][0] - out[out.length - 1][0], out[0][1] - out[out.length - 1][1]) <= tolMm) out.pop();
  let changed = true;
  while (changed && out.length > 3) {
    changed = false;
    for (let i = 0; i < out.length; i++) {
      const a = out[(i + out.length - 1) % out.length];
      const b = out[i];
      const c = out[(i + 1) % out.length];
      if (distanceToSegment(b, a, c) < tolMm / 2) {
        out.splice(i, 1);
        changed = true;
        break;
      }
    }
  }
  return out;
}
