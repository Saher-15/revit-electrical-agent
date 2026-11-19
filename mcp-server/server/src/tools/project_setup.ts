import fs from "fs";
import os from "os";
import path from "path";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { sharedRoot } from "../production/paths.js";
import { documentText } from "../production/docs.js";
import { listDocuments, loadProjectData, projectPaths, saveProjectData } from "../production/project.js";

const text = (o: unknown) => ({ content: [{ type: "text" as const, text: typeof o === "string" ? o : JSON.stringify(o, null, 2) }] });

export function registerProjectSetupTools(server: McpServer) {
  server.tool(
    "get_project_setup",
    "Shows what is stored for a project in Documents\\RevitClaude\\Projects\\<project>: the work rules (RULES.md) learned from the project's instruction documents, " +
      "the title block values and stamp image (project.json), and the instruction documents waiting in the instructions folder. " +
      "Call it at the start of any work on a project and follow RULES.md. With no project, lists the projects.",
    { project: z.string().optional().describe("Project name (folder name)") },
    async (args) => {
      try {
        if (!args.project) {
          const root = path.dirname(projectPaths("_probe").dir);
          const names = fs.readdirSync(root, { withFileTypes: true }).filter((e) => e.isDirectory() && e.name !== "_probe").map((e) => e.name);
          return text({ projectsFolder: root, projects: names });
        }
        const p = projectPaths(args.project);
        const rules = fs.existsSync(p.rules) ? fs.readFileSync(p.rules, "utf8") : null;
        return text({
          folder: p.dir,
          rules: rules ?? "(no RULES.md yet: read the instruction documents with read_project_documents, write the rules, and save them with save_project_rules after the user approves)",
          data: loadProjectData(args.project),
          instructionDocuments: listDocuments(p.instructions),
          markupPdfs: listDocuments(p.markups).filter((f) => f.toLowerCase().endsWith(".pdf")),
        });
      } catch (error) {
        return text(`get_project_setup failed: ${error instanceof Error ? error.message : String(error)}`);
      }
    }
  );

  server.tool(
    "read_project_documents",
    "Extracts the text of project instruction documents (PDF, DOCX, XLSX, TXT, MD, CSV): work instructions, specifications, client standards. " +
      "Default: every document in the project's instructions folder. Long text comes back in pieces: call again with offset to read on. " +
      "Use it to learn the project rules, then write them as short numbered rules and show them to the user before save_project_rules.",
    {
      project: z.string().optional().describe("Project name; reads its instructions folder"),
      files: z.array(z.string()).optional().describe("Specific files instead of the project folder"),
      offset: z.number().optional().default(0).describe("Character offset into the combined text"),
      maxChars: z.number().optional().default(40000),
    },
    async (args) => {
      try {
        let files = args.files || [];
        if (!files.length) {
          if (!args.project) throw new Error("Give project or files.");
          files = listDocuments(projectPaths(args.project).instructions);
          if (!files.length) throw new Error(`No documents in ${projectPaths(args.project).instructions}. Ask the user to put the instruction documents there.`);
        }
        let all = "";
        const errors: string[] = [];
        for (const f of files) {
          try { all += `\n\n===== ${path.basename(f)} =====\n` + (await documentText(f)); }
          catch (e) { errors.push(`${f}: ${e instanceof Error ? e.message : e}`); }
        }
        const start = Math.max(0, args.offset);
        const piece = all.slice(start, start + args.maxChars);
        const next = start + piece.length < all.length ? start + piece.length : undefined;
        return text({ files, totalChars: all.length, offset: start, nextOffset: next, errors: errors.length ? errors : undefined, text: piece });
      } catch (error) {
        return text(`read_project_documents failed: ${error instanceof Error ? error.message : String(error)}`);
      }
    }
  );

  server.tool(
    "save_project_rules",
    "Saves the project's work rules (RULES.md, markdown) and/or its title block values, title block family and stamp image (project.json). " +
      "Rules must come from the project documents or the user, each with its source (document + page/section). Save only after the user approved them. " +
      "fields are merged into what is stored; send a field with an empty string to clear it.",
    {
      project: z.string(),
      rules: z.string().optional().describe("Full RULES.md text (replaces the old one; the old one is kept as RULES.bak.md). When REVITCLAUDE_ROOT (shared folder) is set, RULES.md is read-only for this tool: the text is appended to RULES.proposed.md instead, for the project lead to approve."),
      proposedBy: z.string().optional().describe("Who proposes the rules (shared folder only). Default: the Windows user name."),
      source: z.string().optional().describe("Where the rules come from (shared folder only), e.g. engineer name + date or document + section."),
      fields: z.record(z.string()).optional().describe("Title block values, e.g. {\"Project Name\": \"...\", \"Project Number\": \"...\", \"Client\": \"...\"}"),
      titleBlock: z.string().optional().describe("Title block 'Family: Type' (partial) used for this project's sheets"),
      stampImage: z.string().optional().describe("Full path of the engineer stamp image (PNG/JPG)"),
    },
    async (args) => {
      try {
        const p = projectPaths(args.project);
        let proposed = false;
        if (args.rules !== undefined && sharedRoot()) {
          const head = `

## Proposed by ${args.proposedBy || os.userInfo().username} · ${new Date().toISOString().slice(0, 10)} · source: ${args.source || "(not stated)"}

`;
          fs.appendFileSync(path.join(p.dir, "RULES.proposed.md"), head + args.rules, "utf8");
          proposed = true;
        } else if (args.rules !== undefined) {
          if (fs.existsSync(p.rules)) fs.copyFileSync(p.rules, path.join(p.dir, "RULES.bak.md"));
          fs.writeFileSync(p.rules, args.rules, "utf8");
        }
        if (args.stampImage && !fs.existsSync(args.stampImage)) throw new Error(`Stamp image not found: ${args.stampImage}`);
        const data = (args.fields || args.titleBlock || args.stampImage)
          ? saveProjectData(args.project, { fields: args.fields, titleBlock: args.titleBlock, stampImage: args.stampImage })
          : loadProjectData(args.project);
        return text({ folder: p.dir, rulesSaved: args.rules !== undefined && !proposed, rulesProposed: proposed, data });
      } catch (error) {
        return text(`save_project_rules failed: ${error instanceof Error ? error.message : String(error)}`);
      }
    }
  );
}
