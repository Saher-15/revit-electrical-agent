import fs from "fs";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { readMarkups } from "../production/pdf.js";
import { loadTasks, mergeTasks, summary, taskJsonPath, writeTaskFiles } from "../production/tasks.js";

export function registerReadPdfMarkupsTool(server: McpServer) {
  server.tool(
    "read_pdf_markups",
    "Reads the review markups (clouds, comments, callouts, ink, highlights, stamps; Bluebeam or Acrobat) in a PDF of drawings and turns them into a task list. " +
      "Runs offline, Revit is not needed. Each markup comes back with page, sheet number (when sheetNumbers is given), author, date, comment, replies, " +
      "the drawing text printed next to it (room names, tags) and its position in paper mm from the bottom-left corner. " +
      "Writes Documents\\RevitClaude\\tasks\\<pdf name>\\markups.json, tasks.xlsx and REPLY.md. Reading the same PDF again keeps the status already recorded. " +
      "Then: read each comment, find the place in Revit (sheet_point_to_model turns the position into model coordinates), fix it with the other tools, " +
      "and record the result with update_markup_tasks. Ask the user before acting on a comment you do not understand.",
    {
      file: z.string().describe("Full path of the PDF"),
      pages: z.array(z.number()).optional().describe("Only these pages (1-based)"),
      sheetNumbers: z.array(z.string()).optional().describe("Sheet numbers of the Revit model (from get_documentation_info); used to tell which sheet each page is"),
      nearbyMm: z.number().optional().default(15).describe("How far around a markup to collect drawing text, in paper mm"),
      maxReturn: z.number().optional().default(150).describe("Most markups to return in this answer (all are in the files)"),
    },
    async (args) => {
      try {
        const { pages, markups, ignored, pageCount } = await readMarkups(args.file, args);
        const jsonPath = taskJsonPath(args.file);
        const existing = fs.existsSync(jsonPath) ? loadTasks(jsonPath) : undefined;
        const merged = mergeTasks(existing, args.file, markups);
        const files = await writeTaskFiles(jsonPath, merged);
        const shown = merged.tasks.slice(0, args.maxReturn).map((t) => ({
          id: t.id, page: t.page, sheet: t.sheet, kind: t.kind, author: t.author, date: t.date, comment: t.comment,
          replies: t.replies, noText: t.noText, nearbyText: t.nearbyText, xMm: t.xMm, yMm: t.yMm, status: t.status,
        }));
        const skipped = ignored.shxText + ignored.emptyStamps;
        const notes = [
          skipped ? `Skipped ${ignored.shxText} Acrobat "AutoCAD SHX Text" annotations (drawing text, not comments) and ${ignored.emptyStamps} stamps without text.` : "",
          merged.tasks.some((t) => t.noText) ? "Markups with noText mark an area without a written comment: look at the drawing there (export the sheet) or ask the designer." : "",
        ].filter(Boolean);
        const result = {
          pageCount,
          ignored,
          pages,
          markupCount: merged.tasks.length,
          status: summary(merged),
          files,
          notes: notes.length ? notes : undefined,
          note: merged.tasks.length === 0 && !skipped
            ? "No markups found. The PDF may be flattened (markups burned into the drawing); then they cannot be read as comments."
            : merged.tasks.length > shown.length ? `Showing ${shown.length} of ${merged.tasks.length}; the rest are in ${files.json}.` : undefined,
          markups: shown,
        };
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `read_pdf_markups failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
