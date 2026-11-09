import fs from "fs";
import path from "path";
import ExcelJS from "exceljs";
import { ModelSnapshot, loadSnapshot, roomIndex, roomLabel } from "./snapshot.js";

// Bill of quantities (כתב כמויות) from a model snapshot.
// Every counted thing is matched against mapping rules (first match wins) that give it
// a BOQ item number, description, unit and price. Unmatched things still appear with an
// automatic description so nothing in the model is silently dropped.

export interface MappingRule {
  category?: string;
  family?: string;
  type?: string;
  typeMark?: string;
  size?: string;
  item?: string;
  description?: string;
  unit?: string;
  unitPrice?: number;
  chapter?: string;
  /** true drops matching things from the BOQ. */
  exclude?: boolean;
}

export interface BoqOptions {
  snapshotPath: string;
  outputPath?: string;
  mappingPath?: string;
  /** Percent added to lengths of tray, conduit and cable. Default 0. */
  lengthAllowancePercent?: number;
  /** Add cable quantities estimated from circuit lengths. Default true. */
  includeCableEstimate?: boolean;
}

export interface BoqLine {
  chapter: string;
  item: string;
  description: string;
  unit: string;
  unitPrice?: number;
  mapped: boolean;
  source: { category: string; family: string; type: string; typeMark: string; size: string };
  quantity: number;
  byLevel: Map<string, number>;
  byRoom: Map<string, number>;
}

export interface BoqResult {
  outputPath: string;
  lineCount: number;
  mappedLines: number;
  unmappedLines: number;
  excludedThings: number;
  chapters: { chapter: string; lines: number }[];
  notes: string[];
}

const UNIT_EACH = "יח'";
const UNIT_METER = 'מ"א';
const NO_ROOM = "ללא חדר";
const NO_LEVEL = "ללא קומה";

const CHAPTERS: [string, string][] = [
  ["OST_ElectricalEquipment", "לוחות וציוד חשמל"],
  ["OST_LightingFixtures", "גופי תאורה"],
  ["OST_LightingDevices", "מפסקים ואביזרי תאורה"],
  ["OST_ElectricalFixtures", "שקעים ואביזרי חשמל"],
  ["OST_CableTray", "תעלות כבלים"],
  ["OST_CableTrayFitting", "אביזרי תעלות"],
  ["OST_Conduit", "צנרת"],
  ["OST_ConduitFitting", "אביזרי צנרת"],
  ["CABLES", "כבלים (הערכה מאורך המעגלים)"],
  ["OST_DataDevices", "תקשורת ונתונים"],
  ["OST_CommunicationDevices", "מערכות תקשורת"],
  ["OST_TelephoneDevices", "טלפוניה"],
  ["OST_FireAlarmDevices", "גילוי אש"],
  ["OST_SecurityDevices", "מערכות ביטחון"],
  ["OST_NurseCallDevices", "קריאת אחות"],
];
const chapterOf = new Map(CHAPTERS);
const chapterOrder = new Map(CHAPTERS.map(([, name], i) => [name, i]));

function matches(pattern: string | undefined, value: string): boolean {
  if (pattern === undefined || pattern === null || String(pattern).trim() === "") return true;
  const p = String(pattern).trim();
  if (p.length > 2 && p.startsWith("/") && p.endsWith("/")) {
    try {
      return new RegExp(p.slice(1, -1), "i").test(value);
    } catch {
      return false;
    }
  }
  return value.toLowerCase().includes(p.toLowerCase());
}

function findRule(rules: MappingRule[], src: BoqLine["source"]): MappingRule | undefined {
  return rules.find(
    (r) =>
      matches(r.category, src.category) &&
      matches(r.family, src.family) &&
      matches(r.type, src.type) &&
      matches(r.typeMark, src.typeMark) &&
      matches(r.size, src.size)
  );
}

export async function loadMapping(mappingPath: string | undefined): Promise<MappingRule[]> {
  if (!mappingPath) return [];
  if (!fs.existsSync(mappingPath)) throw new Error(`Mapping file not found: ${mappingPath}`);
  if (mappingPath.toLowerCase().endsWith(".json")) {
    const rules = JSON.parse(fs.readFileSync(mappingPath, "utf8").replace(/^﻿/, ""));
    if (!Array.isArray(rules)) throw new Error("Mapping JSON must be an array of rules");
    return rules;
  }
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.readFile(mappingPath);
  const ws = wb.getWorksheet("מיפוי") ?? wb.worksheets[0];
  if (!ws) throw new Error(`No worksheet in ${mappingPath}`);
  const headers: string[] = [];
  ws.getRow(1).eachCell((cell, col) => (headers[col] = String(cell.text).trim()));
  const rules: MappingRule[] = [];
  ws.eachRow((row, n) => {
    if (n === 1) return;
    const rule: any = {};
    row.eachCell((cell, col) => {
      const key = headers[col];
      if (!key || cell.text === "") return;
      if (key === "unitPrice") rule.unitPrice = Number(cell.value);
      else if (key === "exclude") rule.exclude = /^(1|true|yes|כן|x)$/i.test(cell.text.trim());
      else rule[key] = cell.text.trim();
    });
    // A rule only counts when it says what to do with the match.
    // Unit and chapter are pre-filled on every row of a generated mapping sheet, so they alone don't make a rule.
    if (rule.item || rule.description || rule.exclude || (rule.unitPrice !== undefined && !isNaN(rule.unitPrice))) rules.push(rule);
  });
  return rules;
}

export function buildBoq(snap: ModelSnapshot, rules: MappingRule[], opts: BoqOptions): { lines: BoqLine[]; excluded: number; notes: string[] } {
  const rooms = roomIndex(snap);
  const lines = new Map<string, BoqLine>();
  const notes: string[] = [];
  let excluded = 0;
  const allowance = 1 + Math.max(0, opts.lengthAllowancePercent ?? 0) / 100;

  const add = (bic: string, src: BoqLine["source"], unit: string, qty: number, level: string | undefined, roomKey: string | undefined) => {
    const rule = findRule(rules, src);
    if (rule?.exclude) {
      excluded++;
      return;
    }
    const chapter = rule?.chapter || chapterOf.get(bic) || src.category || "אחר";
    const autoDescription = [src.family, src.type].filter((x) => x).join(" - ") + (src.size && !src.type.includes(src.size) ? ` ${src.size}` : "");
    const key = rule ? `rule:${rules.indexOf(rule)}|${rule.item ? "" : `${src.family}|${src.type}|${src.size}`}` : `auto:${bic}|${src.family}|${src.type}|${src.size}`;
    let line = lines.get(key);
    if (!line) {
      line = {
        chapter,
        item: rule?.item ?? "",
        description: rule?.description || autoDescription,
        unit: rule?.unit || unit,
        unitPrice: rule?.unitPrice,
        mapped: !!rule,
        source: src,
        quantity: 0,
        byLevel: new Map(),
        byRoom: new Map(),
      };
      lines.set(key, line);
    }
    line.quantity += qty;
    const lv = level || NO_LEVEL;
    line.byLevel.set(lv, (line.byLevel.get(lv) ?? 0) + qty);
    const rk = (roomKey && roomLabel(rooms.get(roomKey))) || NO_ROOM;
    const roomName = roomKey && rooms.get(roomKey) ? `${rk} (${rooms.get(roomKey)!.level ?? ""})` : NO_ROOM;
    line.byRoom.set(roomName, (line.byRoom.get(roomName) ?? 0) + qty);
  };

  for (const e of snap.elements) {
    const src = {
      category: e.category ?? e.builtInCategory,
      family: e.family ?? "",
      type: e.type ?? "",
      typeMark: e.parameters?.["Type Mark"] ?? "",
      size: e.parameters?.["Size"] ?? "",
    };
    add(e.builtInCategory, src, UNIT_EACH, 1, e.level, e.roomKey ?? e.spaceKey);
  }

  for (const r of snap.raceways) {
    const bic = /tray/i.test(r.category) || r.widthMm !== undefined ? "OST_CableTray" : "OST_Conduit";
    const src = { category: r.category, family: "", type: r.type ?? "", typeMark: "", size: r.size ?? "" };
    add(bic, src, UNIT_METER, r.lengthM * allowance, r.level, r.roomKey);
  }

  if (opts.includeCableEstimate !== false) {
    const panelLevel = new Map(snap.panels.map((p) => [p.name, p.level]));
    let missingWire = 0;
    for (const p of snap.panels) {
      for (const c of p.circuits) {
        if (!c.lengthM) continue;
        if (!c.wireSize) missingWire++;
        const size = c.wireSize || `${c.poles}P ${c.ratingA}A`;
        const src = { category: "Cables", family: "", type: `כבל ${size}`, typeMark: "", size };
        add("CABLES", src, UNIT_METER, c.lengthM * allowance, panelLevel.get(c.panel), undefined);
      }
    }
    notes.push("Cable lengths come from Revit circuit lengths (panel to devices path estimate), not from routed cables.");
    if (missingWire > 0) notes.push(`${missingWire} circuit(s) have no wire size; they are grouped by poles and rating instead.`);
  }
  if (allowance > 1) notes.push(`Lengths include a ${opts.lengthAllowancePercent}% allowance.`);

  const sorted = [...lines.values()].sort(
    (a, b) =>
      (chapterOrder.get(a.chapter) ?? 99) - (chapterOrder.get(b.chapter) ?? 99) ||
      a.chapter.localeCompare(b.chapter) ||
      (a.item || "~").localeCompare(b.item || "~") ||
      a.description.localeCompare(b.description)
  );
  for (const l of sorted) l.quantity = l.unit === UNIT_EACH ? Math.round(l.quantity) : Math.ceil(l.quantity * 10) / 10;
  return { lines: sorted, excluded, notes };
}

function rtlSheet(wb: ExcelJS.Workbook, name: string): ExcelJS.Worksheet {
  return wb.addWorksheet(name, {
    views: [{ rightToLeft: true, state: "frozen", ySplit: 1 }],
    pageSetup: { orientation: "landscape", paperSize: 9, fitToPage: true, fitToWidth: 1, fitToHeight: 0, printTitlesRow: "1:1" },
  });
}

function styleHeader(row: ExcelJS.Row) {
  row.font = { bold: true, color: { argb: "FFFFFFFF" } };
  row.fill = { type: "pattern", pattern: "solid", fgColor: { argb: "FF1F4E79" } };
  row.alignment = { vertical: "middle", horizontal: "center", wrapText: true };
}

const UNMAPPED_FILL: ExcelJS.Fill = { type: "pattern", pattern: "solid", fgColor: { argb: "FFFFF2CC" } };

export async function writeBoqWorkbook(snap: ModelSnapshot, lines: BoqLine[], notes: string[], outputPath: string, rules: MappingRule[] = []) {
  const wb = new ExcelJS.Workbook();
  wb.creator = "Revit MCP";
  wb.created = new Date();

  // 1. The bill itself, grouped by chapter, with price formulas.
  const ws = rtlSheet(wb, "כתב כמויות");
  ws.columns = [
    { header: "סעיף", key: "item", width: 14 },
    { header: "תיאור", key: "description", width: 60 },
    { header: "יחידה", key: "unit", width: 9 },
    { header: "כמות", key: "quantity", width: 11 },
    { header: "מחיר יחידה", key: "unitPrice", width: 13 },
    { header: 'סה"כ', key: "total", width: 15 },
    { header: "הערות", key: "note", width: 40 },
  ];
  styleHeader(ws.getRow(1));
  const chapterTotals: string[] = [];
  let currentChapter = "";
  let chapterStart = 0;
  const closeChapter = () => {
    if (!currentChapter) return;
    const r = ws.addRow({ description: `סה"כ ${currentChapter}` });
    r.getCell("total").value = { formula: `SUM(F${chapterStart}:F${r.number - 1})` };
    r.font = { bold: true };
    chapterTotals.push(`F${r.number}`);
  };
  for (const l of lines) {
    if (l.chapter !== currentChapter) {
      closeChapter();
      currentChapter = l.chapter;
      const h = ws.addRow({ description: l.chapter });
      h.font = { bold: true, size: 12 };
      h.fill = { type: "pattern", pattern: "solid", fgColor: { argb: "FFD9E1F2" } };
      chapterStart = h.number + 1;
    }
    const r = ws.addRow({
      item: l.item,
      description: l.description,
      unit: l.unit,
      quantity: l.quantity,
      unitPrice: l.unitPrice ?? null,
      note: l.mapped ? "" : "לא ממופה: להשלים סעיף ומחיר בגיליון מיפוי",
    });
    r.getCell("total").value = { formula: `IF(E${r.number}="","",D${r.number}*E${r.number})` };
    r.getCell("quantity").numFmt = l.unit === UNIT_EACH ? "#,##0" : "#,##0.0";
    if (!l.mapped) r.eachCell((c) => (c.fill = UNMAPPED_FILL));
  }
  closeChapter();
  if (chapterTotals.length) {
    const g = ws.addRow({ description: 'סה"כ כללי' });
    g.getCell("total").value = { formula: chapterTotals.join("+") };
    g.font = { bold: true, size: 12 };
  }
  ws.getColumn("unitPrice").numFmt = "#,##0.00";
  ws.getColumn("total").numFmt = "#,##0.00";

  // 2. Quantities per level.
  const levels = [...new Set([...snap.levels.map((l) => l.name), ...lines.flatMap((l) => [...l.byLevel.keys()])])].filter((lv) =>
    lines.some((l) => l.byLevel.has(lv))
  );
  const wl = rtlSheet(wb, "לפי קומה");
  wl.columns = [
    { header: "פרק", key: "chapter", width: 24 },
    { header: "סעיף", key: "item", width: 14 },
    { header: "תיאור", key: "description", width: 50 },
    { header: "יחידה", key: "unit", width: 9 },
    ...levels.map((lv, i) => ({ header: lv, key: `lv${i}`, width: 12 })),
    { header: 'סה"כ', key: "quantity", width: 11 },
  ];
  styleHeader(wl.getRow(1));
  for (const l of lines) {
    const row: Record<string, any> = { chapter: l.chapter, item: l.item, description: l.description, unit: l.unit, quantity: l.quantity };
    levels.forEach((lv, i) => {
      const q = l.byLevel.get(lv);
      if (q) row[`lv${i}`] = l.unit === UNIT_EACH ? Math.round(q) : Math.round(q * 10) / 10;
    });
    wl.addRow(row);
  }

  // 3. Quantities per room (long format, filterable).
  const wr = rtlSheet(wb, "לפי חדר");
  wr.columns = [
    { header: "חדר", key: "room", width: 30 },
    { header: "פרק", key: "chapter", width: 24 },
    { header: "סעיף", key: "item", width: 14 },
    { header: "תיאור", key: "description", width: 50 },
    { header: "יחידה", key: "unit", width: 9 },
    { header: "כמות", key: "quantity", width: 11 },
  ];
  styleHeader(wr.getRow(1));
  const roomRows: Record<string, any>[] = [];
  for (const l of lines) {
    for (const [room, q] of l.byRoom) {
      roomRows.push({ room, chapter: l.chapter, item: l.item, description: l.description, unit: l.unit, quantity: l.unit === UNIT_EACH ? Math.round(q) : Math.round(q * 10) / 10 });
    }
  }
  roomRows.sort((a, b) => (a.room === NO_ROOM ? 1 : b.room === NO_ROOM ? -1 : a.room.localeCompare(b.room, "he", { numeric: true })));
  roomRows.forEach((r) => wr.addRow(r));
  wr.autoFilter = { from: "A1", to: "F1" };

  // 4. Mapping sheet in the exact format loadMapping() reads back.
  const wm = wb.addWorksheet("מיפוי", { views: [{ state: "frozen", ySplit: 1 }] });
  wm.columns = [
    { header: "category", key: "category", width: 24 },
    { header: "family", key: "family", width: 34 },
    { header: "type", key: "type", width: 34 },
    { header: "typeMark", key: "typeMark", width: 10 },
    { header: "size", key: "size", width: 14 },
    { header: "item", key: "item", width: 14 },
    { header: "description", key: "description", width: 50 },
    { header: "unit", key: "unit", width: 8 },
    { header: "unitPrice", key: "unitPrice", width: 11 },
    { header: "chapter", key: "chapter", width: 24 },
    { header: "exclude", key: "exclude", width: 8 },
  ];
  styleHeader(wm.getRow(1));
  // Existing rules first, in their order (first match wins), then one row per unmapped thing to fill in.
  for (const rule of rules) {
    wm.addRow({
      category: rule.category,
      family: rule.family,
      type: rule.type,
      typeMark: rule.typeMark,
      size: rule.size,
      item: rule.item,
      description: rule.description,
      unit: rule.unit,
      unitPrice: rule.unitPrice ?? null,
      chapter: rule.chapter,
      exclude: rule.exclude ? "x" : null,
    });
  }
  for (const l of lines.filter((x) => !x.mapped)) {
    const r = wm.addRow({
      category: l.source.category,
      family: l.source.family,
      type: l.source.type,
      typeMark: l.source.typeMark,
      size: l.source.size,
      unit: l.unit,
      chapter: l.chapter,
    });
    r.eachCell({ includeEmpty: true }, (c) => (c.fill = UNMAPPED_FILL));
  }

  // 5. Where the numbers came from.
  const wi = rtlSheet(wb, "מקור");
  wi.columns = [
    { header: "שדה", key: "k", width: 28 },
    { header: "ערך", key: "v", width: 90 },
  ];
  styleHeader(wi.getRow(1));
  const info: [string, string][] = [
    ["מודל", snap.document.title],
    ["קובץ", snap.document.path],
    ["יוצא מ-Revit", `${snap.exportedAt} (Revit ${snap.revitVersion})`],
    ["מודלים מקושרים", snap.links.map((l) => `${l.name}${l.loaded ? "" : " (לא טעון)"}`).join(", ")],
    ["חדרים / חללים", `${snap.rooms.filter((r) => r.kind === "Room").length} / ${snap.rooms.filter((r) => r.kind === "Space").length}`],
    ["אלמנטים", String(snap.elements.length)],
    ["קטעי תעלה/צנרת", String(snap.raceways.length)],
    ["לוחות", String(snap.panels.length)],
    ...Object.entries(snap.document.projectInfo ?? {}).map(([k, v]) => [k, v] as [string, string]),
    ...notes.map((n) => ["הערה", n] as [string, string]),
    ...snap.warnings.slice(0, 50).map((w) => ["אזהרת ייצוא", w] as [string, string]),
  ];
  info.forEach(([k, v]) => wi.addRow({ k, v }));

  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  await wb.xlsx.writeFile(outputPath);
}

export async function generateBoq(opts: BoqOptions): Promise<BoqResult> {
  const snap = loadSnapshot(opts.snapshotPath);
  const rules = await loadMapping(opts.mappingPath);
  const { lines, excluded, notes } = buildBoq(snap, rules, opts);
  const outputPath =
    opts.outputPath ||
    path.join(path.dirname(opts.snapshotPath), path.basename(opts.snapshotPath).replace(/\.json$/i, "") + "_BOQ.xlsx");
  await writeBoqWorkbook(snap, lines, notes, outputPath, rules);

  const chapters = new Map<string, number>();
  lines.forEach((l) => chapters.set(l.chapter, (chapters.get(l.chapter) ?? 0) + 1));
  return {
    outputPath,
    lineCount: lines.length,
    mappedLines: lines.filter((l) => l.mapped).length,
    unmappedLines: lines.filter((l) => !l.mapped).length,
    excludedThings: excluded,
    chapters: [...chapters].map(([chapter, n]) => ({ chapter, lines: n })),
    notes,
  };
}
