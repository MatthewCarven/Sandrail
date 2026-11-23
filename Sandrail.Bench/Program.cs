using System.Diagnostics;
using Sandrail.Core.Generation;

namespace Sandrail.Bench;

/// <summary>
/// Headless measurements. <b>Run in Release</b>: Debug reads like a real number and isn't one.
/// </summary>
/// <remarks>
/// One arm per flag, the Overburden way. M3 adds <c>--generate</c>, the sweep over thousands of
/// seeds; until then <c>--noise</c> is the one arm, and the reason it exists is the range it
/// prints: a dial calibrated against the range noise is documented to have, rather than the range
/// it reaches, is a dial that never fires.
/// </remarks>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--noise"))
        {
            return NoiseArm(args);
        }

        if (args.Contains("--terrain"))
        {
            return TerrainArm(args);
        }

        Console.WriteLine("Arms: --noise [--seed=<n>] [--octaves=<n>]");
        Console.WriteLine("      --terrain [--seeds=<n>] [--hills=] [--plains=] [--feature=] [--octaves=] [--gain=] [--region=] [--rarity=]");
        return 1;
    }

    /// <summary>
    /// M3a's measurements, over many seeds: how high the terrain goes on the grid, how steep, how much of
    /// it is a wall to a car on sand, and how often the road could follow the ground from one tile to the
    /// next (M3-PLAN facts 1 to 3). The dials come from the command line, so they can be tried without a
    /// rebuild.
    /// </summary>
    private static int TerrainArm(string[] args)
    {
        int seeds = (int)ParseFlag(args, "--seeds=", 100UL);
        TerrainSettings defaults = new();
        var settings = new TerrainSettings
        {
            HillsM = ParseFloat(args, "--hills=", defaults.HillsM),
            PlainsM = ParseFloat(args, "--plains=", defaults.PlainsM),
            FeatureM = ParseFloat(args, "--feature=", defaults.FeatureM),
            Octaves = (int)ParseFlag(args, "--octaves=", (ulong)defaults.Octaves),
            Gain = ParseFloat(args, "--gain=", defaults.Gain),
            RegionM = ParseFloat(args, "--region=", defaults.RegionM),
            ReliefRarity = ParseFloat(args, "--rarity=", defaults.ReliefRarity),
        };

        // The steepest the stock car climbs from rest on each surface, measured by the climb oracle (worklog, M3a);
        // the road climbs a level every two tiles.
        static float ClimbsDeg(Sandrail.Core.Cars.Surface s) => s.Id switch { "base:dirt" => 25.3f, "base:grass" => 22.5f, "base:sand" => 19.7f, _ => 32.3f };
        const float RoadPerTileM = 1.25f;
        const int Step = 2;

        var heights = new List<float>();
        var slopes = new List<float>();
        var wallShares = new List<float>();
        var followShares = new List<float>();
        var hillyShares = new List<float>();
        double totalMs = 0.0;

        _ = new Terrain(0UL, settings);
        for (int seed = 1; seed <= seeds; seed++)
        {
            var clock = Stopwatch.StartNew();
            var terrain = new Terrain((ulong)seed, settings);
            totalMs += clock.Elapsed.TotalMilliseconds;

            int walls = 0;
            int hilly = 0;
            int count = 0;
            (float Slope, float X, float Z) steepest = (0.0f, 0.0f, 0.0f);
            for (float z = -Terrain.GridHalfM; z <= Terrain.GridHalfM; z += Step)
            {
                for (float x = -Terrain.GridHalfM; x <= Terrain.GridHalfM; x += Step)
                {
                    float slope = terrain.SlopeDegAt(x, z);
                    heights.Add(terrain.HeightAt(x, z));
                    slopes.Add(slope);
                    walls += slope > ClimbsDeg(terrain.SurfaceAt(x, z)) ? 1 : 0;
                    hilly += terrain.AmplitudeAt(x, z) > (settings.PlainsM + settings.HillsM) / 2.0f ? 1 : 0;
                    count++;
                    if (slope > steepest.Slope)
                    {
                        steepest = (slope, x, z);
                    }
                }
            }

            // A few seeds: say where each one's steepest ground is, to look at it in the game.
            if (seeds <= 5)
            {
                Console.WriteLine($"  seed {seed}: steepest {steepest.Slope:F1} deg at x {steepest.X:F0}, z {steepest.Z:F0}, height {terrain.HeightAt(steepest.X, steepest.Z):F1} m");
            }

            int follow = 0;
            int pairs = 0;
            for (int row = 0; row < 32; row++)
            {
                for (int col = 0; col < 32; col++)
                {
                    float x = -Terrain.GridHalfM + (20.0f * col) + 10.0f;
                    float z = -Terrain.GridHalfM + (20.0f * row) + 10.0f;
                    float here = terrain.HeightAt(x, z);
                    if (col < 31)
                    {
                        follow += MathF.Abs(terrain.HeightAt(x + 20.0f, z) - here) <= RoadPerTileM ? 1 : 0;
                        pairs++;
                    }

                    if (row < 31)
                    {
                        follow += MathF.Abs(terrain.HeightAt(x, z + 20.0f) - here) <= RoadPerTileM ? 1 : 0;
                        pairs++;
                    }
                }
            }

            wallShares.Add(walls / (float)count);
            hillyShares.Add(hilly / (float)count);
            followShares.Add(follow / (float)pairs);
        }

        float[] h = heights.Order().ToArray();
        float[] s = slopes.Order().ToArray();
        float[] w = wallShares.Order().ToArray();
        float[] f = followShares.Order().ToArray();
        float[] y = hillyShares.Order().ToArray();
        Console.WriteLine($"terrain  {seeds} seeds  hills {settings.HillsM} m  plains {settings.PlainsM} m  feature {settings.FeatureM} m  "
            + $"octaves {settings.Octaves}  gain {settings.Gain}  region {settings.RegionM} m  rarity {settings.ReliefRarity}");
        Console.WriteLine($"  {totalMs / seeds:F0} ms a terrain ({Terrain.Samples} x {Terrain.Samples})");
        Console.WriteLine($"  height on the grid, m:   min {h[0]:F1}  p1 {Percentile(h, 0.01):F1}  p50 {Percentile(h, 0.5):F1}  p99 {Percentile(h, 0.99):F1}  max {h[^1]:F1}");
        Console.WriteLine($"  slope on the grid, deg:  p50 {Percentile(s, 0.5):F1}  p90 {Percentile(s, 0.9):F1}  p99 {Percentile(s, 0.99):F1}  p99.9 {Percentile(s, 0.999):F1}  max {s[^1]:F1}");
        Console.WriteLine($"  hilly share of the grid: p10 {Percentile(y, 0.1):P0}  p50 {Percentile(y, 0.5):P0}  p90 {Percentile(y, 0.9):P0}");
        Console.WriteLine($"  steeper than its surface climbs (a wall): p50 {Percentile(w, 0.5):P1}  p90 {Percentile(w, 0.9):P1}  max {w[^1]:P1}  seeds with any: {w.Count(v => v > 0) / (float)seeds:P0}");
        Console.WriteLine($"  tile to next tile the road can follow (<= {RoadPerTileM} m): min {f[0]:P0}  p10 {Percentile(f, 0.1):P0}  p50 {Percentile(f, 0.5):P0}");
        return 0;
    }

    private static float ParseFloat(string[] args, string prefix, float fallback)
    {
        string? arg = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
        return arg is null ? fallback : float.Parse(arg[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Times 2D fBm over a 1024 × 1024 grid at a terrain-like frequency and prints the spread of
    /// what it returned.
    /// </summary>
    private static int NoiseArm(string[] args)
    {
        ulong seed = ParseFlag(args, "--seed=", 1UL);
        int octaves = (int)ParseFlag(args, "--octaves=", 4UL);
        const int Side = 1024;
        const float Frequency = 0.01f;

        var samples = new float[Side * Side];

        // One untimed pass so the JIT has finished with the loop before the clock starts.
        Fill(samples, seed, octaves, Side, Frequency);

        var clock = Stopwatch.StartNew();
        Fill(samples, seed, octaves, Side, Frequency);
        clock.Stop();

        Array.Sort(samples);
        double nsPerSample = clock.Elapsed.TotalMilliseconds * 1_000_000.0 / samples.Length;

        Console.WriteLine($"fbm2d  seed {seed}  octaves {octaves}  {Side}x{Side} at frequency {Frequency}");
        Console.WriteLine($"  {clock.Elapsed.TotalMilliseconds:F1} ms, {nsPerSample:F1} ns a sample");
        Console.WriteLine(
            $"  min {samples[0]:F3}  p1 {Percentile(samples, 0.01):F3}  p50 {Percentile(samples, 0.50):F3}  "
            + $"p99 {Percentile(samples, 0.99):F3}  max {samples[^1]:F3}");
        return 0;
    }

    private static void Fill(float[] samples, ulong seed, int octaves, int side, float frequency)
    {
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                samples[(z * side) + x] = GradientNoise.Fbm2D(seed, x * frequency, z * frequency, octaves, 2.0f, 0.5f);
            }
        }
    }

    private static float Percentile(float[] sorted, double fraction) =>
        sorted[(int)Math.Min(sorted.Length - 1, fraction * sorted.Length)];

    private static ulong ParseFlag(string[] args, string prefix, ulong fallback)
    {
        string? arg = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
        return arg is null ? fallback : ulong.Parse(arg[prefix.Length..]);
    }
}
