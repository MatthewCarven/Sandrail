using Sandrail.Core.Cars;

namespace Sandrail.Core.Generation;

/// <summary>
/// The generator's stages, each with a seed of its own (CLAUDE.md non-negotiable 4): turning one stage's
/// dial can't reshape what an earlier stage made (M3-PLAN, "The model").
/// </summary>
public static class GenStage
{
    public const ulong Terrain = 0x7E44A1_0001UL;
    public const ulong Route = 0x7E44A1_0002UL;
    public const ulong Stunts = 0x7E44A1_0003UL;
    public const ulong Ground = 0x7E44A1_0004UL;

    /// <summary>The seed a stage works from, from the track's seed.</summary>
    public static ulong Seed(ulong seed, ulong stage) => Hash64.Mix(seed ^ stage);
}

/// <summary>
/// The terrain's dials (M3-PLAN, stage 1): how high the hills go in the hilly patches and in the plains,
/// how big a hill is, how big a patch is and how much of the map is hilly. Every number is a starting point,
/// set from the bench's <c>--terrain</c> arm.
/// </summary>
/// <remarks>
/// <b>Heights are an amplitude times normalised fBm</b>, and fBm reaches about ±0.55 of its amplitude with
/// nearly everything inside ±0.37 (M3-PLAN fact 1), so hills of <see cref="HillsM"/> stand about a third of
/// that above and below the mean, and rarely more than a half.
/// </remarks>
public sealed record TerrainSettings
{
    /// <summary>The amplitude in the hilly patches, metres.</summary>
    public float HillsM { get; init; } = 50.0f;

    /// <summary>The amplitude on the plains, metres.</summary>
    public float PlainsM { get; init; } = 3.0f;

    /// <summary>The longest wavelength in the hills, metres: about twice a hill's width.</summary>
    public float FeatureM { get; init; } = 200.0f;

    public int Octaves { get; init; } = 3;

    /// <summary>Each octave's share of the one before: lower is smoother.</summary>
    public float Gain { get; init; } = 0.45f;

    /// <summary>The wavelength of the field that says where the hilly patches are, metres: about twice a patch's width.</summary>
    public float RegionM { get; init; } = 500.0f;

    /// <summary>
    /// How much of the map is plain, 0 to 1: 0 ramps the hills in across the whole relief field and 1
    /// leaves only its peaks hilly, as Overburden's <c>ReliefRarity</c> does.
    /// </summary>
    public float ReliefRarity { get; init; } = 0.3f;

    /// <summary>Every problem with the settings, in words; empty means they can be used.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!(HillsM >= 0.0f && HillsM <= 200.0f))
        {
            problems.Add($"hillsM is {HillsM}, and must be from 0 to 200");
        }

        if (!(PlainsM >= 0.0f && PlainsM <= HillsM))
        {
            problems.Add($"plainsM is {PlainsM}, and must be from 0 to hillsM ({HillsM})");
        }

        if (!(FeatureM >= 20.0f && FeatureM <= 5000.0f))
        {
            problems.Add($"featureM is {FeatureM}, and must be from 20 to 5000");
        }

        if (Octaves is < 1 or > 8)
        {
            problems.Add($"octaves is {Octaves}, and must be from 1 to 8");
        }

        if (!(Gain > 0.0f && Gain < 1.0f))
        {
            problems.Add($"gain is {Gain}, and must be above 0 and below 1");
        }

        if (!(RegionM >= FeatureM && RegionM <= 20000.0f))
        {
            problems.Add($"regionM is {RegionM}, and must be at least featureM ({FeatureM})");
        }

        if (!(ReliefRarity >= 0.0f && ReliefRarity <= 1.0f))
        {
            problems.Add($"reliefRarity is {ReliefRarity}, and must be from 0 to 1");
        }

        return problems;
    }
}

/// <summary>
/// The ground under a generated track (M3-PLAN, stage 1, M3a): heights a metre apart over a 1024 m square
/// with the 640 m grid in its middle, from a seed and <see cref="TerrainSettings"/> alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Relief regions, as Overburden's:</b> a single octave of noise at <see cref="TerrainSettings.RegionM"/>
/// says where the map is hilly, and the hills' amplitude runs from <see cref="TerrainSettings.PlainsM"/>
/// there to <see cref="TerrainSettings.HillsM"/>, so a map has plains and a hilly patch or two rather than
/// lumps everywhere. Overburden's threshold and ramp are reused unchanged; its ±0.6 is a single octave's
/// reach, which is what the relief field is here too.
/// </para>
/// <para>
/// <b>The ground flattens out past the grid</b> (M3-PLAN decision 6): from the grid's edge to 32 m short
/// of the terrain's, the heights ease down to 0, the old plain's level, so a car that leaves the grid finds
/// gentle ground, and the last 32 m are flat.
/// </para>
/// <para>
/// <b>The heights are computed once, eagerly</b>, about a million samples, rows in parallel: each sample
/// depends on nothing but its own position, so the result is the same however the rows are shared out.
/// </para>
/// </remarks>
public sealed class Terrain
{
    /// <summary>The terrain's side, metres, centred on the grid's middle.</summary>
    public const float SizeM = 1024.0f;

    /// <summary>Samples a side: one a metre, and one more to close the far edge (a power of two and one, which Jolt's height field wants).</summary>
    public const int Samples = 1025;

    /// <summary>Half the grid's side: inside it the terrain is all there.</summary>
    public const float GridHalfM = 320.0f;

    /// <summary>The flat border at the terrain's edge, metres.</summary>
    public const float FlatEdgeM = 32.0f;

    // Overburden's relief shaping (WorldGenerator.ReliefShare), unchanged.
    private const float ReliefLowestSample = -0.6f;
    private const float ReliefReachableSample = 0.6f;
    private const float ReliefRarestThreshold = 0.5f;

    private readonly float[] _heights;
    private readonly ulong _seed;
    private readonly ulong _shapeSeed;
    private readonly ulong _reliefSeed;
    private readonly float _offsetX;
    private readonly float _offsetZ;

    public Terrain(ulong seed, TerrainSettings settings)
    {
        IReadOnlyList<string> problems = settings.Problems();
        if (problems.Count > 0)
        {
            throw new ArgumentException("Terrain settings refused: " + string.Join("; ", problems), nameof(settings));
        }

        Settings = settings;
        _seed = GenStage.Seed(seed, GenStage.Terrain);
        _shapeSeed = Hash64.Mix(_seed ^ 0x5A4EUL);
        _reliefSeed = Hash64.Mix(_seed ^ 0x5E11EFUL);

        // Off the noise's lattice, so the grid's middle isn't a lattice point where every octave is 0.
        _offsetX = Hash64.ToUnitFloat(Hash64.Mix(_seed ^ 0x0FF5E7A1UL)) * 1000.0f;
        _offsetZ = Hash64.ToUnitFloat(Hash64.Mix(_seed ^ 0x0FF5E7B2UL)) * 1000.0f;

        _heights = new float[Samples * Samples];
        Parallel.For(0, Samples, j =>
        {
            float z = (j * SpacingM) - (SizeM / 2.0f);
            for (int i = 0; i < Samples; i++)
            {
                _heights[(j * Samples) + i] = Field((i * SpacingM) - (SizeM / 2.0f), z);
            }
        });
    }

    /// <summary>Metres between samples.</summary>
    public const float SpacingM = 1.0f;

    public TerrainSettings Settings { get; }

    /// <summary>The heights, row by row from the north-west corner: row j is z = -512 + j, column i is x = -512 + i.</summary>
    public IReadOnlyList<float> Heights => _heights;

    /// <summary>One sample: column i, row j.</summary>
    public float At(int i, int j) => _heights[(Math.Clamp(j, 0, Samples - 1) * Samples) + Math.Clamp(i, 0, Samples - 1)];

    /// <summary>The height at a point on the ground, between the samples: the same surface the height field's triangles make, near enough.</summary>
    public float HeightAt(float x, float z)
    {
        float u = (x + (SizeM / 2.0f)) / SpacingM;
        float v = (z + (SizeM / 2.0f)) / SpacingM;
        int i = (int)MathF.Floor(u);
        int j = (int)MathF.Floor(v);
        float fu = u - i;
        float fv = v - j;
        float a = At(i, j) + ((At(i + 1, j) - At(i, j)) * fu);
        float b = At(i, j + 1) + ((At(i + 1, j + 1) - At(i, j + 1)) * fu);
        return a + ((b - a) * fv);
    }

    /// <summary>How steep the ground is at a point, degrees, across two metres.</summary>
    public float SlopeDegAt(float x, float z)
    {
        float dx = (HeightAt(x + SpacingM, z) - HeightAt(x - SpacingM, z)) / (2.0f * SpacingM);
        float dz = (HeightAt(x, z + SpacingM) - HeightAt(x, z - SpacingM)) / (2.0f * SpacingM);
        return MathF.Atan(MathF.Sqrt((dx * dx) + (dz * dz))) * (180.0f / MathF.PI);
    }

    /// <summary>
    /// What the ground is at a point (M3-PLAN fact 7), by its height: firm dirt on the high ground, grass
    /// in the hollows, and sand everywhere else, steep faces included, since this is a dune buggy's country.
    /// </summary>
    /// <remarks>
    /// <b>Steep ground is sand, not dirt, on purpose.</b> The first version painted anything over 12° as
    /// dirt, where the sand had slid off; but the `climb` oracle measured dirt good for 25° against sand's
    /// 20°, so the hilly patches decision 1 wanted to be walls on sand were climbable dirt (2026-11-23).
    /// A dune's slip face is sand anyway. The game draws steep ground darker, so it still reads as steep.
    /// </remarks>
    public Surface SurfaceAt(float x, float z)
    {
        float h = HeightAt(x, z);
        return h > DirtAboveM ? Surfaces.Dirt : h < GrassBelowM ? Surfaces.Grass : Surfaces.Sand;
    }

    /// <summary>Higher than this, metres above the old plain, the ground is firm dirt.</summary>
    public const float DirtAboveM = 5.0f;

    /// <summary>Lower than this, metres below the old plain, the ground is grass.</summary>
    public const float GrassBelowM = -4.0f;

    /// <summary>The field itself at a point, from the seed: what the samples hold, computed fresh. For the tests.</summary>
    public float Field(float x, float z)
    {
        TerrainSettings s = Settings;
        float amplitude = AmplitudeAt(x, z);
        float shape = GradientNoise.Fbm2D(_shapeSeed, (x / s.FeatureM) + _offsetX, (z / s.FeatureM) + _offsetZ, s.Octaves, 2.0f, s.Gain);
        return amplitude * shape * EdgeFade(x, z);
    }

    /// <summary>The hills' amplitude at a point: <see cref="TerrainSettings.PlainsM"/> on the plains up to <see cref="TerrainSettings.HillsM"/> in the hilly patches.</summary>
    public float AmplitudeAt(float x, float z)
    {
        TerrainSettings s = Settings;
        if (s.HillsM == s.PlainsM)
        {
            return s.HillsM;
        }

        float relief = Math.Clamp(GradientNoise.Sample2D(_reliefSeed, (x / s.RegionM) + _offsetZ, (z / s.RegionM) + _offsetX), -1.0f, 1.0f);
        return s.PlainsM + ((s.HillsM - s.PlainsM) * ReliefShare(relief, s.ReliefRarity));
    }

    /// <summary>
    /// How much of the way from the plains' amplitude to the hills' a point at this relief sample gets:
    /// Overburden's threshold, which rarity slides, and a square ramp above it so the foothills ease in.
    /// </summary>
    public static float ReliefShare(float relief, float rarity)
    {
        float threshold = ReliefLowestSample + ((ReliefRarestThreshold - ReliefLowestSample) * rarity);
        float ramp = Math.Clamp((relief - threshold) / (ReliefReachableSample - threshold), 0.0f, 1.0f);
        return ramp * ramp;
    }

    /// <summary>1 on the grid, easing to 0 by <see cref="FlatEdgeM"/> short of the terrain's edge.</summary>
    public static float EdgeFade(float x, float z)
    {
        float out_ = MathF.Max(MathF.Abs(x), MathF.Abs(z)) - GridHalfM;
        float span = (SizeM / 2.0f) - FlatEdgeM - GridHalfM;
        float t = Math.Clamp(out_ / span, 0.0f, 1.0f);
        return 1.0f - (t * t * (3.0f - (2.0f * t)));
    }
}
