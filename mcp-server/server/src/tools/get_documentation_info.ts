import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerGetDocumentationInfoTool(server: McpServer) {
  server.tool(
    "get_documentation_info",
    "Read-only. Lists what you need to name things when drawing/documenting: the active view, levels, plan/ceiling/drafting/section views " +
      "(with scale, discipline, template and the sheet they are on), view templates, view family types, sheets with their placed views, " +
      "title block types, tag types per category, text note types, line styles, wire types, schedules, panel schedule templates and panels. " +
      "Call it before create_views, create_sheets, tag_elements, create_schedule, create_panel_schedules, create_wires or create_annotations.",
    {
      levelName: z.string().optional().describe("Only views whose level name contains this text"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("get_documentation_info", args)
        );
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [{ type: "text", text: `get_documentation_info failed: ${error instanceof Error ? error.message : String(error)}` }],
        };
      }
    }
  );
}
