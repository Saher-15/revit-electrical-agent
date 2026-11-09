import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { proposeDesign } from "../analysis/design.js";

const typeChoice = z
  .object({
    typeId: z.number().optional().describe("FamilySymbol id (from get_available_family_types)"),
    familyName: z.string().optional(),
    typeName: z.string().optional(),
    lumens: z.number().optional().describe("Fixtures: luminous flux per fixture, lm"),
    watts: z.number().optional(),
  })
  .optional();

export function registerProposeElectricalDesignTool(server: McpServer) {
  server.tool(
    "propose_electrical_design",
    "Proposes an electrical layout for rooms of a snapshot, without touching Revit: lighting count by the lumen method for the room type's lux " +
      "target laid on a grid inside the room outline, sockets spread along the walls clear of doors and corners, a switch beside each door, " +
      "and an emergency light inside each exit door where the room type needs one. Writes a proposal JSON and an HTML preview with a plan per " +
      "level. Types default to the ones the model already uses most. Show the preview and the counts to the engineer and get approval, then " +
      "call apply_electrical_design. Rooms that already have a role (e.g. lights) are left alone unless skipExisting is false.",
    {
      snapshotPath: z.string().describe("Snapshot JSON from export_model_snapshot"),
      roomFilter: z.string().optional().describe("Only rooms whose number, name or key contains this text"),
      levelName: z.string().optional().describe("Only rooms on levels whose name contains this text"),
      roles: z.array(z.enum(["lighting", "socket", "switch", "emergency"])).optional().describe("What to propose. Default: all"),
      skipExisting: z.boolean().optional().default(true),
      fixture: typeChoice.describe("Lighting fixture type and its lumens"),
      socket: typeChoice.describe("Socket (receptacle) type"),
      switchType: typeChoice.describe("Light switch type"),
      emergencyFixture: typeChoice.describe("Emergency light type"),
      rulesPath: z.string().optional().describe("JSON overriding room types (lux, socket spacing, emergency) and heights"),
      outputPath: z.string().optional().describe("Proposal path without extension. Default: <snapshot>_DESIGN"),
    },
    async (args) => {
      try {
        const result = await proposeDesign(args);
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `propose_electrical_design failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
