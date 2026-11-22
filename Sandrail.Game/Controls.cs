using Godot;
using Sandrail.Core.Pads;

namespace Sandrail.Game;

/// <summary>
/// The input actions, registered in code rather than hand-written into <c>project.godot</c>, where
/// each one is a serialised object nobody can read (M1-PLAN decision 1).
/// </summary>
/// <remarks>
/// <para>
/// Keys are bound by physical position, so WASD is WASD on any layout. The default pad bindings
/// assume a standard layout, which a real PlayStation or Xbox pad has.
/// </para>
/// <para>
/// <b>A pad with a saved layout gets that instead.</b> The binding run (<c>--bindpad</c>) records what a
/// pad actually sends for each control into <c>user://pads/&lt;guid&gt;.json</c>; when a pad with that GUID
/// is connected, its layout replaces every default pad binding, so a clone whose button numbers don't
/// match their labels can't fire the wrong control. One pad at a time: the layout's bindings answer to
/// any pad, which is right for one player.
/// </para>
/// </remarks>
public static class Controls
{
    public const string PadFolder = "user://pads";

    private static bool _listening;

    /// <summary>The layout loaded for a connected pad, if any.</summary>
    public static PadProfile? Layout { get; private set; }

    /// <summary>The actions a pad drives, and so the ones a layout rebinds.</summary>
    public static readonly string[] PadActions =
        [SteerLeft, SteerRight, Throttle, Brake, WideLock, Handbrake, Respawn, Camera];

    public const string Throttle = "throttle";
    public const string Brake = "brake";
    public const string SteerLeft = "steer_left";
    public const string SteerRight = "steer_right";
    public const string Handbrake = "handbrake";
    public const string WideLock = "wide_lock";
    public const string Respawn = "respawn";
    public const string Camera = "camera";
    public const string Tuning = "tuning";
    public const string Telemetry = "telemetry";
    public const string Record = "record";

    public static void Register()
    {
        Add(Throttle, Key.W, Key.Up, JoyAxis.TriggerRight, 1.0f);
        Add(Brake, Key.S, Key.Down, JoyAxis.TriggerLeft, 1.0f);
        Add(SteerLeft, Key.A, Key.Left, JoyAxis.LeftX, -1.0f);
        Add(SteerRight, Key.D, Key.Right, JoyAxis.LeftX, 1.0f);
        Add(Handbrake, Key.Space, JoyButton.A);

        // Shift with no side given matches either Shift key.
        Add(WideLock, Key.Shift, JoyButton.LeftShoulder);
        Add(Respawn, Key.R, JoyButton.Y);
        Add(Camera, Key.C, JoyButton.Back);

        // The tuning tools are keyboard only: they're for the desk, not the sofa.
        Add(Tuning, Key.F2);
        Add(Telemetry, Key.F3);
        Add(Record, Key.F4);

        Key[] numbers = [Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6];
        for (int i = 0; i < numbers.Length; i++)
        {
            Add(Station(i + 1), numbers[i]);
        }

        Layout = ApplyPadLayout();
        if (!_listening)
        {
            // A pad plugged in later gets its layout too: register afresh, which applies it.
            Input.JoyConnectionChanged += (_, _) => Register();
            _listening = true;
        }
    }

    /// <summary>The action that puts the car at a test-ground station: 1 to 5.</summary>
    public static string Station(int number) => $"station_{number}";

    /// <summary>Where a pad's layout lives, by its GUID.</summary>
    public static string LayoutPath(string guid) => $"{PadFolder}/{PadProfile.FileName(guid)}";

    /// <summary>Loads a pad's saved layout, or null if it has none or the file is refused.</summary>
    public static PadProfile? LoadLayout(string guid)
    {
        string path;
        try
        {
            path = LayoutPath(guid);
        }
        catch (System.ArgumentException)
        {
            return null;
        }

        if (!FileAccess.FileExists(path))
        {
            return null;
        }

        try
        {
            return PadProfile.Parse(FileAccess.GetFileAsString(path));
        }
        catch (System.IO.InvalidDataException e)
        {
            GD.PushWarning($"{path}: {e.Message}. Using the default pad bindings.");
            return null;
        }
    }

    public static void SaveLayout(PadProfile layout)
    {
        DirAccess.MakeDirRecursiveAbsolute(PadFolder);
        using FileAccess? file = FileAccess.Open(LayoutPath(layout.Guid), FileAccess.ModeFlags.Write);
        file?.StoreString(layout.ToJson() + "\n");
    }

    /// <summary>
    /// For the first connected pad with a saved layout: takes every pad event off the pad actions and adds
    /// the layout's. Keys are left alone.
    /// </summary>
    private static PadProfile? ApplyPadLayout()
    {
        foreach (int device in Input.GetConnectedJoypads())
        {
            PadProfile? layout = LoadLayout(Input.GetJoyGuid(device));
            if (layout is null)
            {
                continue;
            }

            foreach (string action in PadActions)
            {
                foreach (InputEvent e in InputMap.ActionGetEvents(action))
                {
                    if (e is InputEventJoypadButton or InputEventJoypadMotion)
                    {
                        InputMap.ActionEraseEvent(action, e);
                    }
                }
            }

            foreach (PadBinding b in layout.Bindings)
            {
                InputEvent bound = b.Kind == PadBinding.Axis
                    ? new InputEventJoypadMotion { Axis = (JoyAxis)b.Index, AxisValue = b.Direction }
                    : new InputEventJoypadButton { ButtonIndex = (JoyButton)b.Index };
                InputMap.ActionAddEvent(b.Action, bound);
            }

            GD.Print($"Pad layout for \"{layout.Name}\" loaded: {layout.Bindings.Count} controls from {LayoutPath(layout.Guid)}");
            return layout;
        }

        return null;
    }

    private static void Add(string action, Key key)
    {
        Begin(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
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
