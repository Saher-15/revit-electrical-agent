import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerRotateElementsTool(server: McpServer) {
  server.tool(
    "rotate_elements",
    "Rotates EXISTING elements in the active view about the vertical axis through each element's own centre (or one common centre). " +
      "CHANGES THE MODEL. Clockwise degrees as seen in the view; 90 = turn right. Each element is its own sub-transaction: one that Revit refuses " +
      "(Revit may not allow rotating legend components) is reported with its error and the others still rotate. Try ONE id first.",
    {
      elementIds: z.array(z.number()).min(1).describe("Revit element ids"),
      clockwiseDegrees: z.number().default(90).describe("Clockwise degrees; 90 turns right, -90 turns left"),
      commonCenter: z.boolean().default(false).describe("Rotate all about one shared centre instead of each about its own centre"),
    },
    async (args) => {
      try {
        const response: any = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("rotate_elements", args)
        );
        const r = response?.response ?? response?.Response ?? response;
        return { content: [{ type: "text", text: JSON.stringify(r, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `rotate_elements failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
