import fs from "fs";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { Placement, loadProposal } from "../analysis/design.js";

interface AppliedRecord {
  name: string;
  roomKey: string;
  role: string;
  elementId: number;
  error?: string;
}

export function registerApplyElectricalDesignTool(server: McpServer) {
  server.tool(
    "apply_electrical_design",
    "Draws an approved proposal from propose_electrical_design in Revit, through place_electrical_element in batches. CHANGES THE MODEL: only " +
      "call it after the engineer approved the proposal. Narrow with roomFilter / roles to draw part of it. Placements already drawn (recorded " +
      "in <proposal>_APPLIED.json) are skipped, so a second call continues instead of duplicating. Returns placed and failed counts with errors.",
    {
      proposalPath: z.string().describe("Proposal JSON from propose_electrical_design"),
      roomFilter: z.string().optional().describe("Only rooms whose key or name contains this text"),
      roles: z.array(z.enum(["lighting", "socket", "switch", "emergency"])).optional(),
      batchSize: z.number().optional().default(20).describe("Elements per Revit request"),
      maxElements: z.number().optional().default(400).describe("Stop after this many in one call"),
    },
    async (args) => {
      try {
        const proposal = loadProposal(args.proposalPath);
        const appliedPath = args.proposalPath.replace(/\.json$/i, "") + "_APPLIED.json";
        const applied: AppliedRecord[] = fs.existsSync(appliedPath) ? JSON.parse(fs.readFileSync(appliedPath, "utf8")) : [];
        const done = new Set(applied.filter((a) => a.elementId > 0).map((a) => a.name));
        const filter = args.roomFilter?.toLowerCase();
        const roomNames = new Map(proposal.rooms.map((r) => [r.key, `${r.room} ${r.key}`.toLowerCase()]));
        const todo: Placement[] = proposal.placements
          .filter((p) => !done.has(p.name))
          .filter((p) => !args.roles?.length || args.roles.includes(p.role))
          .filter((p) => !filter || (roomNames.get(p.roomKey) ?? p.roomKey.toLowerCase()).includes(filter))
          .slice(0, Math.max(1, args.maxElements));

        let placed = 0;
        const errors: string[] = [];
        const size = Math.max(1, Math.min(50, args.batchSize));
        for (let i = 0; i < todo.length; i += size) {
          const batch = todo.slice(i, i + size);
          const data = batch.map(({ role, roomKey, ...rest }) => rest);
          let response: any;
          try {
            response = await withRevitConnection(async (revitClient) => revitClient.sendCommand("place_electrical_element", { data }));
          } catch (error) {
            errors.push(`batch ${i / size + 1}: ${error instanceof Error ? error.message : String(error)}`);
            break; // Revit is busy or a dialog is open: stop instead of hammering it.
          }
          const items: any[] = response?.response ?? response?.Response ?? [];
          batch.forEach((p, k) => {
            const r = items.find((x) => x?.name === p.name) ?? items[k] ?? {};
            const id = Number(r.elementId ?? -1);
            const rec: AppliedRecord = { name: p.name, roomKey: p.roomKey, role: p.role, elementId: id, error: r.error ?? undefined };
            const prev = applied.findIndex((a) => a.name === p.name);
            if (prev >= 0) applied[prev] = rec;
            else applied.push(rec);
            if (id > 0) placed++;
            else errors.push(`${p.name}: ${r.error ?? response?.message ?? response?.Message ?? "not placed"}`);
          });
          fs.writeFileSync(appliedPath, JSON.stringify(applied, null, 1), "utf8");
        }
        const remaining = proposal.placements.filter((p) => !applied.some((a) => a.name === p.name && a.elementId > 0)).length;
        const result = { appliedPath, attempted: todo.length, placed, failed: todo.length - placed, remainingInProposal: remaining, errors: errors.slice(0, 30) };
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `apply_electrical_design failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
