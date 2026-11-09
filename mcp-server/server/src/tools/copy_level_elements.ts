import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCopyLevelElementsTool(server: McpServer) {
  server.tool(
    "copy_level_elements",
    "Copy a level's electrical elements straight up or down to other levels, keeping their height above the level. " +
      "Elements of the same type already at the same plan position on a target level are skipped. Refuses a model connected to the cloud unless allowCloudModel is set. " +
      "dryRun (default true) copies, reports, then rolls everything back. Copy one target at a time on large floors: the client gives up after 120 s.",
    {
      sourceLevel: z.string().describe("Exact level name to copy from"),
      targetLevels: z.array(z.string()).describe("Exact level names to copy to"),
      categories: z.array(z.string()).optional().default([]).describe("BuiltInCategory names. Default: the electrical device categories."),
      dryRun: z.boolean().optional().default(true),
      allowCloudModel: z.boolean().optional().default(false).describe("Allow a model connected to the cloud. Only when the user accepted working on it without Sync."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("copy_level_elements", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `copy_level_elements failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
