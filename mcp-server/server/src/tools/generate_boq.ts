import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { generateBoq } from "../analysis/boq.js";

export function registerGenerateBoqTool(server: McpServer) {
  server.tool(
    "generate_boq",
    "Builds a bill of quantities (כתב כמויות) Excel file from a snapshot written by export_model_snapshot. Runs offline: Revit is not needed. " +
      "Counts devices, fixtures, panels and fittings per family/type, sums cable tray and conduit length per type and size, and estimates cable " +
      "length per wire size from circuit lengths. Sheets: the bill grouped by chapter with price formulas, quantities per level, per room, a " +
      "mapping sheet, and the source info. Things with no mapping rule are highlighted. To assign BOQ item numbers, descriptions and prices, " +
      "fill the mapping sheet of a previous output (or a JSON array of rules) and pass it as mappingPath. Rule fields category/family/type/" +
      "typeMark/size match as case-insensitive substrings, or as a regex when written /like this/; the first matching rule wins.",
    {
      snapshotPath: z.string().describe("Path of the snapshot JSON from export_model_snapshot"),
      outputPath: z.string().optional().describe("Where to write the .xlsx. Default: next to the snapshot, <name>_BOQ.xlsx"),
      mappingPath: z.string().optional().describe("Mapping rules: .xlsx (sheet 'מיפוי') or .json array"),
      lengthAllowancePercent: z.number().optional().default(0).describe("Percent added to tray, conduit and cable lengths"),
      includeCableEstimate: z.boolean().optional().default(true).describe("Add cable lengths estimated from circuit lengths"),
    },
    async (args) => {
      try {
        const result = await generateBoq(args);
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        return {
          content: [{ type: "text", text: `generate_boq failed: ${error instanceof Error ? error.message : String(error)}` }],
        };
      }
    }
  );
}
