// Office placement rules applied to every placement before it is sent to Revit (office.json "placement").
import { PlacementCfg } from "./config.js";

export interface WritablePlacement {
  familyName?: string;
  category: string;
  parameters?: Record<string, string>;
  appendParameters?: Record<string, string>;
  workset?: string;
}

/**
 * Adds the office's forced instance parameters (by family-name match; values already on the placement win), the
 * workset for the category, and an optional provenance tag appended to Comments so the designer's text stays.
 */
export function applyPlacementConfig<T extends WritablePlacement>(p: T, cfg: PlacementCfg | undefined, provenance?: string): T {
  const out: T = { ...p, parameters: { ...(p.parameters ?? {}) }, appendParameters: { ...(p.appendParameters ?? {}) } };
  const fam = (p.familyName ?? "").toLowerCase();
  for (const rule of cfg?.instanceParams ?? []) {
    if (!rule.familyMatch || !fam.includes(rule.familyMatch.toLowerCase())) continue;
    for (const [k, v] of Object.entries(rule.set)) if (!(k in out.parameters!)) out.parameters![k] = String(v);
  }
  if (!out.workset && cfg?.worksets?.[p.category]) out.workset = cfg.worksets[p.category];
  if (provenance) out.appendParameters!.Comments = provenance;
  if (!Object.keys(out.parameters!).length) delete out.parameters;
  if (!Object.keys(out.appendParameters!).length) delete out.appendParameters;
  return out;
}
