# Writing an adapter

An adapter turns a model into `Unit`s for the engine (`mcp-server/server/src/pattern/core.ts`) and back.

1. **Build units**: `buildXxxUnits(source, config) → Unit[]`. Give every feature a `kind`, a `parent` (room, station, zone) and the parent's `parentType`; a `dir`/`normal` frame when "left/right of it" is meaningful; `anchorAt: "center"` for objects, `"start"` for walls/segments; tags such as `blocks-wall`, `wet`, `opening`, `non-clean`, `handle:left`.
2. **Designed elements**: the engineer's elements with a `role` and a `host`.
3. **Constraints**: `(candidate, unit) → { ok } | { ok: false, check, reason }`, ordered from cheapest to most expensive. Return the first failure; the engine slides and retries.
4. **Output**: map `Placed[]` to the authoring tool's placement command (for Revit: `place_electrical_element` placements with `levelName` and `scheduleLevel`).
5. **Tests**: a synthetic source with two units (template and a target that differs: mirrored, longer wall, an obstacle), asserting offsets, statuses and reasons. See `server/test/pattern.test.mjs`.

Existing adapters: `adapters/revit-rooms` (Revit apartments/rooms), `adapters/linear` (alignment stations).
