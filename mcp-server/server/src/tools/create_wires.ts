import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateWiresTool(server: McpServer) {
  server.tool(
    "create_wires",
    "Draw wiring for existing circuits in a plan view: arc (or chamfer) wires from device to device in nearest-neighbour order starting at the device " +
      "closest to the panel, plus a home run pointing to the panel. Circuits that already have wires in the view are skipped. Create the circuits first " +
      "(create_electrical_circuit); get circuit ids from get_electrical_summary.",
    {
      viewId: z.number().optional().describe("Plan view. Default: the active view."),
      circuitIds: z.array(z.number()).optional().default([]),
      panelNames: z.array(z.string()).optional().default([]).describe("Wire every circuit of these panels"),
      wiringType: z.enum(["arc", "chamfer"]).optional().default("arc"),
      wireTypeName: z.string().optional(),
      homeRun: z.boolean().optional().default(true),
      homeRunLength: z.number().optional().default(12).describe("Home run length in PAPER mm (scaled by the view scale)"),
      skipCircuitsWithWires: z.boolean().optional().default(true),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("create_wires", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `create_wires failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
