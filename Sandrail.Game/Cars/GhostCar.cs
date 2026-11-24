using System;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Core.Ghosts;

namespace Sandrail.Game.Cars;

/// <summary>
/// A ghost beside you (M4-PLAN, M4c): a best run's car, see-through, with no body and no collider, posed
/// from its snapshots at the race clock's time. It wears the car the run was driven on, wing and all.
/// </summary>
/// <remarks>
/// <para>
/// <b>On the race clock</b> (fact 6): before the clock starts it waits on the line; R back to a checkpoint
/// leaves it where its run was, so you see what the crash cost; a restart puts it on the line again. Once its
/// run is over it fades out at the finish.
/// </para>
/// <para>
/// <b>It fades as it nears the camera</b>, so a ghost behind you never blocks the road ahead.
/// </para>
/// </remarks>
public partial class GhostCar : Node3D
{
    /// <summary>How see-through a ghost is: this much of it shows.</summary>
    public const float Opacity = 0.4f;

    /// <summary>Within this of the camera it's gone, and from <see cref="ClearM"/> on it's all there.</summary>
    private const float NearM = 3.0f;

    private const float ClearM = 7.0f;

    /// <summary>How long it takes to fade out after its finish, seconds.</summary>
    private const float FadeOutSeconds = 1.0f;

    private CarLooks? _looks;
    private float _shownOpacity = -1.0f;

    /// <summary>The run it plays; null shows nothing.</summary>
    public Ghost? Run { get; private set; }

    public override void _Ready()
    {
        // Posed every frame from the clock, not moved by physics: Godot mustn't interpolate it as well.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
    }

    /// <summary>Plays a run, or nothing: the looks rebuilt for the run's own car.</summary>
    public void Play(Ghost? run)
    {
        Run = run;
        Visible = run is not null;
        if (run is null)
        {
            return;
        }

        if (_looks is null)
        {
            _looks = new CarLooks();
            AddChild(_looks);
        }

        _looks.Show(run.Spec);
        _shownOpacity = -1.0f;
    }

    /// <summary>
    /// Poses it at a time on the race clock, its wheels spun on by <paramref name="seconds"/> of rolling, and
    /// fades it by how near it is to <paramref name="camera"/> and how long ago its run ended.
    /// </summary>
    public void ShowAt(double clock, float seconds, Camera3D? camera)
    {
        if (Run is not Ghost run || _looks is null)
        {
            return;
        }

        Snapshot s = run.At(clock);
        var at = new Vector3(s.Position.X, s.Position.Y, s.Position.Z);
        var turn = new Quaternion(s.Rotation.X, s.Rotation.Y, s.Rotation.Z, s.Rotation.W).Normalized();
        GlobalTransform = new Transform3D(new Basis(turn), at);

        CarSpec spec = run.Spec;
        (float left, float right) = WheelModel.FrontWheelsDeg(spec, s.SteerDeg);
        Vector3 forward = -GlobalBasis.Z;
        System.Numerics.Vector3 v = run.VelocityAt(clock);
        float rolling = new Vector3(v.X, v.Y, v.Z).Dot(forward);
        float rear = rolling * (1.0f - s.Handbrake);
        _looks.Wheel(0, s.Droop.X, left, rolling, seconds);
        _looks.Wheel(1, s.Droop.Y, right, rolling, seconds);
        _looks.Wheel(2, s.Droop.Z, 0.0f, rear, seconds);
        _looks.Wheel(3, s.Droop.W, 0.0f, rear, seconds);

        float near = camera is null ? 1.0f : Mathf.SmoothStep(NearM, ClearM, camera.GlobalPosition.DistanceTo(at));
        float over = (float)Math.Clamp(1.0 - ((clock - run.Duration) / FadeOutSeconds), 0.0, 1.0);
        float opacity = Opacity * near * over;
        Visible = opacity > 0.01f;
        if (MathF.Abs(opacity - _shownOpacity) > 0.01f)
        {
            _shownOpacity = opacity;
            _looks.Fade(MathF.Max(opacity, 0.01f));
        }
    }
}
