using Godot;
using Sandrail.Game.Cars;

namespace Sandrail.Game.Ui;

/// <summary>
/// The speedometer, the keys, the recording light, and a line for passing messages. The F3 overlay
/// and the F2 panel hang off it too, so everything drawn over the world shares one layer.
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label _speed = null!;
    private Label _recording = null!;
    private Label _message = null!;
    private double _messageLeft;

    public required Buggy Car { get; init; }

    public RunRecorder? Recorder { get; set; }

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

        _message = new Label { LabelSettings = Settings(18), Visible = false };
        Pin(_message, 0.0f, 1.0f, new Vector2(24.0f, -52.0f), growUp: true);
        AddChild(_message);

        var keys = new Label
        {
            Text = "W/S: throttle, brake, reverse  ·  A/D: steer (Shift: wide lock)  ·  R: respawn  ·  1-5: stations  ·  C: camera  ·  "
                + "F2: tuning  ·  F3: telemetry  ·  F4: record",
            LabelSettings = Settings(16),
        };
        Pin(keys, 0.0f, 1.0f, new Vector2(24.0f, -16.0f), growUp: true);
        AddChild(keys);
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
        _speed.Text = $"{Car.LinearVelocity.Length() * 3.6f:F0} km/h";

        bool recording = Recorder?.Recording ?? false;
        _recording.Visible = recording;
        if (recording)
        {
            _recording.Text = $"REC {Recorder!.Seconds:F1} s";
        }

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
