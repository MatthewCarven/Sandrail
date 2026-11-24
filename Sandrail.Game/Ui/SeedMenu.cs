using System;
using Godot;

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

    /// <summary>The seed in the box when it opens: the one being driven, if it's generated.</summary>
    public string Seed { get; init; } = string.Empty;

    /// <summary>Builds a seed's track in place of this one.</summary>
    public required Action<ulong> Generate { get; init; }

    /// <summary>Saves the generated track being driven and says where; null when there's no generated track to save.</summary>
    public Func<string>? Save { get; init; }

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

        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 8);
        seedRow.AddChild(Text("Seed", 18));
        _seed = new LineEdit
        {
            Text = Seed,
            PlaceholderText = "a whole number",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _seed.AddThemeFontSizeOverride("font_size", 18);
        _seed.TextSubmitted += _ => GenerateTyped();
        seedRow.AddChild(_seed);
        seedRow.AddChild(Button("Generate", GenerateTyped));
        rows.AddChild(seedRow);

        rows.AddChild(Button("New random seed", () =>
        {
            ulong seed = (Sandrail.Core.Generation.Hash64.Mix((ulong)DateTime.UtcNow.Ticks) % 999_999UL) + 1UL;
            Go(seed);
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
            else
            {
                _panel.Visible = true;
                GetTree().Paused = true;
                _seed.GrabFocus();
            }
        }
    }

    private void Close()
    {
        _panel.Visible = false;
        GetTree().Paused = false;
    }

    private void GenerateTyped()
    {
        if (ulong.TryParse(_seed.Text.Trim(), out ulong seed))
        {
            Go(seed);
        }
        else
        {
            _note.Text = $"\"{_seed.Text}\" isn't a seed: a seed is a whole number, 0 or more.";
        }
    }

    private void Go(ulong seed)
    {
        GetTree().Paused = false;
        Generate(seed);
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
