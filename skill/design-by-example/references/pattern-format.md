# Pattern format (learn → replicate)

Written by `learn_design_pattern` (engine: `mcp-server/server/src/pattern/core.ts`). Read a rule to the engineer as: "<role> in <room type>, <along> mm along the <anchor> (negative = left, looking at it from the room), <across> mm from its face, <height> mm above the floor".

`<snapshot>_PATTERN.json`

```json
{
  "templateApartment": { "level": "03", "rooms": ["03-01 ..."], "apartmentType": "A" },
  "rules": [
    {
      "id": "mbr-bed-left-socket",
      "roomType": "master_bedroom",
      "role": "socket", "familyName": "TPZ Socket Double", "typeName": "16A",
      "anchor": { "kind": "bed", "side": "left", "ref": "edge" },
      "offset": { "along": 300, "height": 400 },
      "host": "wall-behind-anchor",
      "circuitGroup": "sockets-bedrooms",
      "source": { "elementId": 123456, "apartment": "03-01" }
    },
    {
      "id": "mbr-door-switch",
      "roomType": "master_bedroom", "role": "switch",
      "anchor": { "kind": "door", "side": "handle", "ref": "frame" },
      "offset": { "along": 150, "height": 1100 }, "host": "wall-of-door"
    },
    {
      "id": "mbr-ceiling-light",
      "roomType": "master_bedroom", "role": "light",
      "anchor": { "kind": "room", "ref": "centroid" }, "host": "ceiling"
    }
  ],
  "fallbacks": "room-rules.md",
  "learnedAt": "2026-11-09T16:00:00Z"
}
```

Anchor kinds: bed, nightstand, wardrobe, tv, desk, sofa, kitchen_counter, sink, cooktop, oven, fridge, dishwasher, washing_machine, dryer, toilet, basin, shower, bath, boiler, ac_indoor, door, window, wall (longest free / specific orientation), room (centroid, grid NxM).
Sides: left/right (looking from the room at the anchor), handle/hinge for doors, near/far for windows.
`along` is signed mm along the wall; `height` is AFF. `host` is one of wall-behind-anchor, wall-of-door, wall-of-window, ceiling, floor, nearest-free-wall.

Replication output `<snapshot>_REPLICATE.json`: per target apartment, per rule → `{ ruleId, point xyz mm, hostWallId, status: placed|moved|dropped, reason, checks: [...] }`, plus an HTML preview per level like `propose_electrical_design`.


### parentOrdinal

Each rule remembers which parent of its type it came from (`parentOrdinal`, 0 = first in the unit's `parentOrder`: rooms by type, largest first). On replication the rules of bedroom #1 go to the target's first bedroom, #2 to the second; a target with more bedrooms than the template reuses the last bedroom's rules, one with fewer drops the surplus rules with a reason.
