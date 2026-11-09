using RevitMCPCommandSet.Utils;
using Xunit;

public class CadWallDetectorTests
{
    [Fact]
    public void TwoParallelLinesMakeOneWallOnTheCentreLine()
    {
        var segs = new List<PlanSegment> { new(0, 0, 5000, 0, "A-WALL"), new(0, 200, 5000, 200, "A-WALL") };
        var w = Assert.Single(CadWallDetector.Detect(segs));
        Assert.Equal(200, w.ThicknessMm);
        Assert.Equal(5000, w.LengthMm);
        Assert.Equal(100, w.Y1);
        Assert.Equal(100, w.Y2);
        Assert.Equal("A-WALL", w.Layer);
    }

    [Fact]
    public void OppositeDirectionAndPartialOverlapUseTheOverlap()
    {
        // Second line runs backwards and is shifted by 1 m.
        var segs = new List<PlanSegment> { new(0, 0, 4000, 0), new(5000, -150, 1000, -150) };
        var w = Assert.Single(CadWallDetector.Detect(segs));
        Assert.Equal(150, w.ThicknessMm);
        Assert.Equal(3000, w.LengthMm);
        Assert.Equal(1000, Math.Min(w.X1, w.X2));
        Assert.Equal(4000, Math.Max(w.X1, w.X2));
        Assert.Equal(-75, w.Y1);
    }

    [Fact]
    public void LinesTooFarApartOrNotParallelAreNotWalls()
    {
        var segs = new List<PlanSegment>
        {
            new(0, 0, 3000, 0), new(0, 900, 3000, 900),          // 900 mm: a corridor, not a wall
            new(0, 2000, 3000, 2000), new(0, 2100, 3000, 2400),   // 6 degrees apart
        };
        Assert.Empty(CadWallDetector.Detect(segs));
    }

    [Fact]
    public void ShortLinesAndSmallOverlapsAreIgnored()
    {
        var segs = new List<PlanSegment>
        {
            new(0, 0, 200, 0), new(0, 100, 200, 100),            // shorter than 300 mm
            new(0, 1000, 4000, 1000), new(3800, 1100, 8000, 1100) // overlap only 200 mm
        };
        Assert.Empty(CadWallDetector.Detect(segs));
    }

    [Fact]
    public void EachLineBelongsToTheClosestPartnerOnly()
    {
        // Three parallel lines: 0, 100 and 300. The 0-100 pair wins; 300 is left alone.
        var segs = new List<PlanSegment> { new(0, 0, 3000, 0), new(0, 100, 3000, 100), new(0, 300, 3000, 300) };
        var w = Assert.Single(CadWallDetector.Detect(segs));
        Assert.Equal(100, w.ThicknessMm);
    }

    [Fact]
    public void RotatedWallsAreFound()
    {
        // A 45-degree wall, 200 mm thick.
        double d = 200 / Math.Sqrt(2);
        var segs = new List<PlanSegment> { new(0, 0, 3000, 3000), new(-d, d, 3000 - d, 3000 + d) };
        var w = Assert.Single(CadWallDetector.Detect(segs));
        Assert.Equal(200, w.ThicknessMm, 1);
    }

    [Fact]
    public void PdfPageMapsOntoTheTitleBlock()
    {
        // A3 page (420 x 297) onto a title block from (-1, -0.5) to (1, 0.5) feet.
        var (x, y) = CadWallDetector.PageToSheet(210, 148.5, 420, 297, -1, -0.5, 1, 0.5);
        Assert.Equal(0, x, 6);
        Assert.Equal(0, y, 6);
        var (x2, y2) = CadWallDetector.PageToSheet(420, 0, 420, 297, -1, -0.5, 1, 0.5);
        Assert.Equal(1, x2, 6);
        Assert.Equal(-0.5, y2, 6);
        Assert.Equal((5.0, 6.0), CadWallDetector.PageToSheet(5, 6, 0, 0, 0, 0, 1, 1));
    }
}
