# Default devices per room type (used only when the template apartment has no rule for that anchor)

Heights are mm above finished floor, centre of device. Override in office config `heights` and `roomTypes[].defaults`.

| Room | Default devices |
| --- | --- |
| master_bedroom | Ceiling light + 2-way switches (door 1100, bed sides 700); 1 double socket each side of bed at 400 (300 mm from bed edge, outside nightstand); TV socket + TV/data outlet opposite bed at 1200 behind TV unit; 1 socket at 300 on free wall; AC point per AC unit at 2200 near the unit; shutter switch by the window at 1100. |
| bedroom | Ceiling light + switch at door 1100; 1 double socket each bed side at 400; desk: 2 sockets + data at 300/1100; 1 socket free wall at 300; AC point; shutter switch. |
| living | Ceiling light(s) + 2-way switches; TV wall: 3 sockets + TV + data at 300 or behind TV unit at 1200; sofa wall: 2 sockets at 300; AC point; shutter switches; intercom by entrance at 1400. |
| dining | 1–2 sockets at 300; pendant light + switch. |
| kitchen | 3-phase 25 A cooktop outlet at 300 behind/beside cooktop (never behind the oven); oven socket at 300; fridge socket at 300 behind fridge; dishwasher socket in adjacent cabinet at 300; counter sockets: double every 600–900 mm of counter at counter+250 (not within 300 of sink); hood socket at 2000; under-cabinet light + switch; ceiling light + switch at door. |
| bathroom | Ceiling light (IP44) + switch outside door at 1100; mirror light + switch; 1 socket at 1200 ≥ 600 from basin edge; boiler switch/indicator by the door at 1100; extractor fan if no window; washing machine socket at 1200 if the washer is here. |
| toilet | Ceiling light + switch outside at 1100; extractor fan. |
| mamad | Per standard: ceiling light + switch at door 1100; 2 sockets at 300; TV/data/phone outlet; ventilation/filter socket at 2200 near the filter; AC point. All on the template apartment's walls. |
| laundry | Washing machine socket at 1200; dryer socket at 1200 (3-phase if config says); light + switch; boiler timer. |
| corridor | Ceiling light with 2-way switches at each end 1100; 1 socket at 300; panel: 1800 to top, by the entrance, not in a wet room. |
| balcony | IP44 light + switch inside 1100; 1 IP44 socket at 1200; AC condenser 3-phase/1-phase point per unit. |
| storage | Light + switch; 1 socket at 300. |

Electrical panel: one per apartment, entrance hall/corridor, 1800 mm to top, not opposite a door swing. Bell push outside at 1400, chime inside.

## Engineer review defaults (2026-11-10, residential bathrooms)
- **Bathroom**: heater socket ("תנור חימום") at 2300 mm **above the bathroom door**, centred over the opening on the inside face; a basin socket ("שירות - כיור") at 1100 mm beside the basin.
- **Toilet / laundry**: washing machine **and** dryer sockets at 600 mm beside the stacked appliances, a basin socket at 1100 mm, and a socket above the door at 2300 mm.
- The hosting checks treat the wall above a door head (> 2150 mm) as solid, and the wet zones end at 2250 mm, so these points are not dropped or moved.
- When the engineer's template places these points, copy them exactly; never relocate them on your own.

## Engineer review defaults (2026-11-10, master bedroom + en-suite)
- Shutter switch above the nightstand: one side only, the side nearer the room door.
- Single-phase water-protected isolator for the boiler: next to the boiler in the service balcony, not in the bathroom.
- One electric-boiler switch per apartment, next to the general toilet; none at the bedroom door.
- Light switch heights: single and 2-way 1200 mm; the 2-way above the nightstand 600 mm.
