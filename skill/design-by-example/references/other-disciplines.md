# Other disciplines in the model (HVAC, plumbing, fire, structure): stay out of their way

Topaz designs electrical only. Other systems in the model (linked or in the host file) are read-only context. This file says how to avoid conflicts with them. It adds no rules for designing them.

## What Claude does
1. **Find them.** At session start and in S1 of `full-project.md`, note which disciplines exist: linked models (`manage_links`) and categories in the host (mechanical equipment, ducts, pipes, plumbing fixtures, sprinklers, air terminals). Export the point-like ones with `export_model_snapshot` `extraCategories` (e.g. `OST_MechanicalEquipment`, `OST_PlumbingFixtures`); the engine already treats mechanical equipment, plumbing fixtures and speciality equipment from the architect/links as obstacles for wall hosting. Say in one line what was found and what was not readable (the snapshot may not carry run geometry of ducts and pipes; do not claim it does).
2. **Treat them as obstacles, never as targets.** Do not place an electrical element at the same spot as, or inside the clearance of, another discipline's element. Ceiling devices stay clear of air terminals, sprinklers and duct runs; wall devices stay clear of pipes, wet-wall fixtures and equipment.
3. **Clearances are values, not guesses.** Use a clearance only if it is written in RULES.md, `office.json` or stated by the engineer, with its source. No value exists → ask in the question queue; meanwhile do not place the element in the contested spot (leave it out and list it).
4. **Conflicts are reported, not resolved on their side.** If the electrical element conflicts, move or drop the electrical one by the normal rules and say why. If the other discipline's element is the problem (e.g. a duct through a panel location), report it to the engineer with element ids and level; never move, delete or modify another discipline's element, and never ask for `send_code_to_revit` to do so.
5. **Check visually when geometry is unknown.** There is no clash-detection tool. For a doubtful area use `get_current_view_elements` / `ai_element_filter` on the relevant categories in the working view, and tell the engineer it is a visual/manual check, not a clash run.
6. **Record it.** Open conflicts go in `PROJECT-STATE.md` (question queue) and in the gate report; decisions the engineer makes (which system yields) become a numbered, sourced rule in the project's RULES.md after their yes.

## Not built (say so)
Automatic clash detection and resolution, routing around other systems, clearance tables per system. They belong to the future multi-discipline adapters and need those disciplines' engineers; not in scope for the Topaz electrical pilot.
