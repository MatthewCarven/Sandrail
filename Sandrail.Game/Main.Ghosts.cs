using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Sandrail.Core.Ghosts;
using Sandrail.Core.Tracks;
using Sandrail.Game.Cars;

namespace Sandrail.Game;

/// <summary>
/// Ghosts on a track (M4-PLAN, M4c): the best run beside you, see-through, on the race clock; each driver's
/// best saved under the track's fingerprint, so bests last between sessions; G to choose whose. And since P4
/// (PROFILE-PLAN, decision 7), each profiled car's best clean run, so car A's best can race beside car B.
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
/// <para>
/// <b>A profiled car's ghost</b> is <c>profile-&lt;car fingerprint&gt;.ghost</c> beside them: its fastest clean profile
/// run on the track, at whatever caution that was. Filed by the car's fingerprint, not its name, because the fingerprint
/// is the car: numbers changed in F2 make another car, with a ghost of its own to race the old one's, and a car set
/// inactive, or one whose file is renamed, keeps its ghost. The header carries the car's name for the board. A profile's
/// runs still set no bests: the autodriver's best is the autodriver driving in play.
/// </para>
/// </remarks>
public partial class Main
{
    /// <summary>Whose ghost shows (decision 4): his best by default, then the autodriver's, then each profiled car's (P4), then none.</summary>
    public enum GhostChoice
    {
        Yours,
        Autodriver,
        Profile,
        None,
    }

    /// <summary>What a profiled car's ghost file is called, before the car's fingerprint.</summary>
    private const string ProfileGhostPrefix = "profile-";

    /// <summary>Static, so starting again from the Esc menu keeps it.</summary>
    private static GhostChoice _ghostChoice = GhostChoice.Yours;

    /// <summary>With <see cref="GhostChoice.Profile"/>: which car's, by fingerprint, and its name for a track it has no ghost on.</summary>
    private static string? _ghostProfileCar;

    private static string _ghostProfileName = string.Empty;

    private string _ghostFolder = "user://ghosts";
    private Ghost? _ghostYou;
    private Ghost? _ghostAuto;
    private GhostCar? _ghostCar;

    /// <summary>Each profiled car's best clean run on this track (P4), the fastest first.</summary>
    private readonly List<Ghost> _profileGhosts = new();

    /// <summary>The profile run's ghost, its last snapshot in, for the profiler to keep if the run was clean and its car's fastest (P4).</summary>
    private Ghost? _profileRunGhost;

    /// <summary>The cars a profile kept a ghost for, by fingerprint, for its last word; static, as Profile every car starts the scene again.</summary>
    private static readonly HashSet<string> ProfileGhostsKept = new();

    /// <summary>The physics tick the race clock started on; -1 before it has.</summary>
    private int _raceGoTick = -1;

    /// <summary>Set when a finished run is a best, so its ghost is saved once its last snapshot is in.</summary>
    private GhostDriver? _saveGhostAs;

    /// <summary>The furthest the car got from the ghost playing this run, metres, and when in the run: a check, printed at the finish.</summary>
    private float _ghostGap;

    private double _ghostGapAt;

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
        GhostChoice.Profile => _profileGhosts.FirstOrDefault(g => g.Header.Car == _ghostProfileCar),
        _ => null,
    };

    /// <summary>The track's ghosts and bests, read from their files, and the ghost car for the one chosen.</summary>
    private bool SetUpGhosts(string[] args)
    {
        _ghostFolder = Flag(args, "--ghosts=") ?? _ghostFolder;
        _ghostYou = LoadGhost(GhostDriver.You);
        _ghostAuto = LoadGhost(GhostDriver.Autodriver);
        LoadProfileGhosts();
        if (Flag(args, "--ghost=") is string choice)
        {
            if (GhostNamed(choice) is not { } named)
            {
                Refuse($"--ghost={choice}: it's you, auto, none, or a car profiled on this track by its name or fingerprint"
                    + (_profileGhosts.Count == 0 ? " (none has been)." : $" ({string.Join(", ", _profileGhosts.Select(ProfileName))})."));
                return false;
            }

            Choose(named.Choice, named.Profile);
        }

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
        string profiled = _profileGhosts.Count == 0 ? "no profiled car's"
            : $"{_profileGhosts.Count} profiled car{(_profileGhosts.Count == 1 ? "'s" : "s'")} ({string.Join(", ", _profileGhosts.Select(g => $"{ProfileName(g)} {Described(g)}"))})";
        GD.Print($"Ghosts for {_trackPrint}: yours {Described(_ghostYou)}, the autodriver's {Described(_ghostAuto)}, {profiled}; showing {GhostWords()}");
        return true;
    }

    private static string Described(Ghost? g) => g is null ? "none"
        : $"{LapTime(g.Header.Seconds)}{(g.Header.Caution is float c ? $" at {c * 100.0f:F0}%" : string.Empty)} on {g.Header.Car}";

    private string GhostPath(GhostDriver driver) =>
        $"{_ghostFolder}/{_trackPrint}/{(driver == GhostDriver.You ? "you" : "auto")}.ghost";

    private string ProfileGhostPath(string car) => $"{_ghostFolder}/{_trackPrint}/{ProfileGhostPrefix}{car}.ghost";

    private Ghost? LoadGhost(GhostDriver driver) => ReadGhost(ProjectSettings.GlobalizePath(GhostPath(driver)), driver, null);

    /// <summary>A ghost's file read, if it's there and is this track's, this driver's and (for a profiled car's) this car's.</summary>
    private Ghost? ReadGhost(string path, GhostDriver driver, string? car)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            Ghost g = Ghost.Load(File.ReadAllBytes(path));
            if (g.Header.Track != _trackPrint || g.Header.Driver != driver || (car is not null && g.Header.Car != car))
            {
                GD.PrintErr($"{path} is another track's, driver's or car's ghost, so it isn't played.");
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

    /// <summary>The profiled cars' ghosts on this track (P4), one a car, the fastest first.</summary>
    private void LoadProfileGhosts()
    {
        _profileGhosts.Clear();
        string folder = ProjectSettings.GlobalizePath($"{_ghostFolder}/{_trackPrint}");
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (string path in Directory.GetFiles(folder, $"{ProfileGhostPrefix}*.ghost"))
        {
            string car = Path.GetFileNameWithoutExtension(path)[ProfileGhostPrefix.Length..];
            if (ReadGhost(path, GhostDriver.Autodriver, car) is Ghost g)
            {
                _profileGhosts.Add(g);
            }
        }

        SortProfileGhosts();
    }

    private void SortProfileGhosts() =>
        _profileGhosts.Sort((a, b) => a.Header.Seconds != b.Header.Seconds
            ? a.Header.Seconds.CompareTo(b.Header.Seconds)
            : string.CompareOrdinal(a.Header.Car, b.Header.Car));

    /// <summary>Writes a ghost, to a file beside it first so a crash mid-write never loses the old one.</summary>
    private static string SaveGhost(Ghost ghost, string where)
    {
        string path = ProjectSettings.GlobalizePath(where);
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
            GD.Print($"The car was at most {_ghostGap:F3} m from the ghost it raced ({playing.Header.Driver}{(playing.Header.CarName is string n ? $", {n}" : string.Empty)}, "
                + $"{LapTime(playing.Header.Seconds)}), {_ghostGapAt:F2} s in");
        }

        // A profile's run is the profiler's to keep or not (P4), once it knows whether the run was clean.
        if (Profiling)
        {
            _profileRunGhost = ghost;
            return;
        }

        if (_saveGhostAs is not GhostDriver driver || ghost.Header.Driver != driver || _trackPrint is null)
        {
            return;
        }

        _saveGhostAs = null;
        try
        {
            string path = SaveGhost(ghost, GhostPath(driver));
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

    /// <summary>
    /// A profile run has ended (P4): its ghost kept as its car's when the run was clean and is the fastest clean run that
    /// car has made on this track. A tie or an unclean run never replaces one. In words if it was kept, else null.
    /// </summary>
    private string? KeepProfileGhost(bool clean)
    {
        Ghost? ghost = _profileRunGhost;
        _profileRunGhost = null;
        if (!clean || ghost is null || _trackPrint is null)
        {
            return null;
        }

        Ghost? old = _profileGhosts.FirstOrDefault(g => g.Header.Car == ghost.Header.Car);
        if (old is not null && !(ghost.Header.Seconds < old.Header.Seconds))
        {
            return null;
        }

        string path;
        try
        {
            path = SaveGhost(ghost, ProfileGhostPath(ghost.Header.Car));
        }
        catch (IOException e)
        {
            _hud?.Flash($"Couldn't save the car's ghost: {e.Message}", 6.0);
            return null;
        }

        // It races from the next run, as a best of his does; one playing now plays on.
        if (old is not null)
        {
            _profileGhosts.Remove(old);
        }

        _profileGhosts.Add(ghost);
        SortProfileGhosts();
        ProfileGhostsKept.Add(ghost.Header.Car);
        GD.Print($"Kept as {ProfileName(ghost)}'s ghost on this track{(old is null ? string.Empty : $", {old.Header.Seconds - ghost.Header.Seconds:F2} s faster than the last")}: {path}");
        return old is null ? "kept as its ghost" : "its best, kept as its ghost";
    }

    /// <summary>
    /// A profiled car's name for the board: its name when it was profiled, and its fingerprint too when another car profiled
    /// here went by the same name (the same file before and after a change in F2).
    /// </summary>
    private string ProfileName(Ghost g)
    {
        string name = g.Header.CarName ?? g.Header.Car;
        return _profileGhosts.Count(o => (o.Header.CarName ?? o.Header.Car) == name) > 1 ? $"{name} ({g.Header.Car})" : name;
    }

    /// <summary>A ghost named on the command line: you, auto, none, or a car profiled on this track, by name or fingerprint (P4).</summary>
    private (GhostChoice Choice, Ghost? Profile)? GhostNamed(string name) => name switch
    {
        "you" or "yours" => (GhostChoice.Yours, null),
        "auto" or "autodriver" => (GhostChoice.Autodriver, null),
        "none" => (GhostChoice.None, null),
        _ => ProfileGhostNamed(name) is Ghost g ? (GhostChoice.Profile, g) : null,
    };

    /// <summary>The ghost of a car profiled on this track, by its fingerprint or its name; the fastest if two had the name.</summary>
    private Ghost? ProfileGhostNamed(string name) => _profileGhosts.FirstOrDefault(g =>
        string.Equals(g.Header.Car, name, System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(g.Header.CarName, name, System.StringComparison.OrdinalIgnoreCase));

    private void Choose(GhostChoice choice, Ghost? profile)
    {
        _ghostChoice = choice;
        if (profile is not null)
        {
            _ghostProfileCar = profile.Header.Car;
            _ghostProfileName = ProfileName(profile);
        }
    }

    /// <summary>G: the next ghost, his best, the autodriver's, each profiled car's on this track (P4), none.</summary>
    private void ChooseGhost()
    {
        if (_ghostCar is null || _hud is null)
        {
            return;
        }

        var cycle = new List<(GhostChoice Choice, Ghost? Profile)> { (GhostChoice.Yours, null), (GhostChoice.Autodriver, null) };
        cycle.AddRange(_profileGhosts.Select(g => (GhostChoice.Profile, (Ghost?)g)));
        cycle.Add((GhostChoice.None, null));
        int at = _ghostChoice == GhostChoice.Profile
            ? cycle.FindIndex(c => c.Profile?.Header.Car == _ghostProfileCar)
            : cycle.FindIndex(c => c.Choice == _ghostChoice);

        // A car chosen on another track and never profiled on this one: on to the first car profiled here, or none.
        (GhostChoice next, Ghost? profile) = cycle[((at < 0 ? 1 : at) + 1) % cycle.Count];
        Choose(next, profile);
        _ghostCar.Play(ChosenGhost);
        _hud.Flash($"Ghost: {GhostWords()}.", 3.0);
    }

    /// <summary>
    /// The ghost chosen, in words, for the flash and the board; a best saved since this run began races from
    /// the next, and says so (his first ghost looked missing when the board named it, 2026-11-24).
    /// </summary>
    private string GhostWords()
    {
        string words = _ghostChoice switch
        {
            GhostChoice.Yours => _ghostYou is Ghost y ? $"your best, {LapTime(y.Header.Seconds)}" : "your best, none yet on this track",
            GhostChoice.Autodriver => _ghostAuto is Ghost a ? $"the autodriver's best, {LapTime(a.Header.Seconds)}" : "the autodriver's best, none yet on this track",
            GhostChoice.Profile => ChosenGhost is Ghost p ? ProfileWords(p) : $"{_ghostProfileName}'s profile best, none on this track",
            _ => "none",
        };
        return ChosenGhost is Ghost chosen && !ReferenceEquals(chosen, _ghostCar?.Run) ? $"{words}, from the next run (Backspace)" : words;
    }

    /// <summary>A profiled car's ghost in words: whose, its time, the caution it was driven at, and whether it's the car being driven.</summary>
    private string ProfileWords(Ghost g) =>
        $"{ProfileName(g)}'s profile best, {LapTime(g.Header.Seconds)}{(g.Header.Caution is float c ? $" at {c * 100.0f:F0}%" : string.Empty)}"
        + (g.Header.Car == CarPrint ? " (this car)" : string.Empty);

    /// <summary>The race clock's time for the ghost: 0 until the clock starts, then on, past the finish too, so a slower ghost drives on to its own.</summary>
    private double GhostClock(double extraTicks) =>
        _raceGoTick < 0 ? 0.0 : (_ticks - _raceGoTick + extraTicks) / GameClock.TicksPerSecond;

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
            float gap = System.Numerics.Vector3.Distance(ghost.At(t).Position, car.Position);
            if (gap > _ghostGap)
            {
                _ghostGap = gap;
                _ghostGapAt = t;
            }
        }
    }
}
