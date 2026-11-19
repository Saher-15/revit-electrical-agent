import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerSetElementParametersTool(server: McpServer) {
  server.tool(
    "set_element_parameters",
    "Sets instance parameters on EXISTING elements by id (one transaction per batch of 50). CHANGES THE MODEL. Values are text: " +
      "lengths in mm (e.g. 'Elevation from Level' = '1200'), yes/no as 'true'/'false', text as is. Nothing is placed, moved or " +
      "deleted; a missing or read-only parameter is reported per element, not thrown. Use it for height corrections, symbol " +
      "scale flags, Comments and Schedule Level on elements that are already in the model.",
    {
      changes: z
        .array(
          z.object({
            elementId: z.number().describe("Revit element id"),
            set: z.record(z.string()).describe("Parameter name -> value as text"),
          })
        )
        .min(1)
        .describe("Elements and the parameters to set on each"),
    },
    async (args) => {
      const all: any[] = [];
      let changed = 0;
      let failed = 0;
      for (let i = 0; i < args.changes.length; i += 50) {
        const batch = args.changes.slice(i, i + 50);
        try {
          const response: any = await withRevitConnection(async (revitClient) =>
            revitClient.sendCommand("set_element_parameters", { changes: batch })
          );
          const r = response?.response ?? response?.Response ?? response;
          changed += Number(r?.changed ?? 0);
          failed += Number(r?.failed ?? 0);
          for (const x of r?.results ?? []) if (x?.errors?.length) all.push(x);
        } catch (error) {
          return {
            content: [
              {
                type: "text",
                text: `set_element_parameters failed at batch ${i / 50 + 1}: ${error instanceof Error ? error.message : String(error)}` +
                  `\nDone before the failure: changed ${changed}, failed ${failed}.`,
              },
            ],
          };
        }
      }
      return { content: [{ type: "text", text: JSON.stringify({ changed, failed, errors: all.slice(0, 40) }, null, 2) }] };
    }
  );
}
