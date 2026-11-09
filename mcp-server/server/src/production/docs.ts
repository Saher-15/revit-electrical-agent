import fs from "fs";
import path from "path";
import JSZip from "jszip";
import ExcelJS from "exceljs";
import { pdfText } from "./pdf.js";

function xmlText(xml: string): string {
  return xml
    .replace(/<w:tab\/>/g, "\t")
    .replace(/<\/w:p>/g, "\n")
    .replace(/<w:br\/>/g, "\n")
    .replace(/<[^>]+>/g, "")
    .replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, "&")
    .replace(/\n{3,}/g, "\n\n");
}

export async function docxText(file: string): Promise<string> {
  const zip = await JSZip.loadAsync(fs.readFileSync(file));
  const parts = Object.keys(zip.files).filter((n) => /^word\/(document|header\d*|footer\d*)\.xml$/.test(n)).sort();
  let out = "";
  for (const p of parts) out += xmlText(await zip.file(p)!.async("string")) + "\n";
  return out.trim();
}

export async function xlsxText(file: string, maxRows = 2000): Promise<string> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.readFile(file);
  const out: string[] = [];
  wb.eachSheet((ws) => {
    out.push(`## ${ws.name}`);
    let n = 0;
    ws.eachRow((row) => {
      if (n++ >= maxRows) return;
      const vals = (row.values as any[]).slice(1).map((v) => (v && typeof v === "object" ? (v.text ?? v.result ?? JSON.stringify(v)) : v ?? ""));
      out.push(vals.join("\t"));
    });
  });
  return out.join("\n");
}

export const DOC_EXTS = [".pdf", ".docx", ".xlsx", ".txt", ".md", ".csv"];

/** Text of one document. PDF pages are marked with "--- page N ---". */
export async function documentText(file: string): Promise<string> {
  const ext = path.extname(file).toLowerCase();
  if (ext === ".pdf") return (await pdfText(file)).map((p) => `--- page ${p.page} ---\n${p.text}`).join("\n\n");
  if (ext === ".docx") return await docxText(file);
  if (ext === ".xlsx") return await xlsxText(file);
  if ([".txt", ".md", ".csv"].includes(ext)) return fs.readFileSync(file, "utf8");
  throw new Error(`Unsupported file type ${ext}. Supported: ${DOC_EXTS.join(", ")}. Save .doc/.xls as .docx/.xlsx.`);
}
