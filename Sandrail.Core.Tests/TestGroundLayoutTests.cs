using System.Numerics;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tests;

public class TestGroundLayoutTests
{
    private static readonly TestGroundLayout Layout = new();

    private static IEnumerable<Vector3> Samples(RoadRun run)
    {
        foreach (ICentreline piece in run.Pieces)
        {
            for (int k = 0; k <= 40; k++)
            {
                yield return piece.FrameAt(piece.Length * k / 40.0f).Position;
            }
        }
    }

    private static float Apart(IEnumerable<Vector3> a, IEnumerable<Vector3> b)
    {
        List<Vector3> bs = b.ToList();
        return a.Min(p => bs.Min(q => new Vector2(p.X - q.X, p.Z - q.Z).Length()));
    }

    [Fact]
    public void EverythingFitsOnThePlain()
    {
        foreach (RoadRun run in Layout.Runs)
        {
            foreach (Vector3 p in Samples(run))
            {
                Assert.True(MathF.Abs(p.X) < 300.0f && MathF.Abs(p.Z) < 300.0f, $"{run.Name} reaches {p}");
            }
        }

        float half = Layout.Bumps.SizeM / 2.0f;
        Assert.True(MathF.Abs(Layout.BumpCentre.X) + half < 300.0f && MathF.Abs(Layout.BumpCentre.Z) + half < 300.0f);
    }

    [Fact]
    public void TheStationsAreWellApart()
    {
        // Each station's roads, by name prefix, kept at least 30 m from every other station's.
        string[][] groups = [["kicker", "table"], ["slope"], ["banked"], ["loop run-up", "loop", "loop run-out"], ["corners"]];
        for (int i = 0; i < groups.Length; i++)
        {
            for (int j = i + 1; j < groups.Length; j++)
            {
                IEnumerable<Vector3> a = Layout.Runs.Where(r => groups[i].Contains(r.Name)).SelectMany(Samples);
                IEnumerable<Vector3> b = Layout.Runs.Where(r => groups[j].Contains(r.Name)).SelectMany(Samples);
                Assert.True(Apart(a, b) > 30.0f, $"{groups[i][0]} and {groups[j][0]} are {Apart(a, b):F1} m apart");
            }
        }
    }

    [Fact]
    public void TheLoopsExitRoadClearsItsRunUp()
    {
        // Parallel roads 8 m wide, so centres more than 8 m apart leave a gap between their edges.
        RoadRun runUp = Layout.Runs.Single(r => r.Name == "loop run-up");
        RoadRun runOut = Layout.Runs.Single(r => r.Name == "loop run-out");

        Assert.True(Apart(Samples(runUp), Samples(runOut)) > 10.0f, $"only {Apart(Samples(runUp), Samples(runOut)):F1} m");
    }

    [Fact]
    public void TheTableAndTheSlopeComeBackDownToTheGround()
    {
        foreach (string name in new[] { "table", "slope", "banked" })
        {
            RoadRun run = Layout.Runs.Single(r => r.Name == name);
            ICentreline last = run.Pieces[^1];
            Assert.Equal(TestGroundLayout.Lift, last.FrameAt(last.Length).Position.Y, 2);
        }

        Assert.InRange(Layout.Lip.Position.Y, 2.5f, 3.5f);
        Assert.InRange(Layout.PlateauHeight, 5.0f, 7.0f);
    }

    [Fact]
    public void TheCornerLapClosesExactly()
    {
        RoadRun lap = Layout.Runs.Single(r => r.Name == "corners");
        ICentreline last = lap.Pieces[^1];
        Frame end = last.FrameAt(last.Length);

        CentrelineTests.Near(Layout.CornerLapStart.Position, end.Position, 0.001f, "lap end");
        CentrelineTests.Near(Layout.CornerLapStart.Forward, end.Forward, 1e-4f, "lap heading");
        Assert.Equal(360.0f, TestGroundLayout.Corners.Sum(c => c.AngleDeg), 3);
    }

    [Fact]
    public void TheCornerLapsSolvedStraightsAreRealStraights()
    {
        // The two lengths solved to close the lap: a negative one would mean the corners can't make a lap
        // in this order, and a very short one a corner straight into the next.
        RoadRun lap = Layout.Runs.Single(r => r.Name == "corners");

        Assert.InRange(lap.Pieces[0].Length, 15.0f, 200.0f);
        Assert.InRange(lap.Pieces[4].Length, 15.0f, 200.0f);
    }

    [Fact]
    public void EveryCornerOnTheLapIsDifferent()
    {
        Assert.Equal(TestGroundLayout.Corners.Length, TestGroundLayout.Corners.Distinct().Count());
        Assert.Contains(TestGroundLayout.Corners, c => c.AngleDeg < 0.0f);
        Assert.Contains(TestGroundLayout.Corners, c => c.BankDeg == 0.0f);
        Assert.Contains(TestGroundLayout.Corners, c => c.BankDeg >= 30.0f);
    }

    [Fact]
    public void TheStationsAreNumberedOneToSix()
    {
        Assert.Equal([1, 2, 3, 4, 5, 6], Layout.Stations.Select(s => s.Number));
        Assert.All(Layout.Stations, s => Assert.True(MathF.Abs(s.Start.Up.Y - 1.0f) < 1e-4f, $"{s.Name} doesn't start level"));
    }
}
