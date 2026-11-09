import fs from "fs";
import path from "path";
import { projectFolder } from "./paths.js";
import { DOC_EXTS } from "./docs.js";

export interface ProjectData {
  /** Title block values by field name, e.g. {"Project Name": "...", "Project Number": "..."} */
  fields: Record<string, string>;
  /** PNG/JPG of the engineer stamp. */
  stampImage?: string;
  /** Title block family to use, "Family: Type" (partial). */
  titleBlock?: string;
  updated?: string;
}

export function projectPaths(project: string) {
  const dir = projectFolder(project);
  const instructions = path.join(dir, "instructions");
  const markups = path.join(dir, "markups");
  fs.mkdirSync(instructions, { recursive: true });
  fs.mkdirSync(markups, { recursive: true });
  return { dir, instructions, markups, rules: path.join(dir, "RULES.md"), data: path.join(dir, "project.json"), stampLog: path.join(dir, "stamps.log") };
}

export function loadProjectData(project: string): ProjectData {
  const p = projectPaths(project);
  if (!fs.existsSync(p.data)) return { fields: {} };
  const d = JSON.parse(fs.readFileSync(p.data, "utf8"));
  return { fields: d.fields || {}, stampImage: d.stampImage, titleBlock: d.titleBlock, updated: d.updated };
}

export function saveProjectData(project: string, patch: Partial<ProjectData>): ProjectData {
  const p = projectPaths(project);
  const cur = loadProjectData(project);
  const next: ProjectData = {
    fields: { ...cur.fields, ...(patch.fields || {}) },
    stampImage: patch.stampImage ?? cur.stampImage,
    titleBlock: patch.titleBlock ?? cur.titleBlock,
    updated: new Date().toISOString(),
  };
  for (const [k, v] of Object.entries(next.fields)) if (v === null || v === undefined) delete next.fields[k];
  fs.writeFileSync(p.data, JSON.stringify(next, null, 2), "utf8");
  return next;
}

export function listDocuments(folder: string): string[] {
  if (!fs.existsSync(folder)) return [];
  const out: string[] = [];
  const walk = (d: string, depth: number) => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const full = path.join(d, e.name);
      if (e.isDirectory() && depth < 3) walk(full, depth + 1);
      else if (e.isFile() && DOC_EXTS.includes(path.extname(e.name).toLowerCase()) && !e.name.startsWith("~$")) out.push(full);
    }
  };
  walk(folder, 0);
  return out.sort();
}
