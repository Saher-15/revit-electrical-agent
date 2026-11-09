import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerGetElectricalSummaryTool(server: McpServer) {
  server.tool(
    "get_electrical_summary",
    "Read-only overview of the electrical model: element counts per electrical category, panels with their distribution system, circuit count and connected load (VA), " +
      "devices that are not yet on a circuit, available cable tray / conduit types and distribution systems. Call this before designing circuits or raceways. " +
      "Start without includeCircuits; then narrow with panelNameFilter or levelName and includeCircuits=true for one panel's circuits.",
    {
      includeCircuits: z.boolean().optional().default(false).describe("Include each panel's circuit list (large on big models; combine with panelNameFilter)"),
      includeCircuitElementIds: z.boolean().optional().default(false).describe("Include element ids inside each circuit"),
      panelNameFilter: z.string().optional().describe("Only panels whose name contains this text"),
      levelName: z.string().optional().describe("Only panels on this level"),
      maxPanels: z.number().optional().default(50).describe("Maximum number of panels to list"),
      maxUnassignedIds: z.number().optional().default(50).describe("How many unassigned device ids to return"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("get_electrical_summary", args)
        );
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [{ type: "text", text: `get_electrical_summary failed: ${error instanceof Error ? error.message : String(error)}` }],
        };
      }
    }
  );
}
