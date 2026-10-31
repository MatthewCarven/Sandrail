using System.Diagnostics;
using Tumbleweed.Core.Generation;

namespace Tumbleweed.Bench;

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

        Console.WriteLine("Arms: --noise [--seed=<n>] [--octaves=<n>]");
        return 1;
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
