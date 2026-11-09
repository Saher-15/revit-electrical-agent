import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

const placement = z.object({
  viewId: z.number().optional(),
  viewName: z.string().optional().describe("Alternative to viewId (exact or partial name)"),
  x: z.number().optional().describe("Paper mm from the sheet origin (bottom-left). Views: viewport center. Schedules: top-left corner. Omit for automatic layout."),
  y: z.number().optional(),
  viewportTypeName: z.string().optional().describe("Viewport type (partial name), e.g. 'Title w Line'"),
});

const sheetSchema = z.object({
  sheetId: z.number().optional().describe("Existing sheet to add views to (or to move views already on it). Omit to create a new sheet."),
  number: z.string().optional().describe("Sheet number, e.g. 'E-101'"),
  name: z.string().optional().describe("Sheet name, e.g. 'Lighting Plan - Level 1'"),
  titleBlockId: z.number().optional(),
  titleBlockName: z.string().optional().describe("Title block 'Family: Type' (partial). Default: the first one in the project."),
  views: z.array(placement).optional().default([]).describe("Plan/drafting/section views to place. Without x/y they are laid out in a grid inside the title block."),
  schedules: z.array(placement).optional().default([]).describe("Schedules and panel schedules to place (stacked from the top-left when x/y are omitted)"),
  parameters: z.record(z.string()).optional().default({}).describe("Sheet parameters, e.g. {\"Drawn By\": \"SH\", \"Checked By\": \"..\"}"),
});

export function registerCreateSheetsTool(server: McpServer) {
  server.tool(
    "create_sheets",
    "Create sheets with a title block and place views, schedules and panel schedules on them, or add/move views on an existing sheet. " +
      "Reports each viewport's size and whether it runs past the title block (then change the view scale or crop and call again with sheetId to move it). " +
      "A view can be on one sheet only; duplicate it with create_views to reuse it.",
    { data: z.array(sheetSchema).describe("Sheets to create or update") },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("create_sheets", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `create_sheets failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
