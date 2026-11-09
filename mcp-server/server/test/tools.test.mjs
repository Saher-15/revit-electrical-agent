// Runs the MCP tools through a real MCP client/server pair (no Revit needed for these tools).
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "fs";
import os from "os";
import path from "path";

const home = fs.mkdtempSync(path.join(os.tmpdir(), "revitclaude-tools-"));
process.env.REVITCLAUDE_HOME = home;

const { McpServer } = await import("@modelcontextprotocol/sdk/server/mcp.js");
const { Client } = await import("@modelcontextprotocol/sdk/client/index.js");
const { InMemoryTransport } = await import("@modelcontextprotocol/sdk/inMemory.js");
const { registerTools } = await import("../build/tools/register.js");
const { markupPdf } = await import("./fixtures.mjs");

const server = new McpServer({ name: "t", version: "1" });
await registerTools(server);
const [a, b] = InMemoryTransport.createLinkedPair();
await server.connect(a);
const client = new Client({ name: "c", version: "1" });
await client.connect(b);
const call = async (name, args) => {
  const r = await client.callTool({ name, arguments: args });
  const t = r.content[0].text;
  try { return JSON.parse(t); } catch { return t; }
};

test("new tools are registered", async () => {
  const names = (await client.listTools()).tools.map((t) => t.name);
  for (const n of ["read_pdf_markups", "update_markup_tasks", "get_project_setup", "read_project_documents", "save_project_rules",
                   "read_cad_link", "sheet_point_to_model", "get_title_block_fields", "fill_title_blocks", "place_stamp"])
    assert.ok(names.includes(n), n);
});

test("markup round trip through the tools", async () => {
  const pdf = path.join(home, "comments.pdf");
  fs.writeFileSync(pdf, await markupPdf());
  const r = await call("read_pdf_markups", { file: pdf, sheetNumbers: ["E-101"] });
  assert.equal(r.markupCount, 2);
  assert.equal(r.markups[0].sheet, "E-101");
  const id = r.markups[0].id;
  const u = await call("update_markup_tasks", { file: pdf, updates: [{ id, status: "done", action: "moved", elementIds: [5] }, { id: "nope", status: "done" }] });
  assert.equal(u.status.done, 1);
  assert.deepEqual(u.unknownIds, ["nope"]);
  assert.equal(u.stillOpen.length, 1);
});

test("project rules and documents", async () => {
  const empty = await call("read_project_documents", { project: "Tower 5" });
  assert.match(empty, /No documents/);
  const dir = path.join(home, "Projects", "Tower 5", "instructions");
  fs.writeFileSync(path.join(dir, "a.txt"), "x".repeat(50));
  const r1 = await call("read_project_documents", { project: "Tower 5", maxChars: 30 });
  assert.equal(r1.nextOffset, 30);
  const r2 = await call("read_project_documents", { project: "Tower 5", offset: 30, maxChars: 1000 });
  assert.equal(r2.nextOffset, undefined);
  await call("save_project_rules", { project: "Tower 5", rules: "1. A\n", fields: { "Project Name": "Tower 5" } });
  await call("save_project_rules", { project: "Tower 5", rules: "1. B\n" });
  const s = await call("get_project_setup", { project: "Tower 5" });
  assert.equal(s.rules, "1. B\n");
  assert.equal(s.data.fields["Project Name"], "Tower 5");
  assert.ok(fs.existsSync(path.join(home, "Projects", "Tower 5", "RULES.bak.md")));
  const list = await call("get_project_setup", {});
  assert.deepEqual(list.projects, ["Tower 5"]);
  const bad = await call("save_project_rules", { project: "Tower 5", stampImage: "/no/such.png" });
  assert.match(bad, /not found/);
});

test("place_stamp refuses without approval or sheets, before reaching Revit", async () => {
  const noApproval = await client.callTool({ name: "place_stamp", arguments: { sheetNumbers: ["E-101"], engineerApproved: false, approvedBy: "x", approvalNote: "y", imagePath: "a.png" } });
  assert.ok(noApproval.isError || /refused|literal|true/i.test(noApproval.content[0].text));
  const noSheets = await call("place_stamp", { engineerApproved: true, approvedBy: "x", approvalNote: "ok", imagePath: "a.png" });
  assert.match(noSheets, /name the sheets/);
  const noImage = await call("place_stamp", { sheetNumbers: ["E-101"], engineerApproved: true, approvedBy: "x", approvalNote: "ok", imagePath: "/no/stamp.png" });
  assert.match(noImage, /not found/);
});
