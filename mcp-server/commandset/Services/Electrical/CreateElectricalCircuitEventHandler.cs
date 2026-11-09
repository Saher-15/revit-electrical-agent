using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Electrical
{
    /// <summary>
    /// Creates an electrical circuit (ElectricalSystem) from devices and,
    /// optionally, connects it to a panel. A panel without a distribution
    /// system gets one assigned (explicit or matched to the circuit) first.
    /// </summary>
    public class CreateElectricalCircuitEventHandler : ElectricalHandlerBase<List<CircuitCreationInfo>, List<CircuitInfo>>
    {
        public override string GetName() => "create_electrical_circuit";

        protected override AIResult<List<CircuitInfo>> Run()
        {
            if (Input == null || Input.Count == 0)
                return Fail("No circuits were given.");

            var results = new List<CircuitInfo>();
            using (var tg = new TransactionGroup(Doc, "MCP: create circuits"))
            {
                tg.Start();
                foreach (var info in Input)
                {
                    var r = new CircuitInfo();
                    results.Add(r);
                    using (var t = new Transaction(Doc, "Create circuit"))
                    {
                        t.Start();
                        ElectricalUtils.MakeSilent(t);
                        try
                        {
                            CreateOne(info, r);
                            if (r.CircuitId > 0) t.Commit(); else t.RollBack();
                        }
                        catch (Exception ex)
                        {
                            r.Error = ex.Message;
                            r.CircuitId = -1;
                            t.RollBack();
                        }
                    }
                }
                tg.Assimilate();
            }

            int ok = results.Count(x => x.CircuitId > 0);
            string msg = $"Created {ok} of {results.Count} circuit(s).";
            if (ok == 0) return Fail(msg + " " + string.Join(" | ", results.Where(x => x.Error != null).Select(x => x.Error)));
            return Ok(results, msg);
        }

        private void CreateOne(CircuitCreationInfo info, CircuitInfo r)
        {
            if (info.ElementIds == null || info.ElementIds.Count == 0) { r.Error = "elementIds is empty."; return; }

            if (!Enum.TryParse(info.SystemType ?? "PowerCircuit", true, out ElectricalSystemType systemType))
            {
                r.Error = $"Unknown systemType '{info.SystemType}'. Use PowerCircuit, Data, Telephone, Security, FireAlarm, NurseCall, Controls or Communication.";
                return;
            }
            r.SystemType = systemType.ToString();

            var ids = new List<ElementId>();
            foreach (long id in info.ElementIds.Distinct())
            {
                var el = Doc.GetElement(ElectricalUtils.ToElementId(id));
                if (el == null)
                {
                    Warnings.Add($"Element {id} not found in '{Doc.Title}' (received ids: {string.Join(",", info.ElementIds)}).");
                    continue;
                }
                var fi = el as FamilyInstance;
                if (fi?.MEPModel?.ConnectorManager == null)
                {
                    Warnings.Add($"Element {id} ({el.Category?.Name}) has no electrical connector and was skipped.");
                    continue;
                }
                var connectors = fi.MEPModel.ConnectorManager.Connectors.Cast<Connector>()
                    .Where(c => c.Domain == Domain.DomainElectrical).ToList();
                bool hasMatching = connectors.Any(c => c.ElectricalSystemType == systemType);
                if (!hasMatching)
                {
                    string available = string.Join("/", connectors.Select(c => c.ElectricalSystemType.ToString()).Distinct());
                    Warnings.Add($"Element {id} has no {systemType} connector (has: {available}) and was skipped.");
                    continue;
                }
                // A connector already on a circuit of this type cannot join another one.
                var existing = fi.MEPModel.GetElectricalSystems();
                if (existing != null && existing.Any(s => s.SystemType == systemType && !(fi.MEPModel is ElectricalEquipment)))
                {
                    Warnings.Add($"Element {id} is already on circuit {existing.First(s => s.SystemType == systemType).CircuitNumber} and was skipped.");
                    continue;
                }
                ids.Add(el.Id);
            }
            if (ids.Count == 0) { r.Error = "None of the elements can be circuited (see warnings)."; return; }

            ElectricalSystem system = ElectricalSystem.Create(Doc, ids, systemType);
            if (system == null) { r.Error = "Revit refused to create the circuit."; return; }
            Doc.Regenerate();

            if (!string.IsNullOrWhiteSpace(info.LoadName))
            {
                var p = system.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NAME);
                if (p != null && !p.IsReadOnly) p.Set(info.LoadName);
            }

            FamilyInstance panel = ResolvePanel(info, out string perr);
            if (panel != null)
            {
                EnsureDistributionSystem(panel, system, info.DistributionSystem);
                try
                {
                    system.SelectPanel(panel);
                    Doc.Regenerate();
                }
                catch (Exception ex)
                {
                    string ds = "(none)";
                    try { ds = (panel.MEPModel as ElectricalEquipment)?.DistributionSystem?.Name ?? "(none)"; } catch { }
                    Warnings.Add($"Circuit created but could not connect to panel '{panel.Name}' (distribution system {ds}): {ex.Message}");
                }
            }
            else if (perr != null)
            {
                Warnings.Add(perr);
            }

            Fill(system, r, true);
        }

        /// <summary>
        /// Panels placed by the API have no distribution system, and SelectPanel then fails with
        /// "panel and circuit do not match". Assign one: by name if given, else the first
        /// distribution system whose voltage/phases fit the circuit.
        /// </summary>
        private void EnsureDistributionSystem(FamilyInstance panel, ElectricalSystem system, string requestedName)
        {
            var eq = panel.MEPModel as ElectricalEquipment;
            DistributionSysType current = null;
            try { current = eq?.DistributionSystem; } catch { }
            if (current != null && string.IsNullOrWhiteSpace(requestedName)) return;

            var all = new FilteredElementCollector(Doc).OfClass(typeof(DistributionSysType)).Cast<DistributionSysType>().ToList();
            if (all.Count == 0) { Warnings.Add("The project has no distribution systems (Manage > MEP Settings > Electrical Settings)."); return; }

            DistributionSysType chosen = null;
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                chosen = all.FirstOrDefault(d => d.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase))
                      ?? all.FirstOrDefault(d => d.Name.IndexOf(requestedName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (chosen == null) Warnings.Add($"No distribution system named like '{requestedName}'; matching by voltage instead.");
            }

            if (chosen == null)
            {
                double volts = 0;
                int poles = 0;
                try { volts = ElectricalUtils.ToVolts(system.Voltage); } catch { }
                try { poles = system.PolesNumber; } catch { }
                if (volts <= 0)
                {
                    // Circuit voltage is not known before a panel is assigned: take it from the first device connector.
                    foreach (Element e in system.Elements)
                    {
                        try
                        {
                            var vp = e.get_Parameter(BuiltInParameter.RBS_ELEC_VOLTAGE);
                            if (vp != null && vp.HasValue) volts = ElectricalUtils.ToVolts(vp.AsDouble());
                            var pp = e.get_Parameter(BuiltInParameter.RBS_ELEC_NUMBER_OF_POLES);
                            if (poles <= 0 && pp != null && pp.HasValue) poles = pp.AsInteger();
                        }
                        catch { }
                        if (volts > 0) break;
                    }
                }
                if (poles <= 0) poles = 1;

                // Distance (in volts) between the circuit and a voltage definition: 0 when inside [min,max].
                double Gap(VoltageType vt)
                {
                    if (vt == null) return double.MaxValue;
                    double min = ElectricalUtils.ToVolts(vt.MinValue), max = ElectricalUtils.ToVolts(vt.MaxValue), act = ElectricalUtils.ToVolts(vt.ActualValue);
                    if (volts >= min - 0.5 && volts <= max + 0.5) return 0;
                    return Math.Min(Math.Abs(act - volts), Math.Min(Math.Abs(min - volts), Math.Abs(max - volts)));
                }

                var scored = all.Select(d =>
                {
                    double lg = double.MaxValue, ll = double.MaxValue; string phase = "?";
                    try { lg = Gap(d.VoltageLineToGround); } catch { }
                    try { ll = Gap(d.VoltageLineToLine); } catch { }
                    try { phase = d.ElectricalPhase.ToString(); } catch { }
                    // Single-pole circuits run line-to-ground, multi-pole line-to-line; the other one is a weak fallback.
                    double primary = poles == 1 ? lg : ll;
                    double secondary = poles == 1 ? ll : lg;
                    bool phaseOk = poles < 3 || phase == ElectricalPhase.ThreePhase.ToString();
                    double score = (phaseOk ? 0 : 1000) + Math.Min(primary, secondary + 50);
                    return new { Ds = d, Score = score, Lg = lg, Ll = ll, Phase = phase };
                }).OrderBy(x => x.Score)
                  .ThenByDescending(x => x.Ds.Name.IndexOf("Wye", StringComparison.OrdinalIgnoreCase) >= 0)
                  .ThenBy(x => x.Ds.Name).ToList();

                var best = scored.FirstOrDefault();
                if (best == null || best.Score > 60 || volts <= 0)
                {
                    string table = string.Join("; ", scored.Take(8).Select(x =>
                        $"{x.Ds.Name} [{x.Phase}, L-G gap {(x.Lg == double.MaxValue ? "n/a" : x.Lg.ToString("0"))} V, L-L gap {(x.Ll == double.MaxValue ? "n/a" : x.Ll.ToString("0"))} V]"));
                    Warnings.Add($"No distribution system fits a {poles}-pole {volts:0} V circuit. Candidates: {table}. Pass distributionSystem explicitly.");
                    return;
                }
                chosen = best.Ds;
            }

            var param = panel.get_Parameter(BuiltInParameter.RBS_FAMILY_CONTENT_DISTRIBUTION_SYSTEM);
            if (param == null || param.IsReadOnly)
            {
                Warnings.Add($"Panel '{panel.Name}' has no writable Distribution System parameter.");
                return;
            }
            if (param.AsElementId() != chosen.Id)
            {
                param.Set(chosen.Id);
                Doc.Regenerate();
                Warnings.Add($"Panel '{panel.Name}': distribution system set to '{chosen.Name}'.");
            }
        }

        private FamilyInstance ResolvePanel(CircuitCreationInfo info, out string error)
        {
            error = null;
            if (info.PanelId > 0)
            {
                var fi = Doc.GetElement(ElectricalUtils.ToElementId(info.PanelId)) as FamilyInstance;
                if (fi != null && fi.MEPModel is ElectricalEquipment) return fi;
                error = $"panelId {info.PanelId} is not electrical equipment.";
                return null;
            }
            if (string.IsNullOrWhiteSpace(info.PanelName)) return null;

            var panels = new FilteredElementCollector(Doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.MEPModel is ElectricalEquipment)
                .ToList();
            var match = panels.FirstOrDefault(p => p.Name.Equals(info.PanelName, StringComparison.OrdinalIgnoreCase))
                     ?? panels.FirstOrDefault(p => p.Name.IndexOf(info.PanelName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (match == null) error = $"No panel named like '{info.PanelName}'. Circuit left unassigned.";
            return match;
        }

        public static void Fill(ElectricalSystem system, CircuitInfo r, bool includeElementIds)
        {
            r.CircuitId = system.Id.GetValue();
            r.CircuitNumber = system.CircuitNumber;
            r.PanelName = system.PanelName;
            r.PanelId = system.BaseEquipment?.Id.GetValue() ?? -1;
            r.SystemType = system.SystemType.ToString();
            r.LoadName = system.LoadName;
            try { r.Voltage = Math.Round(ElectricalUtils.ToVolts(system.Voltage), 1); } catch { }
            try { r.ApparentLoadVA = Math.Round(ElectricalUtils.ToVoltAmperes(system.ApparentLoad), 1); } catch { }
            r.ElementCount = system.Elements?.Size ?? 0;
            if (includeElementIds && system.Elements != null)
                foreach (Element e in system.Elements) r.ElementIds.Add(e.Id.GetValue());
        }
    }
}
