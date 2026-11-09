# Reading the architect's model

The architect's model is a Revit link (sometimes a DWG link). Read it, never edit it.

## Rooms
From the snapshot: `rooms[]` with `key`, `name`, `number`, `level`, `area`, boundary polygon, and `linkName` when the room is in a link. Classify by name keywords (Hebrew/English), per `office-config` → `roomTypes[].keywords`:

| type | keywords |
| --- | --- |
| master_bedroom | חדר שינה הורים, ח. הורים, master, MBR |
| bedroom | חדר שינה, ח.שינה, bedroom, ילדים |
| living | סלון, living, מגורים |
| kitchen | מטבח, kitchen |
| dining | פינת אוכל, dining |
| bathroom | אמבטיה, רחצה, bath, מקלחת, shower |
| toilet | שירותים, WC, toilet |
| mamad | ממ"ד, ממד, protected, shelter, מרחב מוגן |
| laundry | כביסה, שירות, laundry, utility |
| corridor | מסדרון, פרוזדור, hall, corridor |
| balcony | מרפסת, balcony, terrace |
| storage | מחסן, storage |
| meeting | חדר ישיבות, meeting |
| office | משרד, office |

## Furniture and fixtures (anchors)
Collect from the link: `OST_Furniture`, `OST_FurnitureSystems`, `OST_Casework`, `OST_PlumbingFixtures`, `OST_SpecialityEquipment`, `OST_MechanicalEquipment`, `OST_ElectricalEquipment` (architect's boiler/AC placeholders), `OST_GenericModel` (many architect kitchens), `OST_Doors`, `OST_Windows`. For each: family, type, room (point-in-polygon on the location), bounding box (min/max XYZ in mm), facing orientation, host wall id when wall-hosted.

Anchor classification by family/type name keywords (office config `anchors[]`):
bed (מיטה, bed), nightstand (שידה, nightstand), wardrobe (ארון, closet, wardrobe), tv (טלוויזיה, TV, מזנון), desk (שולחן עבודה, desk), sofa (ספה, sofa), dining_table, kitchen_counter (משטח, counter, תחתון/base cabinet), upper_cabinet (עליון, wall cabinet), sink (כיור, sink), cooktop (כיריים, cooktop, hob), oven (תנור, oven), fridge (מקרר, fridge, refrigerator), dishwasher (מדיח, dishwasher), washing_machine (מכונת כביסה, washer), dryer (מייבש, dryer), toilet (אסלה, WC, toilet), basin (כיור רחצה, basin, lavatory), shower (מקלחת, shower), bath (אמבט, bathtub), boiler (דוד, boiler, water heater), ac_indoor (מזגן, מעבה, FCU, AC), mamad_door (דלת ממ"ד, blast door), mamad_window (חלון ממ"ד).

## Heights
- Element bounding box gives top/bottom in absolute Z; subtract the room level elevation for AFF.
- Wardrobe/upper cabinet: a wall segment is "blocked" between bbox.minZ and bbox.maxZ along the wardrobe's wall face.
- Kitchen counter top = bbox.maxZ (typ. 900 AFF) → counter sockets at counter + 200 to 250 mm.
- Door: `fromRoomKey/toRoomKey`, width, hand/facing flip → handle side; switch goes on the handle side, 150–200 mm from the frame.
- Window: sill height = bbox.minZ; no device above a sill below 1200 unless the engineer did so in the template.
- Ceiling height per room: nearest ceiling in link above room centre (or level-to-level minus slab when none).

## When the architect gives only a DWG
`read_cad_link` → layers, lines, blocks in model mm, `detectWalls`. Block names give furniture anchors; room names come from the engineer or Revit rooms. Say plainly that DWG text is not readable.
