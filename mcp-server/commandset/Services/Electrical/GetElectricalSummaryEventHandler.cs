using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Electrical
{
    /// <summary>Read-only overview of the electrical model: counts, panels, circuits, loads, raceway types.</summary>
    public class GetElectricalSummaryEventHandler : ElectricalHandlerBase<ElectricalSummaryRequest, ElectricalSummary>
    {
        public override string GetName() => "get_electrical_summary";

        /// <summary>Level of an instance: its own level, else its Schedule Level, else the level parameter.</summary>
        private string LevelNameOf(FamilyInstance fi)
        {
            var ids = new List<ElementId> { fi.LevelId };
            foreach (var bip in new[] { BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM })
            {
                try { var p = fi.get_Parameter(bip); if (p != null) ids.Add(p.AsElementId()); } catch { }
            }
            foreach (var id in ids)
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                if (Doc.GetElement(id) is Level lvl) return lvl.Name;
            }
            return null;
        }

        /// <summary>
        /// Panels, switchboards and transformers carry a Panel Name; other "electrical equipment"
        /// (e.g. low-voltage connectors, disconnects without a name) are not panels for this summary.
        /// </summary>
        private static bool IsPanel(FamilyInstance fi)
        {
            try
            {
                var name = fi.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString();
                if (!string.IsNullOrWhiteSpace(name)) return true;
                var eq = fi.MEPModel as ElectricalEquipment;
                if (eq?.DistributionSystem != null) return true;
                var assigned = fi.MEPModel?.GetAssignedElectricalSystems();
                return assigned != null && assigned.Count > 0;
            }
            catch { return false; }
        }

        /// <summary>Numeric-first sort key so circuits go 1, 2, ..., 10, 11 rather than 1, 10, 11, 2.</summary>
        private static double CircuitSortKey(string circuitNumber)
        {
            if (string.IsNullOrEmpty(circuitNumber)) return double.MaxValue;
            int i = 0;
            while (i < circuitNumber.Length && char.IsDigit(circuitNumber[i])) i++;
            return i > 0 && double.TryParse(circuitNumber.Substring(0, i), out var n) ? n : double.MaxValue - 1;
        }

        protected override AIResult<ElectricalSummary> Run()
        {
            var req = Input ?? new ElectricalSummaryRequest();
            var s = new ElectricalSummary { DocumentTitle = Doc.Title };
            s.CircuitCountTotal = new FilteredElementCollector(Doc).OfClass(typeof(ElectricalSystem)).GetElementCount();

            foreach (var bic in ElectricalUtils.ElectricalCategories)
            {
                int count;
                try
                {
                    count = new FilteredElementCollector(Doc).OfCategory(bic).WhereElementIsNotElementType().GetElementCount();
                }
                catch { count = 0; }
                if (count > 0) s.ElementCountsByCategory[bic.ToString()] = count;
            }

            // Panels and their circuits
            var panels = new FilteredElementCollector(Doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.MEPModel is ElectricalEquipment && IsPanel(fi))
                .OrderBy(fi => fi.Name)
                .ToList();
            s.PanelCountTotal = panels.Count;

            if (!string.IsNullOrWhiteSpace(req.PanelNameFilter))
                panels = panels.Where(fi => fi.Name.IndexOf(req.PanelNameFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (!string.IsNullOrWhiteSpace(req.LevelName))
            {
                var allLevels = panels.Select(LevelNameOf).Where(n => n != null).Distinct().OrderBy(n => n).ToList();
                panels = panels.Where(fi => (LevelNameOf(fi) ?? "").IndexOf(req.LevelName, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (panels.Count == 0)
                    Warnings.Add($"No panel on a level matching '{req.LevelName}'. Panel levels in this model: {string.Join(", ", allLevels)}.");
            }
            if (!string.IsNullOrWhiteSpace(req.PanelNameFilter) && panels.Count == 0)
                Warnings.Add($"No panel name contains '{req.PanelNameFilter}'.");
            if (panels.Count > Math.Max(1, req.MaxPanels))
            {
                Warnings.Add($"{panels.Count} panels match; showing the first {req.MaxPanels}. Use panelNameFilter or levelName to narrow down.");
                panels = panels.Take(req.MaxPanels).ToList();
            }

            foreach (var fi in panels)
            {
                var eq = (ElectricalEquipment)fi.MEPModel;
                var p = new PanelInfo
                {
                    PanelId = fi.Id.GetValue(),
                    Name = fi.Name,
                    FamilyName = fi.Symbol.FamilyName,
                    TypeName = fi.Symbol.Name,
                    Level = LevelNameOf(fi),
                };
                try { p.DistributionSystem = eq.DistributionSystem?.Name; } catch { }

                ISet<ElectricalSystem> assigned = null;
                try { assigned = fi.MEPModel.GetAssignedElectricalSystems(); } catch { }
                if (assigned != null)
                {
                    p.CircuitCount = assigned.Count;
                    foreach (var sys in assigned.OrderBy(x => CircuitSortKey(x.CircuitNumber)).ThenBy(x => x.CircuitNumber))
                    {
                        var ci = new CircuitInfo();
                        CreateElectricalCircuitEventHandler.Fill(sys, ci, req.IncludeCircuitElementIds);
                        p.TotalApparentLoadVA += ci.ApparentLoadVA;
                        if (req.IncludeCircuits) p.Circuits.Add(ci);
                    }
                    p.TotalApparentLoadVA = Math.Round(p.TotalApparentLoadVA, 1);
                }
                s.Panels.Add(p);
            }

            // Devices with an electrical connector but no circuit
            foreach (var bic in ElectricalUtils.DeviceCategories)
            {
                IEnumerable<FamilyInstance> devices;
                try
                {
                    devices = new FilteredElementCollector(Doc).OfCategory(bic).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>();
                }
                catch { continue; }
                foreach (var fi in devices)
                {
                    var cm = fi.MEPModel?.ConnectorManager;
                    if (cm == null) continue;
                    bool hasElectrical = cm.Connectors.Cast<Connector>().Any(c => c.Domain == Domain.DomainElectrical);
                    if (!hasElectrical) continue;
                    ISet<ElectricalSystem> systems = null;
                    try { systems = fi.MEPModel.GetElectricalSystems(); } catch { }
                    if (systems == null || systems.Count == 0)
                    {
                        s.UnassignedDeviceCount++;
                        if (s.UnassignedDeviceIds.Count < req.MaxUnassignedIds)
                            s.UnassignedDeviceIds.Add(fi.Id.GetValue());
                    }
                }
            }

            s.CableTrayTypes = new FilteredElementCollector(Doc).OfClass(typeof(CableTrayType)).Cast<ElementType>().Select(ElectricalUtils.ToTypeInfo).ToList();
            s.ConduitTypes = new FilteredElementCollector(Doc).OfClass(typeof(ConduitType)).Cast<ElementType>().Select(ElectricalUtils.ToTypeInfo).ToList();
            s.DistributionSystems = new FilteredElementCollector(Doc).OfClass(typeof(DistributionSysType)).Select(e => e.Name).OrderBy(n => n).ToList();

            return Ok(s, $"{s.PanelCountTotal} panel(s) ({s.Panels.Count} listed), {s.CircuitCountTotal} circuit(s), {s.UnassignedDeviceCount} unassigned device(s), {s.ElementCountsByCategory.Values.Sum()} electrical element(s) in '{Doc.Title}'.");
        }
    }
}
