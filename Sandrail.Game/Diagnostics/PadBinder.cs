using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Sandrail.Core.Pads;

namespace Sandrail.Game.Diagnostics;

/// <summary>
/// <c>--bindpad</c>: asks for each game control on the pad in turn, records what the pad sends, and saves it
/// as that pad's layout (<see cref="PadProfile"/>), which the game loads in place of the default pad
/// bindings from then on.
/// </summary>
/// <remarks>
/// <para>
/// Built for his mum's clone pad, whose button numbers don't match their labels and whose triggers are
/// plain buttons (worklog, 2026-11-22), but it binds any pad. It reads the pad the way the game will,
/// through Godot's axis and button numbers, so what it saves is exactly what the actions will match.
/// </para>
/// <para>
/// Each control waits up to <see cref="PromptSeconds"/>; one not answered is skipped and has no pad
/// binding. An input already given to another control is refused, except a stick's other direction,
/// which is how one stick steers both ways. Hands off between prompts, so one push can't answer two.
/// </para>
/// </remarks>
public partial class PadBinder : CanvasLayer
{
    private const int AxisCount = 10;
    private const int ButtonCount = 64;
    private const float Pushed = 0.6f;
    private const float Released = 0.3f;
    private const double PromptSeconds = 20.0;
    private const double RestSeconds = 1.5;

    private static readonly (string Action, string Title, string Ask)[] Prompts =
    [
        (Controls.SteerLeft, "STEER LEFT", "push the stick you'll steer with all the way LEFT"),
        (Controls.SteerRight, "STEER RIGHT", "push the same stick all the way RIGHT"),
        (Controls.Throttle, "THROTTLE", "push the right stick UP for analogue throttle (or press a button instead)"),
        (Controls.Brake, "BRAKE AND REVERSE", "pull the right stick DOWN (or press a button instead)"),
        (Controls.WideLock, "WIDE LOCK", "press the button you want for it, L1 say"),
        (Controls.Handbrake, "HANDBRAKE", "press the button you want for it, R1 say"),
        (Controls.Respawn, "RESPAWN", "press the button you want for it, Triangle say"),
        (Controls.Camera, "CAMERA", "press the button you want for it, Select say"),
    ];

    private readonly float[] _rest = new float[AxisCount];
    private readonly List<PadBinding> _bindings = new();
    private readonly List<string> _skipped = new();
    private Label _label = null!;
    private Stage _stage = Stage.WaitingForPad;
    private int _device = -1;
    private int _prompt;
    private bool _started;
    private double _clock;
    private string _note = string.Empty;

    private enum Stage
    {
        WaitingForPad,
        WaitingForTouch,
        Resting,
        Asking,
        LetGo,
        Done,
    }

    public override void _Ready()
    {
        _label = new Label
        {
            Position = new Vector2(32.0f, 24.0f),
            LabelSettings = new LabelSettings
            {
                Font = new SystemFont { FontNames = ["Consolas", "Cascadia Mono", "Courier New", "monospace"] },
                FontSize = 20,
                OutlineSize = 4,
                OutlineColor = Colors.Black,
            },
        };
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        switch (_stage)
        {
            case Stage.WaitingForPad:
                if (Input.GetConnectedJoypads().Count > 0)
                {
                    _device = Input.GetConnectedJoypads()[0];
                    Enter(Stage.WaitingForTouch);
                }
                else if (_clock > 180.0)
                {
                    GD.Print("bindpad: no pad connected in three minutes; nothing saved");
                    GetTree().Quit();
                }

                break;

            case Stage.WaitingForTouch:
                // Nothing starts until the player is at the pad: the first run timed out every prompt on
                // an empty room (2026-11-22). Any button will do; then hands off before the rest is read.
                if (AnyButton())
                {
                    _note = "let go, and keep your hands off for a moment";
                    Enter(Stage.LetGo);
                }
                else if (_clock > 300.0)
                {
                    GD.Print("bindpad: nobody pressed a button in five minutes; nothing saved");
                    GetTree().Quit();
                }

                break;

            case Stage.Resting:
                // The last reading before the first prompt is each axis's rest: a trigger may rest at -1.
                for (int a = 0; a < AxisCount; a++)
                {
                    _rest[a] = Input.GetJoyAxis(_device, (JoyAxis)a);
                }

                if (_clock >= RestSeconds)
                {
                    Enter(Stage.Asking);
                }

                break;

            case Stage.Asking:
                Ask();
                break;

            case Stage.LetGo:
                if (!_started)
                {
                    // Released after the starting press: read the rest next, then the first prompt.
                    if (AllButtonsUp())
                    {
                        _started = true;
                        _note = string.Empty;
                        Enter(Stage.Resting);
                    }
                }
                else if (AllReleased())
                {
                    _prompt++;
                    Enter(_prompt < Prompts.Length ? Stage.Asking : Stage.Done);
                }

                break;

            case Stage.Done:
                if (_clock >= 6.0)
                {
                    GetTree().Quit();
                }

                break;
        }

        _label.Text = Screen();
    }

    private void Enter(Stage stage)
    {
        _stage = stage;
        _clock = 0.0;
        if (stage == Stage.Done)
        {
            Save();
        }
    }

    private void Ask()
    {
        (string action, _, _) = Prompts[_prompt];
        PadBinding? got = Detect(action);

        if (got is not null)
        {
            PadBinding? clash = _bindings.FirstOrDefault(b => b.Kind == got.Kind && b.Index == got.Index && b.Direction == got.Direction);
            if (clash is not null)
            {
                _note = $"{Describe(got)} is already {clash.Action.Replace('_', ' ')}: try another, then let go";
                return;
            }

            _bindings.Add(got);
            _note = $"got it: {Describe(got)}";
            Enter(Stage.LetGo);
        }
        else if (_clock >= PromptSeconds)
        {
            _skipped.Add(action);
            _note = $"skipped {action.Replace('_', ' ')}: no pad binding for it";
            Enter(Stage.LetGo);
        }
    }

    /// <summary>The input being pushed hardest past the threshold, if any: an axis by its farthest push, else a button.</summary>
    private PadBinding? Detect(string action)
    {
        int bestAxis = -1;
        float bestPush = Pushed;
        for (int a = 0; a < AxisCount; a++)
        {
            float push = Input.GetJoyAxis(_device, (JoyAxis)a) - _rest[a];
            if (MathF.Abs(push) > bestPush)
            {
                bestPush = MathF.Abs(push);
                bestAxis = a;
            }
        }

        if (bestAxis >= 0)
        {
            float push = Input.GetJoyAxis(_device, (JoyAxis)bestAxis) - _rest[bestAxis];
            return new PadBinding(action, PadBinding.Axis, bestAxis, push < 0.0f ? -1 : 1);
        }

        for (int b = 0; b < ButtonCount; b++)
        {
            if (Input.IsJoyButtonPressed(_device, (JoyButton)b))
            {
                return new PadBinding(action, PadBinding.Button, b, 0);
            }
        }

        return null;
    }

    private bool AllReleased()
    {
        for (int a = 0; a < AxisCount; a++)
        {
            if (MathF.Abs(Input.GetJoyAxis(_device, (JoyAxis)a) - _rest[a]) > Released)
            {
                return false;
            }
        }

        return AllButtonsUp();
    }

    private bool AnyButton() => !AllButtonsUp();

    private bool AllButtonsUp()
    {
        for (int b = 0; b < ButtonCount; b++)
        {
            if (Input.IsJoyButtonPressed(_device, (JoyButton)b))
            {
                return false;
            }
        }

        return true;
    }

    private void Save()
    {
        if (_bindings.Count == 0)
        {
            // An empty layout would take the default bindings away and give the pad nothing in their place.
            GD.Print("bindpad: nothing was bound, so nothing was saved");
            return;
        }

        var layout = new PadProfile(Input.GetJoyGuid(_device), Input.GetJoyName(_device), _bindings.ToList());
        Controls.SaveLayout(layout);
        string path = ProjectSettings.GlobalizePath(Controls.LayoutPath(layout.Guid));

        var summary = new StringBuilder();
        summary.AppendLine($"bindpad: saved a layout for \"{layout.Name}\" ({layout.Guid}) to {path}");
        foreach (PadBinding b in _bindings)
        {
            summary.AppendLine($"bindpad:   {b.Action,-12} = {Describe(b)} ({b.Kind} {b.Index}{(b.Kind == PadBinding.Axis ? $", {b.Direction:+0;-0}" : string.Empty)})");
        }

        foreach (string action in _skipped)
        {
            summary.AppendLine($"bindpad:   {action,-12} = skipped");
        }

        GD.Print(summary.ToString().TrimEnd());
    }

    private string Screen()
    {
        var text = new StringBuilder();
        text.AppendLine("PAD BINDING");
        text.AppendLine();
        switch (_stage)
        {
            case Stage.WaitingForPad:
                text.AppendLine("No pad connected. Plug it in (and press ANALOG so its light is on).");
                break;
            case Stage.WaitingForTouch:
                text.AppendLine($"{Input.GetJoyName(_device)}");
                text.AppendLine("Press any button on the pad to start.");
                text.AppendLine("(Its ANALOG light should be on.)");
                break;
            case Stage.Resting:
                text.AppendLine($"{Input.GetJoyName(_device)}");
                text.AppendLine("Hands off the pad for a moment...");
                break;
            case Stage.LetGo when !_started:
                text.AppendLine("Let go of the button, and keep your hands off for a moment...");
                break;
            case Stage.Asking:
            case Stage.LetGo:
                (_, string title, string ask) = Prompts[_prompt];
                text.AppendLine($"{_prompt + 1} of {Prompts.Length}: {title}");
                text.AppendLine($"  {ask}");
                text.AppendLine(_stage == Stage.Asking
                    ? $"  ({PromptSeconds - _clock:F0} s, or it's skipped)"
                    : "  now let go of everything");
                text.AppendLine();
                text.AppendLine(_note);
                break;
            case Stage.Done when _bindings.Count == 0:
                text.AppendLine("Nothing was bound, so nothing was saved: the default bindings stay.");
                break;
            case Stage.Done:
                text.AppendLine($"Saved. The game will use this layout whenever this pad is plugged in.");
                text.AppendLine();
                foreach (PadBinding b in _bindings)
                {
                    text.AppendLine($"  {b.Action.Replace('_', ' '),-12} {Describe(b)}");
                }

                foreach (string action in _skipped)
                {
                    text.AppendLine($"  {action.Replace('_', ' '),-12} (skipped)");
                }

                break;
        }

        return text.ToString().Replace("\r\n", "\n");
    }

    /// <summary>
    /// What a binding is, in words. Axis names are Godot's standard ones; buttons are given by number only,
    /// because on a clone pad the standard names are exactly what can't be trusted.
    /// </summary>
    private static string Describe(PadBinding b)
    {
        if (b.Kind == PadBinding.Button)
        {
            return $"button {b.Index}";
        }

        return b.Index switch
        {
            0 => $"left stick {(b.Direction < 0 ? "left" : "right")}",
            1 => $"left stick {(b.Direction < 0 ? "up" : "down")}",
            2 => $"right stick {(b.Direction < 0 ? "left" : "right")}",
            3 => $"right stick {(b.Direction < 0 ? "up" : "down")}",
            4 => "left trigger",
            5 => "right trigger",
            _ => $"axis {b.Index} {(b.Direction < 0 ? "minus" : "plus")}",
        };
    }
}
