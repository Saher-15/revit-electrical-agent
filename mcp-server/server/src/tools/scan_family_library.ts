import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerScanFamilyLibraryTool(server: McpServer) {
  server.tool(
    "scan_family_library",
    "Open every .rfa file under a folder (read-only, nothing is loaded into the project) and report each family's category, placement type " +
      "(WorkPlaneBased = face based, OneLevelBasedHosted = wall/ceiling hosted, OneLevelBased = level based...) and connector count, " +
      "with a count per placement type. Use it to find wall-hosted families in the office library. Paged: call again with skip=nextSkip until nextSkip is absent.",
    {
      folder: z.string().describe("Library folder, e.g. Z:\\BIM-REVIT\\Revit 2023\\Revit Families 2023\\3D families"),
      recursive: z.boolean().optional().default(true),
      includeBackups: z.boolean().optional().default(false).describe("Also scan Revit backup copies (name.0001.rfa)"),
      skip: z.number().optional().default(0).describe("Continue from the previous call's nextSkip"),
      maxFiles: z.number().optional().default(60).describe("Files per call; keep it under ~60 so the call ends within 2 minutes"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("scan_family_library", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `scan_family_library failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
