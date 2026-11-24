using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sandrail.Core.Generation;

namespace Sandrail.Game.Ui;

/// <summary>
/// The Esc menu (M3-PLAN decision 5, M3e): the seed, "generate", which builds that seed's track where you
/// are, "new random seed", and saving the generated track being driven, so it can be driven again or shared
/// as its file. The game stops while it's open, the race clock with it.
/// </summary>
/// <remarks>
/// The full title screen waits for when there's more than one thing to choose (decision 5). Keyboard and
/// mouse only: Esc is the one key, since his pad's Start button is his wide lock (2026-11-23).
/// </remarks>
public partial class SeedMenu : CanvasLayer
{
    /// <summary>What's being driven, in words, for the top of the menu.</summary>
    public required string Where { get; init; }

    /// <summary>A smaller line under it: the track's fingerprint, which its ghosts are filed under (M4a).</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>The code in the box when it opens: the one being driven, if it has one (M4a).</summary>
    public string Seed { get; init; } = string.Empty;

    /// <summary>
    /// Goes to a code's track in place of this one: this version's is generated, another version's driven from
    /// its file. Returns why it can't, or null.
    /// </summary>
    public required Func<TrackCode, string?> Generate { get; init; }

    /// <summary>Saves the generated track being driven and says where; null when there's no generated track to save.</summary>
    public Func<string>? Save { get; init; }

    /// <summary>What can be watched now (M4d): the last run, the saved bests. Asked each time the menu opens.</summary>
    public Func<IReadOnlyList<(string Label, Action Watch)>>? Replays { get; init; }

    /// <summary>Whether Esc belongs to something else just now: a replay, which it leaves.</summary>
    public Func<bool>? EscTaken { get; init; }

    /// <summary>
    /// The tracks there are to load (his ask, 2026-11-24: "i know i can do it via the command line but am very
    /// very lazy"), each with what starts it. Asked each time the list opens.
    /// </summary>
    public Func<IReadOnlyList<(string Label, Action Load)>>? Tracks { get; init; }

    /// <summary>Opens the editor on an empty grid (M5a); null hides the button.</summary>
    public Action? NewTrack { get; init; }

    /// <summary>Opens the editor on the track being driven (M5a); null hides the button.</summary>
    public Action? EditThis { get; init; }

    /// <summary>Opens the editor on an empty grid on this seed's ground (M5d); null hides the button.</summary>
    public Action? NewOnGround { get; init; }

    /// <summary>The garage (PROFILE-PLAN, P2): every car, each with what picking it does; null for the one being driven or one that can't be read.</summary>
    public Func<IReadOnlyList<(string Label, Action? Pick)>>? Cars { get; init; }

    /// <summary>The profile (P3): the car being driven, swept over this track; null where there's no race to profile on.</summary>
    public Action? Profile { get; init; }

    /// <summary>The profile of every car in the garage over this track.</summary>
    public Action? ProfileAll { get; init; }

    /// <summary>Opens the folder the profile sheets are in; says which.</summary>
    public Func<string>? OpenProfiles { get; init; }

    /// <summary>Done before Quit quits: rows still waiting for the sheet are kept in a file beside it.</summary>
    public Action? Quitting { get; init; }

    /// <summary>Which list is open under its button: the tracks or the cars; null for none.</summary>
    private string? _listing;

    private VBoxContainer _replays = null!;
    private ScrollContainer _trackList = null!;
    private VBoxContainer _trackRows = null!;
    private PanelContainer _panel = null!;
    private LineEdit _seed = null!;
    private Label _note = null!;

    /// <summary>Whether it's open, the game stopped behind it.</summary>
    public bool Open => _panel.Visible;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 10;

        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Both;
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.1f, 0.92f),
            ContentMarginLeft = 28.0f,
            ContentMarginRight = 28.0f,
            ContentMarginTop = 22.0f,
            ContentMarginBottom = 22.0f,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
        });
        AddChild(_panel);

        var rows = new VBoxContainer { CustomMinimumSize = new Vector2(420.0f, 0.0f) };
        rows.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(rows);

        rows.AddChild(Text("SANDRAIL", 30));
        rows.AddChild(Text(Where, 18));
        if (Detail.Length > 0)
        {
            Label detail = Text(Detail, 14);
            detail.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.6f);
            rows.AddChild(detail);
        }

        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 8);
        seedRow.AddChild(Text("Code", 18));
        _seed = new LineEdit
        {
            Text = Seed,
            PlaceholderText = "a seed like 5, or a code like 5v1",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _seed.AddThemeFontSizeOverride("font_size", 18);
        _seed.TextSubmitted += _ => GenerateTyped();
        seedRow.AddChild(_seed);
        seedRow.AddChild(Button("Generate", GenerateTyped));
        rows.AddChild(seedRow);

        rows.AddChild(Button("New random seed", () =>
        {
            ulong seed = (Hash64.Mix((ulong)DateTime.UtcNow.Ticks) % 999_999UL) + 1UL;
            Go(TrackCode.Of(seed));
        }));

        if (Save is not null)
        {
            rows.AddChild(Button("Save this track", () =>
            {
                try
                {
                    _note.Text = $"Saved to {Save()}";
                }
                catch (Exception e) when (e is System.IO.IOException or InvalidOperationException)
                {
                    _note.Text = $"Couldn't save it: {e.Message}";
                }
            }));
        }

        // Load a track: a list that opens under the button, the game's own tracks and every saved one. Change car
        // (P2) opens the garage in the same place.
        if (Tracks is not null || Cars is not null)
        {
            if (Tracks is not null)
            {
                rows.AddChild(Button("Load a track", ToggleTracks));
            }

            if (Cars is not null)
            {
                rows.AddChild(Button("Change car", ToggleCars));
            }

            _trackRows = new VBoxContainer();
            _trackRows.AddThemeConstantOverride("separation", 6);
            _trackRows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _trackList = new ScrollContainer
            {
                Visible = false,
                CustomMinimumSize = new Vector2(0.0f, 220.0f),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            };
            _trackList.AddChild(_trackRows);
            rows.AddChild(_trackList);
        }

        // The profile (P3): the menu closes and the autodriver drives the sweep; Esc stops it.
        if (Profile is Action profile)
        {
            rows.AddChild(Button("Profile this car", () =>
            {
                Close();
                profile();
            }));
        }

        if (ProfileAll is Action profileAll)
        {
            rows.AddChild(Button("Profile every car", () =>
            {
                Close();
                profileAll();
            }));
        }

        if (OpenProfiles is Func<string> open)
        {
            rows.AddChild(Button("Open the profiles folder", () =>
            {
                try
                {
                    _note.Text = $"Opened {open()}: a sheet a track, each run a row, its car's numbers at the end.";
                }
                catch (System.IO.IOException e)
                {
                    _note.Text = $"Couldn't open it: {e.Message}";
                }
            }));
        }

        // The editor (M5): the track being driven, or an empty grid. Unpaused first, as Generate is: the scene
        // starts again.
        if (EditThis is Action edit)
        {
            rows.AddChild(Button("Edit this track", () =>
            {
                GetTree().Paused = false;
                edit();
            }));
        }

        if (NewTrack is Action fresh)
        {
            rows.AddChild(Button("New track", () =>
            {
                GetTree().Paused = false;
                fresh();
            }));
        }

        if (NewOnGround is Action onGround)
        {
            rows.AddChild(Button("New track on this ground", () =>
            {
                GetTree().Paused = false;
                onGround();
            }));
        }

        _replays = new VBoxContainer();
        _replays.AddThemeConstantOverride("separation", 10);
        rows.AddChild(_replays);

        rows.AddChild(Button("Resume", Close));
        rows.AddChild(Button("Quit", () =>
        {
            Quitting?.Invoke();
            GetTree().Quit();
        }));

        _note = Text("Esc closes this.", 15);
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rows.AddChild(_note);
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed(Controls.Menu))
        {
            if (Open)
            {
                Close();
            }
            else if (EscTaken?.Invoke() != true)
            {
                ShowReplays();
                _panel.Visible = true;
                GetTree().Paused = true;
                _seed.GrabFocus();
            }
        }
    }

    /// <summary>Opens the list of tracks to load, read afresh, or closes it.</summary>
    private void ToggleTracks()
    {
        IReadOnlyList<(string Label, Action? Pick)> tracks = _listing == "tracks" ? [] : Tracks?.Invoke().Select(t => (t.Label, (Action?)t.Load)).ToList() ?? [];
        ShowList("tracks", tracks, tracks.Count == 0 ? "No tracks to load." : $"{tracks.Count} tracks: the game's own, then the ones you've saved, newest first.");
    }

    /// <summary>Opens the garage, read afresh, or closes it (P2).</summary>
    private void ToggleCars()
    {
        IReadOnlyList<(string Label, Action? Pick)> cars = _listing == "cars" ? [] : Cars?.Invoke() ?? [];
        ShowList("cars", cars, $"{cars.Count} cars: the stock car, then yours, newest first. F2's Save as a new car adds one.");
    }

    /// <summary>
    /// The list under the buttons, filled with one kind of thing, or closed if that kind was open already: a button a
    /// row, picking it unpausing the game first, as Generate does, since the scene starts again.
    /// </summary>
    private void ShowList(string kind, IReadOnlyList<(string Label, Action? Pick)> items, string note)
    {
        if (_listing == kind)
        {
            _listing = null;
            _trackList.Visible = false;
            return;
        }

        foreach (Node old in _trackRows.GetChildren())
        {
            _trackRows.RemoveChild(old);
            old.QueueFree();
        }

        foreach ((string label, Action? pick) in items)
        {
            Button row = Button(label, () =>
            {
                if (pick is not null)
                {
                    GetTree().Paused = false;
                    pick();
                }
            });
            row.Disabled = pick is null;
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddThemeFontSizeOverride("font_size", 16);
            _trackRows.AddChild(row);
        }

        _note.Text = note;
        _listing = kind;

        // As tall as the window leaves room for, so the menu never runs off the screen; it scrolls for the rest.
        float room = GetViewport().GetVisibleRect().Size.Y - 520.0f;
        _trackList.CustomMinimumSize = new Vector2(0.0f, Math.Clamp(room, 90.0f, 300.0f));
        _trackList.Visible = items.Count > 0;
    }

    /// <summary>A button for each thing there is to watch now, the menu closing as it starts.</summary>
    private void ShowReplays()
    {
        foreach (Node old in _replays.GetChildren())
        {
            _replays.RemoveChild(old);
            old.QueueFree();
        }

        foreach ((string label, Action watch) in Replays?.Invoke() ?? [])
        {
            _replays.AddChild(Button(label, () =>
            {
                Close();
                watch();
            }));
        }

        _replays.Visible = _replays.GetChildCount() > 0;
    }

    private void Close()
    {
        _panel.Visible = false;
        GetTree().Paused = false;
    }

    private void GenerateTyped()
    {
        if (TrackCode.TryParse(_seed.Text, out TrackCode code, out string problem))
        {
            Go(code);
        }
        else
        {
            _note.Text = problem;
        }
    }

    private void Go(TrackCode code)
    {
        // Unpaused first: starting the scene again takes this menu out of the tree at once, and a node out of
        // the tree has no tree to unpause. Unpausing after (M4a) left the new track paused, its camera still
        // while the car rolled on (found 2026-11-24). If the code can't be driven, it pauses again and says why.
        GetTree().Paused = false;
        if (Generate(code) is string cannot)
        {
            GetTree().Paused = true;
            _note.Text = cannot;
        }
    }

    private static Label Text(string text, int size)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.Pressed += pressed;
        return button;
    }
}
