using System.Numerics;
using Sandrail.Core.Driving;
using Sandrail.Core.Generation;
using Sandrail.Core.Ghosts;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>M4d: the replay cameras beside a track (M4-PLAN, M4d's checks).</summary>
public class ReplayCameraTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    /// <summary>The shakedown on the plain (every piece, the loop included), sprints 1 and 5 and circuits 2 and 8 on their ground.</summary>
    private static readonly Lazy<(string Name, LaidTrack Track, Func<float, float, float> Ground, IReadOnlyList<ReplayCamera> Cameras)[]> Tracks = new(() =>
    {
        var list = new List<(string, LaidTrack, Func<float, float, float>)>();
        string shakedown = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "tracks", "shakedown.json"));
        list.Add(("shakedown", Track.Load(shakedown, Catalogue.Value), (_, _) => 0.0f));
        foreach (ulong seed in new ulong[] { 1, 2, 5, 8 })
        {
            Generated g = TrackGenerator.Generate(seed, new GenSettings(), Catalogue.Value);
            Terrain ground = g.Terrain;
            list.Add(($"seed {seed}", g.Track.Lay(Catalogue.Value), (x, z) => ground.HeightAt(x, z)));
        }

        return list.Select(t => (t.Item1, t.Item2, t.Item3, ReplayCameras.Place(t.Item2, t.Item3))).ToArray();
    });

    [Fact]
    public void EveryCameraStandsAboveTheGround()
    {
        foreach ((string name, _, Func<float, float, float> ground, IReadOnlyList<ReplayCamera> cameras) in Tracks.Value)
        {
            Assert.All(cameras, c => Assert.True(c.Position.Y >= ground(c.Position.X, c.Position.Z) + 2.0f, $"{name}: a camera for {c.Why} at {c.Position} is in the ground"));
        }
    }

    /// <summary>Each sees nearly all its stretch of road over the ground; the hills can't hide a corner from its camera.</summary>
    [Fact]
    public void EveryCameraSeesItsRoad()
    {
        foreach ((string name, LaidTrack track, Func<float, float, float> ground, IReadOnlyList<ReplayCamera> cameras) in Tracks.Value)
        {
            RacingLine line = ReplayCameras.Line(track);
            foreach (ReplayCamera c in cameras)
            {
                float view = ReplayCameras.View(line, c.Position, c.FromM, c.ToM, ground);
                Assert.True(view >= 0.9f, $"{name}: the camera for {c.Why} at {c.AtM:F0} m sees {view:P0} of {c.FromM:F0}-{c.ToM:F0} m");
            }
        }
    }

    /// <summary>The stretches cover the whole route, one after the other, so there's always a camera on the car.</summary>
    [Fact]
    public void TheCamerasCoverTheRoute()
    {
        foreach ((string name, LaidTrack track, _, IReadOnlyList<ReplayCamera> cameras) in Tracks.Value)
        {
            RacingLine line = ReplayCameras.Line(track);
            Assert.True(cameras.Count >= 3, $"{name} has {cameras.Count} cameras");
            for (int k = 0; k + 1 < cameras.Count; k++)
            {
                Assert.Equal(cameras[k].ToM, cameras[k + 1].FromM, 3);
                Assert.True(cameras[k + 1].AtM - cameras[k].AtM <= ReplayCameras.MostGapM + 1.0f, $"{name}: {cameras[k + 1].AtM - cameras[k].AtM:F0} m between cameras");
            }

            if (line.Closed)
            {
                Assert.Equal(cameras[^1].ToM, cameras[0].FromM, 3);
            }
            else
            {
                Assert.Equal(0.0f, cameras[0].FromM);
                Assert.Equal(line.Length, cameras[^1].ToM, 3);
            }

            for (float along = 0.0f; along < line.Length; along += 7.0f)
            {
                ReplayCamera c = cameras[ReplayCameras.Covering(cameras, along)];
                bool inside = c.FromM <= c.ToM ? along >= c.FromM && along < c.ToM : along >= c.FromM || along < c.ToM;
                Assert.True(inside, $"{name}: {along} m is covered by a camera for {c.FromM}-{c.ToM} m");
            }
        }
    }

    /// <summary>Every stunt has a camera of its own beside it.</summary>
    [Fact]
    public void EveryStuntHasItsCamera()
    {
        foreach ((string name, LaidTrack track, _, IReadOnlyList<ReplayCamera> cameras) in Tracks.Value)
        {
            int jumps = track.Route.Count(p => p.Def.Out.Kind == ConnectorKind.Lip);
            int loops = track.Route.Count(p => p.Def.Path.Any(s => s.Loop is not null));
            Assert.Equal(jumps, cameras.Count(c => c.Why == "the jump"));
            Assert.Equal(loops, cameras.Count(c => c.Why == "the loop"));
        }

        Assert.Contains(Tracks.Value[0].Cameras, c => c.Why == "the loop");
    }

    [Fact]
    public void TheCamerasAreTheSameEachTime()
    {
        (_, LaidTrack track, Func<float, float, float> ground, IReadOnlyList<ReplayCamera> cameras) = Tracks.Value[3];
        Assert.Equal(cameras, ReplayCameras.Place(track, ground));
    }

    /// <summary>A ghost's distance along the route rises with its run, and goes back after a car put back.</summary>
    [Fact]
    public void AGhostIsReadAlongTheRoute()
    {
        LaidTrack track = Tracks.Value[0].Track;
        RacingLine line = ReplayCameras.Line(track);
        var snapshots = new List<Snapshot>();
        for (int i = 0; i < line.Count; i += 3)
        {
            snapshots.Add(new Snapshot { Position = line.PositionAt(i) + (Vector3.UnitY * 0.6f), Rotation = Quaternion.Identity });
        }

        snapshots.Add(new Snapshot { Position = line.PositionAt(10), Rotation = Quaternion.Identity, Cut = true });
        var ghost = new Ghost(new GhostHeader { Track = "test" }, new Cars.CarSpec(), snapshots);
        float[] along = ReplayCameras.AlongRoute(line, ghost);
        for (int k = 1; k + 1 < along.Length; k++)
        {
            Assert.True(along[k] >= along[k - 1], $"{along[k]} after {along[k - 1]}");
        }

        Assert.Equal(line.DistanceAt(10), along[^1], 1);
    }
}
