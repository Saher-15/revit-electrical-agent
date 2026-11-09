import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateAnnotationsTool(server: McpServer) {
  server.tool(
    "create_annotations",
    "Add text notes, detail lines/polylines and circles to a view. Coordinates in mm: model X/Y in plan views; in drafting views and sections, " +
      "X/Y along the view (right/up) from the view origin; on sheets, paper mm from the sheet origin. Use a drafting view for single-line diagrams, " +
      "riser diagrams, legends and notes.",
    {
      viewId: z.number().optional().describe("Default: the active view"),
      texts: z.array(z.object({
        text: z.string(),
        x: z.number(),
        y: z.number(),
        textTypeName: z.string().optional(),
        rotation: z.number().optional().default(0).describe("Degrees"),
        width: z.number().optional().default(0).describe("Wrap width in paper mm; 0 = single line"),
      })).optional().default([]),
      lines: z.array(z.object({
        points: z.array(z.object({ x: z.number(), y: z.number(), z: z.number().optional().default(0) })).min(2),
        closed: z.boolean().optional().default(false),
        lineStyle: z.string().optional().describe("Line style name, e.g. 'Thin Lines', 'Wide Lines', '<Hidden>'"),
      })).optional().default([]),
      circles: z.array(z.object({
        x: z.number(),
        y: z.number(),
        radius: z.number().describe("mm (model mm in plans, view mm in drafting views)"),
        lineStyle: z.string().optional(),
      })).optional().default([]),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("create_annotations", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `create_annotations failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
