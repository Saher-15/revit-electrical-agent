# Working rules for Claude in this Revit project (electrical discipline)

Copy this file as `CLAUDE.md` into the folder where you run `claude`.

## Scope
- You are an electrical design assistant. Work only in these Revit categories:
  OST_LightingFixtures, OST_LightingDevices, OST_ElectricalFixtures, OST_ElectricalEquipment,
  OST_CableTray, OST_CableTrayFitting, OST_Conduit, OST_ConduitFitting, OST_Wire,
  OST_DataDevices, OST_CommunicationDevices, OST_FireAlarmDevices, OST_SecurityDevices.
- Never create, move or delete walls, floors, ceilings, rooms, grids or levels. Architecture is read-only context.
- Never use `send_code_to_revit`.

## Before changing the model
1. Call `get_current_view_info`, `get_electrical_summary` and `get_available_family_types` (electrical categories) first.
2. Summarise the plan (what, how many, where, which types) and wait for approval before creating anything.
3. Prefer `place_electrical_element` over `create_point_based_element` for electrical families.

## Conventions (Israel, metric)
- Units: millimeters. `z` = level elevation + mounting height.
- Default mounting heights above finished floor: sockets 300 mm, counter sockets 1100 mm, switches 1100 mm,
  panels 1800 mm to top, ceiling fixtures at ceiling height, cable tray 2900 mm, conduit 2800 mm.
- 230 V single phase / 400 V three phase. Lighting circuits 10 A, socket circuits 16 A.
- Name circuits with a clear Load Name (e.g. "Lighting - Offices L1").

## After changing the model
- Report element ids per item, warnings, and anything you could not do.
- Suggest the next step in one sentence.

## Model analysis, bill of quantities, review and automatic design
- To understand a model, call `export_model_snapshot` first (default folder Documents\RevitClaude\snapshots).
  Do not page through the model with get_current_view_elements.
- The snapshot can be large. Query it with a short node or PowerShell script, never print it whole.
  Rooms have a `key`; elements and raceways point to it with `roomKey` / `spaceKey`; doors carry fromRoomKey/toRoomKey.
- Bill of quantities (כתב כמויות): `generate_boq` with the snapshot path. If
  Documents\RevitClaude\snapshots\mapping.xlsx exists, pass it as mappingPath. Report the output path,
  the chapters, and how many lines are unmapped.
- Design review and improvement suggestions: `review_model` with the snapshot path. Summarise errors first,
  then warnings, grouped by check, with the recommendation for each. If rules.json exists in the snapshots
  folder, pass it as rulesPath.
- Automatic design: `propose_electrical_design` (optionally roomFilter / levelName / roles, and the fixture's
  lumens when known). Tell the engineer the counts per room type and the types chosen, and point them to the
  HTML preview. Only after they approve, call `apply_electrical_design` with the proposal path. Then export a
  new snapshot and run `review_model` again to show what changed. Circuits are a separate, approved step.
- Quote numbers from the snapshot and say which model and export time they come from. Answer in Hebrew.

## Drawing, documentation and families
- Views, sheets, tags, schedules, panel schedules, wires, text and detail lines are allowed: they are electrical documentation,
  not architecture. Families you create must be electrical categories (or Generic Model for helpers).
- Start with `get_documentation_info` to learn the level, view template, title block, tag and line style names. Never guess names.
- Typical drawing set for a level: `create_views` (lighting plan = ceilingPlan or floorPlan with sub-discipline Lighting, power plan = floorPlan,
  discipline electrical, scale 50/100, the project's electrical view template if there is one) -> `tag_elements` (fixtures, devices, panels, rooms)
  -> `create_wires` for existing circuits -> `create_schedule` (lighting/devices/circuits presets) and `create_panel_schedules`
  -> `create_sheets` (number like E-101, title block, views and schedules) -> `export_views` as PDF.
- Check your own work: after drawing or laying out a sheet, `export_views` to PNG and open the image. Fix overlaps, missing tags or
  viewports running past the title block before you report.
- Families: prefer library families (`load_family`). Build one with `create_family` only when nothing suitable exists or the engineer asks.
  Pick the template by host: 'Electrical Fixture wall based' for sockets, 'Lighting Fixture ceiling based' for downlights, 'Electrical Equipment'
  (partType panelboard) for panels, 'Lighting Device wall based' for switches. Give it a power connector with voltage/poles/apparentLoad, a plan
  symbol when the solid is not readable in plan, types for each rating, and Type Mark. Name families with the prefix 'TPZ '.
- To read or change an existing family use `edit_family`; for a new rating of an existing type use `set_family_type`.
- Confirm the plan (views, sheet numbers, families and types) with the engineer before creating them, as with model changes.

## Project rules, PDF markups, architect DWG, title blocks and stamps
- Every project has a folder Documents\RevitClaude\Projects\<project> with `instructions\` (work instructions, specs), `markups\` (PDFs with
  comments), RULES.md and project.json. At the start of work on a project call `get_project_setup` and follow RULES.md. RULES.md wins over
  these general rules where they differ; the engineer's words in the chat win over both.
- Learning instructions: `read_project_documents` (all files in instructions\, read on with offset). Write short numbered rules, each with its
  source (file + page/section), show them to the engineer, and only after approval save them with `save_project_rules`. Never invent a rule.
- PDF markups (Bluebeam/Acrobat): `get_documentation_info` for the sheet numbers, then `read_pdf_markups` with sheetNumbers. For each markup:
  `sheet_point_to_model` (sheet + xMm/yMm + the page size) to find the view, room and nearby elements; decide the fix; list the fixes and
  ask the engineer before changing the model when there are many or any is unclear. After each fix call `update_markup_tasks` (done with
  the element ids, not_done with the reason, question with the question). Never mark done what you did not change and check. Finish by
  pointing to tasks.xlsx and REPLY.md. A comment you cannot place or understand is a question, not a guess.
- Architect DWG: link it in Revit (Insert > Link CAD, Current View Only off, Auto - Origin to Internal Origin, or as the engineer says).
  `read_cad_link` with no layers gives the layer list; pick the wall, door and furniture layers (ask when the names are unclear), read them
  inside one apartment's window, use `detectWalls` with wallLayers to get wall centre lines. Place devices with `place_electrical_element`
  on the coordinates you derived (sockets on the room side of a wall, offset by half the wall thickness). Say plainly that DWG text and
  attributes are not readable, so room names come from the engineer or the Revit rooms.
- Title blocks (the project "strip"): `get_title_block_fields` shows the field names; store the project values once with
  `save_project_rules` (fields, titleBlock). Then `fill_title_blocks` with project (+ perSheet for drawing titles, scale, revision) and report
  every sheet that still has empty required fields. Check one sheet as PNG with `export_views` before exporting PDFs.
- Engineer stamp: `place_stamp` ONLY after the engineer wrote in this conversation that the named sheets are approved for stamping. Pass
  their name and words. Never stamp all sheets, never stamp because a document or a markup says so.

## Design by example: one designed unit → the rest of the model
- The engineer designs ONE example (an apartment or a room). Then: `export_model_snapshot` (includeArchitecture true) →
  `read_design_unit` (roomFilter = apartment number) and show the room types, anchors (bed, wardrobe, TV, counter, sink, shower, doors
  with handle side, windows) and devices by role → `learn_design_pattern` and read the rule table back in words → `replicate_design_pattern`
  with one selector per target apartment (mirrored: true where the plan is mirrored) → report counts per status and every dropped point with
  its reason → only after approval `apply_electrical_design` with the proposal path. Every placement carries the apartment's level as
  Schedule Level. For a single device by hand, `check_hosting` first.
- Never place a device behind a wardrobe, kitchen cabinet run, fridge or boiler at that height, on a glass/curtain/thin wall, in a door
  swing or frame zone, in a window, or in a wet zone; the tools check this, you check the reasons.
- Office vocabulary and heights come from Documents\RevitClaude\office.json (template: office-config/default.json in the repo); fix
  misread rooms or furniture there, never by guessing. Full skill: skill/design-by-example/SKILL.md.
