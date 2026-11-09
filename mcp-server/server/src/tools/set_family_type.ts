import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

const typeSchema = z.object({
  typeId: z.number().optional().describe("Type to start from"),
  category: z.string().optional().describe("BuiltInCategory when finding the type by name"),
  familyName: z.string().optional(),
  typeName: z.string().optional(),
  newName: z.string().optional().describe("Duplicate the type under this name first"),
  values: z.record(z.string()).optional().default({}).describe("Type parameters to set (lengths in mm), e.g. {\"Type Mark\": \"L1\", \"Apparent Load\": \"36\"}"),
});

export function registerSetFamilyTypeTool(server: McpServer) {
  server.tool(
    "set_family_type",
    "Create project types by duplicating an existing type (e.g. a 36 W variant of a fixture, a 250A panel type) and/or set type parameters, " +
      "without opening the family. Parameters that exist only inside the family (or formulas) need edit_family.",
    { data: z.array(typeSchema) },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("set_family_type", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `set_family_type failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
