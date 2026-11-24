using System.Linq;
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
    public const string Autodrive = "autodrive";
    public const string Bolder = "autodrive_bolder";
    public const string Calmer = "autodrive_calmer";
    public const string ShowLine = "show_line";
    public const string Menu = "menu";
    public const string ChooseGhost = "choose_ghost";
    public const string TestDrive = "test_drive";
    public const string Slower = "time_slower";
    public const string Faster = "time_faster";
    public const string Watch = "watch";
    public const string ReplayPause = "replay_pause";
    public const string ReplayBack = "replay_back";
    public const string ReplayForward = "replay_forward";
    public const string ReplayFaster = "replay_faster";
    public const string ReplaySlower = "replay_slower";

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
        Add(Autodrive, Key.P);
        Add(ShowLine, Key.L);

        // The autodriver's aggression, - and + (the = key), on the number pad too, and the D-pad, which no
        // pad layout rebinds: watching it lap is a sofa thing as much as a desk one.
        Add(Bolder, Key.Equal, Key.KpAdd, JoyButton.DpadUp);
        Add(Calmer, Key.Minus, Key.KpSubtract, JoyButton.DpadDown);

        Key[] numbers = [Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7, Key.Key8, Key.Key9];
        for (int i = 0; i < numbers.Length; i++)
        {
            Add(Station(i + 1), numbers[i]);
        }

        // [ and ] step through the stations, for the showroom's more than nine (M2b).
        Add(PreviousStation, Key.Bracketleft);
        Add(NextStation, Key.Bracketright);

        // The Esc menu (M3e): keyboard only, since a pad's Start button may be a layout's, as his is.
        Add(Menu, Key.Escape);

        // Whose ghost (M4c): his best, the autodriver's, none. Keyboard only, as the menu is.
        Add(ChooseGhost, Key.G);

        // Tab (M5c): from a test drive back to the editor. The editor reads Tab itself, to leave for one.
        Add(TestDrive, Key.Tab);

        // How fast time goes (his ask, 2026-11-24, to make tuning more fun): , and . (< and >), and the D-pad's
        // left and right, which no pad layout rebinds, beside its up and down for the autodriver.
        Add(Slower, Key.Comma, JoyButton.DpadLeft);
        Add(Faster, Key.Period, JoyButton.DpadRight);

        // Replays (M4d): Enter watches the last run; in a replay, Space pauses, the arrows skip and change
        // its speed. They share keys with driving, which nothing does during a replay.
        Add(Watch, Key.Enter);
        Add(ReplayPause, Key.Space);
        Add(ReplayBack, Key.Left);
        Add(ReplayForward, Key.Right);
        Add(ReplayFaster, Key.Up);
        Add(ReplaySlower, Key.Down);

        // On a track, the run from the start again (M2d); pad layouts don't rebind it.
        Add(Restart, Key.Backspace, JoyButton.Start);

        Layout = ApplyPadLayout();
        if (!_listening)
        {
            // A pad plugged in later gets its layout too: register afresh, which applies it.
            Input.JoyConnectionChanged += (_, _) => Register();
            _listening = true;
        }
    }

    /// <summary>The action that puts the car at a station: 1 to 9.</summary>
    public static string Station(int number) => $"station_{number}";

    /// <summary>The highest station a number key reaches.</summary>
    public const int NumberedStations = 9;

    public const string PreviousStation = "station_previous";
    public const string NextStation = "station_next";
    public const string Restart = "restart";

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

            // A button the layout uses is the layout's. Any other action's default on that button comes off,
            // or a control added since the layout was saved can land on one of its buttons: restart's default,
            // the Start button, is his pad's button 6, which his layout made wide lock, so wide lock started
            // the run again (2026-11-23).
            var taken = layout.Bindings.Where(b => b.Kind != PadBinding.Axis).Select(b => b.Index).ToHashSet();
            foreach (StringName action in InputMap.GetActions())
            {
                if (PadActions.Contains(action.ToString()))
                {
                    continue;
                }

                foreach (InputEvent e in InputMap.ActionGetEvents(action))
                {
                    if (e is InputEventJoypadButton button && taken.Contains((int)button.ButtonIndex))
                    {
                        InputMap.ActionEraseEvent(action, e);
                    }
                }
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

    private static void Add(string action, Key key, Key alternate, JoyButton button)
    {
        Add(action, key, button);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = alternate });
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
