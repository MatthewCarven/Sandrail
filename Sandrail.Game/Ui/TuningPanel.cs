using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Game.Cars;

namespace Sandrail.Game.Ui;

/// <summary>
/// F2: a slider and a box for every number in the car's spec, applied the moment it moves
/// (M1-PLAN, M1b). Save writes the spec's file; Reset reloads it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built from <see cref="CarSpec.Tunables"/></b>, so a number added to the spec appears here with no
/// change to this class, in its <see cref="TuneAttribute"/>'s group and range. A number changed from the
/// saved file is shown in yellow, and the title says "unsaved" while any is.
/// </para>
/// <para>
/// <b>A change the spec refuses is not applied</b>: the panel says why in red and the car keeps the
/// last good numbers. The slider stays where it was put, so the fix is to move it back or move the
/// number it conflicts with.
/// </para>
/// <para>
/// Sliders take no keyboard focus, so the arrow keys keep steering while the panel is open. The boxes
/// take focus to be typed in; closing the panel lets go of it.
/// </para>
/// </remarks>
public partial class TuningPanel : PanelContainer
{
    private const float Width = 470.0f;

    private static readonly Color Changed = new(1.0f, 0.85f, 0.3f);
    private static readonly Color Refused = new(1.0f, 0.4f, 0.35f);

    private readonly List<Row> _rows = new();
    private CarSpec _saved = new();
    private Label _title = null!;
    private Label _problem = null!;
    private Label _wingLabel = null!;
    private OptionButton _wingPicker = null!;
    private bool _syncing;

    public required Buggy Car { get; init; }

    public required string SpecPath { get; init; }

    public required Hud Hud { get; init; }

    public override void _Ready()
    {
        _saved = Car.Spec;

        AnchorLeft = 1.0f;
        AnchorRight = 1.0f;
        AnchorTop = 0.0f;
        AnchorBottom = 1.0f;
        OffsetLeft = -Width;
        OffsetRight = 0.0f;
        OffsetTop = 0.0f;
        OffsetBottom = 0.0f;

        var outer = new VBoxContainer();
        AddChild(outer);

        var bar = new HBoxContainer();
        outer.AddChild(bar);
        _title = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bar.AddChild(_title);
        bar.AddChild(MakeButton("Save", Save));
        bar.AddChild(MakeButton("Reset", Reset));

        outer.AddChild(new Label
        {
            Text = "Drag or type; it applies at once. Yellow is changed from the file. Save writes the file, Reset reloads it.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 13, FontColor = new Color(0.8f, 0.8f, 0.8f) },
        });

        _problem = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 14, FontColor = Refused },
            Visible = false,
        };
        outer.AddChild(_problem);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        outer.AddChild(scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);

        foreach (IGrouping<string, Tunable> group in CarSpec.Tunables.GroupBy(t => t.Group))
        {
            list.AddChild(new Label
            {
                Text = group.Key,
                LabelSettings = new LabelSettings { FontSize = 17, FontColor = new Color(0.6f, 0.85f, 1.0f) },
            });

            // The wing is a part chosen by name, not a number, so it gets a list of its own above its settings.
            if (group.Key == "Wing")
            {
                list.AddChild(BuildWingPicker());
            }

            foreach (Tunable tunable in group)
            {
                list.AddChild(BuildRow(tunable));
            }
        }

        Refresh();
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (!Visible)
        {
            GetViewport().GuiReleaseFocus();
        }
    }

    private Control BuildRow(Tunable tunable)
    {
        float step = NiceStep((tunable.Max - tunable.Min) / 400.0f);
        float value = tunable.Get(Car.Spec);

        var name = new Label
        {
            Text = Display(tunable.Name),
            TooltipText = $"{tunable.Name}: slider {tunable.Min} to {tunable.Max}",
            CustomMinimumSize = new Vector2(205.0f, 0.0f),
            MouseFilter = MouseFilterEnum.Pass,
        };

        var slider = new HSlider
        {
            MinValue = tunable.Min,
            MaxValue = tunable.Max,
            Step = step,
            Value = value,
            FocusMode = FocusModeEnum.None,

            // The mouse wheel scrolls the list, never the slider under the pointer: his panel tuning
            // came back with a dozen numbers nudged by a step or two, from scrolling (2026-11-23).
            Scrollable = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };

        // Wide limits, and a fine step so a typed number isn't rounded; the arrows move by the slider's step.
        var box = new SpinBox
        {
            MinValue = -1e6,
            MaxValue = 1e7,
            AllowGreater = true,
            AllowLesser = true,
            Step = 0.0001,
            CustomArrowStep = step,
            Value = value,
            SelectAllOnFocus = true,
            CustomMinimumSize = new Vector2(112.0f, 0.0f),
        };

        var row = new Row(tunable, name, slider, box);
        _rows.Add(row);
        slider.ValueChanged += v => FromSlider(row, v);
        box.ValueChanged += v => FromBox(row, v);

        var line = new HBoxContainer();
        line.AddChild(name);
        line.AddChild(slider);
        line.AddChild(box);
        return line;
    }

    private Control BuildWingPicker()
    {
        _wingLabel = new Label
        {
            Text = "Wing",
            TooltipText = "wingId: which wing is fitted. Its size and stall angle are the part's; the sliders below set it.",
            CustomMinimumSize = new Vector2(205.0f, 0.0f),
            MouseFilter = MouseFilterEnum.Pass,
        };

        _wingPicker = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (Wing wing in Wings.All)
        {
            _wingPicker.AddItem(wing.AreaM2 > 0.0f ? $"{wing.Name} ({wing.AreaM2:0.##} m², stalls at {wing.StallDeg:0}°)" : wing.Name);
        }

        ShowWing(Car.Spec);
        _wingPicker.ItemSelected += index => ApplyWing(Wings.All[(int)index]);

        var line = new HBoxContainer();
        line.AddChild(_wingLabel);
        line.AddChild(_wingPicker);
        return line;
    }

    private void ShowWing(CarSpec spec) =>
        Sync(() => _wingPicker.Selected = Math.Max(0, Wings.All.ToList().FindIndex(w => w.Id == spec.WingId)));

    private void ApplyWing(Wing wing)
    {
        if (_syncing)
        {
            return;
        }

        CarSpec next = Car.Spec with { WingId = wing.Id };
        IReadOnlyList<string> problems = next.Problems();
        if (problems.Count > 0)
        {
            _problem.Text = "Not applied: " + string.Join("; ", problems);
            _problem.Visible = true;
            ShowWing(Car.Spec);
            return;
        }

        _problem.Visible = false;
        Car.ApplySpec(next);
        Refresh();
    }

    private void FromSlider(Row row, double value)
    {
        if (_syncing)
        {
            return;
        }

        float rounded = (float)Math.Round(value, 6);
        Sync(() => row.Box.Value = rounded);
        Apply(row, rounded);
    }

    private void FromBox(Row row, double value)
    {
        if (_syncing)
        {
            return;
        }

        Sync(() => row.Slider.Value = value);
        Apply(row, (float)value);
    }

    private void Apply(Row row, float value)
    {
        CarSpec next = Car.Spec.WithValue(row.Tunable.Name, value);
        IReadOnlyList<string> problems = next.Problems();
        if (problems.Count > 0)
        {
            Refresh();
            _problem.Text = "Not applied: " + string.Join("; ", problems);
            _problem.Visible = true;
            row.Name.Modulate = Refused;
            return;
        }

        _problem.Visible = false;
        Car.ApplySpec(next);
        Refresh();
    }

    private void Save()
    {
        string json = Car.Spec.ToJson() + "\n";
        using FileAccess? file = FileAccess.Open(SpecPath, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            Hud.Flash($"Couldn't save {SpecPath}: {FileAccess.GetOpenError()}");
            return;
        }

        file.StoreString(json);
        _saved = Car.Spec;
        Refresh();
        Hud.Flash($"Saved {Car.Spec.Id} ({Car.Spec.Fingerprint()}) to {SpecPath}");
    }

    private void Reset()
    {
        CarSpec spec;
        try
        {
            spec = CarSpec.Parse(FileAccess.GetFileAsString(SpecPath));
        }
        catch (System.IO.InvalidDataException e)
        {
            Hud.Flash($"Couldn't reload {SpecPath}: {e.Message}");
            return;
        }

        Car.ApplySpec(spec);
        _saved = spec;
        Sync(() =>
        {
            foreach (Row row in _rows)
            {
                float value = row.Tunable.Get(spec);
                row.Slider.Value = value;
                row.Box.Value = value;
            }
        });
        ShowWing(spec);

        _problem.Visible = false;
        Refresh();
        Hud.Flash($"Reloaded {spec.Id} ({spec.Fingerprint()}) from {SpecPath}");
    }

    private void Refresh()
    {
        CarSpec now = Car.Spec;
        foreach (Row row in _rows)
        {
            bool changed = row.Tunable.Get(now) != row.Tunable.Get(_saved);
            row.Name.Modulate = changed ? Changed : Colors.White;
        }

        _wingLabel.Modulate = now.WingId != _saved.WingId ? Changed : Colors.White;

        _title.Text = $"{now.Id}  {now.Fingerprint()}{(now != _saved ? "  unsaved" : string.Empty)}";
    }

    private void Sync(Action set)
    {
        _syncing = true;
        try
        {
            set();
        }
        finally
        {
            _syncing = false;
        }
    }

    private static Button MakeButton(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        button.Pressed += pressed;
        return button;
    }

    /// <summary>The largest of 1, 2 or 5 times a power of ten that doesn't exceed <paramref name="rough"/>.</summary>
    private static float NiceStep(float rough)
    {
        float power = MathF.Pow(10.0f, MathF.Floor(MathF.Log10(rough)));
        float scaled = rough / power;
        return power * (scaled >= 5.0f ? 5.0f : scaled >= 2.0f ? 2.0f : 1.0f);
    }

    /// <summary>
    /// A property name as words with its unit: <c>BumpDampingNsPerM</c> becomes "Bump damping (N·s/m)".
    /// </summary>
    private static string Display(string name)
    {
        (string Suffix, string Unit)[] units =
        [
            ("NPerMps2", "N per (m/s)²"), ("NsPerM", "N·s/m"), ("NPerM", "N/m"), ("DegPerS2", "°/s²"), ("DegPerS", "°/s"),
            ("PerS", "/s"), ("Mps", "m/s"), ("Deg", "°"), ("Kg", "kg"), ("W", "W"), ("N", "N"), ("M", "m"),
        ];

        string unit = string.Empty;
        foreach ((string suffix, string label) in units)
        {
            if (name.Length > suffix.Length
                && name.EndsWith(suffix, StringComparison.Ordinal)
                && char.IsLower(name[name.Length - suffix.Length - 1]))
            {
                name = name[..^suffix.Length];
                unit = $" ({label})";
                break;
            }
        }

        var words = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                words.Append(' ');
                words.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                words.Append(name[i]);
            }
        }

        return words + unit;
    }

    private sealed record Row(Tunable Tunable, Label Name, HSlider Slider, SpinBox Box);
}
