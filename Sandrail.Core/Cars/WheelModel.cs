namespace Sandrail.Core.Cars;

/// <summary>What one tyre does this tick, in its own contact frame.</summary>
/// <param name="Forward">Along the wheel's heading, flattened onto the ground. Positive pushes forward.</param>
/// <param name="Side">Along the wheel's right. Positive pushes right.</param>
/// <param name="SlipAngleDeg">Positive when the contact patch slides right.</param>
/// <param name="Saturated">The tyre was asked for more than its grip and gave all of it.</param>
public readonly record struct TyreForces(float Forward, float Side, float SlipAngleDeg, bool Saturated);

/// <summary>
/// M1-PLAN's "car's model, written down", as plain arithmetic on scalars.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rigid body is Godot's; the laws are here.</b> Game does the raycasts, turns vectors into
/// the contact frame, calls these, and applies what comes back. Keeping the laws out of Godot means
/// every rule in the plan (the friction circle, the slide, the bump stop, the pedal logic) is pinned
/// by a test rather than only by how it felt on the night.
/// </para>
/// <para>
/// Nothing here holds state. Compression rates and smoothed pedals are the caller's.
/// </para>
/// </remarks>
public static class WheelModel
{
    public const float Gravity = 9.81f;

    /// <summary>Below this forward speed brakes and rolling resistance fade out rather than flipping sign every tick.</summary>
    private const float StandstillMps = 0.5f;

    /// <summary>How far each spring is compressed with the car at rest on level ground, assuming an even share of weight.</summary>
    public static float RestCompression(CarSpec spec) =>
        MathF.Min(spec.MassKg * Gravity / 4.0f / spec.SpringNPerM, spec.SuspensionTravelM);

    /// <summary>
    /// The spring, the damper and the bump stop. Never negative: a spring can push the car up but
    /// never pull it down.
    /// </summary>
    /// <param name="compression">Metres, 0 at full stretch, up to the travel.</param>
    /// <param name="compressionRate">Metres a second, positive while compressing.</param>
    public static float SuspensionForce(CarSpec spec, float compression, float compressionRate)
    {
        if (compression <= 0.0f)
        {
            return 0.0f;
        }

        float damping = compressionRate > 0.0f ? spec.BumpDampingNsPerM : spec.ReboundDampingNsPerM;
        float force = (spec.SpringNPerM * compression) + (damping * compressionRate);

        float stopStart = spec.SuspensionTravelM * (1.0f - spec.BumpStopFraction);
        if (compression > stopStart)
        {
            force += spec.SpringNPerM * spec.BumpStopStiffness * (compression - stopStart);
        }

        return MathF.Max(0.0f, force);
    }

    /// <summary>The anti-roll bar's push on the left wheel of an axle; the right gets the same pulling.</summary>
    /// <remarks>
    /// The more compressed side is pushed up and the other pulled down, which is a bar resisting roll.
    /// Applied only where a wheel is on the ground.
    /// </remarks>
    public static float AntiRollForce(float stiffness, float leftCompression, float rightCompression) =>
        stiffness * (leftCompression - rightCompression);

    /// <summary>
    /// The steering lock allowed at this speed: full at rest, narrowing to the minimum at the fade speed,
    /// and multiplied by <see cref="CarSpec.WideLockFactor"/> while the wide-lock key is held.
    /// </summary>
    public static float SteerLimitDeg(CarSpec spec, float speed, bool wideLock = false)
    {
        float t = Math.Clamp(MathF.Abs(speed) / spec.SteerFadeSpeedMps, 0.0f, 1.0f);
        float limit = spec.MaxSteerDeg + ((spec.MinSteerDeg - spec.MaxSteerDeg) * t);
        return limit * SteerScale(spec, wideLock);
    }

    /// <summary>
    /// What the wide-lock key scales steering by: the lock, and with it both turn rates, so full lock
    /// takes the same time to reach either way and the bigger lock never feels slower.
    /// </summary>
    public static float SteerScale(CarSpec spec, bool wideLock) => wideLock ? spec.WideLockFactor : 1.0f;

    /// <summary>
    /// Each front wheel's angle for a steering angle, degrees, positive right: the inside wheel turns
    /// more than the outside one, by <see cref="CarSpec.AckermannShare"/> of the way to true geometry.
    /// </summary>
    /// <remarks>
    /// True geometry puts both wheels' axles through one point on the line of the rear axle, the turn's
    /// centre, so neither scrubs. The steering angle is the one a wheel in the middle of the axle would
    /// need. Past about 72°, where the centre would fall inside the car's own track, the inside wheel
    /// is held at 89° rather than wrapping round.
    /// </remarks>
    public static (float LeftDeg, float RightDeg) FrontWheelsDeg(CarSpec spec, float steerDeg)
    {
        float magnitude = MathF.Abs(steerDeg);
        if (magnitude < 1e-3f || spec.AckermannShare <= 0.0f)
        {
            return (steerDeg, steerDeg);
        }

        const float Degrees = 180.0f / MathF.PI;
        float wheelbase = spec.WheelbaseM;
        float half = spec.TrackM / 2.0f;
        float radius = wheelbase / MathF.Tan(magnitude / Degrees);

        float inner = MathF.Min(MathF.Atan2(wheelbase, radius - half) * Degrees, 89.0f);
        float outer = MathF.Atan2(wheelbase, radius + half) * Degrees;
        inner = magnitude + ((inner - magnitude) * spec.AckermannShare);
        outer = magnitude + ((outer - magnitude) * spec.AckermannShare);

        // Turning right, the right wheel is the inside one.
        return steerDeg > 0.0f ? (outer, inner) : (-inner, -outer);
    }

    /// <summary>What the engine can push at this speed: the force limit off the line, power ÷ speed above it.</summary>
    public static float EngineForce(CarSpec spec, float speed) =>
        MathF.Min(spec.DriveForceN, spec.PowerW / MathF.Max(MathF.Abs(speed), 0.1f));

    /// <summary>
    /// Turns the two pedals into a total drive force and a brake amount, the arcade way: brake while
    /// rolling forward slows you and at a standstill reverses; throttle while rolling backward brakes.
    /// </summary>
    /// <param name="throttle">0..1, already smoothed.</param>
    /// <param name="brake">0..1, already smoothed.</param>
    /// <param name="forwardSpeed">The car's speed along its own heading; negative when reversing.</param>
    /// <returns>Drive in newtons for the whole car (negative is reverse), and brake 0..1.</returns>
    public static (float DriveForce, float BrakeAmount) ResolvePedals(
        CarSpec spec, float throttle, float brake, float forwardSpeed)
    {
        float drive = 0.0f;
        float brakeAmount = 0.0f;

        if (throttle > 0.0f)
        {
            if (forwardSpeed < -1.0f)
            {
                brakeAmount = MathF.Max(brakeAmount, throttle);
            }
            else
            {
                drive += throttle * EngineForce(spec, forwardSpeed);
            }
        }

        if (brake > 0.0f)
        {
            if (forwardSpeed > 1.0f)
            {
                brakeAmount = MathF.Max(brakeAmount, brake);
            }
            else if (forwardSpeed > -spec.ReverseTopSpeedMps)
            {
                drive -= brake * spec.ReverseForceFraction * spec.DriveForceN;
            }
        }

        return (drive, brakeAmount);
    }

    /// <summary>
    /// The share of peak sideways grip a tyre gives at this slip angle: rising linearly to 1 at the
    /// peak, easing to the slide fraction by twice the peak, flat after.
    /// </summary>
    public static float SlipCurve(float slipDeg, float peakDeg, float slideFraction)
    {
        float a = MathF.Abs(slipDeg);
        if (a <= peakDeg)
        {
            return a / peakDeg;
        }

        if (a <= 2.0f * peakDeg)
        {
            return 1.0f + ((slideFraction - 1.0f) * ((a - peakDeg) / peakDeg));
        }

        return slideFraction;
    }

    /// <summary>One tyre's forces for one tick.</summary>
    /// <param name="load">The wheel's suspension force, newtons. Zero or less means no grip at all.</param>
    /// <param name="forwardSpeed">Ground speed at the contact along the wheel's heading.</param>
    /// <param name="sideSpeed">Ground speed at the contact along the wheel's right.</param>
    /// <param name="driveForce">This wheel's share of the drive, signed.</param>
    /// <param name="brakeForce">This wheel's share of the brakes, newtons, not negative.</param>
    /// <param name="step">The physics step, for the low-speed term that cancels sideways speed in one tick.</param>
    /// <param name="locked">
    /// 0..1, how far the wheel is locked (the handbrake on a rear wheel): 0 rolls, 1 is
    /// <see cref="LockedTyre"/>, and between is that far from one to the other. Both lie inside the
    /// friction circle, so any blend does too.
    /// </param>
    public static TyreForces Tyre(
        CarSpec spec,
        Surface surface,
        float load,
        float forwardSpeed,
        float sideSpeed,
        float driveForce,
        float brakeForce,
        float step,
        float locked = 0.0f)
    {
        TyreForces rolling = RollingTyre(spec, surface, load, forwardSpeed, sideSpeed, driveForce, brakeForce, step);
        if (!(locked > 0.0f))
        {
            return rolling;
        }

        float h = MathF.Min(locked, 1.0f);
        TyreForces sliding = LockedTyre(spec, surface, load, forwardSpeed, sideSpeed, step);
        return new TyreForces(
            rolling.Forward + ((sliding.Forward - rolling.Forward) * h),
            rolling.Side + ((sliding.Side - rolling.Side) * h),
            rolling.SlipAngleDeg,
            rolling.Saturated || sliding.Saturated);
    }

    /// <summary>
    /// A locked tyre: it doesn't roll, so it slides, and its force is against the contact patch's motion,
    /// whichever way that is, at the slide's share of the grip (<see cref="CarSpec.SlideGripFraction"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This one law is the handbrake turn.</b> Going straight, the motion is all forward, so it's all
    /// braking. In a corner the patch moves mostly forward and only a little sideways, so only a little of
    /// the force holds the car into the turn: a locked rear axle lets go, and the tail comes round. Once
    /// the car is sideways, the force is sideways too and catches the slide.
    /// </para>
    /// <para>
    /// The force asked for is whatever stops the patch in one tick, taking the wheel's share of the car
    /// as <c>load / g</c> the way the rolling tyre's low-speed term does. At any real speed that's far past
    /// the grip and the grip is what you get; within a few centimetres a second of a standstill it isn't,
    /// so the wheel holds still instead of shivering, and a parked car with the handbrake on stays put.
    /// <see cref="TyreForces.Saturated"/> says it's sliding rather than holding.
    /// </para>
    /// </remarks>
    public static TyreForces LockedTyre(
        CarSpec spec, Surface surface, float load, float forwardSpeed, float sideSpeed, float step)
    {
        float limit = spec.Grip * surface.Grip * load * spec.SlideGripFraction;
        float slipDeg = MathF.Atan2(sideSpeed, MathF.Max(MathF.Abs(forwardSpeed), spec.SlipSpeedFloorMps))
            * (180.0f / MathF.PI);
        if (!(limit > 0.0f))
        {
            return default;
        }

        float mass = load / Gravity;
        float forward = -forwardSpeed * mass / step;
        float side = -sideSpeed * mass / step;
        float demand = MathF.Sqrt((forward * forward) + (side * side));
        if (demand <= limit)
        {
            return new TyreForces(forward, side, slipDeg, false);
        }

        float scale = limit / demand;
        return new TyreForces(forward * scale, side * scale, slipDeg, true);
    }

    /// <summary>A rolling tyre: the slip curve, the brakes and the drive, and the friction circle.</summary>
    private static TyreForces RollingTyre(
        CarSpec spec,
        Surface surface,
        float load,
        float forwardSpeed,
        float sideSpeed,
        float driveForce,
        float brakeForce,
        float step)
    {
        float limit = spec.Grip * surface.Grip * load;
        if (!(limit > 0.0f))
        {
            return default;
        }

        float standstill = Math.Clamp(forwardSpeed / StandstillMps, -1.0f, 1.0f);
        float forwardDemand = driveForce
            - (brakeForce * standstill)
            - (surface.RollingResistance * load * standstill);

        float slipDeg = MathF.Atan2(sideSpeed, MathF.Max(MathF.Abs(forwardSpeed), spec.SlipSpeedFloorMps))
            * (180.0f / MathF.PI);
        float modelSide = -limit * SlipCurve(slipDeg, spec.PeakSlipDeg, spec.SlideGripFraction) * MathF.Sign(slipDeg);

        // Near a standstill the slip angle says little, so the sideways speed is cancelled outright,
        // blended into the model as the car gets going.
        float cancelSide = -sideSpeed * (load / Gravity) / step;
        float speed = MathF.Sqrt((forwardSpeed * forwardSpeed) + (sideSpeed * sideSpeed));
        float model = spec.LowSpeedCancelMps > 0.0f ? Math.Clamp(speed / spec.LowSpeedCancelMps, 0.0f, 1.0f) : 1.0f;
        float sideDemand = cancelSide + ((modelSide - cancelSide) * model);

        float demand = MathF.Sqrt((forwardDemand * forwardDemand) + (sideDemand * sideDemand));
        if (demand <= limit)
        {
            return new TyreForces(forwardDemand, sideDemand, slipDeg, false);
        }

        // The friction circle. Two ways to share a shortfall, blended by ForwardPriority; both points
        // lie inside the circle, so any blend of them does too.
        float scale = limit / demand;
        float sharedForward = forwardDemand * scale;
        float sharedSide = sideDemand * scale;

        float firstForward = Math.Clamp(forwardDemand, -limit, limit);
        float sideRoom = MathF.Sqrt(MathF.Max(0.0f, (limit * limit) - (firstForward * firstForward)));
        float firstSide = Math.Clamp(sideDemand, -sideRoom, sideRoom);

        float p = spec.ForwardPriority;
        return new TyreForces(
            sharedForward + ((firstForward - sharedForward) * p),
            sharedSide + ((firstSide - sharedSide) * p),
            slipDeg,
            true);
    }

    /// <summary>Sea-level air, kg/m³: what a wing's forces are made of.</summary>
    public const float AirDensity = 1.225f;

    /// <summary>
    /// The fitted wing's downforce per (m/s)² of forward speed, at its set angle: ½ρ·A·C_L, so at 30 m/s
    /// it presses 900 times this. 0 with no wing.
    /// </summary>
    public static float DownforcePerMps2(CarSpec spec)
    {
        Wing wing = spec.FittedWing();
        return 0.5f * AirDensity * wing.AreaM2 * wing.LiftCoefficient(spec.WingAngleDeg);
    }

    /// <summary>The fitted wing's drag per (m/s)² of forward speed, at its set angle; on top of <see cref="CarSpec.DragNPerMps2"/>.</summary>
    public static float WingDragPerMps2(CarSpec spec)
    {
        Wing wing = spec.FittedWing();
        return 0.5f * AirDensity * wing.AreaM2 * wing.DragCoefficient(spec.WingAngleDeg);
    }

    /// <summary>
    /// The wing's forces, newtons, from how it's moving through the air: <paramref name="forwardSpeed"/>
    /// along the chassis and <paramref name="upSpeed"/> up it, both measured at the wing. Downforce is
    /// across the air's direction, toward the chassis's floor (negative is lift); drag is along it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The wing meets the air at its set angle less the angle the car is moving at</b>: a car whose nose
    /// is up relative to where it's going has the air coming from below, which presses the wing less, and
    /// past its set angle lifts it. So in the air a wing behind the centre of mass turns the car to follow
    /// its arc, as a dart's flights do; and because the speed is the wing's own, a car pitching swings the
    /// wing through the air and the wing resists it. On the road the two angles are within a degree or
    /// two, and it's simply downforce.
    /// </para>
    /// <para>
    /// Only air from the front counts: going backwards the wing does nothing. A car sliding sideways gets
    /// the share of its speed that's still forward.
    /// </para>
    /// </remarks>
    public static (float Downforce, float Drag) WingForces(CarSpec spec, float forwardSpeed, float upSpeed = 0.0f)
    {
        Wing wing = spec.FittedWing();
        if (!(forwardSpeed > 0.0f) || wing.AreaM2 <= 0.0f)
        {
            return (0.0f, 0.0f);
        }

        float noseUpDeg = MathF.Atan2(-upSpeed, forwardSpeed) * (180.0f / MathF.PI);
        float angle = spec.WingAngleDeg - noseUpDeg;
        float pressure = 0.5f * AirDensity * wing.AreaM2 * ((forwardSpeed * forwardSpeed) + (upSpeed * upSpeed));
        return (pressure * wing.LiftCoefficient(angle), pressure * wing.DragCoefficient(angle));
    }

    /// <summary>
    /// The speed where the engine's power just matches drag and rolling resistance on level ground,
    /// found by bisection. What the straight oracle should read, before traction or gradient say
    /// otherwise. A wing's drag counts, and so does its downforce, which loads the tyres and so their
    /// rolling resistance.
    /// </summary>
    public static float TopSpeedEstimate(CarSpec spec, Surface surface)
    {
        float drag = spec.DragNPerMps2 + WingDragPerMps2(spec);
        float downforce = DownforcePerMps2(spec);
        float low = 0.0f;
        float high = 500.0f;

        for (int i = 0; i < 60; i++)
        {
            float mid = 0.5f * (low + high);
            float rolling = (surface.RollingResistance * spec.MassKg * Gravity) + (surface.RollingResistance * downforce * mid * mid);
            float surplus = EngineForce(spec, mid) - (drag * mid * mid) - rolling;
            if (surplus > 0.0f)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
