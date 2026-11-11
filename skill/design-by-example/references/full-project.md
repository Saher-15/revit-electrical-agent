# Whole project, one engineer approval per stage

Use when the engineer says "design this project" and no designed example exists for some or all units. Residential first; hospitals, schools and rail reuse the stages with their own room types and rules.

## The autonomy rule (read first)
Autonomous means **Claude does the work between gates without asking per point**. It does NOT mean Claude decides what the engineer has not decided. Engineer's standing rule (Avir, 2026-11-10, RULES.md #11): no "own design" or relocation without the engineer's explicit instruction; ask when information is missing.
So: every stage ends with one gate; everything unknown becomes a **question in the queue**, never a guess placed in the model. Anything placed without a learned example is tagged `Claude: own design` (orange) and is listed at the gate.

## State and resume
Keep `PROJECT-STATE.md` in the project folder (next to RULES.md): current stage, units done, open questions, decisions with who/when, and the next action. Update it after every batch so a new session resumes from the file, not from memory. Never record client data in the shared knowledge layers.

## Question queue
One list in `PROJECT-STATE.md`: question, why it blocks, what was assumed for the draft (marked ASSUMPTION), answered by + date. Ask at gates, in one message, numbered, yes/no where possible. An unanswered question blocks only the elements that depend on it; continue with the rest.

## Stages (each ends at a gate; a gate needs an explicit yes)
| # | Stage | What Claude does | Gate: engineer confirms |
| --- | --- | --- | --- |
| S0 | Intake | `get_project_setup`; read instruction documents (`read_project_documents`); draft numbered, sourced rules; list missing documents | the rules (saved only after yes) |
| S1 | Read the model | snapshot; levels, units, room types, anchors; mirrored plans; what the architect model lacks (heights, furniture) | room-type mapping, unit types, which unit is designed first |
| S2 | Template | engineer designs one unit by hand, OR for a unit type with no example Claude uses `room-rules.md` + office.json and says so | the template (read back in words) |
| S3 | Draft design | `learn_design_pattern` → `replicate_design_pattern`, or `propose_electrical_design` for rooms with no example; HTML preview per level | counts, dropped list with reasons, orange list |
| S4 | Place | `apply_electrical_design` in batches of 5, one floor at a time, working copy only; never Save/Sync | per floor: acceptance after review |
| S5 | Circuits and panels | `create_electrical_circuit`, panel schedules; load per apartment and panel | circuit grouping and panel loads (Claude does not size cables or breakers alone) |
| S6 | Review | `review_model`, the review loop, acceptance metrics, `run-report.json` | errors fixed or accepted one by one |
| S7 | Drawings | views, tags, schedules, sheets, PNG check by Claude of each sheet | sheets list and stamp (engineer places stamps; `place_stamp` needs explicit approval) |
| S8 | Handover | BOQ, rules learned (`RULES.proposed.md`), lessons log, report vs targets | the report |

Floors go in order: one floor accepted before the next starts. A correction applies to every unit built from the rule, then to the rule.

## Stop and ask (do not continue past these)
- Model is cloud/ACC and no local copy is open. Another document is active. Revit shows a dialog.
- A room type, family or height has no rule in RULES.md, office.json or room-rules.md.
- The review shows an error class on more than 5 % of a floor: stop, find the rule that caused it.
- A request would touch another discipline's elements, walls, levels, or needs `send_code_to_revit`.
- The engineer's words conflict with a written rule: follow the engineer, then propose updating the rule.

## Never decided by Claude alone
Cable and breaker sizing, short-circuit and voltage-drop results, standards interpretation, any value the client's documents do not state, stamps and signatures, what is sent to the client.

## What is not built yet (say so, do not improvise)
Load and cable calculations, lighting calculation beyond the lumen method, fire/low-current disciplines beyond placement, Civil 3D adapters, a learned library across projects (needs the shared folder and `RULES.proposed.md`). If the engineer asks for one of these, say it is not available and offer the manual route.

## Report at every gate (5 lines)
Done / placed / failed / open questions / next step. Plus the two acceptance numbers from `review-loop.md` after each reviewed floor.
