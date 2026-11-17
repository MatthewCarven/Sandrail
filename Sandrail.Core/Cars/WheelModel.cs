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

    /// <summary>The steering lock allowed at this speed: full at rest, narrowing to the minimum at the fade speed.</summary>
    public static float SteerLimitDeg(CarSpec spec, float speed)
    {
        float t = Math.Clamp(MathF.Abs(speed) / spec.SteerFadeSpeedMps, 0.0f, 1.0f);
        return spec.MaxSteerDeg + ((spec.MinSteerDeg - spec.MaxSteerDeg) * t);
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
    public static TyreForces Tyre(
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

    /// <summary>
    /// The speed where the engine's power just matches drag and rolling resistance on level ground,
    /// found by bisection. What the straight oracle should read, before traction or gradient say
    /// otherwise.
    /// </summary>
    public static float TopSpeedEstimate(CarSpec spec, Surface surface)
    {
        float rolling = surface.RollingResistance * spec.MassKg * Gravity;
        float low = 0.0f;
        float high = 500.0f;

        for (int i = 0; i < 60; i++)
        {
            float mid = 0.5f * (low + high);
            float surplus = EngineForce(spec, mid) - (spec.DragNPerMps2 * mid * mid) - rolling;
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
