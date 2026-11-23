using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sandrail.Core.Generation;

namespace Sandrail.Core.Cars;

/// <summary>
/// Every number that makes a car drive the way it does, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>In Core, though the physics that reads it is in Game</b> (M1-PLAN fact 6): what ghosts and
/// multiplayer need to agree on is the car's numbers and its pose, never its solver. The tuning
/// panel (M1b) builds one slider per property here by reflection, so a new number gets its slider
/// by being added.
/// </para>
/// <para>
/// <b>The defaults are M1-PLAN's starting numbers</b>, less the damping, which M1a's drop oracle
/// moved from 1,000/1,500 to 1,400/2,100 (worklog 2026-10-31). A field missing from a file takes its
/// default, which is how a field added later reaches an older file. <b>A field the file has and
/// this record doesn't is refused</b>: a misspelt tuning value that silently never applied is the
/// worst kind of playtest finding, because it reads as "that change did nothing".
/// </para>
/// <para>
/// Units are in the names. Angles are degrees; the model converts.
/// </para>
/// </remarks>
public sealed record CarSpec
{
    public string Id { get; init; } = "base:buggy";

    // The body.

    [Tune("Body", 300.0f, 1500.0f)]
    public float MassKg { get; init; } = 650.0f;

    [Tune("Body", 2.0f, 5.0f)]
    public float ChassisLengthM { get; init; } = 3.2f;

    [Tune("Body", 1.0f, 2.5f)]
    public float ChassisWidthM { get; init; } = 1.5f;

    [Tune("Body", 0.3f, 1.2f)]
    public float ChassisHeightM { get; init; } = 0.6f;

    /// <summary>How far below the chassis's middle the centre of mass sits: the first anti-roll fix.</summary>
    [Tune("Body", 0.0f, 0.6f)]
    public float CentreOfMassDropM { get; init; } = 0.3f;

    /// <summary>Godot's angular damping, replacing the project default rather than adding to it.</summary>
    [Tune("Body", 0.0f, 3.0f)]
    public float AngularDamping { get; init; } = 0.1f;

    // Wheels and suspension.

    [Tune("Wheels", 1.8f, 3.6f)]
    public float WheelbaseM { get; init; } = 2.6f;

    [Tune("Wheels", 1.2f, 2.4f)]
    public float TrackM { get; init; } = 1.7f;

    [Tune("Wheels", 0.25f, 0.6f)]
    public float WheelRadiusM { get; init; } = 0.38f;

    /// <summary>Height of the suspension mounts above the chassis's middle; the rays start here.</summary>
    [Tune("Wheels", -0.4f, 0.4f)]
    public float MountHeightM { get; init; } = 0.0f;

    [Tune("Suspension", 0.1f, 0.6f)]
    public float SuspensionTravelM { get; init; } = 0.35f;

    [Tune("Suspension", 4000.0f, 60000.0f)]
    public float SpringNPerM { get; init; } = 16_000.0f;

    [Tune("Suspension", 0.0f, 6000.0f)]
    public float BumpDampingNsPerM { get; init; } = 1_400.0f;

    [Tune("Suspension", 0.0f, 8000.0f)]
    public float ReboundDampingNsPerM { get; init; } = 2_100.0f;

    /// <summary>The last fraction of travel, where the bump stop starts pushing.</summary>
    [Tune("Suspension", 0.0f, 0.4f)]
    public float BumpStopFraction { get; init; } = 0.1f;

    /// <summary>The bump stop's stiffness as a multiple of the spring's.</summary>
    [Tune("Suspension", 0.0f, 40.0f)]
    public float BumpStopStiffness { get; init; } = 10.0f;

    [Tune("Suspension", 0.0f, 20000.0f)]
    public float AntiRollFrontNPerM { get; init; } = 4_000.0f;

    [Tune("Suspension", 0.0f, 20000.0f)]
    public float AntiRollRearNPerM { get; init; } = 2_000.0f;

    // Tyres.

    /// <summary>Friction coefficient on road; a surface multiplies it.</summary>
    [Tune("Tyres", 0.3f, 2.0f)]
    public float Grip { get; init; } = 1.1f;

    [Tune("Tyres", 2.0f, 20.0f)]
    public float PeakSlipDeg { get; init; } = 8.0f;

    /// <summary>The share of peak grip left once the slip angle is twice the peak: the slide.</summary>
    [Tune("Tyres", 0.3f, 1.0f)]
    public float SlideGripFraction { get; init; } = 0.8f;

    /// <summary>The slip angle is taken against at least this forward speed, so it can't blow up near a standstill.</summary>
    [Tune("Tyres", 0.5f, 10.0f)]
    public float SlipSpeedFloorMps { get; init; } = 3.0f;

    /// <summary>Below this speed sideways motion is cancelled directly, or a parked car creeps down a slope.</summary>
    [Tune("Tyres", 0.0f, 5.0f)]
    public float LowSpeedCancelMps { get; init; } = 2.0f;

    /// <summary>How far grip forces are raised from the contact toward the centre of mass, 0..1: a roll lever.</summary>
    [Tune("Tyres", 0.0f, 1.0f)]
    public float GripForceHeight { get; init; } = 0.3f;

    /// <summary>
    /// When a tyre is asked for more than it has, 0 shares the shortfall between pushing and turning in
    /// proportion and 1 lets pushing take its share first.
    /// </summary>
    /// <remarks>
    /// 1 is M1-PLAN's "forward force first", which gives power oversteer. It is a dial because a key
    /// is all or nothing: at 1, a held throttle below about 24 m/s spends all of a rear tyre's grip on
    /// pushing and the car spins whenever it turns. See the 2026-10-31 worklog.
    /// </remarks>
    [Tune("Tyres", 0.0f, 1.0f)]
    public float ForwardPriority { get; init; } = 0.3f;

    // Engine and brakes.

    /// <summary>The most the engine pushes, off the line.</summary>
    [Tune("Engine and brakes", 1000.0f, 12000.0f)]
    public float DriveForceN { get; init; } = 4_500.0f;

    /// <summary>Above the speed where power ÷ speed falls below the force limit, power is what limits.</summary>
    [Tune("Engine and brakes", 20000.0f, 400000.0f)]
    public float PowerW { get; init; } = 110_000.0f;

    /// <summary>Share of the drive on the rear wheels: 1 is rear-wheel drive.</summary>
    [Tune("Engine and brakes", 0.0f, 1.0f)]
    public float RearDriveShare { get; init; } = 1.0f;

    [Tune("Engine and brakes", 0.0f, 1.0f)]
    public float ReverseForceFraction { get; init; } = 0.4f;

    [Tune("Engine and brakes", 0.0f, 30.0f)]
    public float ReverseTopSpeedMps { get; init; } = 10.0f;

    [Tune("Engine and brakes", 2000.0f, 20000.0f)]
    public float BrakeForceN { get; init; } = 9_000.0f;

    [Tune("Engine and brakes", 0.0f, 1.0f)]
    public float BrakeFrontShare { get; init; } = 0.6f;

    /// <summary>
    /// How far the handbrake locks the rear wheels, 0..1: 0 is off, and 1 locks them, so they slide
    /// against their own motion at <see cref="SlideGripFraction"/> of the grip and the tail comes round.
    /// Between, each rear tyre is that far from rolling to locked (M1d).
    /// </summary>
    [Tune("Engine and brakes", 0.0f, 1.0f)]
    public float HandbrakeLock { get; init; } = 1.0f;

    /// <summary>Drag is this times speed squared. With 110 kW it sets top speed near 50 m/s.</summary>
    [Tune("Engine and brakes", 0.2f, 3.0f)]
    public float DragNPerMps2 { get; init; } = 0.86f;

    // Steering and pedals.

    [Tune("Steering and pedals", 10.0f, 45.0f)]
    public float MaxSteerDeg { get; init; } = 30.0f;

    /// <summary>The steering lock left at <see cref="SteerFadeSpeedMps"/> and above.</summary>
    [Tune("Steering and pedals", 1.0f, 30.0f)]
    public float MinSteerDeg { get; init; } = 8.0f;

    [Tune("Steering and pedals", 10.0f, 100.0f)]
    public float SteerFadeSpeedMps { get; init; } = 50.0f;

    /// <summary>How fast the wheels turn toward the input. A keyboard tap is otherwise a jerk.</summary>
    [Tune("Steering and pedals", 30.0f, 600.0f)]
    public float SteerRateDegPerS { get; init; } = 120.0f;

    /// <summary>How fast they come back to centre, which wants to be quicker than turning in.</summary>
    [Tune("Steering and pedals", 30.0f, 900.0f)]
    public float SteerReturnRateDegPerS { get; init; } = 240.0f;

    /// <summary>
    /// What holding the wide-lock key (Shift) multiplies the steering lock by, at every speed. The turn
    /// rates scale with it, so full lock arrives in the same time either way. His ask, 2026-11-22.
    /// </summary>
    [Tune("Steering and pedals", 1.0f, 3.0f)]
    public float WideLockFactor { get; init; } = 2.0f;

    /// <summary>
    /// How much the inside front wheel turns more than the outside one, 0..1: 1 is true Ackermann
    /// geometry, both wheels rolling round one centre; 0 is both at the same angle.
    /// </summary>
    /// <remarks>
    /// Added with wide lock: at 60° the two wheels want 76° and 48°, and at the same angle they scrub
    /// each other to a near standstill (worklog 2026-11-22).
    /// </remarks>
    [Tune("Steering and pedals", 0.0f, 1.0f)]
    public float AckermannShare { get; init; } = 1.0f;

    /// <summary>How fast throttle and brake go from nothing to full: keyboard easing.</summary>
    [Tune("Steering and pedals", 1.0f, 20.0f)]
    public float PedalRatePerS { get; init; } = 5.0f;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions CompactOptions = new(Options) { WriteIndented = false };

    /// <summary>
    /// Every number in the spec, in the order it's declared, with its panel group and slider range.
    /// </summary>
    /// <remarks>
    /// Built by reflection, so a new number reaches the tuning panel and the recorder by being added.
    /// One without a <see cref="TuneAttribute"/> still gets a slider (group "Other", 0 to three times its
    /// default); <c>EveryNumberHasATuneRange</c> asks for a proper one.
    /// </remarks>
    public static IReadOnlyList<Tunable> Tunables { get; } = BuildTunables();

    /// <summary>A copy with one number changed, by property name. Not validated: ask <see cref="Problems"/>.</summary>
    /// <exception cref="ArgumentException">No number has that name.</exception>
    public CarSpec WithValue(string name, float value)
    {
        Tunable tunable = Tunables.FirstOrDefault(t => t.Name == name)
            ?? throw new ArgumentException($"A car spec has no number called {name}.", nameof(name));

        // The copy constructor, then the one init-only setter. Reflection may call an init accessor, and
        // the copy is private to this method until it's returned, so nothing ever sees it half-made.
        CarSpec copy = this with { };
        tunable.Property.SetValue(copy, value);
        return copy;
    }

    /// <summary>Reads a spec, refusing one with an unknown field or any number out of range.</summary>
    /// <exception cref="InvalidDataException">The file is not JSON, or names a field that doesn't exist, or fails <see cref="Problems"/>.</exception>
    public static CarSpec Parse(string json)
    {
        CarSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<CarSpec>(json, Options);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Not a car spec: {e.Message}", e);
        }

        if (spec is null)
        {
            throw new InvalidDataException("Not a car spec: the file is JSON null.");
        }

        IReadOnlyList<string> problems = spec.Problems();
        if (problems.Count > 0)
        {
            throw new InvalidDataException($"Car spec {spec.Id} refused: " + string.Join("; ", problems));
        }

        return spec;
    }

    /// <summary>Writes the spec, refusing to write one that <see cref="Parse"/> would refuse to read.</summary>
    public string ToJson()
    {
        IReadOnlyList<string> problems = Problems();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"Car spec {Id} not written: " + string.Join("; ", problems));
        }

        return JsonSerializer.Serialize(this, Options);
    }

    /// <summary>
    /// Eight hex digits naming these exact numbers, so an oracle figure or a recorded run says which
    /// spec it came from.
    /// </summary>
    public string Fingerprint()
    {
        ulong hash = 0;
        foreach (byte b in Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, CompactOptions)))
        {
            hash = Hash64.Mix(hash ^ b);
        }

        return (hash >> 32).ToString("x8");
    }

    private static List<Tunable> BuildTunables()
    {
        var defaults = new CarSpec();
        return typeof(CarSpec).GetProperties()
            .Where(p => p.PropertyType == typeof(float))
            .OrderBy(p => p.MetadataToken)
            .Select(p => p.GetCustomAttribute<TuneAttribute>() is TuneAttribute tune
                ? new Tunable(p.Name, tune.Group, tune.Min, tune.Max, p)
                : new Tunable(p.Name, "Other", 0.0f, MathF.Max(1.0f, 3.0f * (float)p.GetValue(defaults)!), p))
            .ToList();
    }

    /// <summary>Every number out of range, in words. Empty means the spec is drivable.</summary>
    /// <remarks>
    /// Each test is written as a refusal of what <em>is</em> allowed, so NaN fails it: every
    /// comparison with NaN is false. Overburden's M3c lesson.
    /// </remarks>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        void Within(string name, float value, float min, float max)
        {
            if (!(value >= min && value <= max))
            {
                problems.Add($"{name} is {value}, and must be from {min} to {max}");
            }
        }

        void Positive(string name, float value, float max)
        {
            if (!(value > 0.0f && value <= max))
            {
                problems.Add($"{name} is {value}, and must be above 0 and at most {max}");
            }
        }

        int colon = Id.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == Id.Length - 1)
        {
            problems.Add($"id is \"{Id}\", and must be namespaced, like base:buggy");
        }

        Positive(nameof(MassKg), MassKg, 100_000.0f);
        Positive(nameof(ChassisLengthM), ChassisLengthM, 20.0f);
        Positive(nameof(ChassisWidthM), ChassisWidthM, 10.0f);
        Positive(nameof(ChassisHeightM), ChassisHeightM, 10.0f);
        Within(nameof(CentreOfMassDropM), CentreOfMassDropM, 0.0f, 5.0f);
        Within(nameof(AngularDamping), AngularDamping, 0.0f, 100.0f);

        Positive(nameof(WheelbaseM), WheelbaseM, 20.0f);
        Positive(nameof(TrackM), TrackM, 10.0f);
        Positive(nameof(WheelRadiusM), WheelRadiusM, 5.0f);
        Within(nameof(MountHeightM), MountHeightM, -5.0f, 5.0f);
        Positive(nameof(SuspensionTravelM), SuspensionTravelM, 5.0f);
        Positive(nameof(SpringNPerM), SpringNPerM, 10_000_000.0f);
        Within(nameof(BumpDampingNsPerM), BumpDampingNsPerM, 0.0f, 1_000_000.0f);
        Within(nameof(ReboundDampingNsPerM), ReboundDampingNsPerM, 0.0f, 1_000_000.0f);
        Within(nameof(BumpStopFraction), BumpStopFraction, 0.0f, 1.0f);
        Within(nameof(BumpStopStiffness), BumpStopStiffness, 0.0f, 1_000.0f);
        Within(nameof(AntiRollFrontNPerM), AntiRollFrontNPerM, 0.0f, 10_000_000.0f);
        Within(nameof(AntiRollRearNPerM), AntiRollRearNPerM, 0.0f, 10_000_000.0f);

        Positive(nameof(Grip), Grip, 10.0f);
        Positive(nameof(PeakSlipDeg), PeakSlipDeg, 45.0f);
        Within(nameof(SlideGripFraction), SlideGripFraction, 0.0f, 1.0f);
        Positive(nameof(SlipSpeedFloorMps), SlipSpeedFloorMps, 100.0f);
        Within(nameof(LowSpeedCancelMps), LowSpeedCancelMps, 0.0f, 100.0f);
        Within(nameof(GripForceHeight), GripForceHeight, 0.0f, 1.0f);
        Within(nameof(ForwardPriority), ForwardPriority, 0.0f, 1.0f);

        Positive(nameof(DriveForceN), DriveForceN, 1_000_000.0f);
        Positive(nameof(PowerW), PowerW, 100_000_000.0f);
        Within(nameof(RearDriveShare), RearDriveShare, 0.0f, 1.0f);
        Within(nameof(ReverseForceFraction), ReverseForceFraction, 0.0f, 1.0f);
        Within(nameof(ReverseTopSpeedMps), ReverseTopSpeedMps, 0.0f, 100.0f);
        Within(nameof(BrakeForceN), BrakeForceN, 0.0f, 1_000_000.0f);
        Within(nameof(BrakeFrontShare), BrakeFrontShare, 0.0f, 1.0f);
        Within(nameof(HandbrakeLock), HandbrakeLock, 0.0f, 1.0f);
        Within(nameof(DragNPerMps2), DragNPerMps2, 0.0f, 1_000.0f);

        Positive(nameof(MaxSteerDeg), MaxSteerDeg, 60.0f);
        Within(nameof(MinSteerDeg), MinSteerDeg, 0.0f, 60.0f);
        Positive(nameof(SteerFadeSpeedMps), SteerFadeSpeedMps, 500.0f);
        Positive(nameof(SteerRateDegPerS), SteerRateDegPerS, 100_000.0f);
        Positive(nameof(SteerReturnRateDegPerS), SteerReturnRateDegPerS, 100_000.0f);
        Within(nameof(WideLockFactor), WideLockFactor, 1.0f, 4.0f);
        Within(nameof(AckermannShare), AckermannShare, 0.0f, 1.0f);
        Positive(nameof(PedalRatePerS), PedalRatePerS, 1_000.0f);

        if (MinSteerDeg > MaxSteerDeg)
        {
            problems.Add($"{nameof(MinSteerDeg)} ({MinSteerDeg}) is more than {nameof(MaxSteerDeg)} ({MaxSteerDeg})");
        }

        return problems;
    }
}
