using System;
using System.Collections.Generic;
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

        // Load a track: a list that opens under the button, the game's own tracks and every saved one.
        if (Tracks is not null)
        {
            rows.AddChild(Button("Load a track", ToggleTracks));
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

        _replays = new VBoxContainer();
        _replays.AddThemeConstantOverride("separation", 10);
        rows.AddChild(_replays);

        rows.AddChild(Button("Resume", Close));
        rows.AddChild(Button("Quit", () => GetTree().Quit()));

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
        if (_trackList.Visible)
        {
            _trackList.Visible = false;
            return;
        }

        foreach (Node old in _trackRows.GetChildren())
        {
            _trackRows.RemoveChild(old);
            old.QueueFree();
        }

        IReadOnlyList<(string Label, Action Load)> tracks = Tracks?.Invoke() ?? [];
        foreach ((string label, Action load) in tracks)
        {
            Button row = Button(label, () =>
            {
                // Unpaused first, as Generate is: the scene leaves the tree as the track loads.
                GetTree().Paused = false;
                load();
            });
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddThemeFontSizeOverride("font_size", 16);
            _trackRows.AddChild(row);
        }

        _note.Text = tracks.Count == 0 ? "No tracks to load." : $"{tracks.Count} tracks: the game's own, then the ones you've saved, newest first.";

        // As tall as the window leaves room for, so the menu never runs off the screen; it scrolls for the rest.
        float room = GetViewport().GetVisibleRect().Size.Y - 520.0f;
        _trackList.CustomMinimumSize = new Vector2(0.0f, Math.Clamp(room, 90.0f, 300.0f));
        _trackList.Visible = tracks.Count > 0;
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
