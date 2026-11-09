using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Production;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Production
{
    /// <summary>
    /// Reads the geometry of linked or imported DWG/DXF files: layers, lines, arcs, polylines and blocks,
    /// in model coordinates (mm). Optionally finds walls as pairs of parallel lines. Read-only.
    /// DWG text is not exposed by the Revit API, so text and attributes cannot be read.
    /// </summary>
    public class ReadCadLinkEventHandler : ElectricalHandlerBase<CadReadRequest, CadReadResult>
    {
        public override string GetName() => "read_cad_link";

        private CadReadRequest _req;
        private readonly Dictionary<string, CadLayerInfo> _layers = new Dictionary<string, CadLayerInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly List<CadCurveInfo> _curves = new List<CadCurveInfo>();
        private readonly List<CadBlockInfo> _blocks = new List<CadBlockInfo>();
        private readonly List<PlanSegment> _segments = new List<PlanSegment>();
        private bool _detail;

        protected override AIResult<CadReadResult> Run()
        {
            _req = Input ?? new CadReadRequest();
            _layers.Clear(); _curves.Clear(); _blocks.Clear(); _segments.Clear();
            _detail = _req.Detail ?? (_req.Layers?.Count > 0);
            var result = new CadReadResult();

            var imports = new FilteredElementCollector(Doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
            if (_req.ImportId > 0) imports = imports.Where(i => i.Id.GetValue() == _req.ImportId).ToList();
            else if (!string.IsNullOrWhiteSpace(_req.Name)) imports = imports.Where(i => DocumentationUtils.Contains(CadName(i), _req.Name)).ToList();
            if (imports.Count == 0)
                return Fail("No linked or imported CAD file found. Link the DWG in Revit first (Insert > Link CAD), then call again.");

            foreach (var imp in imports)
            {
                var info = Describe(imp);
                result.Instances.Add(info);
                var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
                if (imp.ViewSpecific && imp.OwnerViewId != ElementId.InvalidElementId)
                    opt.View = Doc.GetElement(imp.OwnerViewId) as View;
                GeometryElement geo = imp.get_Geometry(opt);
                if (geo == null) { result.Notes.Add($"{info.Name}: no geometry (hidden or unloaded link)."); continue; }
                Walk(geo, Transform.Identity, null, 0);
            }

            result.Layers = _layers.Values.OrderBy(l => l.Layer).ToList();
            foreach (var l in result.Layers) l.LengthM = Math.Round(l.LengthM, 2);

            if (_req.DetectWalls)
            {
                var wallSegs = _segments.Where(s => _req.WallLayers == null || _req.WallLayers.Count == 0
                    || _req.WallLayers.Any(w => DocumentationUtils.Contains(s.Layer, w))).ToList();
                if (wallSegs.Count > 6000)
                {
                    result.Notes.Add($"{wallSegs.Count} lines on the wall layers; only the first 6000 were checked. Pass wallLayers or a window (minX..maxY).");
                    wallSegs = wallSegs.Take(6000).ToList();
                }
                result.Walls = CadWallDetector.Detect(wallSegs, _req.MinWallMm, _req.MaxWallMm)
                    .Select(w => new CadWallInfo { Layer = w.Layer, Start = new[] { w.X1, w.Y1 }, End = new[] { w.X2, w.Y2 }, ThicknessMm = w.ThicknessMm, LengthMm = w.LengthMm })
                    .ToList();
                if (_req.WallLayers == null || _req.WallLayers.Count == 0)
                    result.Notes.Add("Walls were searched on all layers; pass wallLayers (e.g. the architect's wall layer) for a cleaner result.");
            }

            // Everything goes to the file; the answer is limited.
            string file = _req.OutputFile;
            if (string.IsNullOrWhiteSpace(file))
                file = Path.Combine(DocumentationUtils.DefaultFolder("cad"), DocumentationUtils.SafeFileName(result.Instances.FirstOrDefault()?.Name ?? "cad") + ".json");
            var full = new CadReadResult { Instances = result.Instances, Layers = result.Layers, Curves = _curves, Blocks = _blocks, Walls = result.Walls };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, JsonConvert.SerializeObject(full, Formatting.Indented));
                result.File = file;
            }
            catch (Exception ex) { result.Notes.Add($"Could not write {file}: {ex.Message}"); }

            if (_detail)
            {
                int max = Math.Max(0, _req.MaxItems);
                result.Curves = _curves.Take(max).ToList();
                result.Blocks = _blocks.Take(Math.Max(0, max - result.Curves.Count)).ToList();
                result.Truncated = _curves.Count + _blocks.Count > result.Curves.Count + result.Blocks.Count;
                if (result.Truncated) result.Notes.Add($"{_curves.Count} curves and {_blocks.Count} blocks in total; the full list is in {result.File}.");
            }
            else
            {
                result.Notes.Add("Summary only. Call again with layers (and a window) to get the lines, arcs and blocks.");
            }
            if (result.Walls.Count > 500) result.Walls = result.Walls.Take(500).ToList();
            result.Notes.Add("Coordinates are Revit model coordinates in mm (X, Y in plan). DWG text and block attributes are not readable through the Revit API.");

            int total = _layers.Values.Sum(l => l.Lines + l.Arcs + l.Polylines);
            return Ok(result, $"{result.Instances.Count} CAD file(s), {_layers.Count} layer(s), {total} curve(s), {_blocks.Count} block(s)" +
                (_req.DetectWalls ? $", {result.Walls.Count} wall(s) found." : "."));
        }

        private string CadName(ImportInstance imp)
        {
            var type = Doc.GetElement(imp.GetTypeId());
            return type?.Name ?? imp.Name ?? $"CAD {imp.Id.GetValue()}";
        }

        private CadInstanceInfo Describe(ImportInstance imp)
        {
            var info = new CadInstanceInfo { Id = imp.Id.GetValue(), Name = CadName(imp), IsLinked = imp.IsLinked, ViewSpecific = imp.ViewSpecific };
            if (imp.ViewSpecific && Doc.GetElement(imp.OwnerViewId) is View v) info.OwnerView = v.Name;
            if (Doc.GetElement(imp.LevelId) is Level lvl) info.Level = lvl.Name;
            var bb = imp.get_BoundingBox(null);
            if (bb != null)
            {
                info.MinMm = new[] { Mm(bb.Min.X), Mm(bb.Min.Y) };
                info.MaxMm = new[] { Mm(bb.Max.X), Mm(bb.Max.Y) };
            }
            return info;
        }

        private static double Mm(double ft) => Math.Round(ElectricalUtils.FtToMm(ft), 1);

        private string LayerOf(GeometryObject g)
        {
            if (g.GraphicsStyleId == ElementId.InvalidElementId) return "0";
            var gs = Doc.GetElement(g.GraphicsStyleId) as GraphicsStyle;
            return gs?.GraphicsStyleCategory?.Name ?? gs?.Name ?? "0";
        }

        private bool LayerWanted(string layer) =>
            _req.Layers == null || _req.Layers.Count == 0 || _req.Layers.Any(l => DocumentationUtils.Contains(layer, l));

        private bool InWindow(XYZ p)
        {
            double x = ElectricalUtils.FtToMm(p.X), y = ElectricalUtils.FtToMm(p.Y);
            return (!_req.MinX.HasValue || x >= _req.MinX) && (!_req.MaxX.HasValue || x <= _req.MaxX)
                && (!_req.MinY.HasValue || y >= _req.MinY) && (!_req.MaxY.HasValue || y <= _req.MaxY);
        }

        private CadLayerInfo Layer(string name)
        {
            if (!_layers.TryGetValue(name, out var l)) _layers[name] = l = new CadLayerInfo { Layer = name };
            return l;
        }

        private static double[] P(XYZ p) => new[] { Mm(p.X), Mm(p.Y) };

        private void Walk(GeometryElement geo, Transform t, string block, int depth)
        {
            if (geo == null || depth > 12) return;
            foreach (GeometryObject g in geo)
            {
                switch (g)
                {
                    case GeometryInstance gi:
                    {
                        var tt = t.Multiply(gi.Transform);
                        if (depth > 0)
                        {
                            // A nested instance is a DWG block reference.
                            string layer = LayerOf(gi);
                            string name = BlockName(gi);
                            if (InWindow(tt.Origin))
                            {
                                Layer(layer).Blocks++;
                                if (LayerWanted(layer))
                                    _blocks.Add(new CadBlockInfo
                                    {
                                        Layer = layer, Name = name, X = Mm(tt.Origin.X), Y = Mm(tt.Origin.Y),
                                        RotationDeg = Math.Round(Math.Atan2(tt.BasisX.Y, tt.BasisX.X) * 180 / Math.PI, 2),
                                    });
                            }
                            Walk(gi.GetSymbolGeometry(), tt, name, depth + 1);
                        }
                        else Walk(gi.GetSymbolGeometry(), tt, null, depth + 1);
                        break;
                    }
                    case Line line: AddCurve(line.CreateTransformed(t), "line", g, block); break;
                    case Arc arc: AddCurve(arc.CreateTransformed(t), arc.IsBound ? "arc" : "circle", g, block); break;
                    case Curve c: AddCurve(c.CreateTransformed(t), "curve", g, block); break;
                    case PolyLine pl: AddPolyline(pl.GetTransformed(t), g, block); break;
                }
            }
        }

        private string BlockName(GeometryInstance gi)
        {
            try
            {
#if REVIT2023_OR_GREATER
                var sid = gi.GetSymbolGeometryId();
                string n = Doc.GetElement(sid.SymbolId)?.Name;
#else
                string n = null;
#endif
                if (!string.IsNullOrWhiteSpace(n))
                {
                    // Revit names DWG block symbols like "file.dwg.BLOCKNAME"
                    int i = n.LastIndexOf(".dwg.", StringComparison.OrdinalIgnoreCase);
                    return i >= 0 ? n.Substring(i + 5) : n;
                }
            }
            catch { }
            return "(block)";
        }

        private void AddCurve(Curve c, string type, GeometryObject src, string block)
        {
            string layer = LayerOf(src);
            XYZ a = c.IsBound ? c.GetEndPoint(0) : (c as Arc)?.Center ?? XYZ.Zero;
            XYZ b = c.IsBound ? c.GetEndPoint(1) : a;
            if (!InWindow(a) && !InWindow(b)) return;
            var li = Layer(layer);
            if (type == "line") li.Lines++; else li.Arcs++;
            li.LengthM += ElectricalUtils.FtToMm(c.IsBound ? c.Length : 2 * Math.PI * ((Arc)c).Radius) / 1000.0;

            if (type == "line")
                _segments.Add(new PlanSegment(Mm(a.X), Mm(a.Y), Mm(b.X), Mm(b.Y), layer));

            if (!LayerWanted(layer)) return;
            var info = new CadCurveInfo { Layer = layer, Type = type, Block = block };
            if (c is Arc arc)
            {
                info.RadiusMm = Mm(arc.Radius);
                if (arc.IsBound) { info.Points.Add(P(a)); info.Points.Add(P(arc.Evaluate(0.5, true))); info.Points.Add(P(b)); }
                else info.Points.Add(P(arc.Center));
            }
            else if (type == "curve")
            {
                foreach (var p in c.Tessellate().Take(50)) info.Points.Add(P(p));
            }
            else { info.Points.Add(P(a)); info.Points.Add(P(b)); }
            _curves.Add(info);
        }

        private void AddPolyline(PolyLine pl, GeometryObject src, string block)
        {
            string layer = LayerOf(src);
            var pts = pl.GetCoordinates();
            if (pts.Count == 0 || !pts.Any(InWindow)) return;
            var li = Layer(layer);
            li.Polylines++;
            for (int i = 1; i < pts.Count; i++)
            {
                li.LengthM += ElectricalUtils.FtToMm(pts[i - 1].DistanceTo(pts[i])) / 1000.0;
                _segments.Add(new PlanSegment(Mm(pts[i - 1].X), Mm(pts[i - 1].Y), Mm(pts[i].X), Mm(pts[i].Y), layer));
            }
            if (!LayerWanted(layer)) return;
            var info = new CadCurveInfo { Layer = layer, Type = "polyline", Block = block, Closed = pts.Count > 2 && pts[0].IsAlmostEqualTo(pts[pts.Count - 1]) };
            foreach (var p in pts.Take(500)) info.Points.Add(P(p));
            _curves.Add(info);
        }
    }
}
