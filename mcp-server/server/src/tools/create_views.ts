import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

const point = z.object({ x: z.number(), y: z.number(), z: z.number().optional().default(0) });

const viewSchema = z.object({
  viewType: z.enum(["floorPlan", "ceilingPlan", "drafting", "duplicate"]).default("floorPlan")
    .describe("floorPlan, ceilingPlan (reflected ceiling plan), drafting (for details/legends/single-line diagrams), or duplicate an existing view"),
  name: z.string().optional().describe("View name, e.g. 'E-101 Lighting - Level 1'. A suffix is added when the name exists."),
  levelName: z.string().optional().describe("Level for plan views (exact or partial)"),
  viewFamilyTypeName: z.string().optional().describe("View type name (partial), e.g. 'Electrical Plan'. Default: first of that kind."),
  templateName: z.string().optional().describe("View template (partial name), e.g. 'Electrical Lighting'"),
  scale: z.number().optional().describe("Scale denominator, e.g. 50 or 100"),
  discipline: z.enum(["electrical", "architectural", "mechanical", "plumbing", "structural", "coordination"]).optional(),
  subDiscipline: z.string().optional().describe("Value for the Sub-Discipline parameter (e.g. 'Lighting', 'Power') when the project has it"),
  cropMin: point.optional().describe("Crop region corner (model mm), plan views"),
  cropMax: point.optional().describe("Opposite crop corner (model mm)"),
  sourceViewId: z.number().optional().describe("For viewType=duplicate: the view to copy"),
  duplicateOption: z.enum(["duplicate", "withDetailing", "dependent"]).optional().default("duplicate"),
  open: z.boolean().optional().default(false).describe("Make it the active view after creation"),
});

export function registerCreateViewsTool(server: McpServer) {
  server.tool(
    "create_views",
    "Create views in Revit: floor plans and reflected ceiling plans per level (e.g. separate lighting and power plans), drafting views, " +
      "or duplicates (with detailing / dependent) of an existing view. Can set name, view template, scale, discipline, sub-discipline and crop region. " +
      "Returns the new view ids. Use get_documentation_info first for level, template and view type names.",
    { data: z.array(viewSchema).describe("Views to create") },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("create_views", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `create_views failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
