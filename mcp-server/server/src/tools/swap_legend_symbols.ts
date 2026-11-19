import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerSwapLegendSymbolsTool(server: McpServer) {
  server.tool(
    "swap_legend_symbols",
    "Revit cannot rotate a legend component (rotate_elements reports success and nothing changes on screen). This tool reads the 2D " +
      "symbols nested in each legend component's family. With apply=false (default) it only reports them: family, type, visible for " +
      "this type, the values the type drives. With apply=true it CHANGES THE MODEL: loads those symbol families into the project, " +
      "places them in the active legend view at the component's position rotated by clockwiseDegrees, and deletes the legend " +
      "component (deleteOriginals=false keeps it). Run it with apply=false first, then apply on ONE id and export the view to check.",
    {
      elementIds: z.array(z.number()).min(1).describe("Legend component ids, all in one legend view"),
      viewId: z.number().optional().describe("That legend view's id. Pass it: without it the active view is used, and the active view can change between calls. Elements of another view are skipped."),
      clockwiseDegrees: z.number().default(90).describe("Clockwise degrees; 90 turns right"),
      apply: z.boolean().default(false).describe("false = read only"),
      deleteOriginals: z.boolean().default(true).describe("Delete each legend component once all its symbols were placed"),
    },
    async (args) => {
      try {
        const response: any = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("swap_legend_symbols", args)
        );
        const r = response?.response ?? response?.Response ?? response;
        return { content: [{ type: "text", text: JSON.stringify(r, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `swap_legend_symbols failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
