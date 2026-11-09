// Pattern engine: learn one apartment, replicate to another; hosting checks; linear (rail) adapter; config sync.
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const { learn, replicate, summarize } = await import("../build/pattern/core.js");
const { buildUnit, hostingConstraints, toPlacements, roleOf } = await import("../build/pattern/adapters/revit-rooms/adapter.js");
const { DEFAULT_OFFICE_CONFIG, loadOfficeConfig, roomTypeId, anchorKind } = await import("../build/pattern/adapters/revit-rooms/config.js");
const { buildLinearUnits } = await import("../build/pattern/adapters/linear/adapter.js");
const { ANCHORS_BY_ROLE } = await import("../build/tools/design_pattern.js");

const here = path.dirname(fileURLToPath(import.meta.url));
const PRIORITY = ["bed", "nightstand", "desk", "tv", "sofa", "kitchen_counter", "sink", "cooktop", "oven", "fridge", "dishwasher", "washing_machine", "dryer",
  "toilet", "basin", "shower", "bath", "boiler", "ac_indoor", "wardrobe", "door", "window", "wall", "room"];
const learnOpts = { anchorPriority: PRIORITY, keepTagPrefixes: ["handle:"], anchorKindsByRole: ANCHORS_BY_ROLE };

/** Synthetic apartment: master bedroom 4000x3500 + kitchen 4000x3000, shifted by dx and with base elevation z. */
function apartment({ prefix, level, dx, z, extraWardrobe = false }) {
  const X = (x) => x + dx;
  const rooms = [
    { key: `${prefix}-MBR`, kind: "Room", source: "Arch", id: 1, number: `${prefix}-01`, name: "חדר שינה הורים", level, areaM2: 14, perimeterM: 15, volumeM3: 38, heightMm: 2700, baseElevationMm: z,
      boundaryMm: [[X(0), 0], [X(4000), 0], [X(4000), 3500], [X(0), 3500]] },
    { key: `${prefix}-KIT`, kind: "Room", source: "Arch", id: 2, number: `${prefix}-02`, name: "מטבח", level, areaM2: 12, perimeterM: 14, volumeM3: 32, heightMm: 2700, baseElevationMm: z,
      boundaryMm: [[X(5000), 0], [X(9000), 0], [X(9000), 3000], [X(5000), 3000]] },
  ];
  const doors = [{ id: 10, source: "Arch", level, xyzMm: [X(0), 2800, z], widthMm: 900, alongWall: [0, 1], facing: [1, 0], fromRoomKey: `${prefix}-MBR` }];
  const furniture = [
    { id: 100, source: "Arch", category: "Furniture", builtInCategory: "OST_Furniture", family: "מיטה זוגית", type: "1600", xyzMm: [X(2000), 1050, z], bboxMinMm: [X(1200), 50, z], bboxMaxMm: [X(2800), 2050, z + 500] },
    { id: 101, source: "Arch", category: "Casework", builtInCategory: "OST_Casework", family: "ארון בגדים", type: "600", xyzMm: [X(3700), 1500, z], bboxMinMm: [X(3400), 500, z], bboxMaxMm: [X(4000), 2500, z + 2400] },
    { id: 102, source: "Arch", category: "Furniture", builtInCategory: "OST_Furniture", family: "מזנון TV", type: "1000", xyzMm: [X(2000), 3275, z], bboxMinMm: [X(1500), 3050, z], bboxMaxMm: [X(2500), 3500, z + 500] },
    { id: 103, source: "Arch", category: "Casework", builtInCategory: "OST_Casework", family: "Kitchen base cabinet", type: "600", xyzMm: [X(6500), 2700, z], bboxMinMm: [X(5000), 2400, z], bboxMaxMm: [X(8000), 3000, z + 900] },
  ];
  if (extraWardrobe) furniture.push({ id: 104, source: "Arch", category: "Casework", builtInCategory: "OST_Casework", family: "ארון", type: "600", xyzMm: [X(3300), 300, z], bboxMinMm: [X(3000), 0, z], bboxMaxMm: [X(3600), 600, z + 2400] });
  return { rooms, doors, furniture };
}

function snapshot(withDevices) {
  const a = apartment({ prefix: "03", level: "03", dx: 0, z: 0 });
  const b = apartment({ prefix: "04", level: "04", dx: 20000, z: 3000, extraWardrobe: true });
  const el = (id, bic, family, type, x, y, z, roomKey) => ({ id, category: bic.replace("OST_", ""), builtInCategory: bic, family, type, typeId: 1, xyzMm: [x, y, z], roomKey, parameters: {} });
  const elements = withDevices ? [
    el(1, "OST_ElectricalFixtures", "TPZ Socket Double", "16A", 900, 100, 400, "03-MBR"),
    el(2, "OST_ElectricalFixtures", "TPZ Socket Double", "16A", 3100, 100, 400, "03-MBR"),
    el(3, "OST_LightingDevices", "TPZ Switch", "1 gang", 100, 2200, 1100, "03-MBR"),
    el(4, "OST_LightingFixtures", "TPZ Downlight", "12W", 2000, 1750, 2700, "03-MBR"),
    el(5, "OST_ElectricalFixtures", "TPZ TV Outlet", "coax", 2000, 3400, 1200, "03-MBR"),
    el(6, "OST_ElectricalFixtures", "TPZ Socket Double", "16A", 6000, 2950, 1150, "03-KIT"),
    el(7, "OST_ElectricalFixtures", "TPZ Socket Double", "16A", 7000, 2950, 1150, "03-KIT"),
  ] : [];
  return { schemaVersion: 2, exportedAt: "", revitVersion: "2026", document: { title: "t", path: "", isWorkshared: false, projectInfo: {} },
    levels: [{ id: 1, name: "03", elevationMm: 0 }, { id: 2, name: "04", elevationMm: 3000 }], links: [], categoryCounts: {},
    rooms: [...a.rooms, ...b.rooms], doors: [...a.doors, ...b.doors], furniture: [...a.furniture, ...b.furniture], windows: [], walls: [],
    elements, raceways: [], panels: [], circuitsWithoutPanel: [], distributionSystems: [], warnings: [] };
}

const cfg = DEFAULT_OFFICE_CONFIG;

test("office-config/default.json is the embedded default", () => {
  const file = JSON.parse(fs.readFileSync(path.join(here, "..", "..", "..", "office-config", "default.json"), "utf8"));
  assert.deepEqual(file.roomTypes.map((r) => r.id), cfg.roomTypes.map((r) => r.id));
  assert.deepEqual(file.anchors.map((a) => a.kind), cfg.anchors.map((a) => a.kind));
  assert.deepEqual(file.hosting, cfg.hosting);
  assert.deepEqual(file.heights, cfg.heights);
  const merged = loadOfficeConfig(path.join(here, "..", "..", "..", "office-config", "default.json"));
  assert.equal(merged.roomTypes.length, cfg.roomTypes.length);
});

test("room types and anchors are recognised in Hebrew and English", () => {
  assert.equal(roomTypeId("חדר שינה הורים", cfg), "master_bedroom");
  assert.equal(roomTypeId("ח.שינה 2", cfg), "bedroom");
  assert.equal(roomTypeId('ממ"ד', cfg), "mamad");
  assert.equal(roomTypeId("Kitchen", cfg), "kitchen");
  assert.equal(roomTypeId("חלל", cfg), "other");
  assert.equal(anchorKind("כיור רחצה", cfg).kind, "basin");
  assert.equal(anchorKind("Kitchen Sink 600", cfg).kind, "sink");
  assert.equal(anchorKind("ארון בגדים", cfg).kind, "wardrobe");
  assert.equal(anchorKind("ארון בגדים", cfg).blocksWall, true);
  assert.equal(anchorKind("Generic chair", cfg), undefined);
});

test("device roles from category and name", () => {
  const e = (bic, family, type = "") => ({ id: 1, category: "", builtInCategory: bic, family, type, typeId: 1, parameters: {} });
  assert.equal(roleOf(e("OST_ElectricalFixtures", "TPZ Socket")), "socket");
  assert.equal(roleOf(e("OST_ElectricalFixtures", "שקע תלת פאזי 25A")), "socket_3ph");
  assert.equal(roleOf(e("OST_ElectricalFixtures", "מפסק תריס")), "shutter");
  assert.equal(roleOf(e("OST_LightingDevices", "Switch")), "switch");
  assert.equal(roleOf(e("OST_LightingFixtures", "Emergency exit")), "emergency");
  assert.equal(roleOf(e("OST_DataDevices", "TV outlet")), "tv");
  assert.equal(roleOf(e("OST_ElectricalEquipment", "Panel")), "panel");
});

test("read unit: rooms, walls with inward normals, anchors against their wall, door handle side", () => {
  const b = buildUnit(snapshot(true), { roomFilter: "03-" }, cfg);
  assert.equal(b.unit.type, "apartment");
  assert.deepEqual(b.roomTypes, { "03-MBR": "master_bedroom", "03-KIT": "kitchen" });
  const walls = b.unit.features.filter((f) => f.kind === "wall" && f.parent === "03-MBR");
  assert.equal(walls.length, 4);
  const south = walls.find((w) => w.point[1] === 0 && w.dir[0] === 1);
  assert.deepEqual(south.normal, [0, 1]);
  const bed = b.unit.features.find((f) => f.kind === "bed");
  assert.deepEqual(bed.dir, [1, 0]);
  assert.equal(bed.length, 1600);
  assert.ok(b.unit.features.find((f) => f.kind === "wardrobe").tags.includes("blocks-wall"));
  const door = b.unit.features.find((f) => f.kind === "door");
  assert.ok(door.tags.includes("handle:left"));
  assert.equal(b.unit.designed.length, 7);
  assert.equal(b.unit.designed.find((d) => d.id === "4").host, "ceiling");
});

test("learn: rules are relative to bed, door, TV unit, kitchen counter and room centre", () => {
  const b = buildUnit(snapshot(true), { roomFilter: "03-" }, cfg);
  const p = learn(b.unit, learnOpts);
  assert.equal(p.rules.length, 7, p.notes.join("; "));
  const bedSockets = p.rules.filter((r) => r.anchorKind === "bed" && r.role === "socket");
  assert.equal(bedSockets.length, 2);
  assert.deepEqual(bedSockets.map((r) => r.along).sort((a, b) => a - b), [-1100, 1100]);
  assert.ok(bedSockets.every((r) => r.height === 400 && r.across === -950));
  const sw = p.rules.find((r) => r.role === "switch");
  assert.equal(sw.anchorKind, "door");
  assert.deepEqual(sw.anchorTags, ["handle:left"]);
  assert.equal(sw.height, 1100);
  assert.equal(p.rules.find((r) => r.role === "light").anchorKind, "room");
  assert.equal(p.rules.find((r) => r.role === "tv").anchorKind, "tv");
  assert.equal(p.rules.filter((r) => r.anchorKind === "kitchen_counter").length, 2);
});

test("replicate: same offsets in the other apartment, level 04 as Schedule Level, wardrobe moves a socket", () => {
  const snap = snapshot(true);
  const tpl = buildUnit(snap, { roomFilter: "03-" }, cfg);
  const pattern = learn(tpl.unit, learnOpts);
  const tgt = buildUnit(snap, { roomFilter: "04-" }, cfg);
  const placed = replicate(pattern, tgt.unit, hostingConstraints(cfg, []), { slideStepMm: 100, slideMaxMm: 600, fallbackKinds: ["wall"] });
  const s = summarize(placed);
  assert.equal(s.total, 7);
  assert.equal(s.byStatus.dropped ?? 0, 0, JSON.stringify(s.dropped));
  // left bed socket lands at the same offset, 3000 mm higher, 20 m to the right
  const left = placed.find((p) => p.role === "socket" && p.point[0] < 22000);
  assert.deepEqual(left.point, [20900, 100, 3400]);
  assert.equal(left.status, "placed");
  // right bed socket: the extra wardrobe (x 23000..23600) blocks it, so it slides
  const right = placed.find((p) => p.role === "socket" && p.point[0] > 22000 && p.point[0] < 24000 && p.point[1] < 1000);
  assert.equal(right.status, "moved");
  assert.ok(right.point[0] < 22950 || right.point[0] > 23650, `still behind the wardrobe: ${right.point}`);
  // light at the room centre, ceiling; switch by the door at 1100
  assert.deepEqual(placed.find((p) => p.role === "light").point, [22000, 1750, 5700]);
  assert.deepEqual(placed.find((p) => p.role === "switch").point, [20100, 2200, 4100]);
  // counter sockets above the counter are allowed (height 1150 > counter 900)
  assert.equal(placed.filter((p) => p.parent === "04-KIT" && p.status === "placed").length, 2);
  const pl = toPlacements(placed, tgt.levelName, tgt.unit.id);
  assert.equal(pl.length, 7);
  assert.ok(pl.every((p) => p.scheduleLevel === "04" && p.levelName === "04"));
  assert.equal(pl.find((p) => p.role === "light").hostMode, "ceiling");
  assert.equal(pl.find((p) => p.role === "socket").hostMode, "wall");
});

test("hosting checks: behind counter at 300 mm fails, above it passes; door frame; non-clean wall", () => {
  const snap = snapshot(false);
  snap.walls = [{ id: 900, source: "Arch", type: "קיר זכוכית", startMm: [4000, 0], endMm: [4000, 3500], thicknessMm: 12 }];
  const b = buildUnit(snap, { roomFilter: "03-" }, cfg);
  const room = b.unit.features.find((f) => f.kind === "room" && f.parent === "03-KIT");
  const run = (role, point, parent = "03-KIT") => {
    const c = { ruleId: "t", role, parent, feature: room, point, heightMm: point[2] };
    for (const k of hostingConstraints(cfg, [])) { const v = k(c, b.unit); if (!v.ok) return v; }
    return { ok: true };
  };
  assert.equal(run("socket", [6000, 2950, 300]).check, "behind-furniture");
  assert.equal(run("socket", [6000, 2950, 1150]).ok, true);
  assert.equal(run("socket", [50, 2600, 300], "03-MBR").check, "opening");
  assert.equal(run("switch", [100, 2200, 1100], "03-MBR").ok, true);
  assert.equal(run("socket", [3950, 3000, 300], "03-MBR").check, "wall"); // east wall is glass
  assert.equal(run("light", [3950, 3000, 2700], "03-MBR").ok, true); // ceiling devices do not care about walls
});

test("linear adapter: poles learned on one station continue along the whole alignment", () => {
  const alignment = { id: "track-A", points: [[0, 0, 0], [300000, 0, 0]], stationLengthMm: 60000, baseZ: 0 };
  const designed = [
    { id: "pole-1", role: "catenary_pole", point: [10000, -3000, 0], props: { family: "OCS Pole" } },
    { id: "signal-1", role: "signal", point: [500, 2500, 0] },
  ];
  const units = buildLinearUnits(alignment, designed);
  assert.equal(units.length, 5);
  const pattern = learn(units[0], { anchorPriority: ["station_start", "centerline"], maxAnchorDistanceMm: 20000 });
  assert.equal(pattern.rules.length, 2);
  const pole = pattern.rules.find((r) => r.role === "catenary_pole");
  assert.equal(pole.along, 10000);
  assert.equal(pole.across, 3000); // right of travel
  const all = units.slice(1).flatMap((u) => replicate(pattern, u));
  assert.equal(all.length, 8);
  assert.deepEqual(all.filter((p) => p.role === "catenary_pole").map((p) => p.point[0]), [70000, 130000, 190000, 250000]);
  assert.ok(all.every((p) => p.status === "placed"));
});
