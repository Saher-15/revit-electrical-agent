// MCP tools of the pattern engine: read one designed example (a unit), learn its rules,
// replicate them to the other units, check a single hosting point. Revit-free: they work on a
// snapshot from export_model_snapshot and write a proposal that apply_electrical_design draws.
import fs from "fs";
import path from "path";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { loadSnapshot } from "../analysis/snapshot.js";
import { learn, replicate, summarize, Pattern, Pt3, Placed } from "../pattern/core.js";
import { buildUnit, hostingConstraints, toPlacements, UnitSelector } from "../pattern/adapters/revit-rooms/adapter.js";
import { loadOfficeConfig } from "../pattern/adapters/revit-rooms/config.js";

const selector = {
  roomKeys: z.array(z.string()).optional().describe("Room keys of the unit (from the snapshot)"),
  roomFilter: z.string().optional().describe("Rooms whose number/name/key contains this text, e.g. the apartment number '03-01'"),
  levelName: z.string().optional().describe("Only rooms on levels whose name contains this"),
};
const ANCHOR_PRIORITY = ["bed", "nightstand", "desk", "tv", "sofa", "kitchen_counter", "sink", "cooktop", "oven", "fridge", "dishwasher", "washing_machine", "dryer",
  "toilet", "basin", "shower", "bath", "boiler", "ac_indoor", "wardrobe", "door", "window", "wall", "room"];

export const ANCHORS_BY_ROLE: Record<string, string[]> = { light: ["room"], emergency: ["door", "room"], switch: ["door", "bed", "nightstand", "wall"], shutter: ["window", "wall"], panel: ["door", "wall"] };

const text = (v: unknown) => ({ content: [{ type: "text" as const, text: typeof v === "string" ? v : JSON.stringify(v, null, 2) }] });
const fail = (name: string, e: unknown) => text(`${name} failed: ${e instanceof Error ? e.message : String(e)}`);

export function registerDesignPatternTools(server: McpServer) {
  server.tool(
    "read_design_unit",
    "Reads one unit of a repeating object from a snapshot: for Revit rooms, an apartment (group of rooms) or a room. Returns room types, the anchors found " +
      "(walls with clean/non-clean tags, doors with handle side, windows, furniture and fixtures classified by the office config: bed, wardrobe, TV, kitchen counter, " +
      "sink, shower, washing machine...), and the electrical devices already designed there with their role. Use it to show the engineer what Claude understood " +
      "of the example before learning from it. Needs a snapshot with rooms boundaries; furniture/windows/walls need schemaVersion 2 (export_model_snapshot with includeArchitecture).",
    { snapshotPath: z.string(), ...selector, officeConfigPath: z.string().optional().describe("office.json; default Documents\\RevitClaude\\office.json or built-in defaults") },
    async (a) => {
      try {
        const cfg = loadOfficeConfig(a.officeConfigPath);
        const snap = loadSnapshot(a.snapshotPath);
        const b = buildUnit(snap, a as UnitSelector, cfg);
        const byKind: Record<string, number> = {};
        for (const f of b.unit.features) byKind[f.kind] = (byKind[f.kind] ?? 0) + 1;
        const byRole: Record<string, number> = {};
        for (const d of b.unit.designed) byRole[d.role] = (byRole[d.role] ?? 0) + 1;
        return text({ unitId: b.unit.id, unitType: b.unit.type, level: b.levelName, baseZ: b.unit.baseZ, rooms: b.rooms.map((r) => ({ key: r.key, number: r.number, name: r.name, type: b.roomTypes[r.key], areaM2: r.areaM2 })),
          featuresByKind: byKind, designedByRole: byRole, anchors: b.unit.features.filter((f) => !["wall", "room"].includes(f.kind)).map((f) => ({ id: f.id, kind: f.kind, room: f.parent, tags: f.tags, meta: f.meta })),
          warnings: b.warnings, snapshotHasFurniture: !!snap.furniture?.length, snapshotHasWalls: !!snap.walls?.length });
      } catch (e) { return fail("read_design_unit", e); }
    }
  );

  server.tool(
    "learn_design_pattern",
    "Learns the engineer's design of ONE example unit as rules relative to its anchors ('socket 300 mm right of the bed at 400 mm', 'switch 150 mm from the door frame on the " +
      "handle side at 1100', 'light at the room centre'). Writes <snapshot>_PATTERN.json and returns a readable table per room type. Show the table to the engineer and fix " +
      "misread anchors before replicating.",
    { snapshotPath: z.string(), ...selector, officeConfigPath: z.string().optional(), outputPath: z.string().optional().describe("Pattern JSON path; default <snapshot>_PATTERN.json") },
    async (a) => {
      try {
        const cfg = loadOfficeConfig(a.officeConfigPath);
        const snap = loadSnapshot(a.snapshotPath);
        const b = buildUnit(snap, a as UnitSelector, cfg);
        const pattern = learn(b.unit, { anchorPriority: ANCHOR_PRIORITY, keepTagPrefixes: ["handle:"], anchorKindsByRole: ANCHORS_BY_ROLE });
        const out = a.outputPath ?? a.snapshotPath.replace(/\.json$/i, "") + "_PATTERN.json";
        fs.writeFileSync(out, JSON.stringify({ ...pattern, levelName: b.levelName, roomTypes: b.roomTypes }, null, 1), "utf8");
        const table = pattern.rules.map((r) => `${r.parentType} | ${r.role} | ${r.anchorKind}#${r.ordinal}${r.anchorTags ? " " + r.anchorTags.join(",") : ""} | along ${r.along} across ${r.across} | h ${r.height} | ${r.host}`);
        return text({ patternPath: out, unit: b.unit.id, rules: pattern.rules.length, designedDevices: b.unit.designed.length, table, notes: pattern.notes, warnings: b.warnings });
      } catch (e) { return fail("learn_design_pattern", e); }
    }
  );

  server.tool(
    "replicate_design_pattern",
    "Applies a learned pattern to other units (apartments/rooms) of the snapshot, WITHOUT touching Revit: maps rooms by type, finds the same anchors, computes each point, runs " +
      "the hosting checks (non-clean wall, behind wardrobe/cabinets/fridge/boiler, door frame and swing, windows, wet zones, collisions) with slide/mirror fallbacks, and writes " +
      "<pattern>_REPLICATE_<unit>.json in the proposal shape of apply_electrical_design (every placement carries the unit's level as Schedule Level). Returns counts per status and the " +
      "dropped points with reasons. Get the engineer's approval, then apply_electrical_design with the proposal path.",
    { patternPath: z.string(), snapshotPath: z.string(), targets: z.array(z.object({ ...selector, id: z.string().optional(), mirrored: z.boolean().optional() })).min(1).describe("One selector per target unit"),
      officeConfigPath: z.string().optional(), outputPath: z.string().optional().describe("Proposal JSON path; default <pattern>_REPLICATE.json") },
    async (a) => {
      try {
        const cfg = loadOfficeConfig(a.officeConfigPath);
        const snap = loadSnapshot(a.snapshotPath);
        const pattern = JSON.parse(fs.readFileSync(a.patternPath, "utf8")) as Pattern;
        const all: Placed[] = []; const placements: any[] = []; const rooms: any[] = []; const perUnit: any[] = [];
        for (const t of a.targets) {
          const b = buildUnit(snap, t as UnitSelector, cfg);
          const existing: Pt3[] = b.unit.designed.map((d) => d.point);
          const constraints = hostingConstraints(cfg, existing);
          const placed = replicate(pattern, b.unit, constraints, { slideStepMm: cfg.hosting.slideStep, slideMaxMm: cfg.hosting.slideMax, fallbackKinds: ["wall"] });
          for (const p of placed) if (p.point) existing.push(p.point);
          all.push(...placed);
          placements.push(...toPlacements(placed, b.levelName, b.unit.id));
          rooms.push(...b.rooms.map((r) => ({ key: r.key, room: `${r.number} ${r.name}`, level: r.level, roomType: b.roomTypes[r.key], areaM2: r.areaM2 })));
          perUnit.push({ unit: b.unit.id, level: b.levelName, ...summarize(placed), warnings: b.warnings });
        }
        const out = a.outputPath ?? a.patternPath.replace(/\.json$/i, "") + "_REPLICATE.json";
        fs.writeFileSync(out, JSON.stringify({ kind: "replication", patternPath: a.patternPath, snapshotPath: a.snapshotPath, createdAt: new Date().toISOString(), rooms, placements, details: all }, null, 1), "utf8");
        return text({ proposalPath: out, placements: placements.length, ...summarize(all), perUnit });
      } catch (e) { return fail("replicate_design_pattern", e); }
    }
  );

  server.tool(
    "check_hosting",
    "Checks one candidate point for an electrical device against the hosting rules of a unit (non-clean wall, behind furniture, door/window, wet zone, collision) and returns ok / the " +
      "first failing check with its reason. Use it before placing a single device by hand.",
    { snapshotPath: z.string(), ...selector, role: z.string().describe("socket, switch, light, tv, data, ac, panel..."), x: z.number(), y: z.number(), z: z.number().describe("absolute mm"),
      officeConfigPath: z.string().optional() },
    async (a) => {
      try {
        const cfg = loadOfficeConfig(a.officeConfigPath);
        const snap = loadSnapshot(a.snapshotPath);
        const b = buildUnit(snap, a as UnitSelector, cfg);
        const roomFeat = b.unit.features.find((f) => f.kind === "room" && insideRoom(b, f.parent, a.x, a.y));
        if (!roomFeat) return text({ ok: false, reason: "point is not inside a room of the unit" });
        const c = { ruleId: "manual", role: a.role, parent: roomFeat.parent, feature: roomFeat, point: [a.x, a.y, a.z] as Pt3, heightMm: a.z - b.unit.baseZ };
        for (const k of hostingConstraints(cfg, b.unit.designed.map((d) => d.point))) { const v = k(c, b.unit); if (!v.ok) return text({ room: roomFeat.parent, ...v }); }
        return text({ ok: true, room: roomFeat.parent, roomType: b.roomTypes[roomFeat.parent] });
      } catch (e) { return fail("check_hosting", e); }
    }
  );
}

function insideRoom(b: ReturnType<typeof buildUnit>, key: string, x: number, y: number): boolean {
  const r = b.rooms.find((r) => r.key === key);
  if (!r?.boundaryMm) return false;
  const poly = r.boundaryMm;
  let inside = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const [xi, yi] = poly[i], [xj, yj] = poly[j];
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}
