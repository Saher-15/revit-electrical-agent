// Builds test PDFs and Office files in memory, so the tests need no binary fixtures.
import { PDFDocument, PDFHexString, PDFName, PDFString, StandardFonts } from "pdf-lib";
import JSZip from "jszip";

export async function markupPdf() {
  const doc = await PDFDocument.create();
  const font = await doc.embedFont(StandardFonts.Helvetica);
  const page = doc.addPage([1190.55, 841.89]); // A3 landscape
  page.drawText("Kitchen", { x: 300, y: 400, size: 10, font });
  page.drawText("Living room", { x: 800, y: 600, size: 10, font });
  page.drawText("E-101", { x: 1100, y: 40, size: 12, font });
  const ctx = doc.context;
  const annot = (dict) => {
    const ref = ctx.register(ctx.obj(dict));
    page.node.addAnnot(ref);
    return ref;
  };
  const square = annot({
    Type: "Annot", Subtype: "Square", Rect: [280, 380, 360, 420],
    Contents: PDFString.of("Move socket 300 mm left"), T: PDFString.of("Arch"), C: [1, 0, 0],
    M: PDFString.of("D:20261105101530+02'00'"), NM: PDFString.of("sq1"),
  });
  annot({
    Type: "Annot", Subtype: "Text", Rect: [365, 400, 385, 420], IRT: square,
    Contents: PDFString.of("Also add a USB outlet"), T: PDFString.of("PM"),
  });
  annot({
    Type: "Annot", Subtype: "Polygon", IT: PDFName.of("PolygonCloud"), Rect: [780, 580, 900, 640],
    Vertices: [780, 580, 900, 580, 900, 640, 780, 640], Contents: PDFString.of("Missing lighting here"), T: PDFString.of("Arch"),
  });
  annot({ Type: "Annot", Subtype: "Link", Rect: [10, 10, 50, 50] });
  // Second page: no markups.
  doc.addPage([595.28, 841.89]).drawText("Notes", { x: 50, y: 800, size: 12, font });
  return await doc.save();
}

export async function instructionsPdf() {
  const doc = await PDFDocument.create();
  const font = await doc.embedFont(StandardFonts.Helvetica);
  const page = doc.addPage([595.28, 841.89]);
  page.drawText("1. Sockets at 30 cm above floor", { x: 50, y: 780, size: 12, font });
  page.drawText("2. Switches at 110 cm", { x: 50, y: 760, size: 12, font });
  return await doc.save();
}

export async function docx(paragraphs) {
  const zip = new JSZip();
  const body = paragraphs.map((p) => `<w:p><w:r><w:t>${p}</w:t></w:r></w:p>`).join("");
  zip.file("[Content_Types].xml", "<Types/>");
  zip.file("word/document.xml", `<?xml version="1.0"?><w:document xmlns:w="w"><w:body>${body}</w:body></w:document>`);
  return await zip.generateAsync({ type: "nodebuffer" });
}

/** A page like real CAD PDFs: Acrobat SHX text annotations, an approval stamp, a cloud with no words and a comment with \r breaks. */
export async function noisyMarkupPdf() {
  const doc = await PDFDocument.create();
  const font = await doc.embedFont(StandardFonts.Helvetica);
  const page = doc.addPage([1190.55, 841.89]);
  page.drawText("Corridor", { x: 500, y: 300, size: 10, font });
  const ctx = doc.context;
  const annot = (dict) => page.node.addAnnot(ctx.register(ctx.obj(dict)));
  for (let i = 0; i < 3; i++)
    annot({ Type: "Annot", Subtype: "Square", Rect: [100 + i * 50, 100, 140 + i * 50, 110], T: PDFString.of("AutoCAD SHX Text"), Contents: PDFString.of("EMPRESARIOS AGRUPADOS") });
  annot({ Type: "Annot", Subtype: "Square", Rect: [100, 200, 140, 210], T: PDFString.of("autocad shx text"), Contents: PDFString.of("x") });
  annot({ Type: "Annot", Subtype: "Stamp", Rect: [900, 700, 1000, 760], T: PDFString.of("oferh") });
  annot({ Type: "Annot", Subtype: "Stamp", Rect: [900, 600, 1000, 660], T: PDFString.of("oferh"), Contents: PDFString.of("Rejected: see comments") });
  annot({ Type: "Annot", Subtype: "Polygon", IT: PDFName.of("PolygonCloud"), Rect: [480, 280, 600, 340], T: PDFString.of("pedro") });
  annot({ Type: "Annot", Subtype: "Text", Rect: [520, 320, 540, 340], T: PDFHexString.fromText("craig\r"), Contents: PDFHexString.fromText("Split activities:\ra) one\r\nb) two\r") });
  return await doc.save();
}
