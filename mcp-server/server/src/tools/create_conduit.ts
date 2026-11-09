import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

const runSchema = z.object({
  name: z.string().describe("Run name, e.g. 'feeder to panel LP-1'"),
  typeId: z.number().optional().describe("ConduitType ElementId (see get_electrical_summary.conduitTypes). Default: first available type."),
  typeName: z.string().optional().describe("Partial conduit type name when typeId is not given"),
  points: z
    .array(
      z.object({
        x: z.number().describe("X in mm"),
        y: z.number().describe("Y in mm"),
        z: z.number().describe("Absolute elevation of the conduit centerline in mm"),
      })
    )
    .min(2)
    .describe("Polyline points in mm (2 or more). Consecutive segments get elbow fittings."),
  diameter: z.number().optional().describe("Conduit diameter in mm (e.g. 20, 25, 32)"),
  levelName: z.string().optional().describe("Reference level name; defaults to the level at or below the first point"),
  addFittings: z.boolean().optional().default(true).describe("Insert elbow fittings between segments"),
});

export function registerCreateConduitTool(server: McpServer) {
  server.tool(
    "create_conduit",
    "Create conduit runs (polylines) in Revit. The project must have a ConduitType (electrical templates do; use get_electrical_summary to check). All units are millimeters.",
    { data: z.array(runSchema).describe("Conduit runs to create") },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("create_conduit", args)
        );
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [{ type: "text", text: `create_conduit failed: ${error instanceof Error ? error.message : String(error)}` }],
        };
      }
    }
  );
}
