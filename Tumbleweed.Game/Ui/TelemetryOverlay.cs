using System;
using System.Text;
using Godot;
using Tumbleweed.Game.Cars;

namespace Tumbleweed.Game.Ui;

/// <summary>
/// F3: what the car is doing, live, in numbers (M1-PLAN, M1b). The motion, the pedals and steering
/// against the keys behind them, and per wheel its compression, load, slip, surface, and whether it's
/// at its grip limit or in the air.
/// </summary>
/// <remarks>
/// Refreshed twenty times a second rather than every frame, so a number can be read before it changes.
/// Monospace, so the columns line up. Lines are ended with <see cref="Line"/>, never
/// <c>AppendLine</c>, which writes a carriage return as well on Windows, and a Godot label counts that
/// as a second line break.
/// </remarks>
public partial class TelemetryOverlay : Label
{
    private const char Line = '\n';

    private static readonly string[] WheelNames = ["FL", "FR", "RL", "RR"];

    private double _sinceRefresh;

    public required Buggy Car { get; init; }

    public override void _Ready()
    {
        LabelSettings = new LabelSettings
        {
            Font = new SystemFont { FontNames = ["Consolas", "Cascadia Mono", "Courier New", "monospace"] },
            FontSize = 15,
            OutlineSize = 4,
            OutlineColor = Colors.Black,
        };
        Hud.Pin(this, 0.0f, 0.0f, new Vector2(24.0f, 72.0f));
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        _sinceRefresh += delta;
        if (!Visible || _sinceRefresh < 0.05)
        {
            return;
        }

        _sinceRefresh = 0.0;
        CarReadout r = CarReadout.Of(Car);
        DriverInput keys = Car.Driver;
        float travel = Car.Spec.SuspensionTravelM;

        var text = new StringBuilder();
        text.Append($"speed {r.SpeedKmh,6:F1} km/h  forward {r.ForwardMps,6:F1} m/s  sideways {r.LateralG,5:F2} g  fore-aft {r.LongitudinalG,5:F2} g").Append(Line);
        text.Append($"body slip {r.BodySlipDeg,6:F1}°  yaw {r.YawDps,7:F1}°/s  roll {r.RollDeg,5:F1}°  pitch {r.PitchDeg,5:F1}°").Append(Line);
        text.Append($"throttle {Car.Throttle:F2} (key {keys.Throttle:F2})  brake {Car.Brake:F2} (key {keys.Brake:F2})  drive {Car.DriveForce,6:F0} N").Append(Line);
        text.Append($"steer {Car.SteerDeg,6:F1}° of {Car.SteerLimitDeg:F1}° (key {keys.Steer,5:+0.00;-0.00;0.00})").Append(Line);
        text.Append(Line);
        text.Append("     compression               load     slip  surface").Append(Line);

        for (int i = 0; i < 4; i++)
        {
            float compression = Car.Compression(i);
            string surface = Car.SurfaceUnder(i).Id;
            string flags = (Car.Grounded(i) ? string.Empty : "  AIR") + (Car.Saturated(i) ? "  FULL GRIP" : string.Empty);
            text.Append($"{WheelNames[i]}   {Bar(compression / travel)} {compression,5:F3} m  {Car.Load(i),6:F0} N  ")
                .Append($"{Car.SlipDeg(i),6:+0.0;-0.0;0.0}°  {surface[(surface.IndexOf(':') + 1)..]}{flags}")
                .Append(Line);
        }

        Text = text.ToString();
    }

    private static string Bar(float fraction)
    {
        const int Width = 12;
        int filled = Math.Clamp((int)MathF.Round(fraction * Width), 0, Width);
        return "[" + new string('#', filled) + new string('-', Width - filled) + "]";
    }
}
