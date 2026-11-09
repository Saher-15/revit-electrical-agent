import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateElectricalCircuitTool(server: McpServer) {
  server.tool(
    "create_electrical_circuit",
    "Create electrical circuits from devices/fixtures and optionally connect each circuit to a panel. " +
      "Devices need an electrical connector of the given system type (power, data, fire alarm...). " +
      "Returns circuit number, panel, voltage and apparent load (VA). A panel without a distribution system gets one assigned automatically (or pass distributionSystem). Use get_electrical_summary first to find panel ids.",
    {
      data: z
        .array(
          z.object({
            elementIds: z.array(z.number()).min(1).describe("Element ids of the devices/fixtures on this circuit"),
            panelId: z.number().optional().describe("Electrical equipment (panel) element id"),
            panelName: z.string().optional().describe("Panel name when panelId is not given (partial match)"),
            systemType: z
              .enum(["PowerCircuit", "Data", "Telephone", "Security", "FireAlarm", "NurseCall", "Controls", "Communication"])
              .optional()
              .default("PowerCircuit"),
            loadName: z.string().optional().describe("Load Name shown on the panel schedule"),
            distributionSystem: z
              .string()
              .optional()
              .describe("Distribution system to assign to the panel if it has none (e.g. '120/208 Wye', '400/230 Wye'); default: auto-match the circuit voltage"),
          })
        )
        .describe("Circuits to create"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("create_electrical_circuit", args)
        );
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [
            { type: "text", text: `create_electrical_circuit failed: ${error instanceof Error ? error.message : String(error)}` },
          ],
        };
      }
    }
  );
}
