// Office configuration for the Revit rooms adapter. The engineer's office copies
// office-config/default.json to Documents\RevitClaude\office.json and edits it; nothing here is office-specific code.
import fs from "fs";
import path from "path";
import os from "os";

export interface RoomTypeCfg { id: string; keywords: string[]; lux?: number }
export interface AnchorCfg { kind: string; keywords: string[]; blocksWall?: boolean; blockHeight?: [number, number]; wet?: boolean }
export interface OfficeConfig {
  office: string;
  units: "mm";
  heights: Record<string, number>;
  families: Record<string, { familyName?: string; typeName?: string; lumens?: number } | string>;
  hosting: {
    minWallThickness: number;
    nonCleanWallKeywords: string[];
    clearFromDoorFrame: number;
    switchFromDoorFrame: number;
    clearFromWindow: number;
    clearBetweenDevices: number;
    slideStep: number;
    slideMax: number;
    wetZones: { socketMinFromShowerEdge: number; socketMinHeightZone2: number };
    /** A wall device's location point sits inside the wall thickness, outside every room polygon: assign it to the nearest room within this distance. */
    wallAssignMaxMm: number;
  };
  roomTypes: RoomTypeCfg[];
  anchors: AnchorCfg[];
  scheduleLevel: { always: boolean; parameter: string };
  /** How placements are written to the model (office.json "placement"); all optional. */
  placement?: PlacementCfg;
}

export interface PlacementCfg {
  /** Elements per place request; Revit times out above ~5. */
  batchSize?: number;
  refreshViewAfterCommit?: boolean;
  requireActiveDocumentMatch?: boolean;
  /** Provenance written to a parameter (default Comments) with per-kind colours. */
  provenance?: { parameter: string; derived: string; own: string; colors?: Record<string, number[]> };
  /** Instance parameters forced on every placed family whose name contains familyMatch (case-insensitive), e.g. WP families' "Symbol offset" = 0. */
  instanceParams?: { familyMatch: string; set: Record<string, string | number> }[];
  /** Category name -> user workset name. */
  worksets?: Record<string, string>;
}

export const DEFAULT_OFFICE_CONFIG: OfficeConfig = {
  office: "default (Israel, residential)",
  units: "mm",
  heights: { socket: 300, socketBedside: 400, socketCounter: 1150, socketBathroom: 1200, socketLaundry: 1200, switch: 1100, switchBedside: 700,
    tv: 1200, data: 300, ac: 2200, hood: 2000, panelTop: 1800, intercom: 1400, bell: 1400, cableTray: 2900, conduit: 2800 },
  families: { familyPrefix: "TPZ " },
  hosting: {
    minWallThickness: 70,
    nonCleanWallKeywords: ["זכוכית", "glass", "curtain", "מעקה", "railing", "shaft", "פיר", "ויטרינה"],
    clearFromDoorFrame: 100, switchFromDoorFrame: 150, clearFromWindow: 100, clearBetweenDevices: 100, slideStep: 100, slideMax: 600,
    wetZones: { socketMinFromShowerEdge: 600, socketMinHeightZone2: 1200 },
    wallAssignMaxMm: 400,
  },
  roomTypes: [
    { id: "master_bedroom", keywords: ["חדר שינה הורים", "ח. הורים", "הורים", "master", "mbr"] },
    { id: "bedroom", keywords: ["חדר שינה", "ח.שינה", "ח. שינה", "ח שינה", "ילדים", "bedroom"] },
    { id: "living", keywords: ["סלון", "מגורים", "living"] },
    { id: "dining", keywords: ["פינת אוכל", "dining"] },
    { id: "kitchen", keywords: ["מטבח", "kitchen"] },
    { id: "bathroom", keywords: ["אמבטיה", "רחצה", "מקלחת", "bath", "shower"] },
    { id: "toilet", keywords: ["שירותים", "ש.אורחים", "ש. אורחים", "שירותי אורחים", "אורחים", "wc", "toilet", "guest wc"] },
    { id: "mamad", keywords: ["ממ\"ד", "ממד", "מרחב מוגן", "protected", "shelter"] },
    { id: "laundry", keywords: ["כביסה", "שירות", "laundry", "utility"] },
    { id: "corridor", keywords: ["מסדרון", "פרוזדור", "hall", "corridor"] },
    { id: "circulation", keywords: ["מדרגות", "stair", "מעלית", "elevator", "lift", "לובי", "lobby", "מבואה", "פיר", "shaft", "חדר אשפה", "garbage"] },
    { id: "balcony", keywords: ["מרפסת", "balcony", "terrace"] },
    { id: "storage", keywords: ["מחסן", "storage"] },
    { id: "meeting", keywords: ["חדר ישיבות", "meeting"] },
    { id: "office", keywords: ["משרד", "office"] },
  ],
  anchors: [
    { kind: "bed", keywords: ["מיטה", "bed"] },
    { kind: "nightstand", keywords: ["שידה", "nightstand"] },
    { kind: "wardrobe", keywords: ["ארון", "closet", "wardrobe"], blocksWall: true },
    { kind: "tv", keywords: ["טלוויזיה", "tv", "מזנון"] },
    { kind: "desk", keywords: ["שולחן עבודה", "desk"] },
    { kind: "sofa", keywords: ["ספה", "sofa", "ספה מיטה", "sofa bed", "sofabed"] },
    { kind: "kitchen_counter", keywords: ["משטח", "counter", "תחתון", "base cabinet"], blocksWall: true, blockHeight: [0, 900] },
    { kind: "upper_cabinet", keywords: ["עליון", "wall cabinet"], blocksWall: true, blockHeight: [1400, 2200] },
    { kind: "sink", keywords: ["כיור", "sink"] },
    { kind: "cooktop", keywords: ["כיריים", "cooktop", "hob"] },
    { kind: "oven", keywords: ["תנור", "oven"] },
    { kind: "fridge", keywords: ["מקרר", "fridge", "refrigerator"], blocksWall: true },
    { kind: "dishwasher", keywords: ["מדיח", "dishwasher"] },
    { kind: "washing_machine", keywords: ["מכונת כביסה", "washer", "washing"] },
    { kind: "dryer", keywords: ["מייבש", "dryer"] },
    { kind: "toilet", keywords: ["אסלה", "toilet", "wc"] },
    { kind: "basin", keywords: ["כיור רחצה", "basin", "lavatory"] },
    { kind: "shower", keywords: ["מקלחת", "shower"], wet: true },
    { kind: "bath", keywords: ["אמבט", "bathtub"], wet: true },
    { kind: "boiler", keywords: ["דוד", "boiler", "water heater"], blocksWall: true },
    { kind: "ac_indoor", keywords: ["מזגן", "מעבה", "fcu", "ac"] },
  ],
  scheduleLevel: { always: true, parameter: "Schedule Level" },
};

export function defaultOfficeConfigPath(): string {
  if (process.env.REVITCLAUDE_ROOT) return path.join(process.env.REVITCLAUDE_ROOT, "office.json");
  const docs = process.env.REVITCLAUDE_HOME || path.join(os.homedir(), "Documents", "RevitClaude");
  return path.join(docs, "office.json");
}

/** Default config merged with the office file (top-level keys; roomTypes/anchors merged by id/kind). */
export function loadOfficeConfig(configPath?: string): OfficeConfig {
  const cfg: OfficeConfig = JSON.parse(JSON.stringify(DEFAULT_OFFICE_CONFIG));
  const file = configPath ?? defaultOfficeConfigPath();
  if (!fs.existsSync(file)) {
    if (configPath) throw new Error(`Office config not found: ${file}`);
    return cfg;
  }
  const custom = JSON.parse(fs.readFileSync(file, "utf8").replace(/^﻿/, ""));
  for (const [k, v] of Object.entries(custom)) {
    if (k === "roomTypes" && Array.isArray(v)) {
      for (const rt of v as RoomTypeCfg[]) {
        const i = cfg.roomTypes.findIndex((x) => x.id === rt.id);
        if (i >= 0) cfg.roomTypes[i] = { ...cfg.roomTypes[i], ...rt }; else cfg.roomTypes.unshift(rt);
      }
    } else if (k === "anchors" && Array.isArray(v)) {
      for (const a of v as AnchorCfg[]) {
        const i = cfg.anchors.findIndex((x) => x.kind === a.kind);
        if (i >= 0) cfg.anchors[i] = { ...cfg.anchors[i], ...a }; else cfg.anchors.unshift(a);
      }
    } else if (v && typeof v === "object" && !Array.isArray(v) && k in cfg && typeof (cfg as any)[k] === "object") {
      (cfg as any)[k] = { ...(cfg as any)[k], ...(v as object) };
    } else {
      (cfg as any)[k] = v;
    }
  }
  return cfg;
}

const norm = (s: string) => s.toLowerCase().replace(/["'`״׳]/g, "");

export function roomTypeId(name: string, cfg: OfficeConfig): string {
  const text = norm(name ?? "");
  return cfg.roomTypes.find((rt) => rt.keywords.some((k) => text.includes(norm(k))))?.id ?? "other";
}

export function anchorKind(familyAndType: string, cfg: OfficeConfig): AnchorCfg | undefined {
  const text = norm(familyAndType ?? "");
  // Longest keyword first, so "כיור רחצה" wins over "כיור" and "חדר שינה הורים" over "חדר שינה".
  const hits = cfg.anchors
    .map((a) => ({ a, k: a.keywords.filter((k) => text.includes(norm(k))).sort((x, y) => y.length - x.length)[0] }))
    .filter((h) => h.k);
  hits.sort((x, y) => y.k!.length - x.k!.length);
  return hits[0]?.a;
}
