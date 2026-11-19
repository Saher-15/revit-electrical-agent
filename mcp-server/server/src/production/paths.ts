import fs from "fs";
import os from "os";
import path from "path";

/** Documents\RevitClaude\<sub>, created when missing. */
export function claudeFolder(...sub: string[]): string {
  const docs = process.env.REVITCLAUDE_HOME || path.join(os.homedir(), "Documents", "RevitClaude");
  const dir = path.join(docs, ...sub);
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

/** Shared office folder (REVITCLAUDE_ROOT): holds Projects\ and office.json for all users. Unset = personal Documents\RevitClaude. */
export function sharedRoot(): string | undefined {
  return process.env.REVITCLAUDE_ROOT || undefined;
}

/** Folder of one project: <REVITCLAUDE_ROOT or Documents\RevitClaude>\Projects\<name>. */
export function projectFolder(project: string): string {
  const safe = safeName(project);
  if (!safe) throw new Error("Project name is empty.");
  const root = sharedRoot();
  if (!root) return claudeFolder("Projects", safe);
  const dir = path.join(root, "Projects", safe);
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

export function safeName(name: string): string {
  return (name || "").replace(/[<>:"/\\|?*\x00-\x1f]/g, "_").replace(/\s+/g, " ").trim().slice(0, 120);
}

export const PT_TO_MM = 25.4 / 72;
export const round = (v: number, d = 1) => Math.round(v * 10 ** d) / 10 ** d;
