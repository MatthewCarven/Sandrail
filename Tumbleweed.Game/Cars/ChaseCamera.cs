using System;
using Godot;

namespace Tumbleweed.Game.Cars;

/// <summary>
/// Behind and above the car, or on its bonnet; C switches.
/// </summary>
/// <remarks>
/// <para>
/// <b>The camera's direction is smoothed, not its position</b>, so it never falls further behind as
/// the car speeds up: it sits a fixed distance back along a heading and an up that each chase the
/// car's own. The car's up is followed, not the world's, so in a loop the camera goes round with
/// you (M1-PLAN decision 3); on ordinary ground the smoothed up is nearly the world's anyway.
/// </para>
/// <para>
/// It reads the car's <em>interpolated</em> transform and moves in <c>_Process</c>, with its own
/// physics interpolation off, because physics ticks at 120 Hz and the screen at whatever it likes
/// (M1-PLAN fact 2).
/// </para>
/// </remarks>
public partial class ChaseCamera : Camera3D
{
    private const float Distance = 6.5f;
    private const float Height = 2.2f;
    private const float LookHeight = 0.8f;
    private const float HeadingStiffness = 5.0f;
    private const float UpStiffness = 3.0f;
    private const float FovAtRest = 70.0f;
    private const float FovAtTopSpeed = 85.0f;
    private const float TopSpeed = 50.0f;

    private Vector3 _heading = Vector3.Forward;
    private Vector3 _up = Vector3.Up;
    private bool _bonnet;
    private bool _placed;

    public Buggy? Target { get; set; }

    public void ToggleView() => _bonnet = !_bonnet;

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        Current = true;
        Near = 0.1f;
        Far = 2000.0f;
    }

    public override void _Process(double delta)
    {
        if (Target is null)
        {
            return;
        }

        Transform3D car = Target.GetGlobalTransformInterpolated();
        Vector3 carUp = car.Basis.Y.Normalized();
        Vector3 carForward = -car.Basis.Z.Normalized();
        float dt = (float)delta;

        Fov = Mathf.Lerp(FovAtRest, FovAtTopSpeed, Math.Clamp(Target.LinearVelocity.Length() / TopSpeed, 0.0f, 1.0f));

        if (_bonnet)
        {
            GlobalTransform = car * new Transform3D(Basis.Identity, new Vector3(0.0f, 0.9f, -0.4f));
            return;
        }

        if (!_placed)
        {
            _heading = carForward;
            _up = carUp;
            _placed = true;
        }

        _up = _up.Slerp(carUp, 1.0f - MathF.Exp(-UpStiffness * dt)).Normalized();

        Vector3 flat = carForward - (_up * carForward.Dot(_up));
        if (flat.LengthSquared() > 1e-6f)
        {
            _heading = _heading.Slerp(flat.Normalized(), 1.0f - MathF.Exp(-HeadingStiffness * dt)).Normalized();
        }

        Vector3 target = car.Origin + (_up * LookHeight);
        GlobalPosition = car.Origin - (_heading * Distance) + (_up * Height);

        Vector3 look = target - GlobalPosition;
        if (look.LengthSquared() > 1e-6f && MathF.Abs(look.Normalized().Dot(_up)) < 0.999f)
        {
            LookAt(target, _up);
        }
    }
}
