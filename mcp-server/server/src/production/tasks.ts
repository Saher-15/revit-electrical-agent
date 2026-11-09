import fs from "fs";
import path from "path";
import ExcelJS from "exceljs";
import { PdfMarkup } from "./pdf.js";
import { claudeFolder, safeName } from "./paths.js";

export type TaskStatus = "open" | "done" | "not_done" | "question" | "skipped";

export interface MarkupTask extends PdfMarkup {
  status: TaskStatus;
  /** What Claude did or plans to do, in one line. */
  action: string;
  elementIds: number[];
  /** Why it was not done, or the question for the designer. */
  note: string;
  updated: string;
}

export interface TaskFile {
  source: string;
  created: string;
  updated: string;
  tasks: MarkupTask[];
}

export function taskFolder(pdfFile: string): string {
  return claudeFolder("tasks", safeName(path.basename(pdfFile, path.extname(pdfFile))));
}

export function taskJsonPath(pdfFile: string): string {
  return path.join(taskFolder(pdfFile), "markups.json");
}

export function loadTasks(jsonPath: string): TaskFile {
  if (!fs.existsSync(jsonPath)) throw new Error(`No task file at ${jsonPath}. Run read_pdf_markups first.`);
  return JSON.parse(fs.readFileSync(jsonPath, "utf8")) as TaskFile;
}

/** Keeps status and notes of markups already in the file (re-reading the same PDF must not lose work). */
export function mergeTasks(existing: TaskFile | undefined, source: string, markups: PdfMarkup[]): TaskFile {
  const now = new Date().toISOString();
  const old = new Map((existing?.tasks || []).map((t) => [t.id, t]));
  const tasks: MarkupTask[] = markups.map((m) => {
    const prev = old.get(m.id);
    return prev
      ? { ...m, status: prev.status, action: prev.action, elementIds: prev.elementIds, note: prev.note, updated: prev.updated }
      : { ...m, status: "open", action: "", elementIds: [], note: "", updated: now };
  });
  return { source, created: existing?.created || now, updated: now, tasks };
}

const STATUS_HE: Record<TaskStatus, string> = {
  open: "פתוח",
  done: "בוצע",
  not_done: "לא בוצע",
  question: "שאלה למתכנן",
  skipped: "לא רלוונטי",
};

export async function writeTaskFiles(jsonPath: string, file: TaskFile): Promise<{ json: string; xlsx: string; reply: string }> {
  fs.writeFileSync(jsonPath, JSON.stringify(file, null, 2), "utf8");
  const dir = path.dirname(jsonPath);

  const wb = new ExcelJS.Workbook();
  const ws = wb.addWorksheet("Markups", { views: [{ rightToLeft: true, state: "frozen", ySplit: 1 }] });
  ws.columns = [
    { header: "#", key: "n", width: 5 },
    { header: "דף", key: "page", width: 6 },
    { header: "גיליון", key: "sheet", width: 10 },
    { header: "סוג", key: "kind", width: 10 },
    { header: "כותב", key: "author", width: 14 },
    { header: "תאריך", key: "date", width: 16 },
    { header: "הערה", key: "comment", width: 50 },
    { header: "טקסט ליד ההערה", key: "near", width: 36 },
    { header: "סטטוס", key: "status", width: 12 },
    { header: "מה בוצע", key: "action", width: 40 },
    { header: "אלמנטים", key: "ids", width: 18 },
    { header: "הערת Claude", key: "note", width: 40 },
    { header: "X מ\"מ", key: "x", width: 8 },
    { header: "Y מ\"מ", key: "y", width: 8 },
    { header: "id", key: "id", width: 10 },
  ];
  ws.getRow(1).font = { bold: true };
  file.tasks.forEach((t, i) => {
    const row = ws.addRow({
      n: i + 1, page: t.page, sheet: t.sheet || "", kind: t.kind, author: t.author, date: t.date,
      comment: [t.comment, ...t.replies.map((r) => "↳ " + r)].filter(Boolean).join("\n"),
      near: t.nearbyText, status: STATUS_HE[t.status] || t.status, action: t.action,
      ids: t.elementIds.join(", "), note: t.note, x: t.xMm, y: t.yMm, id: t.id,
    });
    row.alignment = { wrapText: true, vertical: "top" };
    const fill = t.status === "done" ? "FFC6EFCE" : t.status === "open" ? undefined : "FFFFEB9C";
    if (fill) row.getCell("status").fill = { type: "pattern", pattern: "solid", fgColor: { argb: fill } };
  });
  const xlsx = path.join(dir, "tasks.xlsx");
  await wb.xlsx.writeFile(xlsx);

  const reply = path.join(dir, "REPLY.md");
  fs.writeFileSync(reply, replyText(file), "utf8");
  return { json: jsonPath, xlsx, reply };
}

export function summary(file: TaskFile): Record<TaskStatus, number> {
  const s: Record<TaskStatus, number> = { open: 0, done: 0, not_done: 0, question: 0, skipped: 0 };
  for (const t of file.tasks) s[t.status] = (s[t.status] || 0) + 1;
  return s;
}

/** Answer to the designer: what was fixed, what was not and why, open questions. */
export function replyText(file: TaskFile): string {
  const s = summary(file);
  const lines = [
    `# תשובה להערות: ${path.basename(file.source)}`,
    "",
    `עודכן: ${file.updated.slice(0, 16).replace("T", " ")}`,
    "",
    `סה"כ ${file.tasks.length} הערות: בוצע ${s.done}, לא בוצע ${s.not_done}, שאלות ${s.question}, לא רלוונטי ${s.skipped}, פתוח ${s.open}.`,
    "",
  ];
  const section = (title: string, st: TaskStatus) => {
    const rows = file.tasks.filter((t) => t.status === st);
    if (!rows.length) return;
    lines.push(`## ${title}`, "");
    for (const t of rows) {
      const where = [t.sheet ? `גיליון ${t.sheet}` : `דף ${t.page}`, t.nearbyText ? `ליד "${t.nearbyText.slice(0, 40)}"` : ""].filter(Boolean).join(", ");
      lines.push(`- **${t.comment || t.kind}** (${where})`);
      if (t.action) lines.push(`  - ${t.action}`);
      if (t.note) lines.push(`  - ${t.note}`);
      if (t.elementIds.length) lines.push(`  - אלמנטים: ${t.elementIds.join(", ")}`);
    }
    lines.push("");
  };
  section("שאלות למתכנן", "question");
  section("לא בוצע", "not_done");
  section("בוצע", "done");
  section("פתוח", "open");
  section("לא רלוונטי", "skipped");
  return lines.join("\n");
}
