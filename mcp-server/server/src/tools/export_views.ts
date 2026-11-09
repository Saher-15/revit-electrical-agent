import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExportViewsTool(server: McpServer) {
  server.tool(
    "export_views",
    "Export views or sheets to PNG/JPG or PDF (default folder Documents\\RevitClaude\\exports) and return the file paths. " +
      "After drawing, export the view to PNG and open the image to check your own work (overlaps, tags, layout) before reporting. " +
      "PDF exports sheets at their paper size and is the deliverable format.",
    {
      viewIds: z.array(z.number()).optional().default([]).describe("Views or sheets. Default: the active view."),
      format: z.enum(["png", "jpg", "pdf"]).optional().default("png"),
      folder: z.string().optional(),
      fileName: z.string().optional().describe("Base file name (Revit appends the view name for images)"),
      pixelSize: z.number().optional().default(2000).describe("Image width in pixels"),
      combine: z.boolean().optional().default(true).describe("PDF: one file for all views"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("export_views", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `export_views failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
