using Sandrail.Core.Driving;
using Sandrail.Core.Generation;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>M3c: stunts swapped into the route and approved by the speed estimate, and checkpoints by distance (M3-PLAN, M3c's checks).</summary>
public class StuntPlannerTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static readonly GenSettings Bare = new() { Stunts = new StuntSettings { Density = 0.0f, CheckpointEveryM = 0.0f } };

    /// <summary>Thirty seeds as the game makes them, and as the route alone.</summary>
    private static readonly Lazy<(Generated Made, LaidTrack Laid, LaidTrack Route)[]> Sweep = new(() =>
        Enumerable.Range(1, 30).Select(s =>
        {
            Generated g = TrackGenerator.Generate((ulong)s, new GenSettings(), Catalogue.Value);
            Generated bare = TrackGenerator.Generate((ulong)s, Bare, Catalogue.Value);
            return (g, g.Track.Lay(Catalogue.Value), bare.Track.Lay(Catalogue.Value));
        }).ToArray());

    [Fact]
    public void EveryStuntedTrackLaysClean()
    {
        Assert.All(Sweep.Value, t => Assert.Empty(t.Laid.Problems));
    }

    /// <summary>
    /// Fact 5: a stunt only takes the place of road with its footprint and way out, so the stunt dial can't
    /// move the route: the same tiles are covered (a jump's gap counted), entered and left the same way.
    /// </summary>
    [Fact]
    public void StuntsAndCheckpointsDontMoveTheRoute()
    {
        foreach ((Generated made, LaidTrack laid, LaidTrack route) in Sweep.Value)
        {
            Assert.Equal(Covered(route), Covered(laid));
            Assert.Equal(route.Route[^1].Out, laid.Route[^1].Out);
            Assert.Equal(route.Route[0].Placement, laid.Route[0].Placement);
        }

        static SortedSet<(int, int)> Covered(LaidTrack t)
        {
            var tiles = new SortedSet<(int, int)>();
            foreach (PlacedPiece p in t.Route)
            {
                foreach (Tile tile in p.Tiles)
                {
                    tiles.Add((tile.Column, tile.Row));
                }

                for (int g = 1; g <= p.Out.Gap; g++)
                {
                    Tile gap = p.Out.Tile.Step(p.Out.Facing, g);
                    tiles.Add((gap.Column, gap.Row));
                }
            }

            return tiles;
        }
    }

    /// <summary>
    /// PLAN suggestion 4: every stunt placed is one the speed estimate approves, checked here afresh. The
    /// stock car at the autodriver's caution arrives at least at the bottom of the stunt's speed hint, and
    /// the plan needn't brake below it through the stunt.
    /// </summary>
    [Fact]
    public void EveryStuntIsOneTheSpeedEstimateApproves()
    {
        int stunts = 0;
        foreach ((_, LaidTrack laid, _) in Sweep.Value)
        {
            RacingLine line = laid.CentreLine(StuntPlanner.Reference);
            float[] plan = line.SpeedProfile(StuntPlanner.Reference, 0.8f);
            float[] can = SpeedEstimate.Achievable(line, plan, StuntPlanner.Reference);
            (_, IReadOnlyList<int> owner) = laid.RouteRoad();
            for (int piece = 0; piece < laid.Route.Count; piece++)
            {
                if (laid.Route[piece].Def.SpeedKmh is not float[] hint)
                {
                    continue;
                }

                int[] samples = Enumerable.Range(0, line.Count).Where(i => owner[line.PieceAt(i)] == piece).ToArray();
                float bottom = hint[0] / 3.6f;
                Assert.True(can[samples[0]] >= bottom, $"{laid.Track.Id}: {laid.Route[piece].Def.Id} arrived at {can[samples[0]] * 3.6f:F0} km/h");
                Assert.True(samples.Min(i => plan[i]) >= bottom, $"{laid.Track.Id}: {laid.Route[piece].Def.Id} braked through");
                stunts++;
            }
        }

        Assert.True(stunts >= 20, $"only {stunts} stunts in thirty tracks");
    }

    /// <summary>All three kinds turn up in the sweep, and the S-bend still does too (not every one is a loop).</summary>
    [Fact]
    public void EveryKindOfStuntTurnsUp()
    {
        var used = Sweep.Value.SelectMany(t => t.Made.Track.Pieces).Select(p => p.Piece).ToHashSet();
        foreach (string piece in new[] { "base:launch-short", "base:launch-long", "base:landing", "base:loop", "base:s-bend", "base:checkpoint" })
        {
            Assert.Contains(piece, used);
        }
    }

    /// <summary>
    /// Checkpoints by distance: from the start, a gate at least every 400 m of road gone by before the next
    /// can come, and none in the last 200 m before the finish.
    /// </summary>
    [Fact]
    public void CheckpointsAreSpacedAlongTheRoute()
    {
        foreach ((Generated made, LaidTrack laid, _) in Sweep.Value)
        {
            float total = laid.Route.Sum(p => p.Road().Pieces.Sum(c => c.Length));
            float along = 0.0f;
            float lastGate = 0.0f;
            int count = 0;
            foreach (PlacedPiece p in laid.Route)
            {
                if (p.Def.Gate == GateKind.Checkpoint)
                {
                    Assert.True(along - lastGate >= 400.0f - 1.0f, $"{laid.Track.Id}: checkpoints {along - lastGate:F0} m apart");
                    Assert.True(total - along >= 200.0f, $"{laid.Track.Id}: a checkpoint {total - along:F0} m from the finish");
                    lastGate = along;
                    count++;
                }

                along += p.Road().Pieces.Sum(c => c.Length);
            }

            Assert.Equal(made.Stats.Stunts.Checkpoints, count);
            Assert.Equal(count, laid.Gates().Count - 2);
        }
    }

    /// <summary>The dials at nothing: no stunts and no checkpoints, the route as M3b and M3d made it.</summary>
    [Fact]
    public void AtNothingTheDialsLeaveTheRouteAlone()
    {
        Generated bare = TrackGenerator.Generate(5UL, Bare, Catalogue.Value);
        Assert.Equal(StuntStats.None, bare.Stats.Stunts);
        Assert.DoesNotContain(bare.Track.Pieces, p => p.Piece is "base:checkpoint" or "base:launch-short" or "base:launch-long" or "base:loop");
    }

    /// <summary>More density, more stunts offered: a full dial places at least as many over the sweep as the default.</summary>
    [Fact]
    public void TheDensityDialAddsStunts()
    {
        int atDefault = Sweep.Value.Sum(t => t.Made.Stats.Stunts.Placed);
        int atFull = Enumerable.Range(1, 30).Sum(s =>
            TrackGenerator.Generate((ulong)s, new GenSettings { Stunts = new StuntSettings { Density = 1.0f } }, Catalogue.Value).Stats.Stunts.Placed);
        Assert.True(atFull >= atDefault, $"{atFull} at full against {atDefault} at the default");
    }

    [Fact]
    public void UnsoundStuntSettingsAreRefused()
    {
        Assert.NotEmpty(new StuntSettings { Density = 1.5f }.Problems());
        Assert.NotEmpty(new StuntSettings { Loop = -1.0f }.Problems());
        Assert.NotEmpty(new StuntSettings { CheckpointEveryM = 50.0f }.Problems());
        Assert.Throws<ArgumentException>(() => TrackGenerator.Generate(1UL, new GenSettings { Stunts = new StuntSettings { Density = -0.1f } }, Catalogue.Value));
    }

    /// <summary>The estimate on the flat: from rest the stock car reaches 100 km/h in about the 5.2 s the straight oracle measured.</summary>
    [Fact]
    public void TheEstimateAcceleratesLikeTheStockCar()
    {
        var road = new ICentreline[] { new LineCentreline(Frame.Level(System.Numerics.Vector3.Zero, 0.0f), 600.0f) };
        var line = new RacingLine(road, closed: false);
        float[] plan = Enumerable.Repeat(100.0f, line.Count).ToArray();
        float[] can = SpeedEstimate.Achievable(line, plan, StuntPlanner.Reference);

        // Time to 100 km/h, step by step.
        double t = 0.0;
        int i = 1;
        for (; i < line.Count && can[i] < 100.0f / 3.6f; i++)
        {
            float ds = line.DistanceAt(i) - line.DistanceAt(i - 1);
            t += ds / Math.Max(0.5, (can[i] + can[i - 1]) / 2.0);
        }

        Assert.InRange(t, 4.8, 5.8);
    }
}
