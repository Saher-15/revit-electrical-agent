import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "fs";
import os from "os";
import path from "path";
import ExcelJS from "exceljs";

const home = fs.mkdtempSync(path.join(os.tmpdir(), "revitclaude-"));
process.env.REVITCLAUDE_HOME = home;

const { readMarkups, pdfText, textNear } = await import("../build/production/pdf.js");
const { mergeTasks, writeTaskFiles, loadTasks, taskJsonPath, replyText } = await import("../build/production/tasks.js");
const { documentText } = await import("../build/production/docs.js");
const { saveProjectData, loadProjectData, projectPaths, listDocuments } = await import("../build/production/project.js");
const { markupPdf, instructionsPdf, docx, noisyMarkupPdf } = await import("./fixtures.mjs");

const pdfPath = path.join(home, "review A3.pdf");
fs.writeFileSync(pdfPath, await markupPdf());

test("reads markups, skips links, attaches replies", async () => {
  const { pages, markups, pageCount } = await readMarkups(pdfPath, { sheetNumbers: ["E-100", "E-101"] });
  assert.equal(pageCount, 2);
  assert.equal(markups.length, 2, "square + cloud; reply and link are not separate tasks");
  const sq = markups.find((m) => m.comment.startsWith("Move socket"));
  assert.ok(sq);
  assert.equal(sq.kind, "Square");
  assert.equal(sq.author, "Arch");
  assert.equal(sq.date, "2026-11-05 10:15");
  assert.deepEqual(sq.replies, ["PM: Also add a USB outlet"]);
  assert.equal(sq.color, "#ff0000");
  assert.match(sq.nearbyText, /Kitchen/);
  assert.doesNotMatch(sq.nearbyText, /Living/);
  assert.equal(sq.sheet, "E-101");
  // Centre (320, 400) pt -> mm
  assert.equal(sq.xMm, Math.round(320 * 25.4 / 72 * 10) / 10);
  assert.equal(sq.yMm, Math.round(400 * 25.4 / 72 * 10) / 10);
  assert.equal(pages[0].widthMm, 420);
  assert.equal(pages[0].heightMm, 297);
  const cloud = markups.find((m) => m.comment.includes("lighting"));
  assert.equal(cloud.kind, "Cloud");
  assert.match(cloud.nearbyText, /Living room/);
  assert.equal(pages[1].markups, 0);
});

test("page filter", async () => {
  const { markups } = await readMarkups(pdfPath, { pages: [2] });
  assert.equal(markups.length, 0);
});

test("task files: xlsx, reply, and status kept when the PDF is read again", async () => {
  const { markups } = await readMarkups(pdfPath, {});
  const json = taskJsonPath(pdfPath);
  let tf = mergeTasks(undefined, pdfPath, markups);
  assert.ok(tf.tasks.every((t) => t.status === "open"));
  tf.tasks[0].status = "done";
  tf.tasks[0].action = "Moved socket 123";
  tf.tasks[0].elementIds = [123];
  tf.tasks[1].status = "question";
  tf.tasks[1].note = "Which fixture type?";
  const files = await writeTaskFiles(json, tf);
  assert.ok(fs.existsSync(files.xlsx));
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.readFile(files.xlsx);
  const ws = wb.getWorksheet("Markups");
  assert.equal(ws.rowCount, 3);
  assert.equal(ws.getRow(2).getCell(9).value, "בוצע");
  const reply = fs.readFileSync(files.reply, "utf8");
  assert.match(reply, /בוצע 1/);
  assert.match(reply, /Which fixture type\?/);

  // Re-read: same ids, statuses survive.
  const again = mergeTasks(loadTasks(json), pdfPath, (await readMarkups(pdfPath, {})).markups);
  assert.equal(again.tasks.find((t) => t.id === tf.tasks[0].id).status, "done");
  assert.deepEqual(again.tasks.find((t) => t.id === tf.tasks[0].id).elementIds, [123]);
});

test("reply text lists questions first", () => {
  const tf = { source: "x.pdf", created: "", updated: "2026-11-09T10:00:00Z", tasks: [
    { id: "1", page: 1, kind: "Square", comment: "a", replies: [], nearbyText: "", status: "done", action: "did", elementIds: [], note: "" },
    { id: "2", page: 1, kind: "Square", comment: "b", replies: [], nearbyText: "", status: "question", action: "", elementIds: [], note: "why?" },
  ] };
  const r = replyText(tf);
  assert.ok(r.indexOf("שאלות למתכנן") < r.indexOf("## בוצע"));
});

test("textNear keeps order by distance and caps length", () => {
  const items = [{ str: "far", x: 100, y: 100, w: 10, h: 5 }, { str: "near", x: 10, y: 10, w: 10, h: 5 }];
  assert.equal(textNear(items, [0, 0, 20, 20], 200), "near | far");
  assert.equal(textNear(items, [0, 0, 20, 20], 5), "near");
});

test("document text: pdf, docx, xlsx, txt", async () => {
  const dir = projectPaths("Hospital A").instructions;
  fs.writeFileSync(path.join(dir, "spec.pdf"), await instructionsPdf());
  fs.writeFileSync(path.join(dir, "rules.docx"), await docx(["Use 3x2.5 cable for sockets", "Emergency lights every 15 m"]));
  const wb = new ExcelJS.Workbook();
  wb.addWorksheet("Heights").addRow(["Device", "Height cm"]).commit();
  wb.getWorksheet("Heights").addRow(["Socket", 30]);
  await wb.xlsx.writeFile(path.join(dir, "heights.xlsx"));
  fs.writeFileSync(path.join(dir, "note.txt"), "Panel in corridor");
  fs.writeFileSync(path.join(dir, "~$lock.docx"), "x");

  const files = listDocuments(dir);
  assert.equal(files.length, 4, "lock file skipped");
  const pdf = await documentText(path.join(dir, "spec.pdf"));
  assert.match(pdf, /--- page 1 ---/);
  assert.match(pdf, /Sockets at 30 cm above floor\n2\. Switches at 110 cm/);
  assert.match(await documentText(path.join(dir, "rules.docx")), /Use 3x2.5 cable for sockets\nEmergency lights every 15 m/);
  assert.match(await documentText(path.join(dir, "heights.xlsx")), /## Heights\nDevice\tHeight cm\nSocket\t30/);
  await assert.rejects(documentText(path.join(dir, "old.doc")), /Unsupported/);
});

test("project data merges fields and clears empty ones only when asked", () => {
  saveProjectData("Hospital A", { fields: { "Project Name": "Hospital A", "Client": "MoH" }, titleBlock: "Topaz A1" });
  saveProjectData("Hospital A", { fields: { "Project Number": "2041" } });
  const d = loadProjectData("Hospital A");
  assert.deepEqual(d.fields, { "Project Name": "Hospital A", "Client": "MoH", "Project Number": "2041" });
  assert.equal(d.titleBlock, "Topaz A1");
  assert.ok(projectPaths("Hospital A").dir.startsWith(home));
});

test("pdf text of the markup page", async () => {
  const pages = await pdfText(pdfPath);
  assert.equal(pages.length, 2);
  assert.match(pages[0].text, /Kitchen/);
});

test("skips Acrobat SHX text and empty stamps, keeps clouds without words, cleans line breaks", async () => {
  const file = path.join(home, "noisy.pdf");
  fs.writeFileSync(file, await noisyMarkupPdf());
  const { markups, ignored, pages } = await readMarkups(file, {});
  assert.deepEqual(ignored, { shxText: 4, emptyStamps: 1 });
  assert.equal(markups.length, 3);
  assert.equal(pages[0].markups, 3);
  const stamp = markups.find((m) => m.kind === "Stamp");
  assert.equal(stamp.comment, "Rejected: see comments");
  const cloud = markups.find((m) => m.kind === "Cloud");
  assert.equal(cloud.noText, true);
  assert.equal(cloud.comment, "");
  assert.match(cloud.nearbyText, /Corridor/);
  const text = markups.find((m) => m.kind === "Text");
  assert.equal(text.comment, "Split activities:\na) one\nb) two");
  assert.equal(text.author, "craig");
  assert.equal(text.noText, undefined);
  assert.ok(markups.every((m) => !/\r/.test(m.comment)));
});
