import fs from "fs";

// Shape of the JSON written by the export_model_snapshot Revit command (schemaVersion 1).
// Units: mm for coordinates and sizes, m for lengths, m2, m3, VA, V, A.

export interface SnapshotRoom {
  key: string;
  kind: "Room" | "Space";
  source: string;
  id: number;
  number: string;
  name: string;
  level?: string;
  department?: string;
  areaM2: number;
  perimeterM: number;
  volumeM3: number;
  heightMm: number;
  baseElevationMm: number;
  centerMm?: number[];
  boundaryMm?: number[][];
}

export interface SnapshotDoor {
  id: number;
  source: string;
  level?: string;
  family?: string;
  type?: string;
  xyzMm: number[];
  widthMm: number;
  alongWall: number[];
  facing: number[];
  wallThicknessMm?: number;
  fromRoomKey?: string;
  toRoomKey?: string;
}

export interface SnapshotElement {
  id: number;
  category: string;
  builtInCategory: string;
  family?: string;
  type: string;
  typeId: number;
  level?: string;
  xyzMm?: number[];
  heightAboveLevelMm?: number;
  hostId?: number;
  hostCategory?: string;
  roomKey?: string;
  spaceKey?: string;
  panel?: string;
  circuitNumber?: string;
  unassigned?: boolean;
  parameters: Record<string, string>;
}

export interface SnapshotRaceway {
  id: number;
  category: string;
  type: string;
  typeId: number;
  level?: string;
  lengthM: number;
  size?: string;
  widthMm?: number;
  heightMm?: number;
  diameterMm?: number;
  serviceType?: string;
  startMm: number[];
  endMm: number[];
  roomKey?: string;
}

export interface SnapshotCircuit {
  id: number;
  circuitNumber: string;
  panel: string;
  systemType: string;
  loadName?: string;
  voltage: number;
  poles: number;
  ratingA: number;
  apparentLoadVA: number;
  trueLoadW?: number;
  startSlot: number;
  lengthM: number;
  wireSize?: string;
  elementIds: number[];
  feedsPanel?: string;
  parameters?: Record<string, string>;
}

export interface SnapshotPanel {
  id: number;
  name: string;
  family: string;
  type: string;
  level?: string;
  roomKey?: string;
  distributionSystem?: string;
  fedFrom?: string;
  totalApparentLoadVA: number;
  parameters: Record<string, string>;
  circuits: SnapshotCircuit[];
}

export interface SnapshotDistributionSystem {
  id: number;
  name: string;
  phase?: string;
  wires: number;
  lineToLineV?: number;
  lineToGroundV?: number;
}

export interface ModelSnapshot {
  schemaVersion: number;
  exportedAt: string;
  revitVersion: string;
  document: { title: string; path: string; isWorkshared: boolean; activeView?: string; projectInfo: Record<string, string> };
  levels: { id: number; name: string; elevationMm: number }[];
  links: { id: number; name: string; loaded: boolean; documentTitle?: string; roomCount: number }[];
  categoryCounts: Record<string, number>;
  rooms: SnapshotRoom[];
  doors: SnapshotDoor[];
  elements: SnapshotElement[];
  raceways: SnapshotRaceway[];
  panels: SnapshotPanel[];
  circuitsWithoutPanel: SnapshotCircuit[];
  distributionSystems: SnapshotDistributionSystem[];
  warnings: string[];
}

export function loadSnapshot(path: string): ModelSnapshot {
  const text = fs.readFileSync(path, "utf8").replace(/^﻿/, "");
  const snap = JSON.parse(text) as ModelSnapshot;
  if (!snap || typeof snap !== "object" || !Array.isArray(snap.elements)) {
    throw new Error(`${path} is not a model snapshot (run export_model_snapshot first)`);
  }
  for (const key of ["rooms", "doors", "raceways", "panels", "circuitsWithoutPanel", "levels", "links", "distributionSystems", "warnings"] as const) {
    if (!Array.isArray(snap[key])) (snap as any)[key] = [];
  }
  return snap;
}

export function roomLabel(room: SnapshotRoom | undefined): string {
  if (!room) return "";
  return [room.number, room.name].filter((x) => x).join(" ");
}

export function roomIndex(snap: ModelSnapshot): Map<string, SnapshotRoom> {
  return new Map(snap.rooms.map((r) => [r.key, r]));
}
