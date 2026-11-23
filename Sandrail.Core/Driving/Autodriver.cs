using System.Numerics;
using Sandrail.Core.Cars;

namespace Sandrail.Core.Driving;

/// <summary>What a driver asks for: the same three numbers a player's keys or pad give.</summary>
public readonly record struct DriveCommand(float Throttle, float Brake, float Steer);

/// <summary>
/// Drives a car along a <see cref="RacingLine"/>, steering its front wheels onto the line and holding the
/// speed the line says each bit can be taken at (PLAN, "His ideas": an autodriver, 2026-11-23).
/// </summary>
/// <remarks>
/// <para>
/// <b>It drives through the same three inputs a player does</b>, so the physics can't tell it from him.
/// Game turns its command into a <c>DriverInput</c> and nothing else changes.
/// </para>
/// <para>
/// <b>Steering is measured from the front axle</b>, at his word to take the wheel positions into account.
/// Three terms, a Stanley controller with a feed-forward: the angle the line's own bend needs,
/// <c>atan(L κ)</c>; the angle between where the car points and where the line goes; and a pull back
/// onto the line that grows with how far the front axle is off it and fades with speed,
/// <c>atan(k e / (v + 1))</c>. The first version aimed at a point ahead instead (pure pursuit), which cuts
/// inside every bend, and on a line already at the inside edge that put the inside tyres on the sand
/// (2026-11-23). This one follows the line itself. The heading term also counter-steers a slide by
/// itself: a car pointing the wrong way is steered back.
/// </para>
/// <para>
/// <b>Speed</b> is the line's profile, taken at the slowest point in the next fraction of a second so it
/// starts braking before the car arrives, held with throttle and brake. <b>The slip guard</b> eases the
/// throttle off as the tail steps out, which is the one thing a driver of this car has to learn.
/// </para>
/// <para>
/// <b>Its aggression is <see cref="Caution"/></b>, the share of grip the speed plan uses, and it can be
/// turned while it drives. Steering doesn't change with it: a bolder driver here is one that carries more
/// speed into the same line.
/// </para>
/// </remarks>
public sealed class Autodriver
{
    private const float PreviewSeconds = 0.4f;
    private const float CrossTrackGain = 3.0f;
    private const float SlipGuardStartDeg = 8.0f;
    private const float BrakeGuardStartDeg = 6.0f;
    private const float SlipGuardSpanDeg = 10.0f;

    /// <summary>Seconds: radians of steering for each radian a second of yaw rate off what the line asks.</summary>
    private const float YawDamping = 0.08f;

    /// <summary>Farther than this from the line, metres, the car has been moved, not driven: the lap timer's figure.</summary>
    private const float LostM = 15.0f;

    /// <summary>How far from where it was a lost car is first looked for, samples behind and ahead (a metre each on a route line).</summary>
    private const int SpunBackSamples = 100;
    private const int SpunAheadSamples = 150;

    /// <summary>As far off the line as a spin throws a car, metres; further than this from where it was, it's been moved.</summary>
    private const float SpunM = 40.0f;

    /// <summary>How far before a loop it stops pulling the car onto the line and only lines it up: the loop piece's settling straight.</summary>
    private const float LoopApproachM = 15.0f;

    private readonly RacingLine _line;
    private readonly CarSpec _spec;
    private float[] _speeds = [];
    private float _caution;
    private int _index = -1;
    private int _frontIndex = -1;

    /// <summary>The share of grip it plans for unless told otherwise.</summary>
    public const float DefaultCaution = 0.8f;

    /// <summary>The range its caution is held to, and the step the game's keys move it by.</summary>
    public const float MinCaution = 0.5f;
    public const float MaxCaution = 1.0f;
    public const float CautionStep = 0.05f;

    /// <param name="caution">The share of the car's grip it plans corners for: lower is slower and safer.</param>
    public Autodriver(RacingLine line, CarSpec spec, float caution = DefaultCaution)
    {
        _line = line;
        _spec = spec;
        Caution = caution;
    }

    /// <summary>
    /// The share of the car's grip it plans corners and braking for, held to <see cref="MinCaution"/> to
    /// <see cref="MaxCaution"/>: what the game calls its aggression (his ask, 2026-11-23). Setting it replans
    /// the whole lap's speeds at once, so it can be turned while it drives; the line itself doesn't move.
    /// </summary>
    public float Caution
    {
        get => _caution;
        set
        {
            _caution = Math.Clamp(value, MinCaution, MaxCaution);
            _speeds = _line.SpeedProfile(_spec, _caution);
        }
    }

    /// <summary>The sample the car's middle was nearest last time it drove.</summary>
    public int Index => _index;

    /// <summary>How far right of the line the car's middle was, metres.</summary>
    public float Lateral { get; private set; }

    /// <summary>The speed it was aiming for, m/s.</summary>
    public float TargetSpeed { get; private set; }

    public float SpeedAt(int i) => _speeds[i];

    /// <summary>Whether it has driven to the end of an open line, where it stops the car.</summary>
    public bool AtTheEnd => !_line.Closed && _index == _line.Count - 1;

    /// <param name="yawRate">
    /// How fast the car is turning, radians a second, positive to the right; NaN where it isn't known (a
    /// kinematic test car, which turns exactly as steered and needs no damping).
    /// </param>
    /// <param name="airborne">
    /// No wheel is on the ground. It keeps its hands off then (M2d): in the air the pedals and the
    /// steering are air control, and the jumps' hints were measured with nothing touched.
    /// </param>
    public DriveCommand Drive(Vector3 position, Vector3 forward, Vector3 up, Vector3 velocity, float yawRate = float.NaN, bool airborne = false)
    {
        int was = _index;
        _index = _line.Nearest(position, _index);
        if (Vector3.Distance(position, _line.PositionAt(_index)) > LostM)
        {
            // Off the line: first a good way either side of where it was, so a car that spun off beside a later
            // stretch of road isn't taken on to it, skipping what's between (seed 1, 2026-11-24: a spin at
            // (21, 0) put it on the road back past 300 m on, a shortcut its checkpoints caught). Only a car
            // further from that than a spin throws it, one put somewhere else (a respawn at a checkpoint), is
            // looked for everywhere. Either way the front axle follows.
            int near = was < 0 ? -1 : _line.Nearest(position, was, SpunBackSamples, SpunAheadSamples);
            _index = near >= 0 && Vector3.Distance(position, _line.PositionAt(near)) <= SpunM ? near : _line.Nearest(position);
            _frontIndex = -1;
        }

        Lateral = _line.LateralOffset(_index, position);

        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, up));
        float speed = velocity.Length();
        float forwardSpeed = Vector3.Dot(velocity, forward);

        // From the front axle: where it is against the line, and which way the line goes and bends right
        // there. Read even a metre ahead, the line's direction and bend lead the car, and on a 30 m circle
        // that left it settled a metre inside the line at 15 m/s (2026-11-23).
        Vector3 frontAxle = position + (forward * (_spec.WheelbaseM / 2.0f));
        _frontIndex = _line.Nearest(frontAxle, _frontIndex < 0 ? _index : _frontIndex);
        float frontOff = _line.LateralOffset(_frontIndex, frontAxle);

        if (airborne)
        {
            TargetSpeed = _speeds[_index];
            return default;
        }

        // The axle is up to half a sample from the nearest one: turn the line's direction by its bend over
        // that gap, so it doesn't jump as the samples go by. And compare the body with the line's direction
        // half a wheelbase back, where the body's middle is: on a bend the body is square to the line at its
        // middle, not at its front axle, and asking for that at the axle left the car a steady quarter of a
        // metre inside on a 30 m circle at 15 m/s.
        float curvature = _line.CurvatureAt(_frontIndex);
        float along = Vector3.Dot(frontAxle - _line.PositionAt(_frontIndex), _line.ForwardAt(_frontIndex));
        float bend = MathF.Atan(_spec.WheelbaseM * curvature);
        float lineHeading = Heading(_line.ForwardAt(_frontIndex)) + (curvature * (along - (_spec.WheelbaseM / 2.0f)));
        float heading = Wrap(lineHeading - Heading(forward));
        float pull = -MathF.Atan(CrossTrackGain * frontOff / (MathF.Max(forwardSpeed, 0.0f) + 1.0f));

        // Coming up to a loop it lines the car up rather than pulling it onto the line: the wheels go
        // straight at the foot, and a car pointing a few degrees across the road then drifts across it all
        // the way round, while one a little to the side but square to it comes out as far to the side. 0.65 m
        // off with the pull on, it went in 4° across and came off the far side (2026-11-23).
        float toLoop = DistanceToStraight(_frontIndex);
        if (toLoop < LoopApproachM)
        {
            pull *= toLoop / LoopApproachM;
        }

        // Yaw damping: steer against turning faster or slower than the line asks. It calms the weave the
        // other terms leave at speed, and catches a car turning in by itself under braking before it grows:
        // both seen on his tuned car, 2026-11-23.
        float damping = float.IsNaN(yawRate) ? 0.0f : -YawDamping * (yawRate - (forwardSpeed * curvature));
        float steerDeg = (bend + heading + pull + damping) * (180.0f / MathF.PI);
        float steer = Math.Clamp(steerDeg / WheelModel.SteerLimitDeg(_spec, speed), -1.0f, 1.0f);

        // A loop is driven with the wheels straight (M1c): its road's sideways drift is built for that,
        // and a heading read off a road pointing at the sky means nothing.
        if (_line.StraightAt(_frontIndex))
        {
            steer = 0.0f;
        }

        // The slowest the line allows between here and a fraction of a second on; at the end of an open
        // line, a stop.
        int last = _line.IndexAhead(_index, speed * PreviewSeconds);
        float target = AtTheEnd ? 0.0f : _speeds[_index];
        for (int i = _index; i != last; i = (i + 1) % _line.Count)
        {
            target = MathF.Min(target, _speeds[(i + 1) % _line.Count]);
        }

        TargetSpeed = target;
        float throttle = Math.Clamp((target - forwardSpeed) * 0.5f, 0.0f, 1.0f);
        float brake = Math.Clamp((forwardSpeed - target) * 0.5f, 0.0f, 1.0f);

        float slip = speed > 3.0f
            ? MathF.Abs(MathF.Atan2(Vector3.Dot(velocity, right), MathF.Abs(forwardSpeed))) * (180.0f / MathF.PI)
            : 0.0f;
        throttle *= Math.Clamp(1.0f - ((slip - SlipGuardStartDeg) / SlipGuardSpanDeg), 0.0f, 1.0f);

        // The same for the brakes, more gently: a tail stepping out under braking is the car turning in by
        // itself, and easing the brakes gives the rear its grip back. Never below a third, so it still stops.
        brake *= Math.Clamp(1.0f - ((slip - BrakeGuardStartDeg) / SlipGuardSpanDeg), 0.35f, 1.0f);

        // Never brake into a standstill: held there, the brake reverses.
        if (forwardSpeed < 2.0f)
        {
            brake = 0.0f;
        }

        return new DriveCommand(throttle, brake, steer);
    }

    /// <summary>How far ahead of a sample the wheels go straight (a loop), metres, up to <see cref="LoopApproachM"/>; that or more if not.</summary>
    private float DistanceToStraight(int from)
    {
        int last = _line.IndexAhead(from, LoopApproachM);
        for (int i = from; ; i = (i + 1) % _line.Count)
        {
            if (_line.StraightAt(i))
            {
                return Math.Max(0.0f, _line.DistanceAt(i) - _line.DistanceAt(from));
            }

            if (i == last)
            {
                return LoopApproachM;
            }
        }
    }

    /// <summary>A direction's heading in the horizontal plane: 0 is -Z, increasing to the right.</summary>
    private static float Heading(Vector3 direction) => MathF.Atan2(direction.X, -direction.Z);

    private static float Wrap(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= 2.0f * MathF.PI;
        }

        while (angle < -MathF.PI)
        {
            angle += 2.0f * MathF.PI;
        }

        return angle;
    }
}
