
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
