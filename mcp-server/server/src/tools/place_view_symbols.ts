import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerPlaceViewSymbolsTool(server: McpServer) {
  server.tool(
    "place_view_symbols",
    "Places 2D symbols (generic annotation or detail item families ALREADY loaded in the project) in a legend, drafting or plan " +
      "view. CHANGES THE MODEL. Pass viewId so the symbols go to that view whatever view is active in Revit; without it the active " +
      "view is used. Coordinates are model mm in that view (x right, y up). Each symbol is its own sub-transaction; the result " +
      "names the view that was used, so check it.",
    {
      viewId: z.number().optional().describe("Target view id (from get_current_view_info or get_documentation_info). Default: the active view"),
      symbols: z
        .array(
          z.object({
            familyName: z.string().describe("Exact family name"),
            typeName: z.string().optional().describe("Type name; default: the family's first type"),
            hostFamilyName: z.string().optional().describe("A loaded host family that nests this symbol: take the symbol from that host. Use it when two hosts nest different symbols under one name; the result's PlacedFamily says which family was placed."),
            separate: z.boolean().optional().describe("With hostFamilyName: load the host's copy as 'name (host)' even when it looks the same as the loaded family. Use it when the loaded one shows fixed text and the host's shows a parameter (e.g. Label)."),
            set: z.record(z.string()).optional().describe("Instance parameters to set on the placed symbol, e.g. {\"Label\": \"D-20\"}"),
            xMm: z.number(),
            yMm: z.number(),
            clockwiseDegrees: z.number().default(0),
          })
        )
        .min(1),
    },
    async (args) => {
      try {
        const response: any = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("place_view_symbols", args)
        );
        const r = response?.response ?? response?.Response ?? response;
        return { content: [{ type: "text", text: JSON.stringify(r, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `place_view_symbols failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
