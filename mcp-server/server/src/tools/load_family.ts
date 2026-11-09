import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerLoadFamilyTool(server: McpServer) {
  server.tool(
    "load_family",
    "Load a Revit family (.rfa) into the project, either from a full file path or by searching the installed Revit libraries by file name " +
      "(e.g. 'Panelboard', 'Cable Tray', 'Downlight'). When several files match, the candidates are returned so you can pick one. Returns the loaded types with their ids.",
    {
      filePath: z.string().optional().describe("Full path to an .rfa file"),
      nameFilter: z.string().optional().describe("Partial file name to search in the Revit libraries (case-insensitive)"),
      loadAllMatches: z.boolean().optional().default(false).describe("Load every matching file instead of listing them"),
      maxResults: z.number().optional().default(20),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("load_family", args)
        );
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [{ type: "text", text: `load_family failed: ${error instanceof Error ? error.message : String(error)}` }],
        };
      }
    }
  );
}
