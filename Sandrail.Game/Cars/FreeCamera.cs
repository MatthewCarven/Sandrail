using System;
using Godot;

namespace Sandrail.Game.Cars;

/// <summary>
/// A replay's free camera (M4-PLAN, M4d): fly anywhere and look at anything. Hold the right mouse button and
/// move the mouse to look; W, A, S and D to move, Q and E down and up, Shift for four times the speed.
/// </summary>
/// <remarks>The keys are read as keys, not the driving actions, since nothing is driven in a replay.</remarks>
public partial class FreeCamera : Camera3D
{
    private const float MetresASecond = 15.0f;
    private const float RadiansAPixel = 0.004f;

    private float _yaw;
    private float _pitch;

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        Near = 0.1f;
        Far = 2000.0f;
    }

    /// <summary>Takes over from another camera, from where it is and the way it looks.</summary>
    public void TakeOver(Camera3D from)
    {
        GlobalTransform = from.GlobalTransform;
        Fov = from.Fov;
        Vector3 euler = GlobalBasis.GetEuler();
        _pitch = euler.X;
        _yaw = euler.Y;
        Current = true;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Current || e is not InputEventMouseMotion motion || !Input.IsMouseButtonPressed(MouseButton.Right))
        {
            return;
        }

        _yaw -= motion.Relative.X * RadiansAPixel;
        _pitch = Math.Clamp(_pitch - (motion.Relative.Y * RadiansAPixel), -1.5f, 1.5f);
        Rotation = new Vector3(_pitch, _yaw, 0.0f);
    }

    public override void _Process(double delta)
    {
        if (!Current)
        {
            return;
        }

        var move = Vector3.Zero;
        move += Input.IsKeyPressed(Key.W) ? Vector3.Forward : Vector3.Zero;
        move += Input.IsKeyPressed(Key.S) ? Vector3.Back : Vector3.Zero;
        move += Input.IsKeyPressed(Key.A) ? Vector3.Left : Vector3.Zero;
        move += Input.IsKeyPressed(Key.D) ? Vector3.Right : Vector3.Zero;
        move += Input.IsKeyPressed(Key.E) ? Vector3.Up : Vector3.Zero;
        move += Input.IsKeyPressed(Key.Q) ? Vector3.Down : Vector3.Zero;
        if (move == Vector3.Zero)
        {
            return;
        }

        float speed = MetresASecond * (Input.IsKeyPressed(Key.Shift) ? 4.0f : 1.0f);
        GlobalPosition += GlobalBasis * move.Normalized() * speed * (float)delta;
    }
}
