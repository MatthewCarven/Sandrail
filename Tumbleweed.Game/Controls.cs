using Godot;

namespace Tumbleweed.Game;

/// <summary>
/// The input actions, registered in code rather than hand-written into <c>project.godot</c>, where
/// each one is a serialised object nobody can read (M1-PLAN decision 1).
/// </summary>
/// <remarks>
/// Keys are bound by physical position, so WASD is WASD on any layout. <b>The pad bindings are
/// untested</b>: there's no pad here yet, which is an owed check, not a finished one.
/// </remarks>
public static class Controls
{
    public const string Throttle = "throttle";
    public const string Brake = "brake";
    public const string SteerLeft = "steer_left";
    public const string SteerRight = "steer_right";
    public const string Handbrake = "handbrake";
    public const string Respawn = "respawn";
    public const string Camera = "camera";

    public static void Register()
    {
        Add(Throttle, Key.W, Key.Up, JoyAxis.TriggerRight, 1.0f);
        Add(Brake, Key.S, Key.Down, JoyAxis.TriggerLeft, 1.0f);
        Add(SteerLeft, Key.A, Key.Left, JoyAxis.LeftX, -1.0f);
        Add(SteerRight, Key.D, Key.Right, JoyAxis.LeftX, 1.0f);
        Add(Handbrake, Key.Space, JoyButton.A);
        Add(Respawn, Key.R, JoyButton.Y);
        Add(Camera, Key.C, JoyButton.Back);
    }

    private static void Add(string action, Key key, Key alternate, JoyAxis axis, float direction)
    {
        Begin(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = alternate });
        InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = direction });
    }

    private static void Add(string action, Key key, JoyButton button)
    {
        Begin(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
    }

    private static void Begin(string action)
    {
        if (InputMap.HasAction(action))
        {
            InputMap.EraseAction(action);
        }

        InputMap.AddAction(action, 0.15f);
    }
}
