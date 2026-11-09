# Hosting, clean walls and Schedule Level

## Hosting
- Wall devices (sockets, switches, TV/data, AC outlets, panels): `hostMode: "wall"`. The host is the nearest wall **face on the room side**. With linked walls the element is face-hosted on the link; that is fine, but then Schedule Level must be set explicitly (below).
- Ceiling fixtures: `hostMode: "ceiling"`; when the link has no ceiling the tool falls back to a reference plane at `z`. Report that.
- Floor boxes: `hostMode: "floor"`.
- Never host on: curtain walls/glazing, railings, columns, furniture, or a wall whose room-side face is not the room being served.

## Clean-wall checks (run before every placement, in this order)
1. **Behind furniture**: reject if the point lies within the plan footprint of a wardrobe, upper/base cabinet run, fridge, boiler, or built-in unit *and* the device height is between that unit's bottom and top (wardrobe ≈ 0–2400; base cabinet 0–900; upper cabinet 1400–2200). Exception: devices the template deliberately put there (e.g. fridge socket behind the fridge, dishwasher socket in the adjacent cabinet) are kept as "behind_appliance" with the same offset.
2. **Door swing and frame**: not within the door leaf arc, not within 100 mm of a frame (switch: 150–200 mm from the frame on the handle side is the target).
3. **Windows**: not inside the window opening ± 100 mm; not above a sill lower than the device height.
4. **Wet zones**: bathroom/toilet: no socket within 600 mm horizontally of shower/bath edge, none below 1200 AFF in zone 2; use the office config `wetZones`.
5. **Mamad**: devices only on the walls and at the heights the standard allows; the template apartment decides. Never on the blast door/window wall within 300 mm of the opening.
6. **Wall thickness/type**: skip walls thinner than `minWallThickness` (default 70 mm) and walls whose type name contains the config `nonCleanWallKeywords` (e.g. "זכוכית", "glass", "מעקה", "railing", "shaft", "פיר").
7. **Non-clean by engineer**: walls the engineer listed (ids or type names) in RULES.md.
8. **Collision**: ≥ 100 mm clear from any existing electrical device unless the template stacks them (then reuse the template's spacing).

Fallback order when a check fails: slide along the same wall away from the obstacle in 100 mm steps up to 600 mm → mirror to the other side of the anchor → nearest other clean wall of the same room → drop the point and list it as "needs engineer".

## Schedule Level
Every placed element: `INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM` ("Schedule Level") = the level of the apartment it serves. Set it right after placement (`modify_element` with the parameter name "Schedule Level" and the level name). This is what drives per-floor schedules, BOQ per level and panel assignment; face-hosted elements on a link otherwise get the wrong or no level.
