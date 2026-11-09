import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { reviewModel } from "../analysis/review.js";

export function registerReviewModelTool(server: McpServer) {
  server.tool(
    "review_model",
    "Design review of a snapshot from export_model_snapshot. Runs offline (Revit not needed) and writes an Excel report plus a Markdown copy. " +
      "Checks: devices on no circuit, panels without distribution system, circuit overload and headroom, estimated voltage drop per circuit, " +
      "three-phase balance per panel, lighting and sockets on one circuit, too many points per circuit, rooms with no lighting, low or excessive " +
      "estimated illuminance (lumen method), lighting power density, too few sockets, missing emergency lighting, rooms lit without a switch, " +
      "devices outside rooms, unloaded links. Every finding has a recommendation and the element ids. Room types and thresholds come from " +
      "defaults (EN 12464 lux, common Israeli practice) that a rules JSON can override.",
    {
      snapshotPath: z.string().describe("Path of the snapshot JSON from export_model_snapshot"),
      outputPath: z.string().optional().describe("Where to write the .xlsx. Default: next to the snapshot, <name>_REVIEW.xlsx"),
      rulesPath: z.string().optional().describe("JSON overriding room types and thresholds (same shape as the defaults)"),
    },
    async (args) => {
      try {
        const result = await reviewModel(args);
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `review_model failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
