namespace Sandrail.Core.Generation;

/// <summary>
/// The test ground's bump patch: a square of noise heights, standing in for M3's terrain (M1-PLAN, M1c).
/// The first use of the ported <see cref="GradientNoise"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never below zero</b>, because the plain's collider is an infinite plane at zero: a dip would be
/// hidden under it and the wheels would ride the plane instead. So the noise is lifted by what it
/// reaches below zero, using the bench's measurement (2026-10-31 worklog: 4-octave fBm spans about
/// ±0.55, and 3 octaves no wider), and clamped, rather than by the ±1 it's documented to have.
/// </para>
/// <para>
/// <b>Zero at the border</b>, eased in over <see cref="EdgeM"/>, so the patch rises out of the plain
/// rather than standing on it as a step.
/// </para>
/// </remarks>
public sealed class BumpField(ulong seed, float sizeM = 128.0f, float amplitudeM = 1.5f, float frequency = 0.07f, int octaves = 3)
{
    /// <summary>The measured reach of the fBm below zero; see the remarks.</summary>
    public const float NoiseReach = 0.55f;

    /// <summary>How far in from the border the bumps take to reach full height.</summary>
    public const float EdgeM = 14.0f;

    private readonly ulong _seed = Hash64.Mix(seed ^ 0xB0B0B0B0UL);

    public float SizeM { get; } = sizeM;

    public float AmplitudeM { get; } = amplitudeM;

    /// <summary>The height at a point measured from the patch's corner, 0..<see cref="SizeM"/> each way.</summary>
    public float HeightAt(float x, float z)
    {
        float noise = GradientNoise.Fbm2D(_seed, x * frequency, z * frequency, octaves, 2.0f, 0.5f);
        float lifted = Math.Clamp((noise + NoiseReach) / (2.0f * NoiseReach), 0.0f, 1.0f);

        float fromEdge = MathF.Min(MathF.Min(x, SizeM - x), MathF.Min(z, SizeM - z));
        float edge = Math.Clamp(fromEdge / EdgeM, 0.0f, 1.0f);
        edge = edge * edge * (3.0f - (2.0f * edge));

        return AmplitudeM * lifted * edge;
    }
}
