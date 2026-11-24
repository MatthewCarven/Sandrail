using Godot;
using Sandrail.Game.Cars;

namespace Sandrail.Game.Ui;

/// <summary>
/// The speedometer, the keys, the recording light, the lap board, and a line for passing messages. The
/// F3 overlay and the F2 panel hang off it too, so everything drawn over the world shares one layer.
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label _speed = null!;
    private Label _recording = null!;
    private Label _banner = null!;
    private Label _message = null!;
    private Label _keys = null!;
    private double _messageLeft;

    public required Buggy Car { get; init; }

    public RunRecorder? Recorder { get; set; }

    /// <summary>A standing line under the recording light, such as the autodriver's; null hides it.</summary>
    public string? Banner { get; set; }

    /// <summary>The lap board at the top right, three lines; null hides it.</summary>
    public string? Laps { get; set; }

    /// <summary>A large line in the middle of the screen, such as the auto-respawn's countdown; null hides it.</summary>
    public string? Alert { get; set; }

    /// <summary>A speed to show instead of the car's, metres a second: a replay's car's (M4d). Null shows the car's.</summary>
    public float? SpeedOverrideMps { get; set; }

    /// <summary>Whether the driving keys show at the bottom: not in a replay, whose board has its own (M4d).</summary>
    public bool ShowKeys { get; set; } = true;

    /// <summary>On a track: the keys line offers checkpoints and a restart instead of stations, which a track hasn't.</summary>
    public bool OnATrack { get; init; }

    private Label _laps = null!;
    private Label _alert = null!;

    public override void _Ready()
    {
        _speed = new Label { LabelSettings = Settings(40) };
        Pin(_speed, 0.0f, 0.0f, new Vector2(24.0f, 16.0f));
        AddChild(_speed);

        _recording = new Label
        {
            LabelSettings = new LabelSettings { FontSize = 26, FontColor = new Color(1.0f, 0.25f, 0.2f), OutlineSize = 6, OutlineColor = Colors.Black },
            Visible = false,
        };
        Pin(_recording, 0.5f, 0.0f, new Vector2(-60.0f, 20.0f));
        AddChild(_recording);

        _banner = new Label
        {
            LabelSettings = new LabelSettings { FontSize = 22, FontColor = new Color(0.45f, 0.85f, 1.0f), OutlineSize = 6, OutlineColor = Colors.Black },
            Visible = false,
        };
        Pin(_banner, 0.5f, 0.0f, new Vector2(-110.0f, 56.0f));
        AddChild(_banner);

        // Monospace, so the times line up; it grows leftward from the right edge, under the banner.
        _laps = new Label
        {
            LabelSettings = new LabelSettings
            {
                Font = new SystemFont { FontNames = ["Consolas", "Cascadia Mono", "Courier New", "monospace"] },
                FontSize = 20,
                OutlineSize = 5,
                OutlineColor = Colors.Black,
            },
            Visible = false,
        };
        Pin(_laps, 1.0f, 0.0f, new Vector2(-24.0f, 100.0f));
        _laps.GrowHorizontal = Control.GrowDirection.Begin;
        AddChild(_laps);

        _alert = new Label
        {
            LabelSettings = new LabelSettings { FontSize = 30, FontColor = new Color(1.0f, 0.8f, 0.3f), OutlineSize = 7, OutlineColor = Colors.Black },
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        Pin(_alert, 0.5f, 0.3f, Vector2.Zero);
        _alert.GrowHorizontal = Control.GrowDirection.Both;
        AddChild(_alert);

        _message = new Label { LabelSettings = Settings(18), Visible = false };
        Pin(_message, 0.0f, 1.0f, new Vector2(24.0f, -98.0f), growUp: true);
        AddChild(_message);

        _keys = new Label
        {
            // Three lines, since two ran off a 1152-wide window once the ghost's key came (M4c).
            Text = "W/S: throttle, brake, reverse  ·  A/D: steer (Shift: wide lock)  ·  Space: handbrake  ·  C: camera  ·  , and .: time slower, faster\n"
                + (OnATrack ? "R: last checkpoint (hold: start again)  ·  Backspace: start again  ·  G: ghost  ·  Enter: replay" : "R: respawn  ·  1-9, [ ]: stations") + "  ·  Esc: menu\n"
                + "In the air: W/S nose up and down, A/D turn  ·  P: autodriver (-/+: its aggression)  ·  L: racing line  ·  F2: tuning  ·  F3: telemetry  ·  F4: record",
            LabelSettings = Settings(16),
        };
        Pin(_keys, 0.0f, 1.0f, new Vector2(24.0f, -16.0f), growUp: true);
        AddChild(_keys);
    }

    /// <summary>Shows a line at the bottom for a few seconds, and prints it to the log too.</summary>
    public void Flash(string text, double seconds = 6.0)
    {
        GD.Print(text);
        _message.Text = text;
        _message.Visible = true;
        _messageLeft = seconds;
    }

    public override void _Process(double delta)
    {
        _speed.Text = $"{(SpeedOverrideMps ?? Car.LinearVelocity.Length()) * 3.6f:F0} km/h"
            + (System.Math.Abs(Engine.TimeScale - 1.0) > 1e-6 ? $"   time ×{Engine.TimeScale:0.##}" : string.Empty);
        _keys.Visible = ShowKeys;

        bool recording = Recorder?.Recording ?? false;
        _recording.Visible = recording;
        if (recording)
        {
            _recording.Text = $"REC {Recorder!.Seconds:F1} s";
        }

        _banner.Visible = Banner is not null;
        _banner.Text = Banner ?? string.Empty;
        _laps.Visible = Laps is not null;
        _laps.Text = Laps ?? string.Empty;
        _alert.Visible = Alert is not null;
        _alert.Text = Alert ?? string.Empty;

        if (_messageLeft > 0.0)
        {
            _messageLeft -= delta;
            _message.Visible = _messageLeft > 0.0;
        }
    }

    /// <summary>
    /// Anchors a control to a point on the screen (0..1 each way) and offsets it from there. A control
    /// pinned to the bottom grows upward, so a longer message doesn't fall off the screen.
    /// </summary>
    public static void Pin(Control control, float anchorX, float anchorY, Vector2 offset, bool growUp = false)
    {
        control.AnchorLeft = anchorX;
        control.AnchorRight = anchorX;
        control.AnchorTop = anchorY;
        control.AnchorBottom = anchorY;
        control.OffsetLeft = offset.X;
        control.OffsetRight = offset.X;
        control.OffsetTop = offset.Y;
        control.OffsetBottom = offset.Y;
        control.GrowVertical = growUp ? Control.GrowDirection.Begin : Control.GrowDirection.End;
    }

    public static LabelSettings Settings(int size) =>
        new() { FontSize = size, OutlineSize = size >= 30 ? 6 : 4, OutlineColor = Colors.Black };
}
