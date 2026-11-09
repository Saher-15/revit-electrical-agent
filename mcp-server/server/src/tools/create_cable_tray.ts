import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

const runSchema = z.object({
  name: z.string().describe("Run name, e.g. 'corridor main tray'"),
  typeId: z.number().optional().describe("CableTrayType ElementId (see get_electrical_summary.cableTrayTypes). Default: first available type."),
  typeName: z.string().optional().describe("Partial cable tray type name when typeId is not given"),
  points: z
    .array(
      z.object({
        x: z.number().describe("X in mm"),
        y: z.number().describe("Y in mm"),
        z: z.number().describe("Absolute elevation of the tray centerline in mm"),
      })
    )
    .min(2)
    .describe("Polyline points in mm (2 or more). Consecutive segments get elbow fittings."),
  width: z.number().optional().describe("Tray width in mm"),
  height: z.number().optional().describe("Tray height in mm"),
  levelName: z.string().optional().describe("Reference level name; defaults to the level at or below the first point"),
  addFittings: z.boolean().optional().default(true).describe("Insert elbow fittings between segments"),
});

export function registerCreateCableTrayTool(server: McpServer) {
  server.tool(
    "create_cable_tray",
    "Create cable tray runs (polylines) in Revit. The project must have a CableTrayType (electrical templates do; use get_electrical_summary to check). All units are millimeters.",
    { data: z.array(runSchema).describe("Cable tray runs to create") },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("create_cable_tray", args)
        );
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [{ type: "text", text: `create_cable_tray failed: ${error instanceof Error ? error.message : String(error)}` }],
        };
      }
    }
  );
}
