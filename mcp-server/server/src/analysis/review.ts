import fs from "fs";
import path from "path";
import ExcelJS from "exceljs";
import { ModelSnapshot, SnapshotCircuit, SnapshotElement, SnapshotPanel, loadSnapshot, roomLabel } from "./snapshot.js";
import {
  DesignRules,
  crossSectionMm2,
  isEmergencyLight,
  isLighting,
  isSocket,
  isSwitch,
  loadRules,
  lumensOf,
  num,
  roomTypeOf,
  wattsOf,
} from "./rules.js";

// Design review of a model snapshot: what is wrong or missing, and what to do about it.
// Every finding names the elements it is about, so Claude can select or fix them in Revit.

export type Severity = "error" | "warning" | "info";

export interface Finding {
  severity: Severity;
  check: string;
  where: string;
  problem: string;
  recommendation: string;
  elementIds: number[];
}

export interface PanelBalance {
  panel: string;
  level?: string;
  phases: number;
  loadA: number;
  loadB: number;
  loadC: number;
  imbalancePct?: number;
  totalVA: number;
  circuits: number;
}

export interface RoomStatus {
  key: string;
  room: string;
  level?: string;
  roomType: string;
  areaM2: number;
  lights: number;
  emergencyLights: number;
  sockets: number;
  switches: number;
  targetLux: number;
  estimatedLux?: number;
  lightingWm2?: number;
  status: string;
}

export interface ReviewOptions {
  snapshotPath: string;
  outputPath?: string;
  rulesPath?: string;
}

export interface ReviewResult {
  outputPath: string;
  markdownPath: string;
  counts: Record<Severity, number>;
  byCheck: Record<string, number>;
  topFindings: Finding[];
}

const SEVERITY_HE: Record<Severity, string> = { error: "שגיאה", warning: "אזהרה", info: "מידע" };
const SEVERITY_ORDER: Record<Severity, number> = { error: 0, warning: 1, info: 2 };

function circuitCurrent(c: SnapshotCircuit): number | undefined {
  if (!c.voltage || !c.apparentLoadVA) return undefined;
  return c.poles >= 3 ? c.apparentLoadVA / (Math.sqrt(3) * c.voltage) : c.apparentLoadVA / c.voltage;
}

function isLightingCircuit(c: SnapshotCircuit, byId: Map<number, SnapshotElement>): boolean {
  const devices = c.elementIds.map((id) => byId.get(id)).filter((e): e is SnapshotElement => !!e);
  if (devices.length > 0) return devices.every((e) => isLighting(e) || isSwitch(e));
  return /light|תאור/i.test(c.loadName ?? "");
}

/** Phase loads of a panel: Revit's per-phase parameters when present, otherwise from slots (1-2 A, 3-4 B, 5-6 C). */
export function panelBalance(p: SnapshotPanel, snap: ModelSnapshot): PanelBalance {
  const ds = snap.distributionSystems.find((d) => d.name === p.distributionSystem);
  const phases = ds?.phase && /single/i.test(ds.phase) ? 1 : 3;
  const loads = [0, 0, 0];
  for (const c of p.circuits) {
    if (c.feedsPanel && c.circuitNumber === undefined) continue;
    const a = num(c.parameters?.["Apparent Load Phase A"]);
    const b = num(c.parameters?.["Apparent Load Phase B"]);
    const cc = num(c.parameters?.["Apparent Load Phase C"]);
    if (a !== undefined || b !== undefined || cc !== undefined) {
      loads[0] += a ?? 0;
      loads[1] += b ?? 0;
      loads[2] += cc ?? 0;
      continue;
    }
    const poles = Math.max(1, c.poles || 1);
    const first = Math.floor((Math.max(1, c.startSlot || 1) - 1) / 2) % 3;
    for (let k = 0; k < poles; k++) loads[(first + k) % 3] += c.apparentLoadVA / poles;
  }
  const r = (x: number) => Math.round(x);
  const bal: PanelBalance = {
    panel: p.name,
    level: p.level,
    phases,
    loadA: r(loads[0]),
    loadB: r(loads[1]),
    loadC: r(loads[2]),
    totalVA: r(loads[0] + loads[1] + loads[2]),
    circuits: p.circuits.length,
  };
  const avg = bal.totalVA / 3;
  // Largest deviation from the average phase load, as a percentage of the average.
  if (phases === 3 && avg > 0) bal.imbalancePct = Math.round(((Math.max(...loads) - avg) / avg) * 1000) / 10;
  return bal;
}

export function reviewSnapshot(snap: ModelSnapshot, rules: DesignRules): { findings: Finding[]; panels: PanelBalance[]; rooms: RoomStatus[] } {
  const findings: Finding[] = [];
  const add = (severity: Severity, check: string, where: string, problem: string, recommendation: string, elementIds: number[] = []) =>
    findings.push({ severity, check, where, problem, recommendation, elementIds });
  const byId = new Map(snap.elements.map((e) => [e.id, e]));

  // 1. Devices that are on no circuit
  const unassigned = snap.elements.filter((e) => e.unassigned);
  const byCat = new Map<string, SnapshotElement[]>();
  unassigned.forEach((e) => byCat.set(e.category, [...(byCat.get(e.category) ?? []), e]));
  for (const [cat, list] of byCat) {
    add("warning", "אביזרים ללא מעגל", cat, `${list.length} אביזרים לא מחוברים לאף מעגל`, "לחבר למעגל ולוח מתאים (create_electrical_circuit)", list.map((e) => e.id));
  }

  // 2. Panels
  const panels: PanelBalance[] = [];
  for (const p of snap.panels) {
    if (!p.distributionSystem) add("error", "לוח ללא מערכת חלוקה", p.name, "ללוח אין Distribution System, ולכן Revit לא יחבר אליו מעגלים", "להגדיר Distribution System (למשל 400/230V 3φ 4W)", [p.id]);
    if (p.circuits.length === 0) add("info", "לוח ריק", p.name, "אין מעגלים בלוח", "לבדוק אם הלוח נדרש או לחבר אליו מעגלים", [p.id]);
    const bal = panelBalance(p, snap);
    panels.push(bal);
    if (bal.imbalancePct !== undefined && bal.totalVA > 3000 && bal.imbalancePct > rules.maxPhaseImbalancePct) {
      add("warning", "חוסר איזון פאזות", p.name,
        `חוסר איזון ${bal.imbalancePct}% (L1 ${bal.loadA} / L2 ${bal.loadB} / L3 ${bal.loadC} VA)`,
        `להעביר מעגלים מהפאזה העמוסה לפאזה הקלה, יעד עד ${rules.maxPhaseImbalancePct}%`, [p.id]);
    }

    // 3. Circuits
    for (const c of p.circuits) {
      const where = `${p.name} / ${c.circuitNumber}${c.loadName ? ` (${c.loadName})` : ""}`;
      const current = circuitCurrent(c);
      const lighting = isLightingCircuit(c, byId);
      if (current !== undefined && c.ratingA > 0) {
        const ratio = current / c.ratingA;
        if (ratio > 1) add("error", "מעגל בעומס יתר", where, `זרם ${current.toFixed(1)}A מעל המאמ"ת ${c.ratingA}A`, "לפצל את המעגל או להגדיל מאמ\"ת וכבל בהתאם", [c.id, ...c.elementIds]);
        else if (ratio > rules.maxCircuitLoadRatio)
          add("warning", "מעגל עמוס", where, `זרם ${current.toFixed(1)}A, ${Math.round(ratio * 100)}% מהמאמ"ת ${c.ratingA}A`, `להשאיר רזרבה: עד ${Math.round(rules.maxCircuitLoadRatio * 100)}% מהמאמ"ת`, [c.id]);
      }
      const devices = c.elementIds.map((id) => byId.get(id)).filter((e): e is SnapshotElement => !!e);
      const lights = devices.filter(isLighting).length;
      const sockets = devices.filter(isSocket).length;
      if (lights > 0 && sockets > 0) add("warning", "תאורה ושקעים באותו מעגל", where, `${lights} גופי תאורה ו-${sockets} שקעים באותו מעגל`, "להפריד מעגלי תאורה ממעגלי בתי תקע", [c.id]);
      const max = lighting ? rules.maxDevicesPerLightingCircuit : rules.maxDevicesPerPowerCircuit;
      if (!c.feedsPanel && devices.length > max) add("warning", "יותר מדי נקודות במעגל", where, `${devices.length} נקודות במעגל (מקסימום מומלץ ${max})`, "לפצל לשני מעגלים", [c.id]);
      if (!c.wireSize && c.elementIds.length > 0) add("info", "מעגל ללא חתך כבל", where, "לא מוגדר חתך מוליך", "להגדיר Wire Type/Size כדי שאפשר יהיה לבדוק מפל מתח", [c.id]);
      const s = crossSectionMm2(c.wireSize);
      if (s && current !== undefined && c.lengthM > 0 && c.voltage > 0) {
        const factor = c.poles >= 3 ? Math.sqrt(3) : 2;
        const dropV = (factor * c.lengthM * current * rules.copperResistivity) / s;
        const pct = (dropV / c.voltage) * 100;
        const limit = lighting ? rules.maxVoltageDropLightingPct : rules.maxVoltageDropOtherPct;
        if (pct > limit)
          add("warning", "מפל מתח", where, `מפל מתח משוער ${pct.toFixed(1)}% (${c.lengthM} מ', ${s} ממ"ר, ${current.toFixed(1)}A)`, `להגדיל חתך או לקצר את המעגל, מותר עד ${limit}%`, [c.id]);
      }
    }
  }
  if (snap.circuitsWithoutPanel.length > 0)
    add("warning", "מעגלים ללא לוח", "", `${snap.circuitsWithoutPanel.length} מעגלים לא מחוברים ללוח`, "לבחור לוח לכל מעגל", snap.circuitsWithoutPanel.map((c) => c.id));

  // 4. Rooms
  const rooms: RoomStatus[] = [];
  const inRoom = new Map<string, SnapshotElement[]>();
  for (const e of snap.elements) {
    const k = e.roomKey ?? e.spaceKey;
    if (k) inRoom.set(k, [...(inRoom.get(k) ?? []), e]);
  }
  const spaceRooms = snap.rooms.some((r) => r.kind === "Room") ? snap.rooms.filter((r) => r.kind === "Room") : snap.rooms;
  for (const room of spaceRooms) {
    if (room.areaM2 < rules.minRoomAreaM2) continue;
    const rt = roomTypeOf(room, rules);
    const els = [...(inRoom.get(room.key) ?? [])];
    const lights = els.filter(isLighting);
    const emergency = els.filter(isEmergencyLight);
    const sockets = els.filter(isSocket);
    const switches = els.filter(isSwitch);
    const label = `${roomLabel(room)} (${room.level ?? room.source})`;
    const st: RoomStatus = {
      key: room.key,
      room: roomLabel(room),
      level: room.level,
      roomType: rt.id,
      areaM2: room.areaM2,
      lights: lights.length,
      emergencyLights: emergency.length,
      sockets: sockets.length,
      switches: switches.length,
      targetLux: rt.lux,
      status: "תקין",
    };
    const flux = lights.map(lumensOf);
    if (lights.length > 0 && flux.every((f) => f !== undefined)) {
      const total = (flux as number[]).reduce((a, b) => a + b, 0);
      st.estimatedLux = Math.round((total * rules.utilisationFactor * rules.maintenanceFactor) / room.areaM2);
    }
    const watts = lights.map(wattsOf);
    if (lights.length > 0 && watts.every((w) => w !== undefined)) st.lightingWm2 = Math.round(((watts as number[]).reduce((a, b) => a + b, 0) / room.areaM2) * 10) / 10;

    const problems: string[] = [];
    if (lights.length === 0) {
      problems.push("אין תאורה");
      add("warning", "חדר ללא תאורה", label, `אין גופי תאורה (${room.areaM2} מ"ר, יעד ${rt.lux} לוקס)`, "להריץ propose_electrical_design לחדר", []);
    } else if (st.estimatedLux !== undefined && st.estimatedLux < rt.lux * 0.8) {
      problems.push("תאורה חלשה");
      add("warning", "רמת הארה נמוכה", label, `הערכה ${st.estimatedLux} לוקס מול יעד ${rt.lux}`, "להוסיף גופים או להחליף לגוף בשטף גבוה יותר", lights.map((e) => e.id));
    } else if (st.estimatedLux !== undefined && st.estimatedLux > rt.lux * 1.6) {
      problems.push("תאורה מוגזמת");
      add("info", "רמת הארה גבוהה מהנדרש", label, `הערכה ${st.estimatedLux} לוקס מול יעד ${rt.lux}`, "אפשר להפחית גופים ולחסוך באנרגיה", lights.map((e) => e.id));
    }
    if (st.lightingWm2 !== undefined && st.lightingWm2 > rules.maxLightingPowerDensityWm2)
      add("info", "צפיפות הספק תאורה", label, `${st.lightingWm2} W/מ"ר`, `לשקול גופי LED יעילים יותר (יעד עד ${rules.maxLightingPowerDensityWm2} W/מ"ר)`, lights.map((e) => e.id));
    const neededSockets = Math.max(rt.minSockets, rt.socketSpacingM > 0 ? Math.floor(room.perimeterM / rt.socketSpacingM) : 0);
    if (neededSockets > 0 && sockets.length < neededSockets) {
      problems.push("חסרים שקעים");
      add(sockets.length === 0 ? "warning" : "info", "מעט בתי תקע", label, `${sockets.length} בתי תקע, מומלץ לפחות ${neededSockets}`, "להוסיף בתי תקע לאורך הקירות", sockets.map((e) => e.id));
    }
    if (rt.emergency && emergency.length === 0) {
      problems.push("אין תאורת חירום");
      add("warning", "תאורת חירום חסרה", label, `ב${rt.id === "corridor" ? "מסדרון" : "חלל"} מסוג ${rt.id} לא נמצאה תאורת חירום`, "להוסיף גוף חירום ליד כל יציאה ולאורך דרך המילוט", []);
    }
    if (rt.switches && lights.length > 0 && switches.length === 0 && room.areaM2 >= 4) {
      problems.push("אין מפסק");
      add("info", "חדר ללא מפסק", label, "יש תאורה אבל לא נמצא מפסק בחדר", "להוסיף מפסק ליד הכניסה (או לוודא שהתאורה נשלטת ממקום אחר)", []);
    }
    if (problems.length) st.status = problems.join(", ");
    rooms.push(st);
  }

  // 5. Model hygiene
  const noRoom = snap.elements.filter((e) => !e.roomKey && !e.spaceKey && e.builtInCategory !== "OST_ElectricalEquipment" && !/Fitting/.test(e.builtInCategory));
  if (noRoom.length > 0)
    add("info", "אביזרים מחוץ לחדרים", "", `${noRoom.length} אביזרים לא נמצאים בתוך חדר`, "לבדוק שהמודל האדריכלי טעון ושהחדרים סגורים", noRoom.map((e) => e.id));
  if (snap.rooms.length === 0) add("warning", "אין חדרים", "", "לא נמצאו חדרים במודל או בקישורים", "לטעון את המודל האדריכלי המקושר, אחרת אין בדיקות לפי חדר", []);
  for (const l of snap.links.filter((x) => !x.loaded)) add("warning", "קישור לא טעון", l.name, "המודל המקושר לא טעון", "לטעון אותו (Manage Links > Reload)", [l.id]);

  findings.sort((a, b) => SEVERITY_ORDER[a.severity] - SEVERITY_ORDER[b.severity] || a.check.localeCompare(b.check));
  return { findings, panels, rooms };
}

function header(ws: ExcelJS.Worksheet) {
  const row = ws.getRow(1);
  row.font = { bold: true, color: { argb: "FFFFFFFF" } };
  row.fill = { type: "pattern", pattern: "solid", fgColor: { argb: "FF1F4E79" } };
  row.alignment = { vertical: "middle", horizontal: "center", wrapText: true };
}

const FILL: Record<Severity, string> = { error: "FFF8CBAD", warning: "FFFFF2CC", info: "FFDDEBF7" };

function sheet(wb: ExcelJS.Workbook, name: string, columns: Partial<ExcelJS.Column>[]) {
  const ws = wb.addWorksheet(name, {
    views: [{ rightToLeft: true, state: "frozen", ySplit: 1 }],
    pageSetup: { orientation: "landscape", paperSize: 9, fitToPage: true, fitToWidth: 1, fitToHeight: 0 },
  });
  ws.columns = columns;
  header(ws);
  return ws;
}

export async function writeReview(snap: ModelSnapshot, findings: Finding[], panels: PanelBalance[], rooms: RoomStatus[], xlsxPath: string, mdPath: string) {
  const wb = new ExcelJS.Workbook();
  const counts = { error: 0, warning: 0, info: 0 } as Record<Severity, number>;
  findings.forEach((f) => counts[f.severity]++);
  const byCheck = new Map<string, number>();
  findings.forEach((f) => byCheck.set(f.check, (byCheck.get(f.check) ?? 0) + 1));

  const summary = sheet(wb, "סיכום", [
    { header: "נושא", key: "k", width: 30 },
    { header: "ערך", key: "v", width: 70 },
  ]);
  [
    ["מודל", snap.document.title],
    ["יוצא מ-Revit", snap.exportedAt],
    ["שגיאות", counts.error],
    ["אזהרות", counts.warning],
    ["מידע", counts.info],
    ...[...byCheck].map(([k, v]) => [k, v]),
    ["הערה", "ספי הבדיקה וסוגי החדרים הם ברירות מחדל שאפשר לשנות בקובץ rules, לא תקן מחייב"],
  ].forEach(([k, v]) => summary.addRow({ k, v }));

  const wf = sheet(wb, "ממצאים", [
    { header: "חומרה", key: "severity", width: 9 },
    { header: "בדיקה", key: "check", width: 24 },
    { header: "מיקום", key: "where", width: 32 },
    { header: "בעיה", key: "problem", width: 55 },
    { header: "המלצה", key: "recommendation", width: 55 },
    { header: "מזהי אלמנטים", key: "ids", width: 30 },
  ]);
  for (const f of findings) {
    const r = wf.addRow({ severity: SEVERITY_HE[f.severity], check: f.check, where: f.where, problem: f.problem, recommendation: f.recommendation, ids: f.elementIds.slice(0, 200).join(", ") });
    r.getCell("severity").fill = { type: "pattern", pattern: "solid", fgColor: { argb: FILL[f.severity] } };
    r.alignment = { wrapText: true, vertical: "top" };
  }
  wf.autoFilter = { from: "A1", to: "F1" };

  const wp = sheet(wb, "לוחות", [
    { header: "לוח", key: "panel", width: 20 },
    { header: "קומה", key: "level", width: 14 },
    { header: "מעגלים", key: "circuits", width: 9 },
    { header: "L1 VA", key: "loadA", width: 11 },
    { header: "L2 VA", key: "loadB", width: 11 },
    { header: "L3 VA", key: "loadC", width: 11 },
    { header: 'סה"כ VA', key: "totalVA", width: 12 },
    { header: "חוסר איזון %", key: "imbalancePct", width: 13 },
  ]);
  panels.forEach((p) => wp.addRow(p));

  const wr = sheet(wb, "חדרים", [
    { header: "חדר", key: "room", width: 26 },
    { header: "קומה", key: "level", width: 12 },
    { header: "סוג", key: "roomType", width: 14 },
    { header: 'שטח מ"ר', key: "areaM2", width: 10 },
    { header: "גופי תאורה", key: "lights", width: 10 },
    { header: "חירום", key: "emergencyLights", width: 8 },
    { header: "בתי תקע", key: "sockets", width: 9 },
    { header: "מפסקים", key: "switches", width: 9 },
    { header: "יעד לוקס", key: "targetLux", width: 9 },
    { header: "לוקס משוער", key: "estimatedLux", width: 11 },
    { header: 'W/מ"ר', key: "lightingWm2", width: 9 },
    { header: "סטטוס", key: "status", width: 40 },
  ]);
  rooms.forEach((r) => {
    const row = wr.addRow(r);
    if (r.status !== "תקין") row.getCell("status").fill = { type: "pattern", pattern: "solid", fgColor: { argb: FILL.warning } };
  });
  wr.autoFilter = { from: "A1", to: "L1" };

  fs.mkdirSync(path.dirname(xlsxPath), { recursive: true });
  await wb.xlsx.writeFile(xlsxPath);

  const md: string[] = [
    `# דוח בדיקת תכנון חשמל: ${snap.document.title}`,
    "",
    `יוצא מ-Revit ב-${snap.exportedAt}. שגיאות: ${counts.error}, אזהרות: ${counts.warning}, מידע: ${counts.info}.`,
    "",
    "| חומרה | בדיקה | מיקום | בעיה | המלצה |",
    "|---|---|---|---|---|",
    ...findings.slice(0, 300).map((f) => `| ${SEVERITY_HE[f.severity]} | ${f.check} | ${f.where} | ${f.problem} | ${f.recommendation} |`),
  ];
  if (findings.length > 300) md.push("", `ועוד ${findings.length - 300} ממצאים בקובץ ה-Excel.`);
  fs.writeFileSync(mdPath, md.join("\n"), "utf8");
}

export async function reviewModel(opts: ReviewOptions): Promise<ReviewResult> {
  const snap = loadSnapshot(opts.snapshotPath);
  const rules = loadRules(opts.rulesPath);
  const { findings, panels, rooms } = reviewSnapshot(snap, rules);
  const base = opts.snapshotPath.replace(/\.json$/i, "");
  const outputPath = opts.outputPath || `${base}_REVIEW.xlsx`;
  const markdownPath = outputPath.replace(/\.xlsx$/i, "") + ".md";
  await writeReview(snap, findings, panels, rooms, outputPath, markdownPath);
  const counts = { error: 0, warning: 0, info: 0 } as Record<Severity, number>;
  const byCheck: Record<string, number> = {};
  findings.forEach((f) => {
    counts[f.severity]++;
    byCheck[f.check] = (byCheck[f.check] ?? 0) + 1;
  });
  return {
    outputPath,
    markdownPath,
    counts,
    byCheck,
    topFindings: findings.slice(0, 25).map((f) => ({ ...f, elementIds: f.elementIds.slice(0, 20) })),
  };
}
