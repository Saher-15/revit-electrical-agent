# Model analysis tools (read, bill of quantities, review, automatic design)

Five tools added on top of the electrical command set. All but `apply_electrical_design` are read-only.

| Tool | Where it runs | What it does |
|------|---------------|--------------|
| `export_model_snapshot` | Revit (C# command) | Writes the whole model to one JSON file: project info, levels, links, element counts per category, rooms from the host model and from loaded links (the architectural model), MEP spaces, every electrical element with family/type/level/position/room/panel/circuit and key parameters, cable tray and conduit segments with size and length, panels with all circuits (rating, poles, slot, VA, wire size, length) and distribution systems. Returns only the file path and counts. |
| `generate_boq` | MCP server (Node, Revit not needed) | Builds a bill of quantities `.xlsx` (Hebrew, right-to-left) from a snapshot: the bill by chapter with price formulas, quantities per level, per room, a mapping sheet, and source info. |
| `review_model` | MCP server (offline) | Design review → `<snapshot>_REVIEW.xlsx` + `.md`: devices on no circuit, panels without distribution system, circuit overload and headroom, voltage drop, phase balance, lighting and sockets on one circuit, points per circuit, rooms without lighting, estimated lux vs target, W/m², too few sockets, missing emergency lighting, rooms without a switch, devices outside rooms, unloaded links. Each finding has a recommendation and element ids. |
| `propose_electrical_design` | MCP server (offline) | Per room: lighting count by the lumen method for the room type's lux target (grid in rectangles, balanced layout in L-shapes, max fixture spacing), sockets along the walls clear of doors and corners, a switch by each door, an emergency light inside each exit door. Writes `<snapshot>_DESIGN.json` and an HTML preview with a plan per level. Types default to the ones the model uses most. |
| `apply_electrical_design` | MCP server → Revit | **Changes the model.** Sends an approved proposal to `place_electrical_element` in batches; records what was placed in `<proposal>_APPLIED.json` so a second call continues instead of duplicating. |

Room types (office, meeting, corridor, toilet, stair, lobby, kitchen, storage, protected room, electrical room, parking, living, bedroom, ...) are recognised from Hebrew and English words in the room name. Their lux targets, socket spacing and emergency needs, the mounting heights and the review thresholds are defaults in `server/src/analysis/rules.ts`, not a signed standard: put a `rules.json` with the fields to change in the snapshots folder, e.g. `{ "maxVoltageDropOtherPct": 4, "roomTypes": [{ "id": "office", "lux": 400 }] }`.

## Typical flow

1. `export_model_snapshot` (default folder `Documents\RevitClaude\snapshots`, where Open Claude runs)
2. `generate_boq` with the returned `filePath` -> `<model>_BOQ.xlsx` next to it.
3. Open the `מיפוי` sheet, fill `item` (סעיף), `description`, `unitPrice` for the yellow rows, save it as `mapping.xlsx` in the snapshots folder.
4. Run `generate_boq` again with `mappingPath` = that file. Mapped rows get your item numbers and prices; new types appear yellow.
5. `review_model` for the findings, `propose_electrical_design` for a layout, open the HTML preview, approve, `apply_electrical_design`.

Mapping rules: `category`, `family`, `type`, `typeMark`, `size` match as case-insensitive "contains" (or a regex written `/like this/`); empty means any. The first matching row wins, so put specific rows above general ones. `exclude` = x drops the matches. Rows that share one `item` merge into one BOQ line.

Quantities: devices and fittings are counted (יח'); tray and conduit are summed per type and size (מ"א); cable is estimated per wire size from Revit circuit lengths (an estimate, not routed cable). `lengthAllowancePercent` adds a waste allowance.

## Files

- `commandset/Models/Analysis/ModelSnapshotModels.cs` - request/result and the snapshot schema
- `commandset/Services/Analysis/ExportModelSnapshotEventHandler.cs` - the Revit API reading
- `commandset/Commands/Analysis/AnalysisCommands.cs` - command class (115 s timeout)
- `server/src/analysis/snapshot.ts`, `boq.ts`, `rules.ts`, `review.ts`, `design.ts` - snapshot types, BOQ, rules and geometry, review, design
- `server/src/tools/{export_model_snapshot,generate_boq,review_model,propose_electrical_design,apply_electrical_design}.ts` - MCP tools
- `CLAUDE-electrical.md` - analysis rules appended (the installer deploys it as CLAUDE.md)
- `command.json` - one entry appended (`export_model_snapshot`); `server/package.json` - adds `exceljs`
