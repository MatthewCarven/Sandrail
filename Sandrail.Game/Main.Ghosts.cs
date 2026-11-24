using System.IO;
using System.Linq;
using Godot;
using Sandrail.Core.Ghosts;
using Sandrail.Core.Tracks;
using Sandrail.Game.Cars;

namespace Sandrail.Game;

/// <summary>
/// Ghosts on a track (M4-PLAN, M4c): the best run beside you, see-through, on the race clock; each driver's
/// best saved under the track's fingerprint, so bests last between sessions; G to choose whose.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where</b>: <c>user://ghosts/&lt;fingerprint&gt;/you.ghost</c> and <c>auto.ghost</c> (or under
/// <c>--ghosts=</c>, which the checks use so they never touch his). A track that has moved has another
/// fingerprint, so its old ghosts are simply never found.
/// </para>
/// <para>
/// <b>Per track, whatever the car</b> (decision 6): a best set on another car still counts, and the board says
/// so. <b>On a circuit the ghost is the best whole run</b> (decision 5), laps and all.
/// </para>
/// </remarks>
public partial class Main
{
    /// <summary>Whose ghost shows (decision 4): his best by default, then the autodriver's, then none.</summary>
    public enum GhostChoice
    {
        Yours,
        Autodriver,
        None,
    }

    /// <summary>Static, so starting again from the Esc menu keeps it.</summary>
    private static GhostChoice _ghostChoice = GhostChoice.Yours;

    private string _ghostFolder = "user://ghosts";
    private Ghost? _ghostYou;
    private Ghost? _ghostAuto;
    private GhostCar? _ghostCar;

    /// <summary>The physics tick the race clock started on; -1 before it has.</summary>
    private int _raceGoTick = -1;

    /// <summary>Set when a finished run is a best, so its ghost is saved once its last snapshot is in.</summary>
    private GhostDriver? _saveGhostAs;

    /// <summary>The furthest the car got from the ghost playing this run, metres: a check, printed at the finish.</summary>
    private float _ghostGap;

    private Sandrail.Core.Cars.CarSpec? _printedSpec;
    private string _carPrint = string.Empty;

    /// <summary>The car's fingerprint, worked out again only when its spec changes (the panel swaps it).</summary>
    private string CarPrint
    {
        get
        {
            if (!ReferenceEquals(_printedSpec, _car!.Spec))
            {
                _printedSpec = _car.Spec;
                _carPrint = _car.Spec.Fingerprint();
            }

            return _carPrint;
        }
    }

    private Ghost? ChosenGhost => _ghostChoice switch
    {
        GhostChoice.Yours => _ghostYou,
        GhostChoice.Autodriver => _ghostAuto,
        _ => null,
    };

    /// <summary>The track's ghosts and bests, read from their files, and the ghost car for the one chosen.</summary>
    private bool SetUpGhosts(string[] args)
    {
        _ghostFolder = Flag(args, "--ghosts=") ?? _ghostFolder;
        if (Flag(args, "--ghost=") is string choice)
        {
            GhostChoice? chosen = choice switch
            {
                "you" or "yours" => GhostChoice.Yours,
                "auto" or "autodriver" => GhostChoice.Autodriver,
                "none" => GhostChoice.None,
                _ => null,
            };
            if (chosen is not GhostChoice c)
            {
                Refuse($"--ghost={choice}: it's you, auto or none.");
                return false;
            }

            _ghostChoice = c;
        }

        _ghostYou = LoadGhost(GhostDriver.You);
        _ghostAuto = LoadGhost(GhostDriver.Autodriver);
        foreach (Ghost g in new[] { _ghostYou, _ghostAuto }.OfType<Ghost>())
        {
            bool auto = g.Header.Driver == GhostDriver.Autodriver;
            var run = new RaceRun(g.Header.Seconds, g.Header.Splits, auto, g.Header.Car);
            double? lap = OnACircuit && g.Header.Splits.Length >= _track!.GatesPerLap() ? LapTimes(g.Header.Splits).Min() : null;
            if (auto)
            {
                _bestRunAuto = run;
                _bestLapAuto = lap;
            }
            else
            {
                _bestRunYou = run;
                _bestLapYou = lap;
            }
        }

        _ghostCar = new GhostCar();
        AddChild(_ghostCar);
        _ghostCar.Play(ChosenGhost);
        GD.Print($"Ghosts for {_trackPrint}: yours {Described(_ghostYou)}, the autodriver's {Described(_ghostAuto)}; showing {GhostWords()}");
        return true;
    }

    private static string Described(Ghost? g) => g is null ? "none" : $"{LapTime(g.Header.Seconds)} on {g.Header.Car}";

    private string GhostPath(GhostDriver driver) =>
        $"{_ghostFolder}/{_trackPrint}/{(driver == GhostDriver.You ? "you" : "auto")}.ghost";

    private Ghost? LoadGhost(GhostDriver driver)
    {
        string path = ProjectSettings.GlobalizePath(GhostPath(driver));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            Ghost g = Ghost.Load(File.ReadAllBytes(path));
            if (g.Header.Track != _trackPrint || g.Header.Driver != driver)
            {
                GD.PrintErr($"{path} is another track's or driver's ghost, so it isn't played.");
                return null;
            }

            return g;
        }
        catch (InvalidDataException e)
        {
            GD.PrintErr($"{path} couldn't be read: {e.Message}");
            return null;
        }
    }

    /// <summary>Writes a best's ghost, to a file beside it first so a crash mid-write never loses the old best.</summary>
    private string SaveGhost(Ghost ghost)
    {
        string path = ProjectSettings.GlobalizePath(GhostPath(ghost.Header.Driver));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string writing = path + ".writing";
        File.WriteAllBytes(writing, ghost.Save());
        File.Move(writing, path, overwrite: true);
        return path;
    }

    /// <summary>A finished run's ghost, its last snapshot in: saved, and kept as that driver's, if the run was a best.</summary>
    private void GhostFiled(Ghost ghost)
    {
        if (_ghostCar?.Run is Ghost playing)
        {
            GD.Print($"The car was at most {_ghostGap:F3} m from the ghost it raced ({playing.Header.Driver}, {LapTime(playing.Header.Seconds)})");
        }

        if (_saveGhostAs is not GhostDriver driver || ghost.Header.Driver != driver || _trackPrint is null)
        {
            return;
        }

        _saveGhostAs = null;
        try
        {
            string path = SaveGhost(ghost);
            GD.Print($"Saved as the {(driver == GhostDriver.You ? "your" : "autodriver's")} ghost: {path}");
        }
        catch (IOException e)
        {
            _hud?.Flash($"Couldn't save the ghost: {e.Message}", 6.0);
            return;
        }

        // It becomes the ghost from the next run: the one racing now plays on to its own finish.
        if (driver == GhostDriver.You)
        {
            _ghostYou = ghost;
        }
        else
        {
            _ghostAuto = ghost;
        }
    }

    /// <summary>G: the next ghost, his best, the autodriver's, none.</summary>
    private void ChooseGhost()
    {
        if (_ghostCar is null || _hud is null)
        {
            return;
        }

        _ghostChoice = (GhostChoice)(((int)_ghostChoice + 1) % 3);
        _ghostCar.Play(ChosenGhost);
        _hud.Flash($"Ghost: {GhostWords()}.", 3.0);
    }

    /// <summary>The ghost chosen, in words, for the flash and the board.</summary>
    private string GhostWords() => _ghostChoice switch
    {
        GhostChoice.Yours => _ghostYou is Ghost y ? $"your best, {LapTime(y.Header.Seconds)}" : "your best, none yet on this track",
        GhostChoice.Autodriver => _ghostAuto is Ghost a ? $"the autodriver's best, {LapTime(a.Header.Seconds)}" : "the autodriver's best, none yet on this track",
        _ => "none",
    };

    /// <summary>The race clock's time for the ghost: 0 until the clock starts, then on, past the finish too, so a slower ghost drives on to its own.</summary>
    private double GhostClock(double extraTicks) =>
        _raceGoTick < 0 ? 0.0 : (_ticks - _raceGoTick + extraTicks) / Engine.PhysicsTicksPerSecond;

    /// <summary>Once a frame: the ghost posed between the last two physics ticks, as the car is drawn.</summary>
    private void PoseGhost(double delta) =>
        _ghostCar?.ShowAt(GhostClock(Engine.GetPhysicsInterpolationFraction()), (float)delta, GetViewport().GetCamera3D());

    /// <summary>Once a tick while recording: how far the car is from the ghost at the same moment of its run.</summary>
    private void MeasureGhostGap(Snapshot car)
    {
        if (_ghostCar?.Run is not Ghost ghost)
        {
            return;
        }

        double t = GhostClock(0.0);
        if (t <= ghost.Duration)
        {
            _ghostGap = System.MathF.Max(_ghostGap, System.Numerics.Vector3.Distance(ghost.At(t).Position, car.Position));
        }
    }
}
