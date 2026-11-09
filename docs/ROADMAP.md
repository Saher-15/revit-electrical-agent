# Roadmap

## Exists (in `mcp-server/`, compiles for Revit 2025/2026)
- Electrical: place_electrical_element (host ceiling/wall/floor/level/plane), cable tray, conduit, circuits, summary, load_family.
- Analysis (offline on snapshot): export_model_snapshot, generate_boq, review_model, propose_electrical_design, apply_electrical_design.
- Drawing: views, sheets, tags, dimensions, schedules, panel schedules, wires, text, export PNG/PDF, create/edit family, copy_level_elements.
- Production: project rules from documents, PDF markups → tasks, sheet_point_to_model, read_cad_link, title block fill, stamp.
- Chat pane inside Revit, "Open Claude" button.

## Next: apartment replication (the goal of this repo)
1. **Snapshot: furniture + fixtures from links** — extend `ExportModelSnapshotEventHandler` to export OST_Furniture, FurnitureSystems, Casework, PlumbingFixtures, SpecialityEquipment, MechanicalEquipment, GenericModel, Windows from the host and every link: family, type, room key, bbox mm, facing, host wall id. Doors already exported.
2. **`read_apartment`** (server, offline) — rooms of one apartment (by room numbers / level / polygon) with classified room types, anchors (classified furniture), walls with openings and blocked segments, ceiling heights, existing electrical devices.
3. **`learn_apartment_pattern`** (server, offline) — the engineer's devices → rules relative to anchors (`pattern-format.md`). Shows a readable table per room type.
4. **`check_hosting`** (server, offline, also used internally) — the checks in `hosting-rules.md` on a candidate point; returns ok / moved / dropped with reason.
5. **`replicate_apartment`** (server → Revit) — pattern × target apartments → preview JSON + HTML; after approval places in batches via place_electrical_element and sets Schedule Level.
6. **Schedule Level in `place_electrical_element`** — set INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM from `levelName` right after placement (C#), so every tool gets it for free.
7. Fix known bug: wall-face reference direction in `PlaceElectricalElementEventHandler` (plan symbols of some families scatter when hosted on link wall faces).
8. Tests: offline tests for classification, anchor offsets, hosting checks against a synthetic apartment snapshot.
