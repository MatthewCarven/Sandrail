using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using Sandrail.Core.Cars;

namespace Sandrail.Game.Cars;

/// <summary>
/// F4: every physics tick of a drive to a CSV, so Claude can read the bit that felt wrong (M1-PLAN, M1b).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is how a tuning session works.</b> He drives, records the corner or the landing that felt off,
/// says what felt off, and Claude reads the run. Each run goes to <c>user://runs/</c>
/// (<c>%APPDATA%\Godot\app_userdata\Sandrail\runs\</c>), named with the time and the spec's fingerprint,
/// <b>with the spec itself saved beside it</b>, because a run made on numbers changed in the panel and
/// never saved would otherwise name a spec that exists nowhere.
/// </para>
/// <para>
/// Numbers are written in the invariant culture: a decimal comma would turn every row into twice the
/// columns. About 2 MB a minute at 120 ticks a second.
/// </para>
/// </remarks>
public partial class RunRecorder : Node
{
    private static readonly string[] WheelNames = ["fl", "fr", "rl", "rr"];

    private System.IO.StreamWriter? _writer;
    private string _path = string.Empty;
    private int _ticks;

    public required Buggy Car { get; init; }

    public bool Recording => _writer is not null;

    public float Seconds => _ticks / (float)Engine.PhysicsTicksPerSecond;

    /// <summary>Starts a new run file and returns its path.</summary>
    public string Start()
    {
        Stop();

        string folder = ProjectSettings.GlobalizePath("user://runs");
        System.IO.Directory.CreateDirectory(folder);

        CarSpec spec = Car.Spec;
        string stem = $"{folder}/{DateTime.Now:yyyyMMdd-HHmmss}-{spec.Fingerprint()}";
        System.IO.File.WriteAllText(stem + ".spec.json", spec.ToJson() + "\n");

        _path = stem + ".csv";
        _writer = new System.IO.StreamWriter(_path, false, new UTF8Encoding(false));
        _writer.WriteLine(Header());
        _ticks = 0;
        return _path;
    }

    /// <summary>Closes the run, if one is open, and returns its path.</summary>
    public string? Stop()
    {
        if (_writer is null)
        {
            return null;
        }

        _writer.Dispose();
        _writer = null;
        return _path;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_writer is null)
        {
            return;
        }

        CarReadout r = CarReadout.Of(Car);
        DriverInput input = Car.Driver;
        Vector3 at = Car.GlobalPosition;

        var row = new List<string>(64)
        {
            F(Seconds), F(at.X), F(at.Y), F(at.Z),
            F(r.SpeedKmh), F(r.ForwardMps), F(r.LateralG), F(r.LongitudinalG),
            F(r.YawDps), F(r.BodySlipDeg), F(r.RollDeg), F(r.PitchDeg),
            F(input.Throttle), F(input.Brake), F(input.Steer), input.Handbrake ? "1" : "0", input.WideLock ? "1" : "0",
            F(Car.Throttle), F(Car.Brake), F(Car.SteerDeg), F(Car.SteerLimitDeg),
            F(Car.DriveForce), F(Car.BrakeAmount), Car.GroundedCount.ToString(CultureInfo.InvariantCulture),
        };

        for (int i = 0; i < 4; i++)
        {
            row.Add(F(Car.Compression(i)));
            row.Add(F(Car.Load(i)));
            row.Add(F(Car.SlipDeg(i)));
            row.Add(Car.Grounded(i) ? "1" : "0");
            row.Add(Car.Saturated(i) ? "1" : "0");
            row.Add(Car.SurfaceUnder(i).Id);
        }

        _writer.WriteLine(string.Join(',', row));
        _ticks++;
    }

    public override void _ExitTree() => Stop();

    private static string Header()
    {
        var columns = new List<string>
        {
            "t_s", "x_m", "y_m", "z_m",
            "speed_kmh", "forward_mps", "lat_g_right", "lon_g", "yaw_dps_left", "body_slip_deg_right", "roll_deg_right_down", "pitch_deg_up",
            "key_throttle", "key_brake", "key_steer_right", "key_handbrake", "key_wide_lock",
            "throttle", "brake", "steer_deg_right", "steer_limit_deg",
            "drive_n", "brake_amount", "wheels_grounded",
        };

        foreach (string w in WheelNames)
        {
            columns.AddRange([$"{w}_comp_m", $"{w}_load_n", $"{w}_slip_deg", $"{w}_grounded", $"{w}_saturated", $"{w}_surface"]);
        }

        return string.Join(',', columns);
    }

    private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
