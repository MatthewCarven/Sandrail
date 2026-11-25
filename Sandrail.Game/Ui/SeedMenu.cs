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

    /// <summary>
    /// Each profiled car's best on this track (PROFILE-PLAN, P4), in a list of its own under Watch a car's profile best,
    /// since there are as many as there are cars. Asked each time the menu opens and the list opens.
    /// </summary>
    public Func<IReadOnlyList<(string Label, Action Watch)>>? ProfileReplays { get; init; }

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

    /// <summary>The garage (PROFILE-PLAN, P2): every car, each with what picking it does and its lock.</summary>
    public Func<IReadOnlyList<CarRow>>? Cars { get; init; }

    /// <summary>
    /// A car in the Change car list: its label; what picking it does (null for the one being driven or one that can't be
    /// read); whether it's set inactive; and what its lock guards, Set inactive or Set active, which does it and says how
    /// it went (null for the stock car, which has no lock).
    /// </summary>
    public sealed record CarRow(string Label, Action? Pick, bool Inactive, Func<string>? Toggle);

    /// <summary>How long a car's lock stays open before it shuts by itself.</summary>
    private const ulong UnlockedMs = 5000;

    /// <summary>The least room left above and below the menu when it fills the window's height.</summary>
    private const float WindowMargin = 16.0f;

    /// <summary>The profile (P3): the car being driven, swept over this track; null where there's no race to profile on.</summary>
    public Action? Profile { get; init; }

    /// <summary>The profile of every car in the garage over this track.</summary>
    public Action? ProfileAll { get; init; }

    /// <summary>Profile every car on every track there is to load, then back here (2026-11-25); null hides the button.</summary>
    public Action? ProfileEverywhere { get; init; }

    /// <summary>Opens the folder the profile sheets are in; says which.</summary>
    public Func<string>? OpenProfiles { get; init; }

    /// <summary>Done before Quit quits: rows still waiting for the sheet are kept in a file beside it.</summary>
    public Action? Quitting { get; init; }

    /// <summary>Which list is open under its button: the tracks, the cars or the profiled cars' bests; null for none.</summary>
    private string? _listing;

    private const string ProfilesList = "profiles";

    /// <summary>The car lock that's open, the button it shows, and when it opened; one at a time.</summary>
    private LockButton? _unlocked;
    private Button? _unlockedButton;
    private ulong _unlockedAtMs;

    private VBoxContainer _replays = null!;
    private Button _profileButton = null!;
    private ScrollContainer _profileList = null!;
    private VBoxContainer _profileRows = null!;
    private ScrollContainer _trackList = null!;
    private VBoxContainer _trackRows = null!;
    private PanelContainer _panel = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _rows = null!;
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

        // The rows scroll when the menu is taller than the window (his yes, 2026-11-25): a menu that ran off the
        // default 1152 x 648 window had cut SANDRAIL off the top, and the profiled cars' bests (P4) cut off Quit. The
        // note stays under the rows, outside the scroll, so what a button says is always in sight.
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(outer);
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        outer.AddChild(_scroll);

        var rows = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(420.0f, 0.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        rows.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(rows);
        _rows = rows;

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

        // Every car on every track (2026-11-25): Load a track's tracks one after another, unattended, then back here.
        if (ProfileEverywhere is Action everywhere)
        {
            rows.AddChild(Button("Profile every car on every track", () =>
            {
                Close();
                everywhere();
            }));
        }

        if (OpenProfiles is Func<string> open)
        {
            rows.AddChild(Button("Open the profiles folder", () =>
            {
                try
                {
                    _note.Text = $"Opened {open()}: a sheet a track, and cars.csv.";
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

        // The profiled cars' bests (P4): a list under its own button, as the cars are under theirs, so a garage of
        // cars never runs the menu off the window.
        _profileButton = Button("Watch a car's profile best", ToggleProfileReplays);
        _profileButton.Visible = false;
        rows.AddChild(_profileButton);
        _profileRows = new VBoxContainer();
        _profileRows.AddThemeConstantOverride("separation", 6);
        _profileRows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _profileList = new ScrollContainer { Visible = false, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _profileList.AddChild(_profileRows);
        rows.AddChild(_profileList);

        rows.AddChild(Button("Resume", Close));
        rows.AddChild(Button("Quit", () =>
        {
            Quitting?.Invoke();
            GetTree().Quit();
        }));

        _note = Text("Esc closes this.", 15);
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        outer.AddChild(_note);

        _rows.MinimumSizeChanged += FitToWindow;
        _note.MinimumSizeChanged += FitToWindow;
        GetViewport().SizeChanged += FitToWindow;
        FitToWindow();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= FitToWindow;
    }

    /// <summary>
    /// The rows as tall as they are, or as tall as the window leaves room for beside the note, a margin and the panel's
    /// own; they scroll for the rest. Fitted again whenever the rows, the note or the window change size.
    /// </summary>
    private void FitToWindow()
    {
        float panel = _panel.GetThemeStylebox("panel").GetMinimumSize().Y;
        float room = GetViewport().GetVisibleRect().Size.Y - (2.0f * WindowMargin) - panel
            - _note.GetCombinedMinimumSize().Y - 10.0f;
        float tall = Math.Min(_rows.GetCombinedMinimumSize().Y, Math.Max(room, 120.0f));
        _scroll.CustomMinimumSize = new Vector2(0.0f, tall);
    }

    public override void _Process(double delta)
    {
        if (_unlocked is not null && Time.GetTicksMsec() - _unlockedAtMs > UnlockedMs)
        {
            Relock();
        }

        if (Input.IsActionJustPressed(Controls.Menu))
        {
            if (Open)
            {
                Close();
            }
            else if (EscTaken?.Invoke() != true)
            {
                ShowReplays();
                _scroll.ScrollVertical = 0;
                _panel.Visible = true;
                GetTree().Paused = true;
                _seed.GrabFocus();
            }
        }
    }

    /// <summary>Opens the list of tracks to load, read afresh, or closes it.</summary>
    private void ToggleTracks()
    {
        if (CloseList("tracks"))
        {
            return;
        }

        IReadOnlyList<(string Label, Action Load)> tracks = Tracks?.Invoke() ?? [];
        FillList("tracks", tracks.Select(t => (Control)RowButton(t.Label, t.Load)).ToList(),
            tracks.Count == 0 ? "No tracks to load." : $"{tracks.Count} tracks: the game's own, then the ones you've saved, newest first.");
    }

    /// <summary>Opens the list of the profiled cars' bests on this track, read afresh, or closes it (P4).</summary>
    private void ToggleProfileReplays()
    {
        if (CloseList(ProfilesList))
        {
            return;
        }

        IReadOnlyList<(string Label, Action Watch)> bests = ProfileReplays?.Invoke() ?? [];
        var rows = new List<Control>();
        foreach ((string label, Action watch) in bests)
        {
            Button row = Button(label, () =>
            {
                Close();
                watch();
            });
            row.AddThemeFontSizeOverride("font_size", 16);
            row.ClipText = true;
            row.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            row.TooltipText = label;
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            rows.Add(row);
        }

        FillList(ProfilesList, rows, bests.Count == 0 ? "No car has been profiled clean on this track yet."
            : $"{bests.Count} profiled car{(bests.Count == 1 ? string.Empty : "s")}: each one's fastest clean run here, the fastest first. G races them too.");
    }

    /// <summary>Opens the garage, read afresh, or closes it (P2).</summary>
    private void ToggleCars()
    {
        if (!CloseList("cars"))
        {
            ShowCars(null);
        }
    }

    /// <summary>
    /// The garage's list, read afresh: a row a car, and beside each of his a lock (2026-11-25). Opening the lock shows Set
    /// inactive (or Set active), which shuts again after five seconds; an inactive car is still driven when picked, but
    /// Profile every car leaves it out.
    /// </summary>
    private void ShowCars(string? said)
    {
        IReadOnlyList<CarRow> cars = Cars?.Invoke() ?? [];
        var rows = new List<Control>();
        foreach (CarRow car in cars)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            // The name gives way when Set inactive shows, cut short with "...", so the row never widens: a wider row
            // widens the menu, which slid the lock across and put Set inactive under the pointer that had just
            // unlocked it, where a double click would press it (seen on the first try).
            Button pick = RowButton(car.Label, car.Pick);
            pick.ClipText = true;
            pick.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            pick.TooltipText = car.Label;
            if (car.Inactive)
            {
                pick.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.6f);
            }

            line.AddChild(pick);
            if (car.Toggle is Func<string> toggle)
            {
                var guarded = new Button
                {
                    Text = car.Inactive ? "Set active" : "Set inactive",
                    Visible = false,
                    FocusMode = Control.FocusModeEnum.None,
                };
                guarded.AddThemeFontSizeOverride("font_size", 16);
                guarded.AddThemeColorOverride("font_color", new Color(1.0f, 0.75f, 0.3f));
                guarded.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.85f, 0.5f));
                var padlock = new LockButton
                {
                    TooltipText = car.Inactive
                        ? "Unlock to set this car active again: Profile every car drives it."
                        : "Unlock to set this car inactive: Profile every car leaves it out.",
                };
                padlock.Pressed += () => ToggleLock(padlock, guarded);

                // The list is filled again after the call has returned, not in it: this button is one of the rows.
                guarded.Pressed += () =>
                {
                    Relock();
                    string done = toggle();
                    Callable.From(() => ShowCars(done)).CallDeferred();
                };
                line.AddChild(guarded);
                line.AddChild(padlock);
            }

            rows.Add(line);
        }

        int inactive = cars.Count(c => c.Inactive);
        FillList("cars", rows, said ?? $"{cars.Count} cars: the stock car, then yours, newest first{(inactive > 0 ? $", then {inactive} inactive" : string.Empty)}. "
            + "F2's Save as a new car adds one. The lock beside a car unlocks Set inactive for five seconds: Profile every car leaves an inactive car out.");
    }

    /// <summary>
    /// Opens a car's lock, showing what it guards and shutting any other, or shuts it if it was open. The note under the
    /// menu is left as it is: a note of another length makes the menu another height, and the centred menu would move
    /// the button just unlocked out from under the pointer (seen on the first try).
    /// </summary>
    private void ToggleLock(LockButton padlock, Button guarded)
    {
        bool opening = _unlocked != padlock;
        Relock();
        if (!opening)
        {
            return;
        }

        padlock.Open = true;
        guarded.Visible = true;
        _unlocked = padlock;
        _unlockedButton = guarded;
        _unlockedAtMs = Time.GetTicksMsec();
    }

    /// <summary>Shuts the open lock, if there is one, and hides what it guards.</summary>
    private void Relock()
    {
        if (_unlocked is not null && IsInstanceValid(_unlocked))
        {
            _unlocked.Open = false;
        }

        if (_unlockedButton is not null && IsInstanceValid(_unlockedButton))
        {
            _unlockedButton.Visible = false;
        }

        _unlocked = null;
        _unlockedButton = null;
    }

    /// <summary>Closes the list if it holds this kind already; true if it did.</summary>
    private bool CloseList(string kind)
    {
        if (_listing != kind)
        {
            return false;
        }

        Relock();
        _listing = null;
        HideLists();
        return true;
    }

    /// <summary>Both lists out of sight: the one under Load a track and Change car, and the profiled cars' bests (P4).</summary>
    private void HideLists()
    {
        if (_trackList is not null)
        {
            _trackList.Visible = false;
        }

        _profileList.Visible = false;
    }

    /// <summary>
    /// A list under its buttons, filled with one kind of thing: a row a thing. The tracks and the cars share the list under
    /// Load a track and Change car; the profiled cars' bests have their own under their button (P4). One is open at a time.
    /// </summary>
    private void FillList(string kind, IReadOnlyList<Control> rows, string note)
    {
        Relock();
        (ScrollContainer list, VBoxContainer holder) = kind == ProfilesList ? (_profileList, _profileRows) : (_trackList, _trackRows);
        HideLists();
        foreach (Node old in holder.GetChildren())
        {
            holder.RemoveChild(old);
            old.QueueFree();
        }

        foreach (Control row in rows)
        {
            holder.AddChild(row);
        }

        _note.Text = note;
        _listing = kind;

        // As tall as the window leaves room for, so the menu never runs off the screen; it scrolls for the rest.
        float room = GetViewport().GetVisibleRect().Size.Y - 520.0f;
        list.CustomMinimumSize = new Vector2(0.0f, Math.Clamp(room, 90.0f, 300.0f));
        list.Visible = rows.Count > 0;
    }

    /// <summary>A row of a list: picking it unpauses the game first, as Generate does, since the scene starts again; greyed out with nothing to do.</summary>
    private Button RowButton(string label, Action? pick)
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
        return row;
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
        _profileButton.Visible = (ProfileReplays?.Invoke().Count ?? 0) > 0;
    }

    /// <summary>
    /// Closes the menu, and any list open in it, so it opens next time as it first did: a list left open put a car
    /// where Change car had been, and a click meant for Change car changed the car (seen 2026-11-25).
    /// </summary>
    private void Close()
    {
        Relock();
        if (_listing is not null)
        {
            _listing = null;
            HideLists();
            _note.Text = "Esc closes this.";
        }

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
