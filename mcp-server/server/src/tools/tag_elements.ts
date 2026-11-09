import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerTagElementsTool(server: McpServer) {
  server.tool(
    "tag_elements",
    "Tag elements in a view: every visible element of the given categories (e.g. OST_LightingFixtures, OST_ElectricalFixtures, OST_LightingDevices, " +
      "OST_ElectricalEquipment, OST_CableTray, OST_Conduit, OST_Rooms) and/or specific element ids. Already-tagged elements are skipped. " +
      "OST_Rooms also tags rooms of the linked architectural model on the view's level. Uses the category's default tag unless tagTypeName/tagTypeId is given; " +
      "if no tag family is loaded for a category, load one with load_family (nameFilter 'Tag').",
    {
      viewId: z.number().optional().describe("View to tag in. Default: the active view."),
      categories: z.array(z.string()).optional().default([]).describe("BuiltInCategory names"),
      elementIds: z.array(z.number()).optional().default([]),
      tagTypeId: z.number().optional(),
      tagTypeName: z.string().optional().describe("Tag 'Family: Type' (partial). See get_documentation_info.tagTypes."),
      addLeader: z.boolean().optional().default(false),
      offsetX: z.number().optional().default(0).describe("Tag offset from the element in PAPER mm (scaled by the view scale)"),
      offsetY: z.number().optional().default(4).describe("Tag offset in paper mm"),
      vertical: z.boolean().optional().default(false),
      skipTagged: z.boolean().optional().default(true),
      includeLinkedRooms: z.boolean().optional().default(true),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("tag_elements", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `tag_elements failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
