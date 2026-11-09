using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Models.Families;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>Family document helpers: templates, parameters, solids, symbols and electrical connectors.</summary>
    public static class FamilyBuildUtils
    {
        // ------------------------------------------------------------------ templates

        /// <summary>Finds a family template (.rft) by full path or partial name. Candidates are returned for the message.</summary>
        public static string FindTemplate(Application app, string wanted, out List<string> candidates)
        {
            candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(wanted)) wanted = "Generic Model";
            if (File.Exists(wanted)) return wanted;

            var roots = new List<string>();
            try { if (Directory.Exists(app.FamilyTemplatePath)) { roots.Add(app.FamilyTemplatePath); roots.Add(Path.GetDirectoryName(app.FamilyTemplatePath)); } } catch { }
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            roots.Add(Path.Combine(programData, "Autodesk", $"RVT {app.VersionNumber}", "Family Templates"));

            var files = new List<string>();
            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { files.AddRange(Directory.EnumerateFiles(root, "*.rft", SearchOption.AllDirectories)); } catch { }
            }
            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            string key = Normalize(Path.GetFileNameWithoutExtension(wanted));
            var matches = files.Where(f => Normalize(Path.GetFileNameWithoutExtension(f)).Contains(key)).ToList();
            if (matches.Count == 0)
            {
                // Every word must appear (e.g. "lighting ceiling" -> "Metric Lighting Fixture ceiling based").
                var words = wanted.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(Normalize).ToList();
                matches = files.Where(f => words.All(w => Normalize(Path.GetFileNameWithoutExtension(f)).Contains(w))).ToList();
            }
            candidates = matches.Select(Path.GetFileName).Distinct().Take(15).ToList();
            if (matches.Count == 0)
            {
                candidates = files.Select(Path.GetFileName).Distinct().OrderBy(n => n).Take(60).ToList();
                return null;
            }
            return matches
                .OrderByDescending(f => Normalize(Path.GetFileNameWithoutExtension(f)) == key || Normalize(Path.GetFileNameWithoutExtension(f)) == "metric" + key)
                .ThenByDescending(f => Path.GetFileName(f).StartsWith("Metric", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.IndexOf("English", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(f => Path.GetFileName(f).Length)
                .First();
        }

        private static string Normalize(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        // ------------------------------------------------------------------ parameters

        public static ForgeTypeId SpecOf(string type)
        {
            switch (Normalize(type))
            {
                case "length": return SpecTypeId.Length;
                case "number": return SpecTypeId.Number;
                case "integer": case "int": return SpecTypeId.Int.Integer;
                case "yesno": case "bool": case "boolean": return SpecTypeId.Boolean.YesNo;
                case "angle": return SpecTypeId.Angle;
                case "voltage": case "electricalpotential": return SpecTypeId.ElectricalPotential;
                case "current": return SpecTypeId.Current;
                case "apparentpower": case "apparentload": return SpecTypeId.ApparentPower;
                case "power": case "electricalpower": return SpecTypeId.ElectricalPower;
                case "wattage": return SpecTypeId.Wattage;
                case "luminousflux": case "lumens": return SpecTypeId.LuminousFlux;
                case "colortemperature": return SpecTypeId.ColorTemperature;
                case "efficacy": return SpecTypeId.Efficacy;
                case "poles": case "numberofpoles": return SpecTypeId.Int.NumberOfPoles;
                case "material": return SpecTypeId.Reference.Material;
                default: return SpecTypeId.String.Text;
            }
        }

        public static ForgeTypeId GroupOf(string group)
        {
            switch (Normalize(group))
            {
                case "electrical": return GroupTypeId.Electrical;
                case "electricalloads": case "loads": return GroupTypeId.ElectricalLoads;
                case "lighting": case "photometrics": return GroupTypeId.ElectricalLighting;
                case "identity": case "identitydata": return GroupTypeId.IdentityData;
                case "dimensions": case "geometry": return GroupTypeId.Geometry;
                case "data": return GroupTypeId.Data;
                case "text": return GroupTypeId.Text;
                case "constraints": return GroupTypeId.Constraints;
                case "materials": return GroupTypeId.Materials;
                default: return GroupTypeId.General;
            }
        }

        /// <summary>Display unit used when values come in as plain numbers.</summary>
        private static ForgeTypeId InputUnit(Document doc, ForgeTypeId spec)
        {
            if (spec == SpecTypeId.Length) return UnitTypeId.Millimeters;
            if (spec == SpecTypeId.Angle) return UnitTypeId.Degrees;
            if (spec == SpecTypeId.ElectricalPotential) return UnitTypeId.Volts;
            if (spec == SpecTypeId.Current) return UnitTypeId.Amperes;
            if (spec == SpecTypeId.ApparentPower) return UnitTypeId.VoltAmperes;
            if (spec == SpecTypeId.ElectricalPower || spec == SpecTypeId.Wattage) return UnitTypeId.Watts;
            if (spec == SpecTypeId.LuminousFlux) return UnitTypeId.Lumens;
            if (spec == SpecTypeId.ColorTemperature) return UnitTypeId.Kelvin;
            if (spec == SpecTypeId.Efficacy) return UnitTypeId.LumensPerWatt;
            return doc.GetUnits().GetFormatOptions(spec).GetUnitTypeId();
        }

        public static double ToInternal(Document doc, ForgeTypeId spec, double value) =>
            UnitUtils.IsMeasurableSpec(spec) ? UnitUtils.ConvertToInternalUnits(value, InputUnit(doc, spec)) : value;

        /// <summary>Existing family parameter by name, or a new one.</summary>
        public static FamilyParameter GetOrAddParameter(Document famDoc, FamilyParameterInfo info, List<string> warnings)
        {
            var mgr = famDoc.FamilyManager;
            var existing = mgr.get_Parameter(info.Name);
            if (existing != null) return existing;
            try
            {
                return mgr.AddParameter(info.Name, GroupOf(info.Group), SpecOf(info.Type), info.IsInstance);
            }
            catch (Exception ex)
            {
                warnings.Add($"Parameter '{info.Name}' not added: {ex.Message}");
                return null;
            }
        }

        /// <summary>Sets a family parameter on the current type from text (numbers in mm, V, A, VA, W, lm, K, degrees).</summary>
        public static bool SetValue(Document famDoc, FamilyParameter p, string value, out string error)
        {
            error = null;
            var mgr = famDoc.FamilyManager;
            try
            {
                if (!string.IsNullOrEmpty(p.Formula)) { error = $"'{p.Definition.Name}' has a formula ({p.Formula})."; return false; }
                switch (p.StorageType)
                {
                    case StorageType.String:
                        mgr.Set(p, value ?? "");
                        return true;
                    case StorageType.Integer:
                        if (bool.TryParse(value, out bool b)) mgr.Set(p, b ? 1 : 0);
                        else if (DocumentationUtils.SameText(value, "yes")) mgr.Set(p, 1);
                        else if (DocumentationUtils.SameText(value, "no")) mgr.Set(p, 0);
                        else mgr.Set(p, int.Parse(value));
                        return true;
                    case StorageType.Double:
                        double d = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                        mgr.Set(p, ToInternal(famDoc, p.Definition.GetDataType(), d));
                        return true;
                    case StorageType.ElementId:
                        var mat = GetOrCreateMaterial(famDoc, value);
                        if (mat == null) { error = $"'{p.Definition.Name}': material '{value}' not available."; return false; }
                        mgr.Set(p, mat.Id);
                        return true;
                    default:
                        error = $"'{p.Definition.Name}' has an unsupported storage type.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = $"'{p.Definition.Name}' = '{value}': {ex.Message}";
                return false;
            }
        }

        public static FamilyType GetOrCreateType(Document famDoc, string name)
        {
            var mgr = famDoc.FamilyManager;
            foreach (FamilyType t in mgr.Types)
                if (DocumentationUtils.SameText(t.Name, name)) { mgr.CurrentType = t; return t; }
            return mgr.NewType(name);
        }

        public static Material GetOrCreateMaterial(Document famDoc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var mat = DocumentationUtils.PickByName(new FilteredElementCollector(famDoc).OfClass(typeof(Material)).Cast<Material>(), m => m.Name, name);
            if (mat != null) return mat;
            return famDoc.GetElement(Material.Create(famDoc, name.Trim())) as Material;
        }

        public static List<FamilyParameterState> ReadParameters(Document famDoc)
        {
            var mgr = famDoc.FamilyManager;
            var list = new List<FamilyParameterState>();
            foreach (FamilyParameter p in mgr.GetParameters())
            {
                var s = new FamilyParameterState
                {
                    Name = p.Definition.Name,
                    Type = p.Definition.GetDataType()?.TypeId?.Split(':').LastOrDefault()?.Split('-').FirstOrDefault(),
                    IsInstance = p.IsInstance,
                    IsShared = p.IsShared,
                    Formula = string.IsNullOrEmpty(p.Formula) ? null : p.Formula,
                };
                foreach (FamilyType t in mgr.Types)
                {
                    string v = null;
                    try { v = t.AsValueString(p); } catch { }
                    if (v == null)
                    {
                        try { v = p.StorageType == StorageType.String ? t.AsString(p) : null; } catch { }
                    }
                    if (!string.IsNullOrEmpty(v)) s.Values[t.Name] = v;
                }
                list.Add(s);
            }
            return list.OrderBy(p => p.Name).ToList();
        }

        public static bool TrySetPartType(Document famDoc, string partType, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(partType)) return false;
            PartType pt;
            switch (Normalize(partType))
            {
                case "panelboard": pt = PartType.PanelBoard; break;
                case "switchboard": pt = PartType.SwitchBoard; break;
                case "transformer": pt = PartType.Transformer; break;
                case "otherpanel": pt = PartType.OtherPanel; break;
                case "equipmentswitch": pt = PartType.EquipmentSwitch; break;
                case "junctionbox": pt = PartType.JunctionBox; break;
                case "normal": pt = PartType.Normal; break;
                default: warnings.Add($"Unknown partType '{partType}'."); return false;
            }
            var p = famDoc.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            if (p == null || p.IsReadOnly) { warnings.Add("This family category has no Part Type parameter."); return false; }
            p.Set((int)pt);
            return true;
        }

        // ------------------------------------------------------------------ geometry

        public static Extrusion CreateSolid(Document famDoc, FamilySolidInfo s)
        {
            double h = ElectricalUtils.MmToFt(Math.Max(1, s.Height));
            double z = ElectricalUtils.MmToFt(s.Z) - (s.Downward ? h : 0);
            XYZ c = new XYZ(ElectricalUtils.MmToFt(s.X), ElectricalUtils.MmToFt(s.Y), z);
            var profile = new CurveArray();
            if (Normalize(s.Shape).StartsWith("cyl") || Normalize(s.Shape) == "disc" || Normalize(s.Shape) == "round")
            {
                double r = ElectricalUtils.MmToFt(Math.Max(1, s.Diameter)) / 2;
                profile.Append(Arc.Create(c, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                profile.Append(Arc.Create(c, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
            }
            else
            {
                double w = ElectricalUtils.MmToFt(Math.Max(1, s.Width)) / 2, d = ElectricalUtils.MmToFt(Math.Max(1, s.Depth)) / 2;
                var p = new[] { c + new XYZ(-w, -d, 0), c + new XYZ(w, -d, 0), c + new XYZ(w, d, 0), c + new XYZ(-w, d, 0) };
                for (int i = 0; i < 4; i++) profile.Append(Line.CreateBound(p[i], p[(i + 1) % 4]));
            }
            var arr = new CurveArrArray();
            arr.Append(profile);
            var plane = SketchPlane.Create(famDoc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, c));
            var ext = famDoc.FamilyCreate.NewExtrusion(true, arr, plane, h);

            if (!string.IsNullOrWhiteSpace(s.Material))
            {
                var mat = GetOrCreateMaterial(famDoc, s.Material);
                var mp = ext.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (mat != null && mp != null && !mp.IsReadOnly) mp.Set(mat.Id);
            }
            if (!s.VisibleInPlan)
            {
                var vis = new FamilyElementVisibility(FamilyElementVisibilityType.Model)
                {
                    IsShownInTopBottom = false,
                    IsShownInPlanRCPCut = false,
                };
                ext.SetVisibility(vis);
            }
            return ext;
        }

        public static int DrawPlanSymbol(Document famDoc, PlanSymbolInfo symbol, List<string> warnings)
        {
            if (symbol == null) return 0;
            int n = 0;
            double z = ElectricalUtils.MmToFt(symbol.Z);
            var plane = SketchPlane.Create(famDoc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z)));
            XYZ P(double x, double y) => new XYZ(ElectricalUtils.MmToFt(x), ElectricalUtils.MmToFt(y), z);
            foreach (var l in symbol.Lines ?? new List<SymbolLineInfo>())
            {
                try
                {
                    var a = P(l.X1, l.Y1); var b = P(l.X2, l.Y2);
                    if (a.DistanceTo(b) < famDoc.Application.ShortCurveTolerance) continue;
                    famDoc.FamilyCreate.NewSymbolicCurve(Line.CreateBound(a, b), plane);
                    n++;
                }
                catch (Exception ex) { warnings.Add($"Symbol line: {ex.Message}"); }
            }
            foreach (var c in symbol.Circles ?? new List<SymbolCircleInfo>())
            {
                try
                {
                    double r = ElectricalUtils.MmToFt(c.Radius);
                    if (c.StartAngle.HasValue && c.EndAngle.HasValue)
                    {
                        famDoc.FamilyCreate.NewSymbolicCurve(Arc.Create(P(c.X, c.Y), r, c.StartAngle.Value * Math.PI / 180, c.EndAngle.Value * Math.PI / 180, XYZ.BasisX, XYZ.BasisY), plane);
                        n++;
                    }
                    else
                    {
                        famDoc.FamilyCreate.NewSymbolicCurve(Arc.Create(P(c.X, c.Y), r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY), plane);
                        famDoc.FamilyCreate.NewSymbolicCurve(Arc.Create(P(c.X, c.Y), r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY), plane);
                        n += 2;
                    }
                }
                catch (Exception ex) { warnings.Add($"Symbol circle: {ex.Message}"); }
            }
            return n;
        }

        // ------------------------------------------------------------------ connectors

        private static XYZ FaceDirection(string face)
        {
            switch (Normalize(face))
            {
                case "bottom": return -XYZ.BasisZ;
                case "front": return -XYZ.BasisY;
                case "back": return XYZ.BasisY;
                case "left": return -XYZ.BasisX;
                case "right": return XYZ.BasisX;
                default: return XYZ.BasisZ;
            }
        }

        public static ConnectorElement AddElectricalConnector(Document famDoc, Element solid, FamilyConnectorInfo info, List<string> warnings)
        {
            var dir = FaceDirection(info.Face);
            var opt = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true };
            PlanarFace face = null;
            foreach (var g in solid.get_Geometry(opt))
            {
                if (!(g is Solid s) || s.Faces.Size == 0) continue;
                foreach (Face f in s.Faces)
                    if (f is PlanarFace pf && pf.FaceNormal.IsAlmostEqualTo(dir) && pf.Reference != null) { face = pf; break; }
                if (face != null) break;
            }
            if (face == null)
            {
                warnings.Add($"No planar '{info.Face}' face on the first solid for the connector (cylinders have only top/bottom).");
                return null;
            }
            var sys = DocumentationUtils.ParseSystemType(info.SystemType);
            var conn = ConnectorElement.CreateElectricalConnector(famDoc, sys, face.Reference);
            ApplyConnectorValues(famDoc, conn, info, warnings);
            return conn;
        }

        public static void ApplyConnectorValues(Document famDoc, ConnectorElement conn, FamilyConnectorInfo info, List<string> warnings)
        {
            var sys = DocumentationUtils.ParseSystemType(info.SystemType);
            if (sys != ElectricalSystemType.PowerBalanced && sys != ElectricalSystemType.PowerUnBalanced) return;
            var mgr = famDoc.FamilyManager;

            void Drive(BuiltInParameter bip, string famName, string type, string group, string value)
            {
                var cp = conn.get_Parameter(bip);
                if (cp == null) { warnings.Add($"Connector has no {bip} parameter."); return; }
                if (info.LinkToParameters)
                {
                    var fp = GetOrAddParameter(famDoc, new FamilyParameterInfo { Name = famName, Type = type, Group = group }, warnings);
                    if (fp == null) return;
                    try { mgr.AssociateElementParameterToFamilyParameter(cp, fp); }
                    catch (Exception ex) { warnings.Add($"Could not link connector {famName}: {ex.Message}"); return; }
                    if (value == null) return;
                    foreach (FamilyType t in mgr.Types)
                    {
                        mgr.CurrentType = t;
                        if (!SetValue(famDoc, fp, value, out string err)) warnings.Add(err);
                    }
                }
                else if (value != null)
                {
                    try
                    {
                        if (cp.StorageType == StorageType.Integer) cp.Set(int.Parse(value));
                        else cp.Set(ToInternal(famDoc, cp.Definition.GetDataType(), double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    catch (Exception ex) { warnings.Add($"Connector {bip}: {ex.Message}"); }
                }
            }

            string F(double? d) => d?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Drive(BuiltInParameter.RBS_ELEC_VOLTAGE, "Voltage", "voltage", "electrical", F(info.Voltage));
            Drive(BuiltInParameter.RBS_ELEC_NUMBER_OF_POLES, "Number of Poles", "poles", "electrical", info.Poles?.ToString());
            Drive(BuiltInParameter.RBS_ELEC_APPARENT_LOAD, "Apparent Load", "apparentPower", "electricalLoads", F(info.ApparentLoad));
            if (info.PowerFactor.HasValue)
            {
                var pf = conn.get_Parameter(BuiltInParameter.RBS_ELEC_POWER_FACTOR);
                try { if (pf != null && !pf.IsReadOnly) pf.Set(info.PowerFactor.Value); } catch (Exception ex) { warnings.Add($"Power factor: {ex.Message}"); }
            }
        }

        public class OverwriteFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }

        /// <summary>A transaction on a family document that never shows a dialog.</summary>
        public static Transaction StartSilent(Document famDoc, string name, out ElectricalUtils.SilentFailuresPreprocessor pre)
        {
            var t = new Transaction(famDoc, name);
            t.Start();
            pre = ElectricalUtils.MakeSilent(t);
            return t;
        }
    }
}
