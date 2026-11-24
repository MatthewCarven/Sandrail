using System;
using System.Collections.Generic;
using Godot;
using Sandrail.Core.Cars;

namespace Sandrail.Game.Cars;

/// <summary>What a driver is asking for this tick: a player's keys, an oracle's script, later a ghost's stream.</summary>
public struct DriverInput
{
    /// <summary>0..1.</summary>
    public float Throttle;

    /// <summary>0..1. Reverses at a standstill.</summary>
    public float Brake;

    /// <summary>-1 full left .. 1 full right.</summary>
    public float Steer;

    /// <summary>Space: locks the rear wheels by <see cref="CarSpec.HandbrakeLock"/> while held, at once, as a lever does.</summary>
    public bool Handbrake;

    /// <summary>Shift: the steering lock times <see cref="CarSpec.WideLockFactor"/> while held.</summary>
    public bool WideLock;
}

/// <summary>
/// The raycast buggy: a <see cref="RigidBody3D"/> chassis and four wheels that are rays.
/// </summary>
/// <remarks>
/// <para>
/// <b>The laws are <see cref="WheelModel"/>'s; this class only measures and applies.</b> Each tick,
/// in <see cref="_IntegrateForces"/>, a ray goes down from each mount; a hit is turned into the
/// wheel's contact frame, the model is asked for the suspension and tyre forces, and they're applied
/// at the contact. M1-PLAN's "car's model, written down" is the specification.
/// </para>
/// <para>
/// <b>Godot's linear damping is replaced with zero, and that is not optional.</b> The project default
/// is 0.1 a second, which on a 650 kg car at 50 m/s is a 3,250 N brake nobody asked for, more than
/// the drag the spec models. Drag is the spec's alone.
/// </para>
/// <para>
/// Visual wheels are moved in <see cref="_PhysicsProcess"/> from the last step's measurements, so
/// physics interpolation smooths them along with the body.
/// </para>
/// </remarks>
public partial class Buggy : RigidBody3D
{
    /// <summary>Collision layer for the world: the plain, the pieces, the terrain.</summary>
    public const uint WorldLayer = 1;

    /// <summary>Collision layer for cars. Cars don't collide with each other, ghost-style (PLAN).</summary>
    public const uint CarLayer = 2;

    private const int FrontLeft = 0;
    private const int FrontRight = 1;
    private const int RearLeft = 2;
    private const int RearRight = 3;

    private readonly Vector3[] _mounts = new Vector3[4];
    private readonly float[] _compression = new float[4];
    private readonly bool[] _grounded = new bool[4];
    private readonly float[] _load = new float[4];
    private readonly float[] _slipDeg = new float[4];
    private readonly float[] _wheelSpeed = new float[4];
    private readonly bool[] _saturated = new bool[4];
    private readonly Surface[] _surface = new Surface[4];

    /// <summary>Each wheel's own steering angle: Ackermann turns the inside front more. The rears stay at 0.</summary>
    private readonly float[] _wheelSteerDeg = new float[4];

    /// <summary>Each collider's surface, once read; null for one painted by position.</summary>
    private readonly Dictionary<ulong, Surface?> _surfaceCache = new();

    /// <summary>
    /// What the ground is at a point, for a body tagged <c>painted</c> (the generated terrain, M3a): one
    /// body, many surfaces (M3-PLAN fact 7). Null where there's no such ground.
    /// </summary>
    public System.Func<Vector3, Surface>? PaintedSurface { get; set; }

    /// <summary>The grey boxes it wears (M4b: in a node of their own, so a ghost can wear the same).</summary>
    private CarLooks? _looks;

    private CarSpec _spec = new();
    private CollisionShape3D? _shape;
    private Vector3 _lastVelocity;
    private PhysicsRayQueryParameters3D _ray = new();
    private float _throttle;
    private float _brake;
    private float _steerDeg;
    private Transform3D? _pendingRespawn;
    private float _pendingSpeed;
    private bool _resetInterpolation;

    /// <summary>Set every physics tick by whoever is driving.</summary>
    public DriverInput Driver { get; set; }

    public CarSpec Spec => _spec;

    /// <summary>The wheels' steering angle now, degrees, positive right.</summary>
    public float SteerDeg => _steerDeg;

    public float Throttle => _throttle;

    public float Brake => _brake;

    /// <summary>The steering lock allowed at the current speed, degrees.</summary>
    public float SteerLimitDeg { get; private set; }

    /// <summary>Last tick's total drive, newtons; negative is reverse.</summary>
    public float DriveForce { get; private set; }

    /// <summary>Last tick's brake, 0..1.</summary>
    public float BrakeAmount { get; private set; }

    /// <summary>How far the rear wheels were locked last tick, 0..1: the handbrake times its lock.</summary>
    public float HandbrakeAmount { get; private set; }

    /// <summary>Last tick's air-control pitch, -1 (nose down) to 1 (nose up); 0 with a wheel down.</summary>
    public float AirPitch { get; private set; }

    /// <summary>Last tick's air-control steer, -1 (left) to 1 (right); 0 with a wheel down.</summary>
    public float AirSteer { get; private set; }

    /// <summary>Last tick's downforce from the wing, newtons.</summary>
    public float WingDownforce { get; private set; }

    /// <summary>Last tick's drag from the wing, newtons, on top of the body's.</summary>
    public float WingDrag { get; private set; }

    /// <summary>World acceleration over the last tick, from the change in velocity.</summary>
    public Vector3 Acceleration { get; private set; }

    public int GroundedCount { get; private set; }

    public float Compression(int wheel) => _compression[wheel];

    public float Load(int wheel) => _load[wheel];

    public float SlipDeg(int wheel) => _slipDeg[wheel];

    public bool Grounded(int wheel) => _grounded[wheel];

    public bool Saturated(int wheel) => _saturated[wheel];

    public Surface SurfaceUnder(int wheel) => _surface[wheel];

    /// <summary>
    /// Sets up the body and applies a spec to it. Call before adding the car to the tree.
    /// </summary>
    public void Configure(CarSpec spec)
    {
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        LinearDampMode = DampMode.Replace;
        LinearDamp = 0.0f;
        AngularDampMode = DampMode.Replace;
        CanSleep = false;
        CollisionLayer = CarLayer;
        CollisionMask = WorldLayer;

        // Low friction on the chassis, so a car on its belly slides rather than sticking.
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.3f };

        for (int i = 0; i < 4; i++)
        {
            _surface[i] = Surfaces.Road;
        }

        _ray = new PhysicsRayQueryParameters3D
        {
            CollisionMask = WorldLayer,
            CollideWithAreas = false,
            CollideWithBodies = true,
        };

        ApplySpec(spec);
    }

    /// <summary>
    /// Drives on a new spec from the next tick: the tuning panel's way in, and its Reset's.
    /// </summary>
    /// <remarks>
    /// The numbers the model reads take effect by being swapped in. The body's own properties are set
    /// here, and the collider and grey boxes are rebuilt only when a dimension changed, so dragging a
    /// grip slider doesn't rebuild the car sixty times a second.
    /// </remarks>
    public void ApplySpec(CarSpec spec)
    {
        bool reshape = _shape is null || ShapeDiffers(_spec, spec);
        _spec = spec;

        Mass = spec.MassKg;
        CenterOfMass = new Vector3(0.0f, -spec.CentreOfMassDropM, 0.0f);
        AngularDamp = spec.AngularDamping;

        float halfTrack = spec.TrackM / 2.0f;
        float halfBase = spec.WheelbaseM / 2.0f;

        // Godot's forward is -Z, so the front axle is at -Z.
        _mounts[FrontLeft] = new Vector3(-halfTrack, spec.MountHeightM, -halfBase);
        _mounts[FrontRight] = new Vector3(halfTrack, spec.MountHeightM, -halfBase);
        _mounts[RearLeft] = new Vector3(-halfTrack, spec.MountHeightM, halfBase);
        _mounts[RearRight] = new Vector3(halfTrack, spec.MountHeightM, halfBase);

        if (_looks is null)
        {
            _looks = new CarLooks();
            AddChild(_looks);
        }

        _looks.Show(spec);
        if (!reshape)
        {
            return;
        }

        var chassisSize = new Vector3(spec.ChassisWidthM, spec.ChassisHeightM, spec.ChassisLengthM);
        if (_shape is null)
        {
            _shape = new CollisionShape3D();
            AddChild(_shape);
        }

        _shape.Shape = new BoxShape3D { Size = chassisSize };
    }

    /// <summary>Whether two specs differ in a dimension the collider or the grey boxes are built from.</summary>
    internal static bool ShapeDiffers(CarSpec a, CarSpec b) =>
        a.ChassisLengthM != b.ChassisLengthM
        || a.ChassisWidthM != b.ChassisWidthM
        || a.ChassisHeightM != b.ChassisHeightM
        || a.WheelRadiusM != b.WheelRadiusM
        || a.WheelbaseM != b.WheelbaseM
        || a.TrackM != b.TrackM
        || a.MountHeightM != b.MountHeightM
        || a.SuspensionTravelM != b.SuspensionTravelM;

    /// <summary>The height of the body's origin above flat ground with the car at rest.</summary>
    public static float RestHeight(CarSpec spec) =>
        -spec.MountHeightM + spec.SuspensionTravelM + spec.WheelRadiusM - WheelModel.RestCompression(spec);

    /// <summary>Teleports the car on the next physics tick, stopped, or rolling forward at a speed (an oracle starting a piece at the speed it's meant for).</summary>
    public void Respawn(Transform3D at, float speedMps = 0.0f)
    {
        _pendingRespawn = at;
        _pendingSpeed = speedMps;
    }

    public override void _Ready()
    {
        // The ray must never hit the car it belongs to. The RID exists once the body is in the tree.
        _ray.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_resetInterpolation)
        {
            ResetPhysicsInterpolation();
            _resetInterpolation = false;
        }

        float step = (float)delta;
        for (int i = 0; i < 4; i++)
        {
            // Visual only: a wheel pushed past its grip spins half as fast again, and a locked one stops.
            float spinSpeed = _wheelSpeed[i] * (_saturated[i] && _throttle > 0.0f ? 1.5f : 1.0f);
            if (i is RearLeft or RearRight)
            {
                spinSpeed *= 1.0f - HandbrakeAmount;
            }

            _looks!.Wheel(i, Droop(i), _wheelSteerDeg[i], spinSpeed, step);
        }

        // The lights: the brakes as the pedals resolved them, and reversing when the drive is backwards.
        _looks!.Lights(BrakeAmount, DriveForce < 0.0f);
    }

    /// <summary>How far a wheel hangs below its mount, metres: all its travel in the air, less its compression on the ground.</summary>
    public float Droop(int wheel) => _spec.SuspensionTravelM - (_grounded[wheel] ? _compression[wheel] : 0.0f);

    /// <summary>
    /// How many times the car has been put somewhere (R, the auto-respawn, a restart), counted as the move
    /// happens, so a ghost's recorder can mark the snapshot after it as a cut (M4b).
    /// </summary>
    public int Teleports { get; private set; }

    /// <summary>The car now, as a ghost keeps it (M4b): pose, steering, droop and pedals.</summary>
    public Sandrail.Core.Ghosts.Snapshot Snapshot()
    {
        Transform3D xf = GlobalTransform;
        Quaternion q = xf.Basis.Orthonormalized().GetRotationQuaternion();
        return new Sandrail.Core.Ghosts.Snapshot
        {
            Position = new System.Numerics.Vector3(xf.Origin.X, xf.Origin.Y, xf.Origin.Z),
            Rotation = new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W),
            SteerDeg = _steerDeg,
            Droop = new System.Numerics.Vector4(Droop(FrontLeft), Droop(FrontRight), Droop(RearLeft), Droop(RearRight)),
            Throttle = _throttle,
            Brake = BrakeAmount,
            Reversing = DriveForce < 0.0f,
            Handbrake = HandbrakeAmount,
        };
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        if (_pendingRespawn is Transform3D at)
        {
            state.Transform = at;
            state.LinearVelocity = -at.Basis.Z.Normalized() * _pendingSpeed;
            state.AngularVelocity = Vector3.Zero;
            Array.Clear(_compression);
            Array.Clear(_grounded);
            _throttle = 0.0f;
            _brake = 0.0f;
            _steerDeg = 0.0f;
            Array.Clear(_wheelSteerDeg);
            _lastVelocity = state.LinearVelocity;
            Acceleration = Vector3.Zero;
            _pendingRespawn = null;
            _resetInterpolation = true;
            Teleports++;
            return;
        }

        CarSpec spec = _spec;
        float dt = state.Step;
        Transform3D xf = state.Transform;
        Vector3 up = xf.Basis.Y.Normalized();
        Vector3 forward = -xf.Basis.Z.Normalized();
        Vector3 comWorld = xf.Origin + state.CenterOfMass;
        Vector3 velocity = state.LinearVelocity;
        float speed = velocity.Length();
        float forwardSpeed = velocity.Dot(forward);
        Acceleration = (velocity - _lastVelocity) / dt;
        _lastVelocity = velocity;

        DriverInput input = Driver;
        _throttle = Mathf.MoveToward(_throttle, Math.Clamp(input.Throttle, 0.0f, 1.0f), spec.PedalRatePerS * dt);
        _brake = Mathf.MoveToward(_brake, Math.Clamp(input.Brake, 0.0f, 1.0f), spec.PedalRatePerS * dt);

        SteerLimitDeg = WheelModel.SteerLimitDeg(spec, speed, input.WideLock);
        float steerTarget = Math.Clamp(input.Steer, -1.0f, 1.0f) * SteerLimitDeg;
        bool returning = MathF.Abs(steerTarget) < MathF.Abs(_steerDeg) || MathF.Sign(steerTarget) != MathF.Sign(_steerDeg);
        float steerRate = (returning ? spec.SteerReturnRateDegPerS : spec.SteerRateDegPerS)
            * WheelModel.SteerScale(spec, input.WideLock);
        _steerDeg = Mathf.MoveToward(_steerDeg, steerTarget, steerRate * dt);

        (float drive, float brakeAmount) = WheelModel.ResolvePedals(spec, _throttle, _brake, forwardSpeed);
        DriveForce = drive;
        BrakeAmount = brakeAmount;
        float rearLock = input.Handbrake ? spec.HandbrakeLock : 0.0f;
        HandbrakeAmount = rearLock;

        PhysicsDirectSpaceState3D space = state.GetSpaceState();
        float rayLength = spec.SuspensionTravelM + spec.WheelRadiusM;
        (_wheelSteerDeg[FrontLeft], _wheelSteerDeg[FrontRight]) = WheelModel.FrontWheelsDeg(spec, _steerDeg);
        int grounded = 0;

        for (int i = 0; i < 4; i++)
        {
            Vector3 mount = xf * _mounts[i];
            _ray.From = mount;
            _ray.To = mount - (up * rayLength);
            Godot.Collections.Dictionary hit = space.IntersectRay(_ray);

            if (hit.Count == 0)
            {
                _grounded[i] = false;
                _compression[i] = 0.0f;
                _load[i] = 0.0f;
                _slipDeg[i] = 0.0f;
                _saturated[i] = false;
                continue;
            }

            Vector3 point = hit["position"].AsVector3();
            Vector3 normal = hit["normal"].AsVector3();
            float x = Math.Clamp(rayLength - mount.DistanceTo(point), 0.0f, spec.SuspensionTravelM);
            float rate = (x - _compression[i]) / dt;
            _compression[i] = x;
            _grounded[i] = true;
            grounded++;

            float load = WheelModel.SuspensionForce(spec, x, rate);
            _load[i] = load;
            state.ApplyForce(up * load, mount - xf.Origin);

            bool front = i == FrontLeft || i == FrontRight;
            Vector3 heading = front ? forward.Rotated(up, -Mathf.DegToRad(_wheelSteerDeg[i])) : forward;
            Vector3 f = (heading - (normal * heading.Dot(normal))).Normalized();
            Vector3 right = f.Cross(normal);
            Vector3 v = state.GetVelocityAtLocalPosition(point - xf.Origin);
            float vf = v.Dot(f);
            float vs = v.Dot(right);
            _wheelSpeed[i] = vf;

            Surface surface = SurfaceOf(hit["collider"].AsGodotObject(), point);
            _surface[i] = surface;

            float driveShare = front ? (1.0f - spec.RearDriveShare) / 2.0f : spec.RearDriveShare / 2.0f;
            float brakeShare = front ? spec.BrakeFrontShare / 2.0f : (1.0f - spec.BrakeFrontShare) / 2.0f;
            TyreForces tyre = WheelModel.Tyre(
                spec, surface, load, vf, vs, drive * driveShare, brakeAmount * spec.BrakeForceN * brakeShare, dt,
                front ? 0.0f : rearLock);
            _slipDeg[i] = tyre.SlipAngleDeg;
            _saturated[i] = tyre.Saturated;

            // Raised toward the centre of mass by a fraction: the roll lever (M1-PLAN fact 5).
            Vector3 applyAt = point + (up * ((comWorld - point).Dot(up) * spec.GripForceHeight));
            state.ApplyForce((f * tyre.Forward) + (right * tyre.Side), applyAt - xf.Origin);
        }

        GroundedCount = grounded;
        ApplyAirControl(state, xf, up, forward, grounded == 0);

        ApplyAntiRoll(state, xf, up, FrontLeft, FrontRight, spec.AntiRollFrontNPerM);
        ApplyAntiRoll(state, xf, up, RearLeft, RearRight, spec.AntiRollRearNPerM);

        state.ApplyCentralForce(-spec.DragNPerMps2 * speed * velocity);

        // The wing, from where it stands (M1d), on the air it meets there: its own velocity in the
        // chassis's fore-and-aft and up-and-down plane. Drag goes along that, downforce across it toward
        // the floor. On the road that's down the chassis, loading the rear tyres through their springs;
        // in the air it turns the car to follow its arc.
        Vector3 wingAt = xf.Basis * new Vector3(0.0f, spec.WingHeightM, spec.WingBackM);
        Vector3 wingVelocity = state.GetVelocityAtLocalPosition(wingAt);
        float wingForward = wingVelocity.Dot(forward);
        float wingUp = wingVelocity.Dot(up);
        (float downforce, float wingDrag) = WheelModel.WingForces(spec, wingForward, wingUp);
        WingDownforce = downforce;
        WingDrag = wingDrag;
        if (downforce != 0.0f || wingDrag != 0.0f)
        {
            Vector3 flow = ((forward * wingForward) + (up * wingUp)).Normalized();
            Vector3 floorward = (-up + (flow * up.Dot(flow))).Normalized();
            state.ApplyForce((floorward * downforce) - (flow * wingDrag), wingAt);
        }
    }

    /// <summary>
    /// Air control (M1d), with no wheel on the ground: the pedals pitch the car (throttle nose up, brake
    /// nose down) and the steering turns and rolls it, by <see cref="WheelModel.AirControlStep"/>. It
    /// changes the spin directly rather than through a torque, so the car's mass and shape don't change
    /// how it answers. Uses the eased pedals and the steering angle, so a keyboard tap is gentle here too.
    /// </summary>
    private void ApplyAirControl(PhysicsDirectBodyState3D state, Transform3D xf, Vector3 up, Vector3 forward, bool inTheAir)
    {
        float pitch = inTheAir ? _throttle - _brake : 0.0f;
        float steer = inTheAir && SteerLimitDeg > 0.0f ? Math.Clamp(_steerDeg / SteerLimitDeg, -1.0f, 1.0f) : 0.0f;
        AirPitch = pitch;
        AirSteer = steer;
        if (pitch == 0.0f && steer == 0.0f)
        {
            return;
        }

        CarSpec spec = _spec;
        float step = state.Step;
        Vector3 spin = state.AngularVelocity;

        // Nose up is a positive turn about the car's right; a right turn is about its down; rolling the
        // right side down is about its forward.
        Vector3 right = xf.Basis.X.Normalized();
        Vector3 down = -up;
        spin += right * WheelModel.AirControlStep(pitch, spec.AirPitchDegPerS2, spin.Dot(right), spec.AirSpinCapDegPerS, step);
        spin += down * WheelModel.AirControlStep(steer, spec.AirYawDegPerS2, spin.Dot(down), spec.AirSpinCapDegPerS, step);
        spin += forward * WheelModel.AirControlStep(steer, spec.AirRollDegPerS2, spin.Dot(forward), spec.AirSpinCapDegPerS, step);
        state.AngularVelocity = spin;
    }

    private void ApplyAntiRoll(PhysicsDirectBodyState3D state, Transform3D xf, Vector3 up, int left, int right, float stiffness)
    {
        float force = WheelModel.AntiRollForce(stiffness, _compression[left], _compression[right]);
        if (_grounded[left])
        {
            state.ApplyForce(up * force, (xf * _mounts[left]) - xf.Origin);
        }

        if (_grounded[right])
        {
            state.ApplyForce(-up * force, (xf * _mounts[right]) - xf.Origin);
        }
    }

    /// <summary>
    /// The surface a collider is tagged with (<c>surface</c> metadata), road if it has none; for painted
    /// ground, the surface at the point the wheel touches.
    /// </summary>
    private Surface SurfaceOf(GodotObject collider, Vector3 at)
    {
        ulong id = collider.GetInstanceId();
        if (_surfaceCache.TryGetValue(id, out Surface? known))
        {
            return known ?? PaintedAt(at);
        }

        Surface surface = Surfaces.Road;
        if (collider.HasMeta("surface"))
        {
            string name = collider.GetMeta("surface").AsString();
            if (name == World.TerrainView.PaintedTag)
            {
                _surfaceCache[id] = null;
                return PaintedAt(at);
            }

            Surface? found = Surfaces.Find(name);
            if (found is null)
            {
                GD.PushWarning($"Unknown surface '{name}' on {collider}; driving it as road.");
            }

            surface = found ?? Surfaces.Road;
        }

        _surfaceCache[id] = surface;
        return surface;
    }

    private Surface PaintedAt(Vector3 at) => PaintedSurface?.Invoke(at) ?? Surfaces.Sand;
}
