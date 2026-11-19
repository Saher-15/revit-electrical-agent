# Session rules learned on real models

Rules the engineer confirmed while running the workflow on real projects. They apply in every
session unless the project RULES.md or the engineer says otherwise. Each rule names the symptom
that produced it so the reason survives.

## Placing
- **Batch 4–5 points per request, one transaction each.** Revit redraws only after a commit; a
  10-point batch kept the engineer staring at a frozen plan for two minutes and he assumed nothing
  was happening. Call RefreshActiveView after every commit.
- **Never place while another document is active.** The tools act on the active document. When
  the engineer opens a second model (or Revit switches after a view is created), pause, tell him
  which window to return to, and resume only when the target model and view are active again.
- **Set the family's instance parameters to the office's values at placement, not the defaults.**
  Topaz WP families (`… Under Plaster WP`) default `Symbol offset` to 47/74 when created through the
  API; the engineer's own elements have 0, and his lighting buttons have `Tpz_Symbol 1-100/1-200 =
  No`. The default made the plan symbol land metres outside the building while the element itself
  was correctly hosted. Read the values from a sibling element the engineer placed, or from
  `office.json > placement.instanceParams`.
- **Put each element in the office workset for its category** (`placement.worksets`); API-created
  elements otherwise fall into the active workset (EL_010_Equipment instead of EL_020_Devices /
  EL_040_Lighting).
- **Every element carries provenance in Comments**: `Claude: derived from 02 | apt D/711` for points
  replicated from the engineer's example, `Claude: own design | apt D/711` for points Claude added or
  moved on its own reasoning. The engineer identifies a point by selecting it; apartment letters alone
  mean nothing to him, so always include the architect's room number.
- **Append provenance to Comments, never overwrite.** The engineer's own Comments ("מקרר", "תנור חימום") are
  design intent; the provenance tag goes after them, separated by " | ".
- **Ignore template points below the unit's floor.** The example floor can hold elements that belong to another
  level (pilot project floor 02: points at −350 and −5000). Copying their relative height put 14 points inside the slab.
  A designed point with z below floor − 50 mm is not learned; say how many were skipped.
- **Colour the two sets differently in the working view**: blue RGB(0,112,192) = derived from the
  example, orange RGB(255,128,0) = own design. Say the two colours in the report.
- **Work in a dedicated view** (`03_Claude`, from the office electrical template) when the level has
  no electrical plan, so the engineer can watch and the office views stay untouched.

## Reporting to the engineer
- Give the apartment map first: letter, compass position, the architect's room numbers, planned count.
- After every apartment: placed / failed, with the reason for each failure (hosting rejects become
  own-design work, not silent drops).
- When asked "how long", answer with the remaining items and a time, and offer to stop at the next
  clean boundary.
- Never Sync, never Save, never launch a second Revit. The engineer decides what to keep.

## Environment
- Windows 11 + NVMe with a reported sector size above 4 KB makes SQL Server LocalDB (Steel
  Connections) crash-loop with "misaligned log IOs"; Revit then hangs on open while loading the
  structural link. Fix: registry `stornvme\Parameters\Device\ForcedPhysicalSectorSizeInBytes = * 4095`
  plus a Windows restart. Closing the structural workset does not avoid it.
- Revit 2026 cannot open a 2025 cloud model without upgrading it in ACC; do not upgrade a shared
  model on your own.

## Learning new rules
At the end of a session, list the rules the conversation produced (corrections, confirmed choices,
family quirks), show them to the engineer, and after a yes write office-wide ones to
`office.json`, workflow ones to this file, and project ones to the project's RULES.md.
