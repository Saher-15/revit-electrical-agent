// Linear adapter: a repeating object along an alignment (railway track, road, pipeline, cable route).
// A unit is one station interval of the alignment; its features are the centreline segment (dir = chainage,
// normal = right of travel) and the station start/end points. Rules learned on one interval
// ("pole 3000 mm right of the centreline every 60 m, signal 1200 mm left at the station start")
// are replicated to all other intervals. Civil 3D / AutoCAD reading is a later command; today the
// alignment comes as a polyline in mm.
import { Designed, Feature, Pt2, Pt3, Unit, len, leftOf, sub, unit as unitVec } from "../../core.js";

export interface Alignment {
  id: string;
  /** Polyline of the centreline, mm. Elevation optional. */
  points: number[][];
  /** Station length, mm (e.g. 60000). */
  stationLengthMm: number;
  /** Rail head / road level when the polyline has no Z. */
  baseZ?: number;
}

export interface LinearDesigned { id: string; role: string; point: Pt3; props?: Record<string, unknown> }

interface Station { index: number; start: Pt2; end: Pt2; dir: Pt2; length: number; chainageMm: number }

function stations(al: Alignment): Station[] {
  const out: Station[] = [];
  let chainage = 0;
  let carry = 0; // distance into the current station
  let index = 0;
  let start: Pt2 | undefined;
  for (let i = 0; i < al.points.length - 1; i++) {
    const a: Pt2 = [al.points[i][0], al.points[i][1]];
    const b: Pt2 = [al.points[i + 1][0], al.points[i + 1][1]];
    const d = unitVec(sub(b, a));
    let remaining = len(sub(b, a));
    let p = a;
    if (!start) start = a;
    while (remaining > 0) {
      const need = al.stationLengthMm - carry;
      if (remaining >= need) {
        const q: Pt2 = [p[0] + d[0] * need, p[1] + d[1] * need];
        out.push({ index, start: start!, end: q, dir: unitVec(sub(q, start!)), length: al.stationLengthMm, chainageMm: chainage });
        index++; chainage += al.stationLengthMm; carry = 0; start = q; p = q; remaining -= need;
      } else {
        carry += remaining; p = b; remaining = 0;
      }
    }
  }
  if (start && carry > 1) out.push({ index, start, end: start, dir: out.length ? out[out.length - 1].dir : [1, 0], length: carry, chainageMm: chainage });
  return out;
}

/** Split the alignment into station units. Designed elements are assigned to the station they fall in. */
export function buildLinearUnits(al: Alignment, designed: LinearDesigned[] = []): Unit[] {
  const baseZ = al.baseZ ?? (al.points[0][2] ?? 0);
  return stations(al).map((s) => {
    const right = [-leftOf(s.dir)[0], -leftOf(s.dir)[1]] as Pt2;
    const parent = `station:${s.index}`;
    const features: Feature[] = [
      { id: `${parent}:centerline`, kind: "centerline", parent, parentType: "station", point: [s.start[0], s.start[1], baseZ], dir: s.dir, normal: right, length: s.length,
        meta: { chainageMm: s.chainageMm } },
      { id: `${parent}:start`, kind: "station_start", parent, parentType: "station", point: [s.start[0], s.start[1], baseZ], dir: s.dir, normal: right },
      { id: `${parent}:end`, kind: "station_end", parent, parentType: "station", point: [s.end[0], s.end[1], baseZ], dir: s.dir, normal: right },
    ];
    const des: Designed[] = designed
      .filter((d) => { const t = (d.point[0] - s.start[0]) * s.dir[0] + (d.point[1] - s.start[1]) * s.dir[1]; return t >= 0 && t < s.length; })
      .map((d) => ({ id: d.id, role: d.role, parent, point: d.point, host: "ground", props: d.props }));
    return { id: `${al.id}/${s.index}`, type: "station", baseZ, origin: s.start, features, designed: des, parents: { [parent]: "station" },
      meta: { chainageMm: s.chainageMm, lengthMm: s.length } };
  });
}
