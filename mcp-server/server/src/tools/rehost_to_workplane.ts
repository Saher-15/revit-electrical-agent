import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerRehostToWorkplaneTool(server: McpServer) {
  server.tool(
    "rehost_to_workplane",
    "Face-based devices placed on faces of a LINKED model (Archicad/IFC links especially) lose their host and vanish when the link is reloaded. " +
      "This finds such instances and re-creates each one on a reference plane of this model at the same point and orientation, copies its instance " +
      "parameters, moves its circuits and tags to the new instance and deletes the old one. One plane is shared per face. " +
      "dryRun (default true) does everything, reports, then rolls back. Refuses a cloud model unless allowCloudModel is set. " +
      "Run per level or in batches of maxElements: the client gives up after 120 s.",
    {
      categories: z.array(z.string()).optional().default([]).describe("BuiltInCategory names. Default: the electrical device categories."),
      levelName: z.string().optional().describe("Only instances on this level (exact name)"),
      elementIds: z.array(z.number()).optional().default([]).describe("Only these element ids"),
      dryRun: z.boolean().optional().default(true),
      allowCloudModel: z.boolean().optional().default(false),
      maxElements: z.number().optional().default(150),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("rehost_to_workplane", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `rehost_to_workplane failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
