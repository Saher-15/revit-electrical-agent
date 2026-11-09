import fs from "fs";
import path from "path";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { loadTasks, summary, taskJsonPath, writeTaskFiles } from "../production/tasks.js";

export function registerUpdateMarkupTasksTool(server: McpServer) {
  server.tool(
    "update_markup_tasks",
    "Records what was done for each PDF markup from read_pdf_markups: status, a one-line action, the Revit element ids, and a note (why not done, or the question for the designer). " +
      "Rewrites tasks.xlsx and REPLY.md (the answer to the designer, in Hebrew). Never mark a markup done unless the change was made and checked.",
    {
      file: z.string().describe("The PDF that was read, or its markups.json"),
      updates: z.array(z.object({
        id: z.string().describe("Markup id from read_pdf_markups"),
        status: z.enum(["open", "done", "not_done", "question", "skipped"]),
        action: z.string().optional().describe("What was done, e.g. 'Moved socket 12345 300 mm left of the door'"),
        elementIds: z.array(z.number()).optional(),
        note: z.string().optional().describe("Reason it was not done, or the question for the designer"),
      })),
    },
    async (args) => {
      try {
        const jsonPath = args.file.toLowerCase().endsWith(".json") ? args.file : taskJsonPath(args.file);
        const tf = loadTasks(jsonPath);
        const byId = new Map(tf.tasks.map((t) => [t.id, t]));
        const unknown: string[] = [];
        const now = new Date().toISOString();
        for (const u of args.updates) {
          const t = byId.get(u.id);
          if (!t) { unknown.push(u.id); continue; }
          t.status = u.status;
          if (u.action !== undefined) t.action = u.action;
          if (u.elementIds !== undefined) t.elementIds = u.elementIds;
          if (u.note !== undefined) t.note = u.note;
          t.updated = now;
        }
        tf.updated = now;
        const files = await writeTaskFiles(jsonPath, tf);
        const open = tf.tasks.filter((t) => t.status === "open").map((t) => ({ id: t.id, comment: t.comment }));
        const result = { status: summary(tf), unknownIds: unknown.length ? unknown : undefined, files, stillOpen: open.slice(0, 50) };
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `update_markup_tasks failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
