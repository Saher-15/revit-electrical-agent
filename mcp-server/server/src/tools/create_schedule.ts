import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateScheduleTool(server: McpServer) {
  server.tool(
    "create_schedule",
    "Create a Revit schedule (a live table in the model) from a preset or from explicit fields, with filters, sorting/grouping, totals, " +
      "and optionally place it on a sheet. Presets: lighting (fixture counts per level and type), devices (electrical fixtures), switches, " +
      "panels (panel list with loads), circuits (panel, circuit, load name, rating, poles, load, wire, length), conduit and cableTray (lengths per type/size). " +
      "Field names are the Revit schedulable field names (English UI); when one is missing the result lists the available fields.",
    {
      name: z.string().optional(),
      preset: z.enum(["lighting", "devices", "switches", "panels", "circuits", "conduit", "cableTray"]).optional(),
      category: z.string().optional().describe("BuiltInCategory when not using a preset (or to override it), e.g. OST_LightingFixtures"),
      fields: z.array(z.string()).optional().default([]).describe("Field names in column order, e.g. ['Level','Family and Type','Count']"),
      filters: z.array(z.object({
        field: z.string().describe("Must also be in fields"),
        condition: z.enum(["equal", "notEqual", "contains", "notContains", "beginsWith", "endsWith", "greater", "greaterOrEqual", "less", "lessOrEqual", "hasValue", "hasNoValue"]).default("equal"),
        value: z.string().optional().describe("Lengths in mm; Level by name"),
      })).optional().default([]),
      sortBy: z.array(z.object({
        field: z.string(),
        descending: z.boolean().optional().default(false),
        header: z.boolean().optional().default(false).describe("Group header row"),
        footer: z.boolean().optional().default(false).describe("Group footer with totals/count"),
      })).optional().default([]),
      itemize: z.boolean().optional().describe("false = one row per group (counts). Presets choose a default."),
      grandTotal: z.boolean().optional().default(true),
      totals: z.array(z.string()).optional().default([]).describe("Fields to total (e.g. Length)"),
      sheetId: z.number().optional().describe("Place on this sheet"),
      x: z.number().optional().describe("Top-left on the sheet, paper mm"),
      y: z.number().optional(),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("create_schedule", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `create_schedule failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
