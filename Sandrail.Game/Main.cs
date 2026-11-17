using System;
using System.Linq;
using System.Reflection;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Core.Generation;
using Sandrail.Game.Cars;
using Sandrail.Game.Oracles;
using Sandrail.Game.Ui;

namespace Sandrail.Game;

/// <summary>
/// The entry point: the buggy on a 640 m plain (one 32 × 32 map of 20 m tiles), the chase camera, the
/// HUD with its F2 tuning panel, F3 telemetry and F4 recorder, and the oracles.
/// </summary>
/// <remarks>
/// <para>
/// Game flags, after the bare <c>--</c>: <c>--surface=road|dirt|grass|sand</c> (road by default, so the
/// first drive and the oracles' targets agree; M1c makes the plain sand once the pieces bring the
/// road), <c>--oracle=straight|drop|brake|circle</c>, and <c>--spec=&lt;path&gt;</c> to drive another spec
/// file than the shipped buggy, so a variant can be tried without touching what plays. The tuning
/// panel saves to whichever file was loaded.
/// </para>
/// <para>
/// <c>--panels=tuning,telemetry</c> opens either at start, and <c>--record</c> starts a run at start.
/// <c>--screenshot=&lt;path.png&gt;</c> is Claude's eyes, since headless draws nothing: it saves one frame
/// at rest and one turning (<c>-rest</c>, <c>-turn</c>), then quits.
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
    private int _ticks;

    public override void _Ready()
    {
        string version = typeof(Main).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        GD.Print($"Sandrail {version}");

        // The published first output of splitmix64, which Hash64Tests pins. Printed from the Godot
        // side, it says this binary links the same Core the tests ran against.
        GD.Print($"Core linked: Hash64.Mix(0) = 0x{Hash64.Mix(0UL):X16} (expected 0xE220A8397B1DCDAF)");

        string[] args = OS.GetCmdlineUserArgs();
        string surfaceName = Flag(args, "--surface=") ?? "road";
        string? oracle = Flag(args, "--oracle=");
        string specPath = Flag(args, "--spec=") ?? BuggyPath;
        string[] panels = (Flag(args, "--panels=") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        _screenshot = Flag(args, "--screenshot=");

        Surface? surface = Surfaces.Find(surfaceName);
        if (surface is null)
        {
            Refuse($"Unknown surface '{surfaceName}'. Known: {string.Join(", ", Surfaces.All.Select(s => s.Id))}.");
            return;
        }

        if (oracle is not null && !Oracle.IsKnown(oracle))
        {
            Refuse($"Unknown oracle '{oracle}'. Known: straight, drop, brake, circle.");
            return;
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
        SetUpGround(surface);

        float lift = oracle == Oracle.Drop ? Oracle.DropHeight : 0.02f;
        _spawn = new Transform3D(Basis.Identity, new Vector3(0.0f, Buggy.RestHeight(spec) + lift, 0.0f));

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
            AddChild(new Oracle { Kind = oracle, Car = _car });
        }

        if (args.Contains("--record"))
        {
            Recording(_recorder.Start());
        }

        GD.Print($"Driving {spec.Id} {spec.Fingerprint()} on {surface.Id}");
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
            bool turning = _ticks > 2 * Engine.PhysicsTicksPerSecond;
            _car.Driver = turning ? new DriverInput { Throttle = 0.7f, Steer = 0.6f } : default;
            return;
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

        var material = new ShaderMaterial { Shader = new Shader { Code = GroundShader } };
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
