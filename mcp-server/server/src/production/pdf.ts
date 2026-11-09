import fs from "fs";
import { PT_TO_MM, round } from "./paths.js";

// pdfjs is loaded lazily so the other tools start even if it is missing.
async function pdfjs(): Promise<any> {
  return await import("pdfjs-dist/legacy/build/pdf.mjs");
}

export async function openPdf(file: string): Promise<any> {
  if (!fs.existsSync(file)) throw new Error(`File not found: ${file}`);
  const lib = await pdfjs();
  const data = new Uint8Array(fs.readFileSync(file));
  return await lib.getDocument({ data, isEvalSupported: false, disableFontFace: true, useSystemFonts: false, verbosity: 0 }).promise;
}

/** Annotation kinds that are review markups. Links, widgets (form fields) and popups are skipped. */
const MARKUP_KINDS = new Set([
  "Text", "FreeText", "Square", "Circle", "Polygon", "PolyLine", "Ink", "Line",
  "Highlight", "Underline", "StrikeOut", "Squiggly", "Stamp", "Caret", "FileAttachment",
]);

export interface PdfMarkup {
  id: string;
  page: number;
  /** Sheet number found in the page text, when one matches a known sheet number. */
  sheet?: string;
  kind: string;
  author: string;
  date: string;
  comment: string;
  /** Replies to this markup (other annotations whose inReplyTo points here). */
  replies: string[];
  /** A shape (cloud, box) with no written comment: it marks an area only. */
  noText?: boolean;
  /** Words printed on the drawing under or next to the markup, to tell where it is. */
  nearbyText: string;
  color?: string;
  /** Centre and size on the page in mm, origin bottom-left (same as a Revit sheet). */
  xMm: number;
  yMm: number;
  widthMm: number;
  heightMm: number;
  pageWidthMm: number;
  pageHeightMm: number;
}

export interface PdfPageInfo {
  page: number;
  widthMm: number;
  heightMm: number;
  markups: number;
  sheet?: string;
}

function hexColor(c: any): string | undefined {
  if (!c || c.length < 3) return undefined;
  return "#" + Array.from(c as ArrayLike<number>).slice(0, 3).map((v) => Number(v).toString(16).padStart(2, "0")).join("");
}

function pdfDate(raw: string | undefined): string {
  // D:20261105101530+02'00' -> 2026-11-05 10:15
  const m = /D?:?(\d{4})(\d{2})(\d{2})(\d{2})?(\d{2})?/.exec(raw || "");
  if (!m) return raw || "";
  return `${m[1]}-${m[2]}-${m[3]}${m[4] ? ` ${m[4]}:${m[5] || "00"}` : ""}`;
}

interface TextItem { str: string; x: number; y: number; w: number; h: number }

function textItems(content: any): TextItem[] {
  return (content.items || [])
    .filter((i: any) => typeof i.str === "string" && i.str.trim())
    .map((i: any) => ({ str: i.str.trim(), x: i.transform[4], y: i.transform[5], w: i.width || 0, h: Math.abs(i.transform[3]) || i.height || 0 }));
}

/** Text inside the markup rectangle grown by `marginPt`, closest first. */
export function textNear(items: TextItem[], rect: number[], marginPt: number, maxChars = 200): string {
  const [x1, y1, x2, y2] = [Math.min(rect[0], rect[2]), Math.min(rect[1], rect[3]), Math.max(rect[0], rect[2]), Math.max(rect[1], rect[3])];
  const cx = (x1 + x2) / 2, cy = (y1 + y2) / 2;
  const hits = items
    .filter((t) => t.x + t.w >= x1 - marginPt && t.x <= x2 + marginPt && t.y + t.h >= y1 - marginPt && t.y <= y2 + marginPt)
    .map((t) => ({ t, d: Math.hypot(t.x + t.w / 2 - cx, t.y + t.h / 2 - cy) }))
    .sort((a, b) => a.d - b.d);
  const seen = new Set<string>();
  let out = "";
  for (const { t } of hits) {
    if (seen.has(t.str)) continue;
    seen.add(t.str);
    if (out.length + t.str.length + 3 > maxChars) break;
    out += (out ? " | " : "") + t.str;
  }
  return out;
}

/** First known sheet number printed on the page. */
export function findSheetNumber(items: TextItem[], sheetNumbers: string[]): string | undefined {
  if (!sheetNumbers.length) return undefined;
  const wanted = new Map(sheetNumbers.map((s) => [s.trim().toUpperCase(), s]));
  // Look at the bottom-right first: that is where title blocks keep the number.
  const ordered = [...items].sort((a, b) => b.x - a.x + (a.y - b.y));
  for (const t of ordered) {
    const hit = wanted.get(t.str.toUpperCase());
    if (hit) return hit;
  }
  return undefined;
}

/** Line breaks in PDF strings are often bare \r. */
export function cleanText(s: string | undefined): string {
  return (s || "").replace(/\r\n?/g, "\n").trim();
}

/** Acrobat turns AutoCAD SHX text into annotations with this author; they are drawing text, not comments. */
export const SHX_AUTHOR = "autocad shx text";

export interface IgnoredMarkups { shxText: number; emptyStamps: number }

export async function readMarkups(file: string, opts: { pages?: number[]; nearbyMm?: number; sheetNumbers?: string[] } = {}) {
  const doc = await openPdf(file);
  const markups: PdfMarkup[] = [];
  const pages: PdfPageInfo[] = [];
  const ignored: IgnoredMarkups = { shxText: 0, emptyStamps: 0 };
  const marginPt = (opts.nearbyMm ?? 15) / PT_TO_MM;
  for (let p = 1; p <= doc.numPages; p++) {
    if (opts.pages?.length && !opts.pages.includes(p)) continue;
    const page = await doc.getPage(p);
    const [vx1, vy1, vx2, vy2] = page.view as number[];
    const info: PdfPageInfo = { page: p, widthMm: round((vx2 - vx1) * PT_TO_MM), heightMm: round((vy2 - vy1) * PT_TO_MM), markups: 0 };
    const annots: any[] = await page.getAnnotations({ intent: "display" });
    const items = textItems(await page.getTextContent());
    info.sheet = findSheetNumber(items, opts.sheetNumbers || []);
    const byId = new Map<string, PdfMarkup>();
    const replies: any[] = [];
    for (const a of annots) {
      const kind: string = a.subtype;
      if (!MARKUP_KINDS.has(kind)) continue;
      const comment = cleanText(a.contentsObj?.str ?? a.contents);
      const author = cleanText(a.titleObj?.str ?? a.title);
      if (author.toLowerCase() === SHX_AUTHOR) { ignored.shxText++; continue; }
      if (a.inReplyTo && a.replyType !== "Group") { replies.push(a); continue; }
      const r: number[] = a.rect;
      const m: PdfMarkup = {
        id: `${p}-${a.id}`,
        page: p,
        sheet: info.sheet,
        kind: a.it === "PolygonCloud" || a.intent === "PolygonCloud" ? "Cloud" : kind,
        author,
        date: pdfDate(a.modificationDate || a.creationDate),
        comment,
        replies: [],
        noText: comment ? undefined : true,
        nearbyText: textNear(items, r, marginPt),
        color: hexColor(a.color),
        xMm: round(((r[0] + r[2]) / 2 - vx1) * PT_TO_MM),
        yMm: round(((r[1] + r[3]) / 2 - vy1) * PT_TO_MM),
        widthMm: round(Math.abs(r[2] - r[0]) * PT_TO_MM),
        heightMm: round(Math.abs(r[3] - r[1]) * PT_TO_MM),
        pageWidthMm: info.widthMm,
        pageHeightMm: info.heightMm,
      };
      byId.set(a.id, m);
      markups.push(m);
      info.markups++;
    }
    for (const a of replies) {
      const parent = byId.get(a.inReplyTo);
      const text = cleanText(a.contentsObj?.str ?? a.contents);
      const who = cleanText(a.titleObj?.str ?? a.title);
      if (parent && text) parent.replies.push(who ? `${who}: ${text}` : text);
    }
    // A stamp with no words and no replies (approval stamps, "Reviewed") is not a task.
    for (let i = markups.length - 1; i >= 0; i--) {
      const m = markups[i];
      if (m.page === p && m.kind === "Stamp" && !m.comment && m.replies.length === 0) {
        markups.splice(i, 1);
        info.markups--;
        ignored.emptyStamps++;
      }
    }
    pages.push(info);
  }
  return { pages, markups, ignored, pageCount: doc.numPages as number };
}

/** Plain text of a PDF, page by page. */
export async function pdfText(file: string, maxPages = 200): Promise<{ page: number; text: string }[]> {
  const doc = await openPdf(file);
  const out: { page: number; text: string }[] = [];
  for (let p = 1; p <= Math.min(doc.numPages, maxPages); p++) {
    const page = await doc.getPage(p);
    const content = await page.getTextContent();
    let text = "";
    let lastY: number | undefined;
    for (const i of content.items as any[]) {
      if (typeof i.str !== "string") continue;
      const y = i.transform[5];
      if (lastY !== undefined && Math.abs(y - lastY) > 2) text += "\n";
      else if (text && !text.endsWith(" ") && i.str && !i.str.startsWith(" ")) text += " ";
      text += i.str;
      lastY = y;
    }
    out.push({ page: p, text: text.replace(/[ \t]*\n[ \t]*/g, "\n").trim() });
  }
  return out;
}
