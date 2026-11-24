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

    /// <summary>The car's file: what Save writes and Reset reloads. Save as a new car moves it to the new car's (P2).</summary>
    public required string SpecPath { get; set; }

    public required Hud Hud { get; init; }

    /// <summary>Whether a name is being typed, so Esc is the panel's (it cancels) and not the menu's.</summary>
    public bool Typing => _nameRow.Visible || _typingEndedFrame == Engine.GetProcessFrames();

    private HBoxContainer _nameRow = null!;
    private LineEdit _name = null!;
    private Label _note = null!;
    private ulong _typingEndedFrame = ulong.MaxValue;
    private bool _wasPaused;

    /// <summary>The car's name, its file's (P2): "stock" for the stock car.</summary>
    private string CarName => Garage.NameOf(SpecPath);

    public override void _Ready()
    {
        _saved = Car.Spec;

        // It keeps working while the game is paused for typing a name, which stops the letters driving the car.
        ProcessMode = ProcessModeEnum.Always;

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
        bar.AddChild(MakeButton("Save as new car", () => StartNaming("A name for this car: it's kept with your others, and Change car in the Esc menu lists it.")));
        bar.AddChild(MakeButton("Reset", Reset));

        outer.AddChild(new Label
        {
            Text = "Drag or type; it applies at once. Yellow is changed from the file. Save writes this car's file (the stock car "
                + "is never written: it's saved as a new car), Reset reloads it.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 13, FontColor = new Color(0.8f, 0.8f, 0.8f) },
        });

        // Save as a new car (P2): a name typed in, Enter to save, Esc not to.
        _nameRow = new HBoxContainer { Visible = false };
        _name = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = "a name, like matthew-2026-11-25" };
        _name.TextSubmitted += SaveNamed;
        _nameRow.AddChild(_name);
        _nameRow.AddChild(MakeButton("Save it", () => SaveNamed(_name.Text)));
        outer.AddChild(_nameRow);
        _note = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 14, FontColor = Changed },
            Visible = false,
        };
        outer.AddChild(_note);

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
            Text = tunable.Words,
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

    public override void _Process(double delta)
    {
        if (_nameRow.Visible && Input.IsActionJustPressed(Controls.Menu))
        {
            EndNaming("Not saved.");
        }
    }

    /// <summary>Save: this car's file; the stock car is never written (P2, decision 5), so it asks for a name instead.</summary>
    private void Save()
    {
        if (Garage.IsStock(SpecPath))
        {
            StartNaming("The stock car stays as it is: give yours a name and it's saved as a new car.");
            return;
        }

        if (Garage.Write(SpecPath, Car.Spec) is string why)
        {
            Hud.Flash($"Couldn't save {CarName}: {why}");
            return;
        }

        _saved = Car.Spec;
        Refresh();
        Say($"Saved {CarName} ({Car.Spec.Fingerprint()}) to {ProjectSettings.GlobalizePath(SpecPath)}");
    }

    /// <summary>Opens the name box, a free name in it, the game paused so typing doesn't drive the car.</summary>
    private void StartNaming(string why)
    {
        if (_nameRow.Visible)
        {
            return;
        }

        _note.Text = why;
        _note.Visible = true;
        _nameRow.Visible = true;
        _name.Text = Garage.FreeName(Garage.IsStock(SpecPath) ? "my-car" : CarName);
        _wasPaused = GetTree().Paused;
        GetTree().Paused = true;
        _name.GrabFocus();
        _name.SelectAll();
    }

    /// <summary>Saves the car under a name in the garage, never over another car, and makes it the car Save writes from now on.</summary>
    private void SaveNamed(string typed)
    {
        string name = Garage.Slug(typed);
        string path = Garage.PathFor(name);
        if (FileAccess.FileExists(path))
        {
            _note.Text = $"There's a car called {name} already: pick another name (or change to it in the Esc menu, and Save writes it).";
            return;
        }

        if (Garage.Write(path, Car.Spec) is string why)
        {
            EndNaming($"Couldn't save it: {why}");
            return;
        }

        SpecPath = path;
        _saved = Car.Spec;
        Refresh();
        GD.Print($"Saved a new car, {name} ({Car.Spec.Fingerprint()}), to {ProjectSettings.GlobalizePath(path)}");
        EndNaming($"Saved as {name} ({Car.Spec.Fingerprint()}): you're driving it, Save writes it, and Change car in the Esc menu lists it.");
    }

    private void EndNaming(string said)
    {
        _nameRow.Visible = false;
        _name.ReleaseFocus();
        _typingEndedFrame = Engine.GetProcessFrames();
        GetTree().Paused = _wasPaused;
        Say(said);
    }

    /// <summary>A word under the panel's buttons and on the screen.</summary>
    private void Say(string said)
    {
        _note.Text = said;
        _note.Visible = true;
        Hud.Flash(said, 5.0);
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
        Say($"Reloaded {CarName} ({spec.Fingerprint()}) from its file.");
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

        _title.Text = $"{CarName}  {now.Fingerprint()}{(now != _saved ? "  unsaved" : string.Empty)}";
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

    private sealed record Row(Tunable Tunable, Label Name, HSlider Slider, SpinBox Box);
}
