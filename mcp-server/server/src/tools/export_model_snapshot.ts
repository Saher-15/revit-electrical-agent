import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExportModelSnapshotTool(server: McpServer) {
  server.tool(
    "export_model_snapshot",
    "Read-only. Exports the whole open model to a JSON file on disk and returns only its path and counts: project info, levels, links, " +
      "element counts per category, rooms (host and linked architectural model) and MEP spaces with area/height/outline, every electrical element " +
      "(lighting, devices, fixtures, equipment, fittings) with family/type/level/position/room/panel/circuit and key parameters, cable tray and " +
      "conduit segments with size and length, panels with their circuits (rating, poles, slot, load, wire size, length) and distribution systems. " +
      "Use it before quantity take-off, design review or automatic design; then read the file from disk (it can be large, so query it with a " +
      "script rather than reading it whole). Units: mm, m, m2, VA, V, A. Large models: narrow with levelName.",
    {
      outputFolder: z.string().optional().describe("Folder for the JSON file. Default: Documents\\RevitClaude\\snapshots"),
      fileName: z.string().optional().describe("File name. Default: <model title>_<timestamp>.json"),
      levelName: z.string().optional().describe("Only levels whose name contains this text"),
      includeLinkedRooms: z.boolean().optional().default(true).describe("Read rooms from loaded links (architectural model)"),
      includeRoomBoundaries: z.boolean().optional().default(true).describe("Include room/space outlines in mm"),
      extraCategories: z.array(z.string()).optional().default([]).describe("Extra BuiltInCategory names to export as elements, e.g. OST_MechanicalEquipment"),
      includeAllParameters: z.boolean().optional().default(false).describe("Dump every parameter with a value instead of the curated list (much larger file)"),
      includeArchitecture: z.boolean().optional().default(true).describe("Furniture, casework, plumbing fixtures, equipment, windows and walls from the host model and all links (schemaVersion 2), for read_design_unit / learn_design_pattern"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("export_model_snapshot", args)
        );
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [{ type: "text", text: `export_model_snapshot failed: ${error instanceof Error ? error.message : String(error)}` }],
        };
      }
    }
  );
}
