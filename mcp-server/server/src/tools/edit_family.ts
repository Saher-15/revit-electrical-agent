import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { familyConnectorSchema, familyParameterSchema } from "./create_family.js";

export function registerEditFamilyTool(server: McpServer) {
  server.tool(
    "edit_family",
    "Open a family loaded in the project and read or change it: list its parameters (with values per type, formulas), types and connectors; " +
      "add parameters, create/rename/delete types and set their values, set formulas, add or update the electrical connector " +
      "(voltage, poles, apparent load), set the part type. Changes are reloaded into the project (overwriting parameter values). " +
      "Call it with only familyName to read the family without changing anything.",
    {
      familyId: z.number().optional(),
      familyName: z.string().optional().describe("Family name (exact or partial)"),
      addParameters: z.array(familyParameterSchema).optional().default([]),
      types: z.array(z.object({
        name: z.string().describe("Existing type, or a new type to create"),
        newName: z.string().optional().describe("Rename the type"),
        values: z.record(z.string()).optional().default({}),
      })).optional().default([]),
      deleteTypes: z.array(z.string()).optional().default([]),
      formulas: z.record(z.string()).optional().default({}).describe("parameter -> formula ('' removes it)"),
      connector: familyConnectorSchema.optional(),
      partType: z.enum(["panelboard", "switchboard", "transformer", "otherPanel", "equipmentSwitch", "junctionBox", "normal"]).optional(),
      saveFolder: z.string().optional().describe("Also save the family as .rfa in this folder"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("edit_family", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `edit_family failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
