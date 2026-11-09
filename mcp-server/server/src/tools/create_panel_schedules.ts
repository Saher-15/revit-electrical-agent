import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreatePanelSchedulesTool(server: McpServer) {
  server.tool(
    "create_panel_schedules",
    "Create Revit panel schedules (לוח טבלה) for panels, reusing a panel's existing schedule, and optionally place them side by side on a sheet. " +
      "The panel family must be a panelboard/switchboard (Part Type).",
    {
      panelIds: z.array(z.number()).optional().default([]),
      panelNames: z.array(z.string()).optional().default([]).describe("Panel Name values (exact or partial)"),
      all: z.boolean().optional().default(false).describe("Every panel in the model"),
      templateName: z.string().optional().describe("Panel schedule template (partial). See get_documentation_info.panelScheduleTemplates."),
      sheetId: z.number().optional(),
      x: z.number().optional().describe("Top-left of the first schedule on the sheet, paper mm"),
      y: z.number().optional(),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("create_panel_schedules", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `create_panel_schedules failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
