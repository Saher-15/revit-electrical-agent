# Work plan — from this repo to tests on real models

**בעברית, בקצרה:** תוכנית עבודה מסודרת בשבעה שלבים. כל שלב מגדיר מה נמסר, איך בודקים שהוא גמור (קריטריוני קבלה), ומה הוא דורש ממך. השלב האחרון הוא בדיקות על מודלים אמיתיים של המשרד (KYM-EL-101 בניין 5 ועוד), עם מדדים ברורים להשוואה מול התכנון הידני של המהנדס. סטטוס מתעדכן בקובץ הזה.

Legend: ☐ not started · ◐ in progress · ☑ done · ⚠ needs Saher

## Phase 0 — Repository and design (☑ done 2026-11-09)
- Public repo, current source tree, generic pattern engine, Revit rooms adapter, linear adapter, 4 MCP tools, skill, office config, `DESIGN.md`, 20 offline tests.

## Phase 1 — Revit bridge for the pattern engine (☑ code done 2026-11-09, ⚠ install on PC pending)
Deliverables
1. ☑ `export_model_snapshot` schemaVersion 2: `furniture[]`, `windows[]`, `walls[]` from the host model and every link (bbox in host mm, facing, room key, wall thickness/type/function).
2. ☑ `place_electrical_element`: writes **Schedule Level** (requested or resolved level) on every placed element; `scheduleLevel` field in the request and result.
3. ☑ Wall-face reference direction fix (horizontal along the wall instead of straight up).
4. ☑ Compiles against Revit 2025 and 2026 APIs (`mcp-server/tests/compile-check`, 0 errors).
5. ☑ CLAUDE-electrical.md has the design-by-example section (installer already deploys it); install = `install-electrical.ps1 -RevitYear 2025|2026`.

Acceptance
- Snapshot of a model with the architect link contains ≥ 1 bed/wardrobe/kitchen counter per apartment with room keys, windows with sill/head, walls with thickness; file written in < 2 min for a 20-floor building (narrow with `levelName`).
- After `apply_electrical_design`, every placed element shows the apartment's level in a schedule filtered by Schedule Level.
- Face-hosted sockets on linked walls show their plan symbol on the wall in the floor plan.

Needs Saher ⚠: run `install-electrical.ps1 -RevitYear 2025` (and 2026), restart Revit. Unsaved work in the live model is lost at restart; Save As a detached copy first.

## Phase 2 — First replicated floor on the pilot building (☐)
Deliverables
1. Snapshot of KYM-EL-101-5 with the architect link, level 03 (the template) and level 04 (target), `includeArchitecture: true`.
2. `read_design_unit` on one apartment of 03 → check the room types and anchors with Saher; tune `office.json` keywords for the office's family names (Hebrew/English).
3. `learn_design_pattern` on that apartment → rule table reviewed by Saher.
4. `replicate_design_pattern` to the same apartment type on level 04 → preview; `apply_electrical_design` after approval, in a **detached copy** of the model.
5. ☑ HTML preview per unit for replication (rooms, furniture footprints, points coloured placed/moved/dropped with reasons) — `<proposal>.html`.

Acceptance
- ≥ 90 % of the engineer's devices in the template get a rule with a plausible anchor (reviewed by Saher).
- On the target apartment: ≥ 85 % of points placed or moved ≤ 300 mm, 0 points behind furniture or on glass walls, every point with Schedule Level = 04.
- Time per apartment (offline part) < 10 s; placement limited by Revit (~16 s per element today, see Phase 4).

## Phase 3 — Apartment types, mirroring, whole building (◐)
Deliverables
1. ☑ Mirrored-apartment detection (room-type layout under reflection about X/Y) → `mirrored` auto-flag in `replicate_design_pattern` (`detectMirror`).
2. ☑ `list_design_units`: apartments from room-number prefixes, type labels A/B/C from room-type signature + area; `targetsAuto` replicates to every apartment of the template's type. ☑ `patternPaths`: one pattern per apartment type, each target paired with the pattern of its type.
3. Fallback to room-type defaults (`room-rules.md`) when an anchor is missing, reported per point.
4. Circuits: group replicated devices per apartment into circuits with `create_electrical_circuit` to the apartment panel (separate approval).
5. Comparison workbook: Claude's replication vs the engineer's manual floors (counts per role per room type, moved/dropped ratio, `review_model` errors) — extends the existing `compare03` sheet.

Acceptance
- A full tower (all typical floors) replicated from one floor; comparison workbook shows per-role counts within ±10 % of the manual floors and 0 review errors of type "device outside room" / "device without level".

## Phase 4 — Performance, robustness, packaging (☐)
1. Placement speed: batch host search per wall instead of ray casting per element (target < 3 s per element), progress reporting per batch.
2. Snapshot v3: optional element solids (for clash detection), caching per model version.
3. Installer: one `install.ps1` (add-in + server + skill + office.json template), version stamp, `releases/` zips per Revit year.
4. ☑ CI: GitHub Actions (`.github/workflows/ci.yml`) runs `npm test`, the Revit 2025/2026 compile check and `dotnet test` on every push.
5. Workshop kit (`docs/workshop/`): slides outline, sample model, exercises.

Acceptance: a new PC goes from zero to "Open Claude" in 30 minutes using only the README.

## Phase 5 — Linear adapter on real alignments (☐, after AutoCAD/Civil 3D bridge)
1. Civil 3D bridge commands: read alignments, profiles, corridor stations; place blocks / Civil objects.
2. Linear adapter on real data; constraints: clearance gauge, platform zones, structures.
3. Acceptance: a railway section where one station's poles/signals/troughs are designed by the engineer is continued over the whole alignment and reviewed by the rail team.

## Phase 6 — Further adapters (☐, per `DESIGN.md` §4.3)
HVAC, plumbing, systems coordination, clash detection and clash resolution, each with: synthetic tests first, then one real model, then rollout.

## Phase 7 — Tests on real models (☐, the goal)
Models (Saher's office, detached copies, never Sync):
| Model | Use | Template | Targets |
| --- | --- | --- | --- |
| KYM-EL-101-5 (building 5) | residential pilot | one apartment of floor 03 (Claude's own floor-03 design exists for comparison; Avir's floors 02/05 are the manual reference) | floors 04–top |
| A second residential project with a different architect | generalisation (other family names, room naming) | one apartment | one floor |
| An office/commercial floor | non-residential room types (office, meeting, corridor) | one room cluster | the floor |

Measurements per run (written by `replicate_design_pattern` and the comparison workbook)
1. Coverage: devices in template with a rule / devices in template.
2. Placement quality: placed / moved / dropped; mean moved distance; dropped reasons histogram.
3. Rule compliance: `review_model` errors and warnings before/after; 0 devices behind furniture, on non-clean walls, in wet zones, without Schedule Level.
4. Agreement with the engineer: per room type and role, |Claude count − manual count| / manual count; list of points the engineer would move (reviewed by Saher/Avir).
5. Time: snapshot, offline steps, placement per apartment; engineer review time per floor.

Exit criteria for the pilot: coverage ≥ 90 %, dropped ≤ 5 %, agreement within ±10 % on sockets/switches/lights, review errors 0, and the engineer accepts a floor with ≤ 15 min of manual corrections.

## Order of execution
Phase 1 → 2 → 3 → 4 → 7 (pilot) → 5 → 6 → 7 (other domains). Phases 1–4 are software work in this repo; 2, 3 and 7 each need a Revit session on Saher's PC with the models open.
