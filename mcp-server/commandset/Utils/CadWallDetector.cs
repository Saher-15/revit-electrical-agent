using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>A straight line in plan, in mm. Revit-free so it can be unit tested.</summary>
    public struct PlanSegment
    {
        public double X1, Y1, X2, Y2;
        public string Layer;
        public PlanSegment(double x1, double y1, double x2, double y2, string layer = null) { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; Layer = layer; }
        public double Length => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));
    }

    public class WallCandidateMm
    {
        public double X1, Y1, X2, Y2;
        public double ThicknessMm;
        public double LengthMm;
        public string Layer;
        public int SegmentA, SegmentB;
    }

    /// <summary>
    /// Finds walls in a CAD plan: pairs of parallel lines a wall thickness apart that overlap along their length.
    /// The wall is the centre line over the overlapping part.
    /// </summary>
    public static class CadWallDetector
    {
        public static List<WallCandidateMm> Detect(IList<PlanSegment> segments, double minThicknessMm = 80, double maxThicknessMm = 600,
            double minLengthMm = 300, double angleToleranceDeg = 1.0, double minOverlapRatio = 0.5)
        {
            var result = new List<WallCandidateMm>();
            var idx = Enumerable.Range(0, segments.Count).Where(i => segments[i].Length >= minLengthMm).ToList();
            double cosTol = Math.Cos(angleToleranceDeg * Math.PI / 180.0);

            var pairs = new List<(int a, int b, double dist, double s0, double s1)>();
            for (int ii = 0; ii < idx.Count; ii++)
            {
                var a = segments[idx[ii]];
                double la = a.Length;
                double ux = (a.X2 - a.X1) / la, uy = (a.Y2 - a.Y1) / la;
                for (int jj = ii + 1; jj < idx.Count; jj++)
                {
                    var b = segments[idx[jj]];
                    double lb = b.Length;
                    double vx = (b.X2 - b.X1) / lb, vy = (b.Y2 - b.Y1) / lb;
                    if (Math.Abs(ux * vx + uy * vy) < cosTol) continue;

                    // Distance of b's midpoint from a's line.
                    double mx = (b.X1 + b.X2) / 2 - a.X1, my = (b.Y1 + b.Y2) / 2 - a.Y1;
                    double dist = Math.Abs(mx * -uy + my * ux);
                    if (dist < minThicknessMm || dist > maxThicknessMm) continue;

                    // Overlap measured along a.
                    double t1 = (b.X1 - a.X1) * ux + (b.Y1 - a.Y1) * uy;
                    double t2 = (b.X2 - a.X1) * ux + (b.Y2 - a.Y1) * uy;
                    double s0 = Math.Max(0, Math.Min(t1, t2)), s1 = Math.Min(la, Math.Max(t1, t2));
                    double overlap = s1 - s0;
                    if (overlap < minLengthMm || overlap < minOverlapRatio * Math.Min(la, lb)) continue;
                    pairs.Add((idx[ii], idx[jj], dist, s0, s1));
                }
            }

            // Closest partner first; every line belongs to one wall at most.
            var used = new HashSet<int>();
            foreach (var p in pairs.OrderBy(p => p.dist).ThenByDescending(p => p.s1 - p.s0))
            {
                if (used.Contains(p.a) || used.Contains(p.b)) continue;
                used.Add(p.a); used.Add(p.b);
                var a = segments[p.a];
                double la = a.Length;
                double ux = (a.X2 - a.X1) / la, uy = (a.Y2 - a.Y1) / la;
                var b = segments[p.b];
                double mx = (b.X1 + b.X2) / 2 - a.X1, my = (b.Y1 + b.Y2) / 2 - a.Y1;
                double side = mx * -uy + my * ux; // signed offset of b from a
                double nx = -uy * side / 2, ny = ux * side / 2;
                result.Add(new WallCandidateMm
                {
                    X1 = Math.Round(a.X1 + ux * p.s0 + nx, 1),
                    Y1 = Math.Round(a.Y1 + uy * p.s0 + ny, 1),
                    X2 = Math.Round(a.X1 + ux * p.s1 + nx, 1),
                    Y2 = Math.Round(a.Y1 + uy * p.s1 + ny, 1),
                    ThicknessMm = Math.Round(p.dist, 1),
                    LengthMm = Math.Round(p.s1 - p.s0, 1),
                    Layer = a.Layer == b.Layer ? a.Layer : $"{a.Layer} / {b.Layer}",
                    SegmentA = p.a,
                    SegmentB = p.b,
                });
            }
            return result.OrderByDescending(w => w.LengthMm).ToList();
        }

        /// <summary>Maps a point on a PDF page to the sheet: the page is taken to show exactly the title block.</summary>
        public static (double x, double y) PageToSheet(double xMm, double yMm, double pageWidthMm, double pageHeightMm,
            double tbMinX, double tbMinY, double tbMaxX, double tbMaxY)
        {
            if (pageWidthMm <= 0 || pageHeightMm <= 0) return (xMm, yMm);
            return (tbMinX + xMm / pageWidthMm * (tbMaxX - tbMinX), tbMinY + yMm / pageHeightMm * (tbMaxY - tbMinY));
        }
    }
}
