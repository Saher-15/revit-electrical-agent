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
import { replicationPreviewHtml } from "../pattern/adapters/revit-rooms/preview.js";
import { listApartments, detectMirrored } from "../pattern/adapters/revit-rooms/units.js";

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
    "list_design_units",
    "Lists the apartments (units) of a snapshot: rooms grouped by room-number prefix (e.g. '03-01'), each with its room types, area, a type label (A, B, C: same room-type " +
      "signature and area within 15 %), and the count of electrical devices already designed in it. Use it to find the template apartment (most devices) and the targets " +
      "(same type, other floors) before learn/replicate.",
    { snapshotPath: z.string(), levelName: z.string().optional(), apartmentPattern: z.string().optional().describe("Regex with one capture group applied to the room number to get the apartment id; default splits on - . _ /"),
      officeConfigPath: z.string().optional() },
    async (a) => {
      try {
        const cfg = loadOfficeConfig(a.officeConfigPath);
        const snap = loadSnapshot(a.snapshotPath);
        const apts = listApartments(snap, cfg, { levelName: a.levelName, apartmentPattern: a.apartmentPattern });
        const devicesByRoom = new Map<string, number>();
        for (const e of snap.elements) if (e.roomKey) devicesByRoom.set(e.roomKey, (devicesByRoom.get(e.roomKey) ?? 0) + 1);
        const rows = apts.map((x) => ({ id: x.id, level: x.level, type: x.type, areaM2: x.areaM2, rooms: x.rooms.length, roomTypes: x.signature, devices: x.roomKeys.reduce((s, k) => s + (devicesByRoom.get(k) ?? 0), 0), roomKeys: x.roomKeys }));
        return text({ apartments: rows.length, byType: rows.reduce<Record<string, number>>((m, r) => { m[r.type!] = (m[r.type!] ?? 0) + 1; return m; }, {}), rows });
      } catch (e) { return fail("list_design_units", e); }
    }
  );

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
        // Signature, area and outlines travel with the pattern so another floor's snapshot can find the same type and mirror.
        const templateSignature = b.rooms.map((r) => b.roomTypes[r.key]).sort().join(",");
        const templateAreaM2 = Math.round(b.rooms.reduce((s, r) => s + r.areaM2, 0) * 10) / 10;
        const templateRooms = b.rooms.map((r) => ({ key: r.key, name: r.name, number: r.number, areaM2: r.areaM2, boundaryMm: r.boundaryMm, centerMm: r.centerMm }));
        fs.writeFileSync(out, JSON.stringify({ ...pattern, levelName: b.levelName, roomTypes: b.roomTypes, templateRoomKeys: b.rooms.map((r) => r.key), templateSignature, templateAreaM2, templateRooms }, null, 1), "utf8");
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
    { patternPath: z.string().describe("Pattern JSON; or give patternPaths for one pattern per apartment type"), patternPaths: z.array(z.string()).optional()
        .describe("Several patterns (one template apartment per type); each target gets the pattern whose template apartment has the same type label"), snapshotPath: z.string(), targets: z.array(z.object({ ...selector, id: z.string().optional(), mirrored: z.boolean().optional() })).optional().describe("One selector per target unit"),
      targetsAuto: z.object({ levelName: z.string().optional(), sameTypeOnly: z.boolean().optional().default(true), apartmentPattern: z.string().optional() }).optional()
        .describe("Instead of targets: every apartment found by list_design_units (optionally on one level), excluding the template, same type as the template by default"),
      detectMirror: z.boolean().optional().default(true).describe("Flip left/right offsets for targets whose room layout mirrors the template"),
      officeConfigPath: z.string().optional(), outputPath: z.string().optional().describe("Proposal JSON path; default <pattern>_REPLICATE.json") },
    async (a) => {
      try {
        const cfg = loadOfficeConfig(a.officeConfigPath);
        const snap = loadSnapshot(a.snapshotPath);
        type StoredPattern = Pattern & { templateRoomKeys?: string[]; templateSignature?: string; templateAreaM2?: number; templateRooms?: any[] };
        const patterns: StoredPattern[] = (a.patternPaths?.length ? a.patternPaths : [a.patternPath]).map((p) => JSON.parse(fs.readFileSync(p, "utf8")));
        const allApts = listApartments(snap, cfg, { apartmentPattern: a.targetsAuto?.apartmentPattern });
        const typeOf = (roomKeys: string[] | undefined) => allApts.find((x) => roomKeys?.some((k) => x.roomKeys.includes(k)))?.type;
        const patternByType = new Map<string | undefined, StoredPattern>();
        // Template from another snapshot (another floor): its keys are not here, so match by room-type signature and area (same 15 % as the type labels).
        const typeOfPattern = (p: StoredPattern) => typeOf(p.templateRoomKeys)
          ?? allApts.find((x) => x.signature === p.templateSignature && Math.abs(x.areaM2 - (p.templateAreaM2 ?? 0)) / Math.max(x.areaM2, 1) <= 0.15)?.type;
        for (const p of patterns) patternByType.set(typeOfPattern(p), p);
        let targets = (a.targets ?? []) as (UnitSelector & { mirrored?: boolean })[];
        if (a.targetsAuto) {
          const apts = a.targetsAuto.levelName ? allApts.filter((x) => (x.level ?? "").toLowerCase().includes(a.targetsAuto!.levelName!.toLowerCase())) : allApts;
          const templateIds = new Set(patterns.map((p) => allApts.find((x) => x.roomKeys.some((k) => p.templateRoomKeys?.includes(k)))?.id));
          targets = apts
            .filter((x) => !templateIds.has(x.id) && (!a.targetsAuto!.sameTypeOnly || patternByType.has(x.type)))
            .map((x) => ({ id: x.id, roomKeys: x.roomKeys }));
          if (!targets.length) return text({ error: "targetsAuto found no target apartments", templateTypes: [...patternByType.keys()], apartments: apts.map((x) => ({ id: x.id, type: x.type, level: x.level })) });
        }
        if (!targets.length) return text({ error: "give targets or targetsAuto" });
        const all: Placed[] = []; const placements: any[] = []; const rooms: any[] = []; const perUnit: any[] = []; const unitsRooms: { unit: string; level?: string; rooms: any[] }[] = [];
        for (const t of targets) {
          const probe = buildUnit(snap, t, cfg);
          const targetType = typeOf(probe.rooms.map((r) => r.key));
          const pattern = patternByType.get(targetType) ?? patterns[0];
          const here = snap.rooms.filter((r) => pattern.templateRoomKeys?.includes(r.key));
          const templateRooms = here.length ? here : (pattern.templateRooms ?? []);
          if (t.mirrored === undefined && a.detectMirror && templateRooms.length) t.mirrored = detectMirrored(templateRooms, probe.rooms, cfg).mirrored;
          const b = buildUnit(snap, t, cfg);
          const existing: Pt3[] = b.unit.designed.map((d) => d.point);
          const constraints = hostingConstraints(cfg, existing);
          const placed = replicate(pattern, b.unit, constraints, { slideStepMm: cfg.hosting.slideStep, slideMaxMm: cfg.hosting.slideMax, fallbackKinds: ["wall"] });
          for (const p of placed) if (p.point) existing.push(p.point);
          all.push(...placed);
          placements.push(...toPlacements(placed, b.levelName, b.unit.id));
          rooms.push(...b.rooms.map((r) => ({ key: r.key, room: `${r.number} ${r.name}`, level: r.level, roomType: b.roomTypes[r.key], areaM2: r.areaM2 })));
          perUnit.push({ unit: b.unit.id, level: b.levelName, type: targetType, pattern: pattern.templateUnitId, mirrored: !!t.mirrored, ...summarize(placed), warnings: b.warnings });
          unitsRooms.push({ unit: b.unit.id, level: b.levelName, rooms: b.rooms });
        }
        const out = a.outputPath ?? a.patternPath.replace(/\.json$/i, "") + "_REPLICATE.json";
        fs.writeFileSync(out, JSON.stringify({ kind: "replication", patternPaths: a.patternPaths ?? [a.patternPath], snapshotPath: a.snapshotPath, createdAt: new Date().toISOString(), rooms, placements, details: all }, null, 1), "utf8");
        const previewPath = out.replace(/\.json$/i, "") + ".html";
        fs.writeFileSync(previewPath, replicationPreviewHtml(snap, unitsRooms, all), "utf8");
        return text({ proposalPath: out, previewPath, placements: placements.length, ...summarize(all), perUnit });
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
