# Design by Example — system design

**תקציר בעברית:** המערכת מחברת את Claude למודל הנדסי שבו המהנדס תכנן *דוגמה אחת* של אובייקט שחוזר (דירה, חדר, קטע מסילה, קומה טיפוסית), לומדת את התכנון כחוקים ביחס לסביבה (ריהוט, קירות, דלתות, ציר מסילה), ומשלימה את שאר המודל מהדוגמה: מיקום, גובה, Hosting תקין ושיוך לקומה. ליבה אחת כללית, מתאמים (adapters) לכל תחום: Revit חשמל בדירות ראשון, מסילה ב-Civil 3D, ואחר כך מ"א, אינסטלציה, תיאום מערכות והתנגשויות. המסמך מגדיר את הארכיטקטורה, קליטת משרד חדש, אסטרטגיית הבדיקות ותוכנית ההטמעה.

## 1. Problem and vision

Engineering models repeat: a residential tower has one or two apartment types copied over 20 floors; a railway has the same pole, signal and cable arrangement every station; a hospital has the same patient room 300 times. Today an engineer designs the first instance and then copies, pastes and fixes for days, because every instance differs slightly: a mirrored plan, a longer wall, a wardrobe where the socket was.

Design by Example lets the engineer design **one instance** by hand in the authoring tool (Revit, later Civil 3D/AutoCAD) and lets Claude **continue the rest of the model from it**, respecting the geometry of each instance and the office's rules. The engineer stays in control: Claude shows what it understood, proposes, and places only after approval.

Goals:
1. Learn from one example, not from a library of hand-written rules.
2. Understand the surroundings the way an engineer does (this socket belongs to the bed, that switch to the door handle side).
3. Never produce an invalid placement: hosting, clean walls, heights, levels and clearances are checked every time.
4. Run in any office: office-specific knowledge lives in a config file and a per-project rules document, never in code.
5. Be auditable: every placement traces back to the rule and the example element it came from.

## 2. Core concepts

| Term | Meaning | Revit rooms example | Rail example |
| --- | --- | --- | --- |
| **Unit** | One instance of the repeating object | an apartment (set of rooms) or a room | a 60 m station interval of an alignment |
| **Feature** | A geometric anchor inside a unit, with a kind, a local frame (along/across) and tags | wall (inward normal), door (handle side), window, bed, wardrobe (blocks-wall), shower (wet), room centroid | centreline (chainage, right of travel), station start/end |
| **Designed** | An element the engineer placed in the example | socket, switch, light, TV outlet, panel | catenary pole, signal, cable trough |
| **Rule** | A designed element expressed relative to a feature: kind, ordinal, along, across, height, host, type | "socket, bed#0, along −1100, across −950, h 400, wall" | "pole, centerline, along 10000, across 3000" |
| **Pattern** | All rules learned from a unit, with provenance | `<snapshot>_PATTERN.json` | same |
| **Constraint** | A function (candidate, unit) → ok / reason, supplied by the adapter | non-clean wall, behind furniture, door swing, window, wet zone, collision | clearance gauge, platform zone, structure clash |
| **Adapter** | Domain code that builds Units from a model and supplies constraints and output mapping | `adapters/revit-rooms` | `adapters/linear` |

The engine (`mcp-server/server/src/pattern/core.ts`) knows only points, directions, parents and parent types. It has two operations:

- `learn(templateUnit, options)` → Pattern. For each designed element it chooses the most plausible anchor (per-role allowed kinds and priority, then distance) and stores the offset in the anchor's local frame.
- `replicate(pattern, targetUnit, constraints, options)` → Placed[]. For every rule and every parent of the matching type it finds the same anchor (kind + ordinal + tags, with fallback kinds), computes the point, runs the constraints and, on failure, slides along the anchor in steps, tries the mirrored side, and finally drops the point with the reason. Mirrored units flip the along axis.

## 3. Architecture

```mermaid
flowchart LR
  subgraph Client
    CC[Claude Code / Claude chat pane]
    SK[Skill: design-by-example]
  end
  subgraph MCP["MCP server (Node, on the engineer's PC)"]
    T[tools/*]
    E[pattern engine core]
    A1[adapter: revit-rooms]
    A2[adapter: linear / rail]
    A3[adapter: hvac, plumbing, coordination (planned)]
    CFG[(office.json + project RULES.md)]
  end
  subgraph Authoring
    RP[Revit add-in + command set]
    C3[Civil 3D / AutoCAD bridge (planned)]
  end
  CC -- stdio --> T
  SK -. guides .-> CC
  T --> E
  E --> A1 & A2 & A3
  A1 --> CFG
  T -- WebSocket --> RP
  T -. planned .-> C3
```

Components:

1. **Authoring bridges.** The Revit add-in (`mcp-server/plugin`, `mcp-server/commandset`) executes commands inside Revit: export snapshot, place elements with a real host, create views/sheets/schedules, read linked CAD, fill title blocks, etc. A Civil 3D/AutoCAD bridge will expose alignments, profiles and blocks the same way.
2. **MCP server.** TypeScript, runs locally, exposes ~60 tools. Offline tools (snapshot analysis, BOQ, review, propose, learn, replicate, PDF markups) never need Revit; model-changing tools do.
3. **Pattern engine + adapters.** Pure functions, fully unit-tested without Revit.
4. **Skill.** `skill/design-by-example/SKILL.md` tells Claude the workflow, the order of tools, the approval gates and where the office/project rules are.
5. **Configuration.** `office-config/default.json` (families, heights, hosting clearances, room and furniture vocabulary) and per-project `RULES.md` learned from the project's instruction documents.

Data flow for the Revit rooms adapter:

```
export_model_snapshot ─► snapshot.json ─► read_design_unit (show what was understood)
                                        ─► learn_design_pattern ─► PATTERN.json (table for the engineer)
                                        ─► replicate_design_pattern ─► REPLICATE.json (counts, moved, dropped + reasons)
            engineer approves ─► apply_electrical_design ─► place_electrical_element (batches) ─► Schedule Level set
                                        ─► export_model_snapshot ─► review_model ─► drawings
```

## 4. Adapters

### 4.1 Revit rooms (implemented, offline part)
- Unit = apartment (rooms selected by number prefix / keys / level) or single room. Type detection by room-name keywords, Hebrew and English.
- Features: walls from the room boundary with inward normal, matched to model walls for thickness/type (non-clean tags); doors with handle side; windows with sill/head; furniture and fixtures classified by family/type keywords with blocks-wall / wet tags and height ranges; room centroid.
- Designed: electrical elements in the unit's rooms, role from category + name (socket, socket_3ph, switch, shutter, light, emergency, tv, data, ac, panel, bell).
- Constraints: non-clean/thin wall, behind blocking furniture at that height, door frame and swing, window opening, wet zones around shower/bath, collision with existing devices.
- Output: placements for `place_electrical_element` with `levelName` and `scheduleLevel` = the unit's level.
- **Needs from the bridge** (next build): snapshot schemaVersion 2 with `furniture[]`, `windows[]`, `walls[]` from the host model and all links; `place_electrical_element` setting `INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM`.

### 4.2 Linear / rail (prototype)
- Unit = station interval of a polyline alignment; features = centreline with chainage and right-of-travel normal, station start/end.
- Proven in tests: a pole and a signal designed on station 0 continue along the alignment.
- Next: Civil 3D bridge (alignments, profiles for Z, corridors), constraints for clearance gauge and platforms, output to AutoCAD blocks / Civil 3D objects.

### 4.3 Planned adapters (Saher's roadmap)
| Adapter | Example unit | Features | Constraints | Output |
| --- | --- | --- | --- | --- |
| **HVAC (מ"א)** | apartment / typical floor | rooms with load class, ceiling heights, shafts, external walls, windows | duct/pipe routing zones, ceiling void depth, no crossing of mamad walls, condensate slope | FCU/VRF units, ducts, grilles via Revit MEP tools |
| **Plumbing (אינסטלציה)** | wet-room cluster | fixtures (toilet, basin, shower, sink, washer), shafts, slab thickness | slopes, max distance to stack, venting | pipes, fixtures connections |
| **Systems coordination** | floor zone | all MEP runs, structural elements, ceiling void | clearances between systems, maintenance access, sleeves through structure | coordination report, routing proposals |
| **Clash detection** | floor / zone | element solids from all disciplines | hard clash, soft clash (clearance), workflow clash (sequence) | clash list grouped by cause, with element ids and location |
| **Clash resolution** | one clash group | the clashing runs and their free space | which system moves (priority table: gravity > large ducts > trays > pipes), slope and support rules | proposed moves/re-routes as previews, applied after approval |

All of these reuse the same engine (learn how the engineer resolved one typical clash or routed one typical floor, repeat), the same approval gate and the same office/project configuration.

## 5. Office onboarding (any company)

1. **Install** (30 min): Revit add-in + MCP server with `install-electrical.ps1 -RevitYear 2025|2026`; Claude Code login; copy the skill to `%USERPROFILE%\.claude\skills\design-by-example`.
2. **Office config** (1 h): copy `office-config/default.json` to `Documents\RevitClaude\office.json`; fill family/type names from `get_available_family_types`; adjust heights, clearances and vocabulary. No code change.
3. **Project rules** (per project): put the client's instruction documents in `Documents\RevitClaude\Projects\<project>\instructions`; `read_project_documents` → numbered rules with sources → approve → `save_project_rules`. Title block fields and stamp image are stored once.
4. **Pilot**: one building, one typical apartment designed by the office's engineer, Claude replicates one floor; compare with the office's manual design using `review_model` and the comparison workbook; tune the config.
5. **Go-live**: the whole building; then the next project type.
6. **Workshop format** (half day): concepts (unit, feature, rule), live demo on the office's model, hands-on: engineer designs one room, Claude continues the floor, reading the reasons for moved/dropped points, editing office.json.

## 6. Test strategy

| Layer | What | How | Status |
| --- | --- | --- | --- |
| Engine unit tests | learn/replicate math, mirror, slide, fallback kinds, ordinals | `node --test` with synthetic units | ✅ `test/pattern.test.mjs` |
| Adapter tests | room/anchor/role classification (he/en), wall normals, door handle side, hosting constraints, placements with Schedule Level | synthetic snapshot of two apartments | ✅ |
| Linear adapter | stations from a polyline, replication along the alignment | synthetic alignment | ✅ |
| Config | `office-config/default.json` equals embedded defaults; merge rules | file vs code | ✅ |
| Offline tools | PDF markups, project rules, documents, BOQ, review, design over an in-memory MCP client | existing `test/*.test.mjs` | ✅ 12 tests |
| C# geometry | CAD wall detector, sheet-point mapping | `dotnet test tests/production` | ✅ 7 tests |
| Compile gate | command set vs Revit 2025 and 2026 API | `dotnet build` per year | ✅ manual, CI planned |
| Revit smoke tests | place one unit in a sample model, export, review, export PNG | PowerShell script on a PC with Revit, run before a release | planned |
| Acceptance | replicate a floor, compare to the engineer's manual floor: counts per role per room type, moved/dropped ratio, review errors | `compare` workbook (exists for floor 03 of the pilot building) | pilot |

Rules: every bug found in a model becomes a synthetic-snapshot test first. Tests never need Revit or network. `npm test` runs everything in under 10 s.

## 7. Data and security

- The server, snapshots, patterns and proposals stay on the engineer's PC (`Documents\RevitClaude`). Only what Claude reads in the conversation leaves the machine. Companies with an in-tenant rule run Claude through an enterprise/API account without training on data, and may redact the snapshot (`export_model_snapshot` options) before analysis.
- Model-changing tools require an explicit approval step in the conversation; `send_code_to_revit` is disabled; stamping requires the engineer's written approval and is logged.
- Nothing office-specific is in the repository: family names, prices, client documents and models live in the office's own folders.

## 8. Roadmap

| Phase | Deliverable | Depends on |
| --- | --- | --- |
| 1 (now) | Engine + Revit rooms adapter + tests + skill + office config (this repo) | — |
| 2 | Snapshot v2 (furniture, windows, walls from links), Schedule Level in placement, wall-face symbol fix; first replicated floor in the pilot building | Revit build on PC |
| 3 | HTML preview per level for replication (like `propose_electrical_design`), mirrored-apartment detection, apartment-type mapping (A↔A', B) | 2 |
| 4 | Civil 3D / AutoCAD bridge; linear adapter on real alignments; rail constraints | AutoCAD 2027 bridge |
| 5 | HVAC and plumbing adapters on Revit MEP (`create_duct`, `create_pipe`, equipment placement) | 2 |
| 6 | Coordination + clash detection (solid intersection on snapshot v3 with geometry), clash resolution proposals | 5 |
| 7 | Packaging for companies: installer, config wizard, workshop kit, versioned releases, CI (npm test, dotnet build 2025/2026) | 1–3 |
