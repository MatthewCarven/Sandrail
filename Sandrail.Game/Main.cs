using System;
using System.Linq;
using System.Reflection;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Core.Generation;
using Sandrail.Core.Pieces;
using Sandrail.Game.Cars;
using Sandrail.Game.Oracles;
using Sandrail.Game.Ui;
using Sandrail.Game.World;

namespace Sandrail.Game;

/// <summary>
/// The entry point: the buggy on a 640 m plain (one 32 × 32 map of 20 m tiles) with M1c's test ground
/// on it, the chase camera, the HUD with its F2 tuning panel, F3 telemetry and F4 recorder, and the
/// oracles.
/// </summary>
/// <remarks>
/// <para>
/// Game flags, after the bare <c>--</c>: <c>--ground=test|bare</c> (the test ground, or the empty plain the
/// first four oracles drive), <c>--station=1..5</c> to start at one of the test ground's stations,
/// <c>--surface=road|dirt|grass|sand</c> for the plain (sand with the test ground, road on the bare
/// plain, so the M1a oracles' targets still hold), <c>--oracle=</c> any of the seven, and
/// <c>--spec=&lt;path&gt;</c> to drive another spec file than the shipped buggy, so a variant can be tried
/// without touching what plays. The tuning panel saves to whichever file was loaded.
/// </para>
/// <para>
/// <c>--panels=tuning,telemetry</c> opens either at start, and <c>--record</c> starts a run at start.
/// <c>--screenshot=&lt;path.png&gt;</c> is Claude's eyes, since headless draws nothing: it saves one frame
/// at rest and one driving (<c>-rest</c>, <c>-turn</c>), then quits; <c>--drive=throttle,steer</c> sets
/// how it drives (0.7,0.6 by default), and <c>--camera=x,y,z:x,y,z</c> looks from one point at another
/// instead of following the car.
/// </para>
/// <para>
/// The plain is drawn with a 10 m grid and a 100 m one, because a plain of one colour gives the eye
/// nothing to judge speed by, and speed is half of feel.
/// </para>
/// </remarks>
public partial class Main : Node3D
{
    private const string BuggyPath = "res://content/base/cars/buggy.json";

    private const string GroundShader = """
        shader_type spatial;

        uniform vec3 base_colour : source_color = vec3(0.4, 0.4, 0.42);

        varying vec3 world_position;

        float grid_line(vec2 coordinate, float sharpness) {
            vec2 width = max(fwidth(coordinate), vec2(1e-4));
            vec2 distance = abs(fract(coordinate - 0.5) - 0.5) / width;
            return 1.0 - clamp(min(distance.x, distance.y) * sharpness, 0.0, 1.0);
        }

        void vertex() {
            world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            float minor = grid_line(world_position.xz / 10.0, 1.0);
            float major = grid_line(world_position.xz / 100.0, 0.5);
            ALBEDO = base_colour * (1.0 - (0.22 * minor) - (0.35 * major));
            ROUGHNESS = 0.95;
        }
        """;

    private Buggy? _car;
    private ChaseCamera? _camera;
    private Hud? _hud;
    private TelemetryOverlay? _telemetry;
    private TuningPanel? _tuning;
    private RunRecorder? _recorder;
    private bool _oracle;
    private Transform3D _spawn;
    private string? _screenshot;
    private DriverInput _screenshotDrive = new() { Throttle = 0.7f, Steer = 0.6f };
    private int _ticks;
    private Shader? _groundShader;
    private TestGroundLayout? _layout;

    public override void _Ready()
    {
        string version = typeof(Main).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        GD.Print($"Sandrail {version}");

        // The published first output of splitmix64, which Hash64Tests pins. Printed from the Godot
        // side, it says this binary links the same Core the tests ran against.
        GD.Print($"Core linked: Hash64.Mix(0) = 0x{Hash64.Mix(0UL):X16} (expected 0xE220A8397B1DCDAF)");

        string[] args = OS.GetCmdlineUserArgs();
        string? oracle = Flag(args, "--oracle=");
        string specPath = Flag(args, "--spec=") ?? BuggyPath;
        string[] panels = (Flag(args, "--panels=") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        _screenshot = Flag(args, "--screenshot=");

        if (oracle is not null && !Oracle.IsKnown(oracle))
        {
            Refuse($"Unknown oracle '{oracle}'. Known: straight, drop, brake, circle, jump, loop, bumps.");
            return;
        }

        // The first four oracles drive the bare plain on road grip, exactly as at M1a, so their figures
        // stay comparable; everything else gets the test ground on sand.
        bool bareOracle = oracle is not null && !Oracle.NeedsTestGround(oracle);
        bool testGround = (Flag(args, "--ground=") ?? (bareOracle ? "bare" : "test")) == "test";
        string surfaceName = Flag(args, "--surface=") ?? (testGround ? "sand" : "road");

        Surface? surface = Surfaces.Find(surfaceName);
        if (surface is null)
        {
            Refuse($"Unknown surface '{surfaceName}'. Known: {string.Join(", ", Surfaces.All.Select(s => s.Id))}.");
            return;
        }

        int station = oracle is not null && Oracle.NeedsTestGround(oracle)
            ? Oracle.StationFor(oracle)
            : int.TryParse(Flag(args, "--station="), out int n) ? n : 1;
        if (testGround && station is < 1 or > 5)
        {
            Refuse($"Unknown station {station}. They're 1 to 5.");
            return;
        }

        if (Flag(args, "--drive=") is string drive)
        {
            string[] parts = drive.Split(',');
            _screenshotDrive = new DriverInput
            {
                Throttle = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                Steer = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0.0f,
            };
        }

        CarSpec spec;
        try
        {
            spec = CarSpec.Parse(FileAccess.GetFileAsString(specPath));
        }
        catch (System.IO.InvalidDataException e)
        {
            Refuse($"{specPath}: {e.Message}");
            return;
        }

        Controls.Register();
        GetNode<DirectionalLight3D>("Sun").RotationDegrees = new Vector3(-50.0f, 30.0f, 0.0f);
        _groundShader = new Shader { Code = GroundShader };
        SetUpGround(surface);

        if (testGround)
        {
            _layout = new TestGroundLayout();
            TestGround.Build(this, _layout, _groundShader);
            _spawn = SpawnAt(_layout.Stations[station - 1].Start, spec);
        }
        else
        {
            float lift = oracle == Oracle.Drop ? Oracle.DropHeight : 0.02f;
            _spawn = new Transform3D(Basis.Identity, new Vector3(0.0f, Buggy.RestHeight(spec) + lift, 0.0f));
        }

        _car = new Buggy { Transform = _spawn };
        _car.Configure(spec);
        AddChild(_car);

        _camera = new ChaseCamera { Target = _car };
        AddChild(_camera);

        _recorder = new RunRecorder { Car = _car };
        AddChild(_recorder);

        _hud = new Hud { Car = _car, Recorder = _recorder };
        AddChild(_hud);
        _telemetry = new TelemetryOverlay { Car = _car, Visible = panels.Contains("telemetry") };
        _hud.AddChild(_telemetry);
        _tuning = new TuningPanel { Car = _car, SpecPath = specPath, Hud = _hud, Visible = panels.Contains("tuning") };
        _hud.AddChild(_tuning);

        if (oracle is not null)
        {
            _oracle = true;
            AddChild(new Oracle { Kind = oracle, Car = _car, Layout = _layout });
        }

        if (args.Contains("--record"))
        {
            Recording(_recorder.Start());
        }

        // A fixed camera for looking at the ground rather than the car: x,y,z:x,y,z, from and at.
        if (Flag(args, "--camera=") is string view)
        {
            float[] v = view.Replace(':', ',').Split(',')
                .Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var overview = new Camera3D { Far = 2000.0f };
            AddChild(overview);
            overview.LookAtFromPosition(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]), Vector3.Up);
            overview.Current = true;
        }

        GD.Print($"Driving {spec.Id} {spec.Fingerprint()} on {surface.Id}{(testGround ? $", from station {station}" : string.Empty)}");
        if (testGround && oracle is null && _screenshot is null)
        {
            Station first = _layout!.Stations[station - 1];
            _hud.Flash($"{first.Number}. {first.Name}: {first.Hint}", 8.0);
        }
    }

    /// <summary>Where the car goes to start from a frame on the road: resting on its wheels, facing along it.</summary>
    private static Transform3D SpawnAt(Frame start, CarSpec spec)
    {
        Vector3 up = start.Up.ToGodot().Normalized();
        Vector3 right = start.Forward.ToGodot().Cross(up).Normalized();
        Vector3 forward = up.Cross(right).Normalized();
        return new Transform3D(new Basis(right, up, -forward), start.Position.ToGodot() + (up * (Buggy.RestHeight(spec) + 0.02f)));
    }

    private void GoToStation(int number)
    {
        if (_layout is null || _car is null)
        {
            return;
        }

        Station station = _layout.Stations[number - 1];
        _spawn = SpawnAt(station.Start, _car.Spec);
        _car.Respawn(_spawn);
        _hud?.Flash($"{station.Number}. {station.Name}: {station.Hint}", 8.0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_car is null || _oracle)
        {
            return;
        }

        _ticks++;
        if (_screenshot is not null)
        {
            bool driving = _ticks > 2 * Engine.PhysicsTicksPerSecond;
            _car.Driver = driving ? _screenshotDrive : default;
            return;
        }

        for (int number = 1; number <= 5; number++)
        {
            if (Input.IsActionJustPressed(Controls.Station(number)))
            {
                GoToStation(number);
            }
        }

        _car.Driver = new DriverInput
        {
            Throttle = Input.GetActionStrength(Controls.Throttle),
            Brake = Input.GetActionStrength(Controls.Brake),
            Steer = Input.GetActionStrength(Controls.SteerRight) - Input.GetActionStrength(Controls.SteerLeft),
            Handbrake = Input.IsActionPressed(Controls.Handbrake),
        };

        if (Input.IsActionJustPressed(Controls.Respawn))
        {
            _car.Respawn(_spawn);
        }
    }

    public override void _Process(double delta)
    {
        if (_car is null)
        {
            return;
        }

        if (Input.IsActionJustPressed(Controls.Camera))
        {
            _camera?.ToggleView();
        }

        if (Input.IsActionJustPressed(Controls.Tuning))
        {
            _tuning?.Toggle();
        }

        if (Input.IsActionJustPressed(Controls.Telemetry) && _telemetry is not null)
        {
            _telemetry.Visible = !_telemetry.Visible;
        }

        if (Input.IsActionJustPressed(Controls.Record))
        {
            ToggleRecording();
        }

        if (_screenshot is not null)
        {
            TakeScreenshots(_screenshot);
        }
    }

    private void ToggleRecording()
    {
        if (_recorder is null || _hud is null)
        {
            return;
        }

        if (_recorder.Recording)
        {
            float seconds = _recorder.Seconds;
            string path = _recorder.Stop()!;
            GD.Print($"Run saved: {path}");
            _hud.Flash($"Saved a {seconds:F1} s run: runs/{System.IO.Path.GetFileName(path)}", 10.0);
        }
        else
        {
            Recording(_recorder.Start());
        }
    }

    /// <summary>The whole path goes to the log, where it can be copied; the HUD gets just the file's name.</summary>
    private void Recording(string path)
    {
        GD.Print($"Recording to {path}");
        _hud?.Flash($"Recording to runs/{System.IO.Path.GetFileName(path)}");
    }

    private void TakeScreenshots(string path)
    {
        int tps = Engine.PhysicsTicksPerSecond;
        string stem = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;

        if (_ticks >= (tps * 3) / 2 && !FileAccess.FileExists(stem + "-rest.png"))
        {
            GetViewport().GetTexture().GetImage().SavePng(stem + "-rest.png");
        }

        if (_ticks >= tps * 5)
        {
            GetViewport().GetTexture().GetImage().SavePng(stem + "-turn.png");
            GD.Print($"Screenshots saved: {stem}-rest.png, {stem}-turn.png");
            string? run = _recorder?.Stop();
            if (run is not null)
            {
                GD.Print($"Run saved: {run}");
            }

            GetTree().Quit();
        }
    }

    private void SetUpGround(Surface surface)
    {
        var ground = GetNode<StaticBody3D>("Ground");
        ground.SetMeta("surface", surface.Id);
        ground.CollisionLayer = Buggy.WorldLayer;

        Color colour = surface.Id switch
        {
            "base:sand" => new Color(0.76f, 0.64f, 0.45f),
            "base:dirt" => new Color(0.5f, 0.38f, 0.26f),
            "base:grass" => new Color(0.33f, 0.48f, 0.24f),
            _ => new Color(0.36f, 0.36f, 0.38f),
        };

        var material = new ShaderMaterial { Shader = _groundShader };
        material.SetShaderParameter("base_colour", colour);
        ground.GetNode<MeshInstance3D>("Mesh").MaterialOverride = material;
    }

    private void Refuse(string message)
    {
        GD.PrintErr(message);
        GetTree().Quit(1);
    }

    private static string? Flag(string[] args, string prefix) =>
        args.LastOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
}
