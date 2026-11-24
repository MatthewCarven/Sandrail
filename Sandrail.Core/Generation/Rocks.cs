using System.Numerics;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Generation;

/// <summary>The rocks' dials (M3-PLAN, stage 4, the rest of M3d): how many, how far from the road they keep, how far out they go, how big.</summary>
public sealed record RockSettings
{
    /// <summary>Rocks for each kilometre of road, both sides together, before any are turned away. 0 is none.</summary>
    public float PerKm { get; init; } = 120.0f;

    /// <summary>The nearest a rock comes to the road's edge, metres: a car that only runs wide never meets one.</summary>
    public float ClearM { get; init; } = 12.0f;

    /// <summary>The furthest out from the road's edge one is scattered, metres: where a shortcut goes.</summary>
    public float BandM { get; init; } = 70.0f;

    public float SmallestM { get; init; } = 0.8f;

    public float LargestM { get; init; } = 3.0f;

    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!(PerKm >= 0.0f && PerKm <= 2000.0f))
        {
            problems.Add($"rocks a kilometre is {PerKm}, and must be 0 to 2000");
        }

        if (!(ClearM >= 4.0f && BandM > ClearM && BandM <= 200.0f))
        {
            problems.Add($"rocks keep {ClearM} m to {BandM} m from the road, and need at least 4 m clear and a band out to at most 200 m");
        }

        if (!(SmallestM >= 0.2f && LargestM >= SmallestM && LargestM <= 10.0f))
        {
            problems.Add($"rocks are {SmallestM} to {LargestM} m, and must be 0.2 to 10 m, smallest first");
        }

        return problems;
    }
}

/// <summary>One rock: where its foot is, its size across, up and along, and which way it's turned.</summary>
public readonly record struct Rock(Vector3 Position, Vector3 Size, float YawDeg);

/// <summary>
/// Rocks scattered off the road (M3-PLAN, stage 4: "rocks scattered off the road and well off the racing
/// line"), so leaving the road costs something more than grip, and a shortcut across open ground isn't free.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never in the way of driving the track</b>: a rock keeps <see cref="RockSettings.ClearM"/> from every
/// road's edge, so the racing line, which stays on the road, and a car that only runs wide, never meet one.
/// <b>Never on the bed</b>: not on the road's banks or cuttings, nor within a few metres of them, so a car that
/// leaves the road can still drive back up (his ask, 2026-11-23), and not in a jump's gap or under a loop.
/// </para>
/// <para>
/// <b>A pure function of the seed, the track and its bedded ground</b>, from the ground's own stage seed, so
/// they aren't in the track file: a generated track loaded from its file gets the same rocks, as it gets
/// the same bed, and the stunt and route dials never move one.
/// </para>
/// </remarks>
public static class Rocks
{
    /// <summary>How far along the road between chances of a rock, metres.</summary>
    private const float StepM = 4.0f;

    /// <summary>The nearest two rocks come, metres between their middles.</summary>
    private const float ApartM = 3.5f;

    /// <summary>How far from the bed's painted ground a rock keeps, metres.</summary>
    private const float OffBedM = 4.0f;

    /// <summary>How far a rock's foot sits into the ground, as a share of its height.</summary>
    private const float SunkShare = 0.3f;

    public static IReadOnlyList<Rock> Scatter(ulong seed, RockSettings settings, LaidTrack track, Terrain bedded)
    {
        var rocks = new List<Rock>();
        if (settings.PerKm <= 0.0f || track.Route.Count == 0)
        {
            return rocks;
        }

        // Every road's middle, every couple of metres, in a coarse grid, for "how far is the nearest road".
        var road = new Dictionary<(int, int), List<(Vector3 At, float Half)>>();
        const float Cell = 16.0f;
        foreach (PlacedPiece piece in track.Route)
        {
            float half = piece.Def.WidthM / 2.0f;
            foreach (ICentreline c in piece.Road().Pieces)
            {
                for (float s = 0.0f; s <= c.Length; s += 2.0f)
                {
                    Vector3 at = c.FrameAt(MathF.Min(s, c.Length)).Position;
                    var key = ((int)MathF.Floor(at.X / Cell), (int)MathF.Floor(at.Z / Cell));
                    if (!road.TryGetValue(key, out var list))
                    {
                        road[key] = list = new List<(Vector3, float)>();
                    }

                    list.Add((at, half));
                }
            }
        }

        var placed = new Dictionary<(int, int), List<Vector3>>();
        float chance = settings.PerKm * StepM / 1000.0f / 2.0f;
        ulong state = GenStage.Seed(seed, GenStage.Ground);

        foreach (PlacedPiece piece in track.Route)
        {
            float half = piece.Def.WidthM / 2.0f;
            foreach (ICentreline c in piece.Road().Pieces)
            {
                if (c is LoopCentreline)
                {
                    continue;
                }

                for (float s = StepM / 2.0f; s < c.Length; s += StepM)
                {
                    Frame f = c.FrameAt(s);
                    var right = new Vector2(f.Right.X, f.Right.Z);
                    if (right.LengthSquared() < 1e-4f)
                    {
                        continue;
                    }

                    right = Vector2.Normalize(right);
                    foreach (float side in new[] { -1.0f, 1.0f })
                    {
                        // Every draw is made whether or not a rock comes of it, so one turned away moves no other.
                        float roll = Unit();
                        float outM = Unit();
                        float along = Unit();
                        float w = Unit();
                        float h = Unit();
                        float l = Unit();
                        float yaw = Unit();
                        if (roll >= chance)
                        {
                            continue;
                        }

                        // Out from the edge, thinning with distance: most near enough to see.
                        float dist = settings.ClearM + ((settings.BandM - settings.ClearM) * outM * outM);
                        Vector2 at = new Vector2(f.Position.X, f.Position.Z) + (right * side * (half + dist))
                            + (new Vector2(right.Y, -right.X) * ((along - 0.5f) * StepM));
                        float size = settings.SmallestM + ((settings.LargestM - settings.SmallestM) * w * w);
                        var dims = new Vector3(size, size * (0.45f + (0.5f * h)), size * (0.7f + (0.6f * l)));
                        if (!Clear(at, dims) || !OnOpenGround(at, dims))
                        {
                            continue;
                        }

                        float ground = bedded.HeightAt(at.X, at.Y);
                        var rock = new Rock(new Vector3(at.X, ground - (dims.Y * SunkShare), at.Y), dims, yaw * 360.0f);
                        rocks.Add(rock);
                        var key = ((int)MathF.Floor(at.X / Cell), (int)MathF.Floor(at.Y / Cell));
                        if (!placed.TryGetValue(key, out var mine))
                        {
                            placed[key] = mine = new List<Vector3>();
                        }

                        mine.Add(rock.Position);
                    }
                }
            }
        }

        return rocks;

        float Unit()
        {
            state += 0x9E3779B97F4A7C15UL;
            return Hash64.ToUnitFloat(Hash64.Mix(state));
        }

        // Clear of every road's edge by ClearM, its own size allowed for, and of every rock already down.
        bool Clear(Vector2 at, Vector3 dims)
        {
            float reach = MathF.Max(dims.X, dims.Z) / 2.0f;
            int cx = (int)MathF.Floor(at.X / Cell);
            int cz = (int)MathF.Floor(at.Y / Cell);
            int span = (int)MathF.Ceiling((settings.ClearM + 10.0f) / Cell);
            for (int dz = -span; dz <= span; dz++)
            {
                for (int dx = -span; dx <= span; dx++)
                {
                    if (road.TryGetValue((cx + dx, cz + dz), out var list))
                    {
                        foreach ((Vector3 p, float half) in list)
                        {
                            if (Vector2.Distance(at, new Vector2(p.X, p.Z)) < half + settings.ClearM + reach)
                            {
                                return false;
                            }
                        }
                    }

                    if (dx is >= -1 and <= 1 && dz is >= -1 and <= 1 && placed.TryGetValue((cx + dx, cz + dz), out var near))
                    {
                        foreach (Vector3 q in near)
                        {
                            if (Vector2.Distance(at, new Vector2(q.X, q.Z)) < ApartM + reach)
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        // On the terrain the generator made, inside the ground, and nowhere near the bed's banks and cuttings.
        bool OnOpenGround(Vector2 at, Vector3 dims)
        {
            float edge = (Terrain.SizeM / 2.0f) - 4.0f;
            if (MathF.Abs(at.X) > edge || MathF.Abs(at.Y) > edge)
            {
                return false;
            }

            float r = OffBedM + (MathF.Max(dims.X, dims.Z) / 2.0f);
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.PI / 4.0f;
                if (bedded.PaintedByBed(at.X + (r * MathF.Cos(a)), at.Y + (r * MathF.Sin(a))))
                {
                    return false;
                }
            }

            return !bedded.PaintedByBed(at.X, at.Y);
        }
    }
}
