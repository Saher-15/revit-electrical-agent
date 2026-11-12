import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerMoveElementsTool(server: McpServer) {
  server.tool(
    "move_elements",
    "Moves EXISTING elements by a distance in mm along the model axes (in a plan, legend or drafting view: dx = right, dy = up; " +
      "these are model mm, so at scale 1:50 one paper mm is 50). CHANGES THE MODEL. Each element is its own sub-transaction: one " +
      "that Revit refuses is reported with its error and the others still move. Electrical and documentation elements only; never " +
      "move architecture.",
    {
      moves: z
        .array(
          z.object({
            elementId: z.number().describe("Revit element id"),
            dxMm: z.number().default(0),
            dyMm: z.number().default(0),
            dzMm: z.number().default(0),
          })
        )
        .min(1),
    },
    async (args) => {
      try {
        const response: any = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("move_elements", args)
        );
        const r = response?.response ?? response?.Response ?? response;
        return { content: [{ type: "text", text: JSON.stringify(r, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `move_elements failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
