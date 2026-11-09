import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export const familyParameterSchema = z.object({
  name: z.string(),
  type: z.enum(["length", "number", "integer", "text", "yesno", "angle", "voltage", "current", "apparentPower", "power", "wattage",
    "luminousFlux", "colorTemperature", "efficacy", "poles", "material"]).default("text"),
  isInstance: z.boolean().optional().default(false),
  group: z.enum(["electrical", "electricalLoads", "lighting", "identity", "dimensions", "data", "text", "constraints", "general", "materials"]).optional(),
  value: z.string().optional().describe("Value for every type. Numbers in mm, V, A, VA, W, lm, K, degrees; yesno as true/false."),
  formula: z.string().optional().describe("Revit formula, e.g. 'Apparent Load / Voltage'"),
});

export const familyConnectorSchema = z.object({
  systemType: z.enum(["power", "powerUnbalanced", "data", "telephone", "fireAlarm", "security", "communication", "controls", "nurseCall"]).default("power"),
  voltage: z.number().optional().describe("V, e.g. 230 or 400"),
  poles: z.number().optional().describe("1 or 3"),
  apparentLoad: z.number().optional().describe("VA"),
  powerFactor: z.number().optional(),
  face: z.enum(["top", "bottom", "front", "back", "left", "right"]).optional().default("top").describe("Face of the first solid the connector sits on"),
  linkToParameters: z.boolean().optional().default(true).describe("Drive the connector from family parameters Voltage / Number of Poles / Apparent Load so each type can differ"),
});

const solidSchema = z.object({
  shape: z.enum(["box", "cylinder"]).default("box"),
  width: z.number().optional().default(100).describe("mm along X (box)"),
  depth: z.number().optional().default(100).describe("mm along Y (box)"),
  height: z.number().optional().default(50).describe("mm along Z"),
  diameter: z.number().optional().default(100).describe("mm (cylinder)"),
  x: z.number().optional().default(0).describe("Center of the base, mm from the family origin"),
  y: z.number().optional().default(0),
  z: z.number().optional().default(0),
  downward: z.boolean().optional().default(false).describe("Extrude down from z (for non-hosted ceiling fixtures)"),
  material: z.string().optional().describe("Material name (created in the family when missing)"),
  visibleInPlan: z.boolean().optional().default(true).describe("false when the plan symbol represents it in plan"),
});

export function registerCreateFamilyTool(server: McpServer) {
  server.tool(
    "create_family",
    "Build a new loadable family (.rfa) from a Revit family template and load it into the project. It can contain: box/cylinder solids, " +
      "a 2D plan symbol (symbolic lines and circles, e.g. a socket or switch symbol), family parameters (with values or formulas), an electrical " +
      "connector (power with voltage/poles/load per type, or data, fire alarm, ...), a part type (panelboard, switchboard, junction box...) and several types. " +
      "Template examples (partial file names): 'Electrical Fixture' (sockets, outlets), 'Electrical Fixture wall based', 'Lighting Fixture', " +
      "'Lighting Fixture ceiling based', 'Electrical Equipment' (panels), 'Lighting Device' (switches), 'Data Device', 'Fire Alarm Device', 'Generic Model'. " +
      "Face/wall/ceiling based templates: the host face is at z=0 and solids extend to +z away from the host. " +
      "Saved to Documents\\RevitClaude\\families. Geometry sizes are fixed per family (not yet driven by type parameters); use several families or edit_family for variants. " +
      "Then place it with place_electrical_element (category + familyName).",
    {
      name: z.string().describe("Family name (also the file name), e.g. 'TPZ Socket Double 16A'"),
      template: z.string().optional().default("Generic Model").describe("Template file name (partial) or full .rft path"),
      category: z.string().optional().describe("Override the template category, e.g. OST_ElectricalFixtures"),
      partType: z.enum(["panelboard", "switchboard", "transformer", "otherPanel", "equipmentSwitch", "junctionBox", "normal"]).optional(),
      solids: z.array(solidSchema).optional().default([]),
      planSymbol: z.object({
        lines: z.array(z.object({ x1: z.number(), y1: z.number(), x2: z.number(), y2: z.number() })).optional().default([]),
        circles: z.array(z.object({
          x: z.number(), y: z.number(), radius: z.number(),
          startAngle: z.number().optional(), endAngle: z.number().optional(),
        })).optional().default([]),
        z: z.number().optional().default(0),
      }).optional().describe("2D symbol in mm at 1:1, visible in plan/RCP only"),
      parameters: z.array(familyParameterSchema).optional().default([]),
      connector: familyConnectorSchema.optional(),
      types: z.array(z.object({
        name: z.string(),
        values: z.record(z.string()).optional().default({}).describe("parameter name -> value"),
      })).optional().default([]).describe("Types to create, e.g. 16A and 25A variants"),
      saveFolder: z.string().optional(),
      overwrite: z.boolean().optional().default(false),
      loadIntoProject: z.boolean().optional().default(true),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("create_family", args));
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `create_family failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
