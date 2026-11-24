using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Profiles;
using Sandrail.Core.Tracks;
using Sandrail.Game.Cars;

namespace Sandrail.Game;

/// <summary>
/// Profiling in the game (PROFILE-PLAN, P3): the autodriver drives the car over the track from its start gate at
/// rising cautions, 50% to 100% by 5% (decision 2, from 50 at his word), until a run isn't clean, and each run is a
/// row in the track's sheet; every finished run of his goes in beside them (decision 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>It's the race, driven by the autodriver</b> (fact 6): the run starts again as Backspace starts it, the
/// autodriver takes the car as P gives it, and the clock, the gates and the meter are the race's own. A profile's
/// runs set no bests and save no ghosts.
/// </para>
/// <para>
/// <b>A run ends</b> at the finish; or stuck, a metre no further along in 8 s; or tipped over or off the world, when
/// the auto-respawn would put it back (a respawn ends a profile run rather than starting it again, so the row says
/// where it went wrong); or past 10 minutes.
/// </para>
/// <para>
/// <b>Profile every car</b> changes car between sweeps, which starts the scene again (P2), so what's left of the
/// profile is kept in statics, as the editor's session is. <b>As fast as the machine goes</b> (decision 6): the time
/// scale at ×16 and up to 64 physics ticks a frame; the physics is the same at any speed (2026-11-24), so only the
/// time it takes changes. Esc stops it, and the rows already written stay.
/// </para>
/// </remarks>
public partial class Main
{
    private const float ProfileFirst = 0.50f;
    private const float ProfileLast = 1.00f;
    private const float ProfileStep = 0.05f;
    private const double ProfileScale = 16.0;
    private const int ProfileTicksPerFrame = 64;
    private const float ProfileStuckSeconds = 8.0f;
    private const double ProfileTimeLimitS = 600.0;

    // The profile across scene restarts: the cars still to do, where the rows go, what's waiting to be written.
    private static bool _profileActive;
    private static readonly Queue<string> ProfileCars = new();
    private static int _profileCarsTotal;
    private static int _profileCarNumber;
    private static int _profileRows;
    private static string? _profileReturnCar;
    private static float _profileOldCaution = Autodriver.DefaultCaution;
    private static int _profileOldTicksPerFrame = 8;
    private static bool _profileFromFlag;
    private static bool _profileFlagUsed;
    private static string? _profileSaidAfter;
    private static string _profilesFolder = string.Empty;
    private static string? _sheetLastFile;

    /// <summary>Rows not yet written: a spreadsheet holding the file open (fact 8) makes them wait, never lost.</summary>
    private static readonly List<(string Folder, string File, string Header, string Row, string CarName, CarSpec Car)> PendingRows = new();

    private static readonly string GameVersion = ShortVersion(typeof(Main).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");

    private CarSpec? _stockSpec;

    /// <summary>Whether this scene's car is being swept now.</summary>
    private bool _sweeping;

    private float _profileCaution;
    private float _profileFurthest;
    private int _profileFurthestTick;
    private ulong _profileEndedFrame = ulong.MaxValue;
    private ulong _pendingRetryAtMs;

    /// <summary>Whether a profile is driving the car, so the race files nothing and the keys are the profiler's.</summary>
    private bool Profiling => _sweeping;

    /// <summary>Whether Esc is the profiler's this frame (it stops the profile), not the menu's.</summary>
    private bool ProfileTakesEsc => _sweeping || _profileEndedFrame == Engine.GetProcessFrames();

    /// <summary>The game's version as a row carries it: <c>0.0.1+f5f635f</c>.</summary>
    private static string ShortVersion(string version)
    {
        int plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 && version.Length > plus + 8 ? version[..(plus + 8)] : version;
    }

    /// <summary>
    /// Where the sheets go (<c>--profiles=</c> for checks), a profile carried on from the scene before (a change of
    /// car), or one asked for by <c>--profile=this|all</c>.
    /// </summary>
    private void SetUpProfiles(string[] args)
    {
        _profilesFolder = ProjectSettings.GlobalizePath(Flag(args, "--profiles=") ?? "user://profiles");
        if (_profileSaidAfter is string said)
        {
            _profileSaidAfter = null;
            _hud?.Flash(said, 8.0);
        }

        if (_profileActive)
        {
            Callable.From(BeginSweep).CallDeferred();
            return;
        }

        if (!_profileFlagUsed && Flag(args, "--profile=") is string which)
        {
            _profileFlagUsed = true;
            if (which is not ("this" or "all"))
            {
                Refuse($"--profile={which}: it's this (the car driven) or all (every car in the garage).");
                return;
            }

            _profileFromFlag = true;
            Callable.From(() => StartProfile(which == "all")).CallDeferred();
        }
    }

    /// <summary>The Esc menu's Profile this car and Profile every car.</summary>
    private void StartProfile(bool every)
    {
        if (_track is null || _race is null || _testDriveFrom is not null || _car is null || _tuning is null || _hud is null)
        {
            _hud?.Flash("A profile needs a track raced from its start gate.", 4.0);
            return;
        }

        ProfileCars.Clear();
        string driving = _tuning.SpecPath;
        foreach (string car in every ? Garage.All().Where(c => c.Spec is not null).Select(c => c.Path) : [driving])
        {
            ProfileCars.Enqueue(car);
        }

        _profileCarsTotal = ProfileCars.Count;
        _profileCarNumber = 0;
        _profileRows = 0;
        _profileReturnCar = driving;
        _profileOldCaution = _caution;
        _profileOldTicksPerFrame = Engine.MaxPhysicsStepsPerFrame;
        _profileActive = true;
        GD.Print($"Profiling {(every ? $"every car ({_profileCarsTotal})" : Garage.NameOf(driving))} on {_track.Track.Id} ({_trackPrint}), "
            + $"cautions {ProfileFirst * 100.0f:F0}% to {ProfileLast * 100.0f:F0}%, into {_profilesFolder}");
        ProfileNextCar();
    }

    /// <summary>The next car to sweep: this scene's, or the scene again with it.</summary>
    private void ProfileNextCar()
    {
        if (ProfileCars.Count == 0)
        {
            EndProfile("done");
            return;
        }

        string next = ProfileCars.Dequeue();
        _profileCarNumber++;
        if (next == _tuning!.SpecPath)
        {
            BeginSweep();
            return;
        }

        StartAgainWithCar(_args, next);
    }

    /// <summary>This scene's car, at rising cautions from the first.</summary>
    private void BeginSweep()
    {
        if (!_profileActive || _car is null || _track is null || _race is null || _tuning is null)
        {
            return;
        }

        _sweeping = true;
        _profileCaution = ProfileFirst;
        Engine.MaxPhysicsStepsPerFrame = ProfileTicksPerFrame;
        GameClock.SetScale(ProfileScale);
        if (_tuning.Visible)
        {
            _tuning.Toggle();
        }

        BeginProfileRun();
    }

    /// <summary>
    /// A run from the start gate at this caution, on the line built for the car as it is. The autodriver takes the car
    /// only once the race is armed (<see cref="ProfileTick"/>), so the clock starts on its first throttle, every run
    /// alike: handed the car at once, it had a tick's head start whenever the run before ended inside a physics tick,
    /// and the stock car's 80% came out 56.70 after seven runs against 56.73 as the first (2026-11-25).
    /// </summary>
    private void BeginProfileRun()
    {
        _caution = _profileCaution;
        RestartRun();
        _routeLine = _track!.RouteLine(_car!.Spec);
        _autodriver = null;
        DrawLine(_car.Spec);
        _profileFurthest = 0.0f;
        _profileFurthestTick = _ticks;
        _hud!.Banner = $"PROFILING   {Garage.NameOf(_tuning!.SpecPath)}{(_profileCarsTotal > 1 ? $", car {_profileCarNumber} of {_profileCarsTotal}" : string.Empty)}"
            + $"   aggression {_caution * 100.0f:F0}%   Esc stops";
    }

    /// <summary>Once a tick while sweeping, after the race has seen it: the run's end, if it's come.</summary>
    private void ProfileTick()
    {
        // Until the run is armed afresh the last one still reads as finished.
        if (_race is null || _raceArmIn > 0)
        {
            return;
        }

        if (!_race.Running && !_race.Finished)
        {
            // Armed: the car is at the start, at rest; the autodriver drives from the next tick, and its first
            // throttle starts the clock.
            _autodriver ??= new Autodriver(_routeLine!, _car!.Spec, _caution);
            _profileFurthestTick = _ticks;
            return;
        }

        if (_race.Finished)
        {
            EndProfileRun(null);
            return;
        }

        if (_meterAlong > _profileFurthest + 1.0f)
        {
            _profileFurthest = _meterAlong;
            _profileFurthestTick = _ticks;
        }

        if (_ticks - _profileFurthestTick > ProfileStuckSeconds * GameClock.TicksPerSecond)
        {
            EndProfileRun($"stuck for {ProfileStuckSeconds:F0} s, {_meterAlong:F0} m along");
        }
        else if (_race.Elapsed(_ticks / (double)GameClock.TicksPerSecond) > ProfileTimeLimitS)
        {
            EndProfileRun($"out of time, {_meterAlong:F0} m along");
        }
    }

    /// <summary>A run's row; then the next caution if it was clean and there's one left, else the next car.</summary>
    private void EndProfileRun(string? why)
    {
        RunSummary run = RunSoFar();
        WriteSheetRow("auto", _profileCaution, run);
        string time = run.Seconds is double s ? LapTime(s) : $"didn't finish ({why ?? "no reason"})";
        string said = $"{Garage.NameOf(_tuning!.SpecPath)} at {_profileCaution * 100.0f:F0}%: {time}, {(run.Clean ? "clean" : "not clean")}";
        GD.Print($"Profile: {said} | splits {string.Join(", ", run.Splits.Select(LapTime))} | top {run.TopMps * 3.6f:F0} km/h, "
            + $"off the road {run.OffRoadSeconds:F2} s, worst slide {run.WorstSlideDeg:F0}°, spins {run.Spins}, respawns {run.Respawns}");
        _hud?.Flash(said, 3.0);

        if (run.Clean && _profileCaution < ProfileLast - 0.001f)
        {
            _profileCaution = MathF.Round((_profileCaution + ProfileStep) / ProfileStep) * ProfileStep;
            BeginProfileRun();
            return;
        }

        _sweeping = false;
        _autodriver = null;
        _hud!.Banner = null;
        ProfileNextCar();
    }

    /// <summary>Esc while sweeping: the profile stops where it is; the rows written stay.</summary>
    private void StopProfile()
    {
        _profileEndedFrame = Engine.GetProcessFrames();
        ProfileCars.Clear();
        EndProfile("stopped");
    }

    /// <summary>The profile over: time back to its own speed, the car the profile began with back, and where the rows went.</summary>
    private void EndProfile(string how)
    {
        _profileActive = false;
        _sweeping = false;
        _autodriver = null;
        _caution = _profileOldCaution;
        Engine.MaxPhysicsStepsPerFrame = _profileOldTicksPerFrame;
        GameClock.SetScale(1.0);
        if (_hud is not null)
        {
            _hud.Banner = null;
        }

        FlushPendingRows();
        string said = $"Profile {how}: {_profileRows} run{(_profileRows == 1 ? string.Empty : "s")} written"
            + (_sheetLastFile is string file ? $" to {file}" : string.Empty)
            + (PendingRows.Count > 0 ? $"; {PendingRows.Count} waiting for the sheet to be closed" : string.Empty) + ".";
        GD.Print(said);
        if (_profileFromFlag)
        {
            HoldPendingRows();
            GetTree().Quit();
            return;
        }

        RestartRun();
        if (_profileReturnCar is string back && back != _tuning?.SpecPath)
        {
            _profileSaidAfter = said;
            StartAgainWithCar(_args, back);
            return;
        }

        _hud?.Flash(said, 8.0);
    }

    /// <summary>A run as a row in the track's sheet, and its car in <c>cars.csv</c>; held if the file can't be written.</summary>
    private void WriteSheetRow(string driver, float? caution, RunSummary run)
    {
        if (_track is null || _trackPrint is null || _car is null || _tuning is null || _runMeter is null)
        {
            return;
        }

        SheetShape shape = SheetShape.Of(_track);
        string name = Garage.NameOf(_tuning.SpecPath) + (_tuning.Unsaved ? " (changed)" : string.Empty);
        _stockSpec ??= Garage.Read(Garage.StockPath).Spec ?? new CarSpec();
        var row = new SheetRow(DateTime.Now, GameVersion, driver, name, CarPrint, caution, CarSheet.Differences(_car.Spec, _stockSpec), run);
        PendingRows.Add((_profilesFolder, CarSheet.FileName(_track.Track.Name, _trackPrint), CarSheet.Line(CarSheet.Header(shape)),
            CarSheet.Line(CarSheet.Cells(shape, row)), name, _car.Spec));
        FlushPendingRows();
    }

    /// <summary>Whether rows have had to wait since the last time the queue was empty, so its emptying can be said.</summary>
    private static bool _rowsWaited;

    /// <summary>Writes every row waiting, in order, stopping at the first the file refuses (a spreadsheet has it open).</summary>
    private void FlushPendingRows()
    {
        int before = PendingRows.Count;
        while (PendingRows.Count > 0)
        {
            var (folder, file, header, row, carName, car) = PendingRows[0];
            try
            {
                CarSheet.AddCar(folder, carName, car);
                _sheetLastFile = CarSheet.Append(folder, file, header, row);
                PendingRows.RemoveAt(0);
                _profileRows++;
            }
            catch (IOException e)
            {
                _rowsWaited = true;
                GD.PrintErr($"The sheet couldn't be written: {e.Message}");
                _hud?.Flash($"The sheet is open somewhere (a spreadsheet?): {PendingRows.Count} row{(PendingRows.Count == 1 ? string.Empty : "s")} "
                    + "waiting, written as soon as it's closed.", 6.0);
                return;
            }
        }

        if (_rowsWaited)
        {
            _rowsWaited = false;
            _hud?.Flash($"The sheet's free again: the {before} row{(before == 1 ? " that waited is" : "s that waited are")} written now.", 4.0);
        }
    }

    /// <summary>
    /// On the way out: rows a spreadsheet is still holding the sheet open against go into a file beside it,
    /// <c>Shakedown 8a6323aa (written while open).csv</c>, so none is lost (fact 8).
    /// </summary>
    private static void HoldPendingRows()
    {
        FlushPendingRowsQuietly();
        foreach (var (folder, file, header, row, _, _) in PendingRows)
        {
            string aside = $"{Path.GetFileNameWithoutExtension(file)} (written while open){Path.GetExtension(file)}";
            try
            {
                string path = CarSheet.Append(folder, aside, header, row);
                GD.Print($"The sheet was still open: a row kept in {path}");
            }
            catch (IOException e)
            {
                GD.PrintErr($"A row couldn't be kept anywhere: {e.Message}");
            }
        }

        PendingRows.Clear();
    }

    /// <summary>The rows waiting, written if the sheet has been let go since, without a word on the screen.</summary>
    private static void FlushPendingRowsQuietly()
    {
        while (PendingRows.Count > 0)
        {
            var (folder, file, header, row, carName, car) = PendingRows[0];
            try
            {
                CarSheet.AddCar(folder, carName, car);
                _sheetLastFile = CarSheet.Append(folder, file, header, row);
                PendingRows.RemoveAt(0);
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    /// <summary>The window closing: rows still waiting are kept beside the sheet.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            HoldPendingRows();
        }
    }

    /// <summary>Once a frame: rows held by an open spreadsheet are tried again every few seconds.</summary>
    private void RetryPendingRows()
    {
        if (PendingRows.Count == 0 || Time.GetTicksMsec() < _pendingRetryAtMs)
        {
            return;
        }

        _pendingRetryAtMs = Time.GetTicksMsec() + 3000;
        FlushPendingRows();
    }

    /// <summary>While sweeping, the keys are the profiler's: Esc stops it; C and L still work.</summary>
    private void WatchProfileKeys()
    {
        if (Input.IsActionJustPressed(Controls.Menu))
        {
            StopProfile();
            return;
        }

        if (Input.IsActionJustPressed(Controls.Camera))
        {
            _camera?.ToggleView();
        }

        if (Input.IsActionJustPressed(Controls.ShowLine) && _lineView is not null)
        {
            _lineView.Visible = !_lineView.Visible;
        }
    }

    /// <summary>The Esc menu's Open the profiles folder: the folder in the system's file browser, made first if it's new.</summary>
    private string OpenProfilesFolder()
    {
        Directory.CreateDirectory(_profilesFolder);
        OS.ShellOpen(_profilesFolder);
        return _profilesFolder;
    }
}
