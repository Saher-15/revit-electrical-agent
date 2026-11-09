import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerPlaceElectricalElementTool(server: McpServer) {
  server.tool(
    "place_electrical_element",
    "Place electrical family instances (lighting fixtures, switches, receptacles, panels, data/fire-alarm devices) with a proper host. " +
      "hostMode 'ceiling' hosts on the ceiling/floor face above the point (falls back to a horizontal reference plane when there is no ceiling), " +
      "'wall' on the nearest wall face/wall, 'floor' on the floor below, 'level' on the level with Z as elevation, 'plane' on a reference plane at Z, " +
      "'auto' decides from the family. Choose the type with typeId (from get_available_family_types) or with category + familyName/typeName. " +
      "All units are millimeters; Z is absolute project elevation (e.g. level elevation + 2700 for a ceiling fixture, + 300 for a socket, + 1200 for a switch).",
    {
      data: z
        .array(
          z.object({
            name: z.string().describe("Short description, e.g. 'office downlight 1'"),
            typeId: z.number().optional().describe("FamilySymbol ElementId. Preferred."),
            category: z
              .string()
              .optional()
              .describe(
                "BuiltInCategory when searching by name: OST_LightingFixtures, OST_LightingDevices, OST_ElectricalFixtures, OST_ElectricalEquipment, OST_DataDevices, OST_FireAlarmDevices, OST_CommunicationDevices, OST_SecurityDevices"
              ),
            familyName: z.string().optional().describe("Partial family name (case-insensitive) when typeId is not given"),
            typeName: z.string().optional().describe("Partial type name (case-insensitive) when typeId is not given"),
            location: z
              .object({
                x: z.number().describe("X in mm"),
                y: z.number().describe("Y in mm"),
                z: z.number().describe("Absolute elevation in mm (mounting height above project zero)"),
              })
              .describe("Placement point in mm"),
            hostMode: z
              .enum(["auto", "ceiling", "wall", "floor", "level", "plane"])
              .optional()
              .default("auto")
              .describe("How to host the element"),
            hostElementId: z.number().optional().describe("Explicit host element id (ceiling, wall or floor)"),
            levelName: z.string().optional().describe("Level name; defaults to the level at or below Z"),
            scheduleLevel: z.string().optional().describe("Level written to 'Schedule Level' after placement; default = the resolved level, '' leaves it unset"),
            rotation: z.number().optional().describe("Rotation about the vertical axis in degrees"),
            parameters: z
              .record(z.string())
              .optional()
              .describe("Instance parameters to set after placement, name -> value as text (lengths in mm)"),
          })
        )
        .describe("Elements to place"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) =>
          revitClient.sendCommand("place_electrical_element", args)
        );
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `place_electrical_element failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
        };
      }
    }
  );
}
