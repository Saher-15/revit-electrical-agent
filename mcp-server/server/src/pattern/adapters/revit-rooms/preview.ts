// HTML preview of a replication: one plan per level with rooms, doors, furniture footprints,
// existing devices, and the replicated points coloured by status (placed / moved / dropped).
import { ModelSnapshot, SnapshotRoom, roomLabel } from "../../../analysis/snapshot.js";
import { Placed } from "../../core.js";

const C = { room: "#5b6b7f", door: "#8a5a2b", furniture: "#c9b79c", existing: "#9aa3ad", placed: "#2a9d55", moved: "#e8a317", dropped: "#d43d3d" };
const esc = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

function levelSvg(name: string, rooms: SnapshotRoom[], snap: ModelSnapshot, placed: Placed[]): string {
  const pts = rooms.flatMap((r) => r.boundaryMm ?? []);
  if (!pts.length) return "";
  const minX = Math.min(...pts.map((p) => p[0])), maxX = Math.max(...pts.map((p) => p[0]));
  const minY = Math.min(...pts.map((p) => p[1])), maxY = Math.max(...pts.map((p) => p[1]));
  const pad = 1000, w = maxX - minX + 2 * pad, h = maxY - minY + 2 * pad;
  const X = (x: number) => (x - minX + pad).toFixed(0), Y = (y: number) => (maxY + pad - y).toFixed(0);
  const u = Math.max(w, h) / 250;
  const keys = new Set(rooms.map((r) => r.key));
  const out = [`<svg viewBox="0 0 ${w.toFixed(0)} ${h.toFixed(0)}" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="${esc(name)}">`];
  for (const f of snap.furniture ?? []) {
    if (!f.roomKey || !keys.has(f.roomKey)) continue;
    out.push(`<rect x="${X(f.bboxMinMm[0])}" y="${Y(f.bboxMaxMm[1])}" width="${(f.bboxMaxMm[0] - f.bboxMinMm[0]).toFixed(0)}" height="${(f.bboxMaxMm[1] - f.bboxMinMm[1]).toFixed(0)}" fill="${C.furniture}" fill-opacity="0.5" stroke="${C.furniture}"><title>${esc(`${f.family ?? ""} ${f.type ?? ""}`)}</title></rect>`);
  }
  for (const r of rooms) {
    out.push(`<polygon points="${r.boundaryMm!.map((p) => `${X(p[0])},${Y(p[1])}`).join(" ")}" fill="none" stroke="${C.room}" stroke-width="${u * 0.4}"/>`);
    const c = r.centerMm ?? r.boundaryMm![0];
    out.push(`<text x="${X(c[0])}" y="${Y(c[1])}" font-size="${u * 2.2}" text-anchor="middle" fill="${C.room}">${esc(roomLabel(r))}</text>`);
  }
  for (const d of snap.doors.filter((d) => (d.fromRoomKey && keys.has(d.fromRoomKey)) || (d.toRoomKey && keys.has(d.toRoomKey)))) {
    const h2 = d.widthMm / 2;
    out.push(`<line x1="${X(d.xyzMm[0] - d.alongWall[0] * h2)}" y1="${Y(d.xyzMm[1] - d.alongWall[1] * h2)}" x2="${X(d.xyzMm[0] + d.alongWall[0] * h2)}" y2="${Y(d.xyzMm[1] + d.alongWall[1] * h2)}" stroke="${C.door}" stroke-width="${u * 0.9}"/>`);
  }
  for (const e of snap.elements.filter((e) => e.xyzMm && keys.has(e.roomKey ?? ""))) out.push(`<circle cx="${X(e.xyzMm![0])}" cy="${Y(e.xyzMm![1])}" r="${u * 0.8}" fill="${C.existing}"/>`);
  for (const p of placed.filter((p) => keys.has(p.parent) && p.point)) {
    const x = +X(p.point![0]), y = +Y(p.point![1]), s = u * 1.4, col = C[p.status];
    const title = `<title>${esc(`${p.ruleId} ${p.status}${p.movedMm ? ` moved ${p.movedMm} mm` : ""}${p.reason ? `: ${p.reason}` : ""}`)}</title>`;
    if (p.role === "light" || p.role === "emergency") out.push(`<circle cx="${x}" cy="${y}" r="${s}" fill="${col}" stroke="#000" stroke-width="${u * 0.15}">${title}</circle>`);
    else if (p.role === "switch" || p.role === "shutter") out.push(`<text x="${x}" y="${y + s * 0.7}" font-size="${s * 2}" font-weight="bold" text-anchor="middle" fill="${col}">S${title}</text>`);
    else out.push(`<polygon points="${x},${y - s} ${x - s},${y + s} ${x + s},${y + s}" fill="${col}">${title}</polygon>`);
  }
  out.push("</svg>");
  return out.join("");
}

export function replicationPreviewHtml(snap: ModelSnapshot, unitsRooms: { unit: string; level?: string; rooms: SnapshotRoom[] }[], placed: Placed[]): string {
  const n = (s: Placed["status"]) => placed.filter((p) => p.status === s).length;
  const dropped = placed.filter((p) => p.status === "dropped").map((p) => `<tr><td>${esc(p.parent)}</td><td>${esc(p.parentType)}</td><td>${esc(p.role)}</td><td>${esc(p.ruleId)}</td><td>${esc(p.reason ?? "")}</td></tr>`).join("");
  const moved = placed.filter((p) => p.status === "moved").map((p) => `<tr><td>${esc(p.parent)}</td><td>${esc(p.role)}</td><td>${p.movedMm}</td><td>${esc(p.reason ?? "")}</td></tr>`).join("");
  return `<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>שכפול תכנון</title>
<style>
:root{--bg:#fff;--fg:#1d2733;--muted:#66717d;--line:#d9dee4}
@media (prefers-color-scheme: dark){:root{--bg:#14181d;--fg:#e6eaee;--muted:#9aa5b1;--line:#2c343d}}
body{background:var(--bg);color:var(--fg);font-family:system-ui,"Segoe UI",Arial,sans-serif;margin:0;padding:16px;max-width:1400px}
h1{font-size:22px;margin:0 0 4px} h2{font-size:18px;margin:28px 0 8px} .muted{color:var(--muted)}
svg{width:100%;height:auto;max-height:80vh;border:1px solid var(--line);border-radius:6px;background:#fff}
table{border-collapse:collapse;width:100%;font-size:13px} th,td{border-bottom:1px solid var(--line);padding:6px;text-align:right;vertical-align:top}
.legend span{display:inline-block;margin-left:16px} .dot{display:inline-block;width:10px;height:10px;border-radius:50%;margin-left:4px;vertical-align:middle}
</style></head><body>
<h1>שכפול תכנון מדוגמה: ${esc(snap.document.title)}</h1>
<p class="muted">שום דבר לא שורטט עדיין. ${n("placed")} מוצבים, ${n("moved")} הוזזו בגלל בדיקת Hosting, ${n("dropped")} נפסלו. עמוד עם העכבר על נקודה לסיבה.</p>
<p class="legend"><span><i class="dot" style="background:${C.placed}"></i>מוצב</span><span><i class="dot" style="background:${C.moved}"></i>הוזז</span><span><i class="dot" style="background:${C.dropped}"></i>נפסל</span><span><i class="dot" style="background:${C.existing}"></i>קיים</span><span><i class="dot" style="background:${C.furniture}"></i>ריהוט/ארונות</span></p>
${unitsRooms.map((u) => `<h2>${esc(u.unit)}${u.level ? ` (${esc(u.level)})` : ""}</h2>${levelSvg(u.unit, u.rooms, snap, placed)}`).join("")}
<h2>הוזזו</h2><table><thead><tr><th>חדר</th><th>תפקיד</th><th>מ"מ</th><th>סיבה</th></tr></thead><tbody>${moved || "<tr><td colspan=4>אין</td></tr>"}</tbody></table>
<h2>נפסלו</h2><table><thead><tr><th>חדר</th><th>סוג חדר</th><th>תפקיד</th><th>חוק</th><th>סיבה</th></tr></thead><tbody>${dropped || "<tr><td colspan=5>אין</td></tr>"}</tbody></table>
</body></html>`;
}
