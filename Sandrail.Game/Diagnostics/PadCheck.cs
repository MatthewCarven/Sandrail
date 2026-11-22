using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Sandrail.Core.Pads;

namespace Sandrail.Game.Diagnostics;

/// <summary>
/// <c>--padcheck</c>: a sensor run on a gamepad. It lists every pad Godot can see, then records each axis's
/// range and each button's presses while the player works the pad, checks the game's own controls against
/// them, and writes the lot to <c>user://padcheck.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// Built because the pad bindings were written with no pad to test on (M1-PLAN decision 1), and Claude
/// can't hold one: the player does the moving, the report does the seeing.
/// </para>
/// <para>
/// The 30 seconds start at the pad's first touch, so launching it from another room costs nothing. The
/// last three seconds ask for hands off, and what the sticks read then is their drift at rest, which is
/// what the dead zone has to cover. Button names are a PlayStation pad's, with Godot's own in brackets.
/// </para>
/// </remarks>
public partial class PadCheck : CanvasLayer
{
    private const int AxisCount = 10;
    private const int ButtonCount = 21;
    private const double HandsOff = 3.0;
    private const double GiveUpAfter = 180.0;

    private static readonly string[] AxisNames =
    [
        "left stick X", "left stick Y", "right stick X", "right stick Y", "L2 trigger", "R2 trigger",
        "axis 6", "axis 7", "axis 8", "axis 9",
    ];

    private static readonly string[] ButtonNames =
    [
        "Cross (A)", "Circle (B)", "Square (X)", "Triangle (Y)", "Share/Create (Back)", "PS (Guide)",
        "Options (Start)", "L3 (left stick)", "R3 (right stick)", "L1 (left shoulder)", "R1 (right shoulder)",
        "D-pad up", "D-pad down", "D-pad left", "D-pad right", "Mute (Misc1)",
        "Paddle 1", "Paddle 2", "Paddle 3", "Paddle 4", "Touchpad click",
    ];

    private static readonly (string Action, string What)[] Checks =
    [
        (Controls.SteerLeft, "steer left"),
        (Controls.SteerRight, "steer right"),
        (Controls.Throttle, "throttle"),
        (Controls.Brake, "brake"),
        (Controls.WideLock, "wide lock"),
        (Controls.Handbrake, "handbrake"),
        (Controls.Respawn, "respawn"),
        (Controls.Camera, "camera"),
    ];

    private readonly SortedDictionary<int, Pad> _pads = new();
    private readonly Dictionary<string, float> _actionPeak = new();
    private readonly List<string> _connections = new();
    private Label _label = null!;
    private double _waited;
    private double? _left;

    /// <summary>How long to record after the first touch.</summary>
    public double Seconds { get; init; } = 30.0;

    public override void _Ready()
    {
        _label = new Label
        {
            Position = new Vector2(32.0f, 24.0f),
            LabelSettings = new LabelSettings
            {
                Font = new SystemFont { FontNames = ["Consolas", "Cascadia Mono", "Courier New", "monospace"] },
                FontSize = 18,
                OutlineSize = 4,
                OutlineColor = Colors.Black,
            },
        };
        AddChild(_label);

        Input.JoyConnectionChanged += OnConnectionChanged;
        foreach (int device in Input.GetConnectedJoypads())
        {
            Track(device);
        }

        foreach ((string action, _) in Checks)
        {
            _actionPeak[action] = 0.0f;
        }
    }

    public override void _ExitTree() => Input.JoyConnectionChanged -= OnConnectionChanged;

    public override void _Process(double delta)
    {
        bool touched = false;
        foreach ((int device, Pad pad) in _pads)
        {
            touched |= pad.Sample(device, _left is double l && l <= HandsOff);
        }

        if (_left is null)
        {
            _waited += delta;
            if (touched)
            {
                _left = Seconds + HandsOff;
            }
            else if (_waited > GiveUpAfter)
            {
                Finish("gave up: no pad input in three minutes");
                return;
            }
        }
        else
        {
            _left -= delta;
            if (_left > HandsOff)
            {
                foreach ((string action, _) in Checks)
                {
                    _actionPeak[action] = MathF.Max(_actionPeak[action], Input.GetActionStrength(action));
                }
            }

            if (_left <= 0.0)
            {
                Finish("complete");
                return;
            }
        }

        // AppendLine ends lines with a carriage return too on Windows, which a label draws as a blank line.
        _label.Text = Screen().Replace("\r\n", "\n");
    }

    private void OnConnectionChanged(long device, bool connected)
    {
        _connections.Add($"{(connected ? "connected" : "DISCONNECTED")}: pad {device} after {_waited:F0} s");
        if (connected)
        {
            Track((int)device);
        }
    }

    private void Track(int device)
    {
        if (!_pads.ContainsKey(device))
        {
            _pads[device] = new Pad(device);
        }
    }

    private string Screen()
    {
        var text = new StringBuilder();
        text.AppendLine("PAD CHECK");
        text.AppendLine();
        if (_pads.Count == 0)
        {
            text.AppendLine("No pad connected. Plug it in by USB or pair it by Bluetooth.");
            text.AppendLine("If Steam is open, it can hide a PlayStation pad from games: close it and try again.");
            text.AppendLine($"Waiting... ({GiveUpAfter - _waited:F0} s)");
            return text.ToString();
        }

        if (_left is null)
        {
            text.AppendLine("Press any button on the pad to start.");
        }
        else if (_left > HandsOff)
        {
            text.AppendLine($"{_left - HandsOff:F0} s left. Work everything:");
            text.AppendLine("  both sticks all the way round, slowly");
            text.AppendLine("  L2 and R2 all the way in and out");
            text.AppendLine("  every button once: shapes, L1 R1, L3 R3, D-pad, Share, Options, PS, touchpad");
        }
        else
        {
            text.AppendLine($"HANDS OFF THE PAD NOW ({_left:F0} s)");
        }

        text.AppendLine();
        foreach ((int device, Pad pad) in _pads)
        {
            text.AppendLine($"pad {device}: {pad.Name}");
            for (int a = 0; a < AxisCount; a++)
            {
                if (pad.Moved(a))
                {
                    text.AppendLine($"  {AxisNames[a],-14} now {Input.GetJoyAxis(device, (JoyAxis)a),6:F2}   seen {pad.Min[a],6:F2} to {pad.Max[a],5:F2}");
                }
            }

            string pressed = string.Join(", ", Enumerable.Range(0, ButtonCount).Where(b => pad.Presses[b] > 0).Select(b => ButtonNames[b]));
            text.AppendLine($"  pressed so far: {(pressed.Length > 0 ? pressed : "nothing")}");
        }

        return text.ToString();
    }

    private void Finish(string how)
    {
        var report = new StringBuilder();
        report.AppendLine($"Pad check, {DateTime.Now:yyyy-MM-dd HH:mm}: {how}.");
        report.AppendLine($"Godot {Engine.GetVersionInfo()["string"]}, {OS.GetName()}.");
        foreach (string line in _connections)
        {
            report.AppendLine(line);
        }

        if (_pads.Count == 0)
        {
            report.AppendLine("No pad was ever connected.");
        }

        foreach ((int device, Pad pad) in _pads)
        {
            report.AppendLine();
            report.AppendLine($"pad {device}: \"{pad.Name}\"");
            report.AppendLine($"  guid {pad.Guid}");
            report.AppendLine($"  info {pad.Info}");
            report.AppendLine("  axes (seen while working it | drift with hands off):");
            for (int a = 0; a < AxisCount; a++)
            {
                string moved = pad.Moved(a) ? $"{pad.Min[a],6:F3} to {pad.Max[a],6:F3}" : "   never moved   ";
                report.AppendLine($"    {AxisNames[a],-14} {moved} | {pad.Drift[a]:F3}");
            }

            report.AppendLine("  buttons (presses):");
            for (int b = 0; b < ButtonCount; b++)
            {
                report.AppendLine($"    {ButtonNames[b],-22} {pad.Presses[b]}");
            }
        }

        report.AppendLine();
        report.AppendLine(Controls.Layout is PadProfile layout
            ? $"the game's controls, using the saved layout for \"{layout.Name}\" (strongest reading while working the pad):"
            : "the game's controls, using the default pad bindings (strongest reading while working the pad):");
        foreach ((string action, string what) in Checks)
        {
            float peak = _actionPeak[action];
            string verdict = peak >= 0.95f ? "OK" : peak > 0.0f ? "PARTIAL" : "NEVER";
            report.AppendLine($"  {what,-32} {peak:F2}  {verdict}");
        }

        string text = report.ToString();
        using (FileAccess? file = FileAccess.Open("user://padcheck.txt", FileAccess.ModeFlags.Write))
        {
            file?.StoreString(text);
        }

        GD.Print(text);
        GD.Print($"padcheck written to {ProjectSettings.GlobalizePath("user://padcheck.txt")}");
        SetProcess(false);
        GetTree().Quit();
    }

    private sealed class Pad
    {
        public Pad(int device)
        {
            Name = Input.GetJoyName(device);
            Guid = Input.GetJoyGuid(device);
            Info = string.Join(", ", Input.GetJoyInfo(device).Select(kv => $"{kv.Key}={kv.Value}"));
            Array.Fill(Min, float.MaxValue);
            Array.Fill(Max, float.MinValue);
            for (int a = 0; a < AxisCount; a++)
            {
                Start[a] = Input.GetJoyAxis(device, (JoyAxis)a);
            }
        }

        public string Name { get; }

        public string Guid { get; }

        public string Info { get; }

        public float[] Start { get; } = new float[AxisCount];

        public float[] Min { get; } = new float[AxisCount];

        public float[] Max { get; } = new float[AxisCount];

        public float[] Drift { get; } = new float[AxisCount];

        public int[] Presses { get; } = new int[ButtonCount];

        private bool[] Down { get; } = new bool[ButtonCount];

        public bool Moved(int axis) => Max[axis] - Min[axis] > 0.05f;

        /// <summary>Reads the pad once; true if anything is being touched.</summary>
        public bool Sample(int device, bool handsOff)
        {
            bool touched = false;
            for (int a = 0; a < AxisCount; a++)
            {
                float value = Input.GetJoyAxis(device, (JoyAxis)a);
                if (handsOff)
                {
                    Drift[a] = MathF.Max(Drift[a], MathF.Abs(value - (a is 4 or 5 ? Start[a] : 0.0f)));
                    continue;
                }

                Min[a] = MathF.Min(Min[a], value);
                Max[a] = MathF.Max(Max[a], value);
                touched |= MathF.Abs(value - Start[a]) > 0.5f;
            }

            for (int b = 0; b < ButtonCount; b++)
            {
                bool down = Input.IsJoyButtonPressed(device, (JoyButton)b);
                if (down && !Down[b] && !handsOff)
                {
                    Presses[b]++;
                }

                Down[b] = down;
                touched |= down;
            }

            return touched;
        }
    }
}
