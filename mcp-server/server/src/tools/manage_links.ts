import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerManageLinksTool(server: McpServer) {
  server.tool(
    "manage_links",
    "Lists the Revit links of the open model (name, status, cloud/file path, instances) and, with action 'reload', reloads the links that are " +
      "not loaded. A model opened detached and saved locally usually shows every link as 'Not Shared'; without the architect link the snapshot " +
      "has no rooms, furniture or walls, so run this with action 'reload' before export_model_snapshot. A localFolder with the link files " +
      "(same file names) is used first; otherwise the stored cloud/server/file path is reloaded (cloud links need the Autodesk sign-in in Revit).",
    {
      action: z.enum(["list", "reload"]).optional().default("list"),
      names: z.array(z.string()).optional().default([]).describe("Only links whose name contains one of these texts; empty = all"),
      onlyUnloaded: z.boolean().optional().default(true).describe("Reload only links that are not loaded"),
      localFolder: z.string().optional().describe("Folder searched (recursively) for a local .rvt named like the link"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("manage_links", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `manage_links failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
