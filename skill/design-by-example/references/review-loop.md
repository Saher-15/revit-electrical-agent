# Review loop: from the engineer's comments to rules

The engineer (often with the project's designer) reviews Claude's design and gives comments. This is
the main way the skill gets better, so every comment ends as a fix in the model **and** a change to
the rule that produced the point.

## 1. Collect
- Chat comments in the engineer's words ("the TV point in the living rooms is on the kitchen wall").
- PDF markups (`read_pdf_markups` → tasks table).
- A walkthrough in Revit: the engineer selects elements; read their ids, Comments (provenance) and
  position, and ask what is wrong in one line.
Write each comment as: **what** (element / room type / unit), **why wrong**, **what the engineer wants**.

## 2. Classify (one per comment)
| Class | Meaning | Fix in model | Fix in rules |
| --- | --- | --- | --- |
| wrong rule | the learned offset/anchor is wrong for this room type | re-place every point built from the rule | edit the rule in `<snapshot>_PATTERN.json`; note the anchor choice in `session-rules.md` |
| missing rule | the example had no such point, or the engineer wants it everywhere | add in every matching room | add to `room-rules.md` defaults (own design) or to the project `RULES.md` |
| hosting | valid rule, bad surface (behind furniture, wet zone, glass, door frame) | slide / mirror / re-host | tighten `hosting-rules.md` or `office.json > hosting` |
| family / parameter | wrong type, symbol, height, workset, instance parameter | `modify_element` or re-place | `office.json > families` / `placement.instanceParams` / `placement.worksets` |
| project-specific | correct in general, wrong in this project | fix here only | project `RULES.md` |
| disagreement | Claude's own-design choice the engineer rejects | follow the engineer | `room-rules.md` + `lessons-log.md` with the reason |
| accepted | the engineer confirms a choice | none | `lessons-log.md` (confirmed) |

## 3. Fix
- Apply a rule change to **all** units built from that rule, not only the one pointed at; list the
  element ids changed per unit.
- Keep provenance honest: a point changed because of a comment becomes `Claude: own design | ... |
  review <date>`.
- Never Sync or Save; the engineer decides when the model is kept.

## 4. Record
- `references/lessons-log.md`: one dated line per lesson (what, where, rule changed).
- Office-wide → `office.json`; workflow → `session-rules.md`; project → `RULES.md`.
- Propose the rule text to the engineer before writing it; after a yes, commit and push to the repo.

## 5. Metrics (report after every review)
- **Acceptance** = points placed and left unchanged / points placed. Target ≥ 95 %.
- **Completeness** = points the engineer added by hand / final points. Target ≤ 5 %.
- **Hosting rejects** = points the engine refused / points attempted, with the top reasons.
Report per unit and overall, derived and own design separately, in a short table, and keep the
table in `Documents\RevitClaude\compare<level>\review_<date>.md`.
