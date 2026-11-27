using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Sandrail.Core.Profiles;
using Sandrail.Core.Tracks;
using Sandrail.Game.Cars;
using Sandrail.Game.World;

namespace Sandrail.Game;

/// <summary>
/// Profile every car on every track (2026-11-25, his ask): Profile every car, track after track, unattended, then back
/// to the track and car he started from. The tracks are Load a track's, the game's own and every saved one; the cars are
/// Profile every car's, the stock car and his active ones. <b>Profile unprofiled cars</b> (2026-11-27, his idea) is the
/// same chain driving only what the sheets haven't got, so a run that stopped is picked up where it left off.
/// </summary>
/// <remarks>
/// <para>
/// <b>It chains what's there</b>: each track is started as Load a track starts one, with the garage's first car, and
/// Profile every car runs on it as it always does (the sweep, the rows in that track's own sheet, the profile ghosts, the
/// rows held while a spreadsheet has the file). When that ends, the next track. Statics carry it across the scenes, as
/// the profile's own do, and every load is watched (<see cref="ChainReload"/>): one that never gets going is tried again,
/// then skipped, and the chain carries on.
/// </para>
/// <para>
/// <b>A track that won't lay soundly is skipped</b> (a half-built editor track), and so is <b>a track already profiled
/// under the same fingerprint</b>: his saved <c>gen-5.json</c> (Seed 5, saved before codes) is the game's <c>gen:5v1</c>,
/// the same sheet. The fingerprint is the scene's own, made as the track is built, so a duplicate costs a track's load
/// and nothing more. Esc stops the lot, and the end of it says how it went, on the board and in the log.
/// </para>
/// <para>
/// <b>Profile unprofiled cars</b> reads each track's sheets as its scene comes up (<see cref="ProfileProgress"/>): a car
/// whose sweep there ended (a run that wasn't clean, or clean at 100%) is passed over, one stopped partway goes on from the
/// caution after its last clean run (his pick: no rows twice), and one never driven there starts at 50%. A track with
/// nothing left costs its load.
/// </para>
/// </remarks>
public partial class Main
{
    /// <summary>
    /// A track to profile: its path, its name for the board (a saved one's file name, since a saved generated track and
    /// the game's share a name), and its whole label for the log.
    /// </summary>
    private sealed record EverywhereTrack(string Path, string Short, string Label);

    /// <summary>A track left out, and why: in a few words for the board, in full for the log.</summary>
    private sealed record SkippedTrack(EverywhereTrack Track, string Short, string Long);

    // Every car on every track across scene restarts: the tracks still to do, those done by fingerprint, those skipped,
    // the cars that wouldn't start, the flags each track starts with, and where to go back to.
    private static bool _everywhereActive;
    private static bool _everywhereUnprofiled;
    private static readonly Queue<EverywhereTrack> EverywhereTracks = new();
    private static readonly Dictionary<string, EverywhereTrack> EverywhereDone = new();
    private static readonly List<SkippedTrack> EverywhereSkipped = new();
    private static readonly List<string> EverywhereCarsSkipped = new();
    private static int _everywhereTotal;
    private static int _everywhereNumber;
    private static EverywhereTrack? _everywhereTrack;
    private static int _everywhereFinished;
    private static int _everywhereAlready;
    private static int _everywhereRuns;
    private static int _everywhereGhosts;
    private static string[] _everywhereBaseArgs = [];
    private static string[] _everywhereReturnArgs = [];
    private static string? _everywhereReturnCar;
    private static readonly System.Diagnostics.Stopwatch EverywhereClock = new();

    /// <summary>
    /// Flags the tracks are started without, beside the place flags: a run recorded from the start, a ghost chosen by a
    /// car's name (refused on a track where it hasn't been profiled), the autodriver handed the car. The way back puts
    /// them back.
    /// </summary>
    private static readonly string[] EverywhereDropped = ["--record", "--autodrive", "--ghost=", "--spec="];

    /// <summary>The chain's name, on the board and in the log.</summary>
    private static string EverywhereName => _everywhereUnprofiled ? "Unprofiled cars on every track" : "Every car on every track";

    /// <summary>
    /// The Esc menu's Profile every car on every track and Profile unprofiled cars, and <c>--profile=everywhere</c> and
    /// <c>--profile=unprofiled</c>.
    /// </summary>
    private void StartProfileEverywhere(bool unprofiled)
    {
        string what = unprofiled ? "Profile unprofiled cars" : "Profile every car on every track";
        if (_car is null || _tuning is null || _hud is null || _testDriveFrom is not null)
        {
            _hud?.Flash($"{what} starts from a drive, not a test drive.", 4.0);
            return;
        }

        PieceCatalogue catalogue = _menuCatalogue ??= TrackView.LoadCatalogue();
        EverywhereTracks.Clear();
        EverywhereDone.Clear();
        EverywhereSkipped.Clear();
        EverywhereCarsSkipped.Clear();
        foreach ((string path, Track track, bool saved) in TrackView.AllTracks())
        {
            var entry = new EverywhereTrack(path, saved ? Path.GetFileName(path) : track.Name,
                saved ? $"{TrackLabel(track)}, saved as {Path.GetFileName(path)}" : TrackLabel(track));
            IReadOnlyList<string> problems;
            try
            {
                problems = track.Lay(catalogue).Problems;
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException)
            {
                problems = [e.Message];
            }

            if (problems.Count > 0)
            {
                string more = problems.Count > 1 ? $", and {problems.Count - 1} more" : string.Empty;
                EverywhereSkipped.Add(new SkippedTrack(entry, "unfinished", $"unfinished: {problems[0]}{more}"));
                continue;
            }

            EverywhereTracks.Enqueue(entry);
        }

        _everywhereUnprofiled = unprofiled;
        _everywhereTotal = EverywhereTracks.Count;
        _everywhereNumber = 0;
        _everywhereTrack = null;
        _everywhereFinished = 0;
        _everywhereAlready = 0;
        _everywhereRuns = 0;
        _everywhereGhosts = 0;
        _everywhereBaseArgs = _args.Where(a => !PlaceFlags.Concat(EverywhereDropped).Any(p => a.StartsWith(p, StringComparison.Ordinal))).ToArray();
        _everywhereReturnArgs = _args;
        _everywhereReturnCar = _tuning.SpecPath;
        _everywhereActive = true;
        EverywhereClock.Restart();
        int cars = ActiveCars().Count();
        GD.Print($"Profiling {(unprofiled ? $"the cars not yet profiled, of {cars}," : $"every car ({cars})")} on every track ({_everywhereTotal}"
            + $"{(EverywhereSkipped.Count > 0 ? $", {EverywhereSkipped.Count} unfinished left out" : string.Empty)}), into {_profilesFolder}");
        foreach (SkippedTrack skipped in EverywhereSkipped)
        {
            GD.Print($"  Left out: {skipped.Track.Label}, {skipped.Long}");
        }

        EverywhereNextTrack();
    }

    /// <summary>
    /// The next track, started as Load a track starts one, with the garage's first car so its sweep needs no change of
    /// car; or the end. From what outlives the scenes, so the watch can move on to it when a track won't start.
    /// </summary>
    private static void EverywhereNextTrack()
    {
        if (EverywhereTracks.Count == 0)
        {
            EndEverywhere("done");
            return;
        }

        EverywhereTrack next = EverywhereTracks.Dequeue();
        _everywhereNumber++;
        _everywhereTrack = next;
        string first = ActiveCars().FirstOrDefault()?.Path ?? Garage.StockPath;
        NextArgs = _everywhereBaseArgs.Append($"--track={next.Path}")
            .Concat(Garage.IsStock(first) ? [] : [$"--spec={first}"])
            .ToArray();
        GD.Print($"Every track: {_everywhereNumber} of {_everywhereTotal}, {next.Label}, {Took(EverywhereClock.Elapsed)} in, {MemoryWords()}: {string.Join(' ', NextArgs)}");
        ChainReload(next.Short);
    }

    /// <summary>
    /// The game's memory and the machine's, in words for the log: <c>the game 1.3 GB, the machine 9.4 GB to spare</c>.
    /// His 7.7 h run ended out of memory with the game at 1.4 GB and the rest of the machine's taken (2026-11-27), so a
    /// run that dies says which it was. To spare is what Windows can still hand out, its memory and swap file together,
    /// which is what ran out.
    /// </summary>
    private static string MemoryWords()
    {
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        var status = new MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatus>() };
        string spare = OperatingSystem.IsWindows() && GlobalMemoryStatusEx(ref status)
            ? $", the machine {status.AvailPageFile / 1073741824.0:F1} GB to spare" : string.Empty;
        return $"the game {me.PrivateMemorySize64 / 1073741824.0:F1} GB{spare}";
    }

    /// <summary>Windows' MEMORYSTATUSEX: the machine's memory, and what's left to hand out (its commit limit, less what's committed).</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    /// <summary>
    /// A track's scene is up: Profile every car on it (or, for Profile unprofiled cars, the cars its sheets haven't got),
    /// unless its fingerprint has been profiled already in this run (the same track saved twice), when it's skipped and
    /// the count of tracks goes down by one.
    /// </summary>
    private void EverywhereArrived()
    {
        if (!_everywhereActive || _everywhereTrack is not EverywhereTrack here)
        {
            return;
        }

        if (_track is null || _race is null || _trackPrint is null || _testDriveFrom is not null || _car is null || _tuning is null)
        {
            EverywhereSkipped.Add(new SkippedTrack(here, "couldn't be raced", "couldn't be raced from its start gate"));
            EverywhereNextTrack();
            return;
        }

        if (EverywhereDone.TryGetValue(_trackPrint, out EverywhereTrack? same))
        {
            GD.Print($"Every track: {here.Label} is the same track as {same.Label} (fingerprint {_trackPrint}), profiled already: skipped");
            EverywhereSkipped.Add(new SkippedTrack(here, $"the same as {same.Short}", $"the same track as {same.Label} (fingerprint {_trackPrint})"));
            _everywhereNumber--;
            _everywhereTotal--;
            EverywhereNextTrack();
            return;
        }

        EverywhereDone[_trackPrint] = here;
        if (!_everywhereUnprofiled)
        {
            StartProfile(true);
            return;
        }

        List<(string Path, float From)> wanted;
        try
        {
            wanted = Unprofiled();
        }
        catch (IOException e)
        {
            // A sheet a spreadsheet won't let go of even for reading: what's in it can't be known, so nothing's driven twice.
            GD.PrintErr($"Every track: {here.Label}: its sheet couldn't be read ({e.Message}): skipped");
            EverywhereSkipped.Add(new SkippedTrack(here, "its sheet couldn't be read", $"its sheet couldn't be read: {e.Message}"));
            EverywhereNextTrack();
            return;
        }

        if (wanted.Count == 0)
        {
            GD.Print($"Every track: {here.Label}: every car's sweep is in its sheet already");
            _everywhereAlready++;
            EverywhereNextTrack();
            return;
        }

        int carryOn = wanted.Count(w => w.From > ProfileSweep.First);
        BeginProfile(wanted, $"the cars not yet profiled here ({wanted.Count} of {ActiveCars().Count()}"
            + $"{(carryOn > 0 ? $", {carryOn} carried on from where their sweep stopped" : string.Empty)})");
    }

    /// <summary>
    /// The active cars this track's sheets haven't a whole sweep of, each with the caution it goes on from: rows still
    /// waiting for a sheet a spreadsheet holds count too.
    /// </summary>
    private List<(string Path, float From)> Unprofiled()
    {
        IEnumerable<(string, string)> waiting = PendingRows
            .Where(r => r.Folder == _profilesFolder && r.File.Contains($" {_trackPrint}", StringComparison.Ordinal))
            .Select(r => (r.Header, r.Row));
        IReadOnlyDictionary<string, SweepState> states = ProfileProgress.Of(_profilesFolder, _trackPrint!, waiting);
        var cars = new List<(string Path, float From)>();
        foreach (Garage.Car car in ActiveCars())
        {
            float? from = states.TryGetValue(car.Spec!.Fingerprint(), out SweepState? state) ? state.From : ProfileSweep.First;
            if (from is float f)
            {
                cars.Add((car.Path, f));
            }
        }

        return cars;
    }

    /// <summary>A track's Profile every car has ended: its runs counted, then the next track, or, stopped, the way back.</summary>
    private static void EverywhereTrackEnded(string how)
    {
        _everywhereGhosts += ProfileGhostsKept.Count;
        EverywhereCarsSkipped.AddRange(ProfileCarsSkipped.Select(c => $"{c} on {_everywhereTrack?.Short ?? "a track"}"));
        if (how == "done")
        {
            _everywhereFinished++;
            EverywhereNextTrack();
            return;
        }

        EndEverywhere(how);
    }

    /// <summary>The run's end: how it went, on the board and in the log, and back to the track and car it began on.</summary>
    private static void EndEverywhere(string how)
    {
        _everywhereActive = false;
        EverywhereTracks.Clear();
        EverywhereClock.Stop();
        string time = Took(EverywhereClock.Elapsed);
        string stoppedOn = how == "done" || _everywhereTrack is null ? string.Empty
            : $", stopped on {_everywhereTrack.Short} (track {_everywhereNumber} of {_everywhereTotal})";
        string tracks = $"{_everywhereFinished} track{(_everywhereFinished == 1 ? string.Empty : "s")}";
        string runs = $"{_everywhereRuns} run{(_everywhereRuns == 1 ? string.Empty : "s")}";
        string already = _everywhereUnprofiled && _everywhereAlready > 0
            ? $"; {_everywhereAlready} track{(_everywhereAlready == 1 ? " had" : "s had")} every car profiled already" : string.Empty;
        string ghosts = _everywhereGhosts > 0 ? $"; {_everywhereGhosts} new best ghost{(_everywhereGhosts == 1 ? string.Empty : "s")}" : string.Empty;
        string waiting = PendingRows.Count > 0 ? $"; {PendingRows.Count} row{(PendingRows.Count == 1 ? string.Empty : "s")} waiting for a sheet to be closed" : string.Empty;
        string[] skips = EverywhereSkipped.Select(s => $"{s.Track.Short} ({s.Short})").ToArray();
        string skipped = skips.Length == 0 ? "none skipped" : $"{skips.Length} skipped: {string.Join(", ", skips)}";
        string cars = EverywhereCarsSkipped.Count == 0 ? string.Empty
            : $"; {EverywhereCarsSkipped.Count} car{(EverywhereCarsSkipped.Count == 1 ? string.Empty : "s")} wouldn't start: {string.Join(", ", EverywhereCarsSkipped)}";
        string head = $"{EverywhereName} {how} in {time}{stoppedOn}";
        string counts = $"{tracks} profiled, {runs}{already}{ghosts}{waiting}";
        GD.Print($"{head}: {counts}; {skipped}{cars}.");

        // On the board in lines, since one ran off the default window, the skipped tracks wrapped at the commas between them.
        var lines = new List<string> { $"{head}.", $"{char.ToUpperInvariant(counts[0])}{counts[1..]}." };
        Wrap(lines, skips.Length == 0 ? "None skipped." : $"{skips.Length} skipped:", skips);
        if (EverywhereCarsSkipped.Count > 0)
        {
            Wrap(lines, $"{EverywhereCarsSkipped.Count} car{(EverywhereCarsSkipped.Count == 1 ? string.Empty : "s")} wouldn't start:", EverywhereCarsSkipped);
        }

        string said = string.Join("\n", lines);
        foreach (SkippedTrack s in EverywhereSkipped)
        {
            GD.Print($"  Skipped: {s.Track.Label}, {s.Long}");
        }

        if (_profileFromFlag)
        {
            HoldPendingRows();
            Tree.Quit();
            return;
        }

        // Back where it began: the flags it was started with, and the car being driven then.
        string? car = _everywhereReturnCar;
        _profileSaidAfter = said;
        _profileSaidSeconds = 15.0;
        NextArgs = _everywhereReturnArgs.Where(a => !a.StartsWith("--spec=", StringComparison.Ordinal))
            .Concat(car is not null && !Garage.IsStock(car) ? [$"--spec={car}"] : [])
            .ToArray();
        GD.Print($"Every track: back to where it began: {string.Join(' ', NextArgs)}");
        Tree.ReloadCurrentScene();
    }

    /// <summary>A heading and a list, as lines of the board no wider than the default window, broken at the commas.</summary>
    private static void Wrap(List<string> lines, string heading, IReadOnlyList<string> items)
    {
        string line = heading;
        for (int i = 0; i < items.Count; i++)
        {
            string piece = items[i] + (i < items.Count - 1 ? "," : ".");
            if (line.Length + piece.Length + 1 > 100)
            {
                lines.Add(line);
                line = "  ";
            }

            line += $" {piece}";
        }

        lines.Add(line);
    }

    /// <summary>Real time in words: 45 s, 12 min 4 s.</summary>
    private static string Took(TimeSpan took) =>
        took.TotalMinutes >= 1.0 ? $"{(int)took.TotalMinutes} min {took.Seconds} s" : $"{took.TotalSeconds:F0} s";

    /// <summary>The board's line for the track, while every track is being profiled: which, how far through, and what wouldn't start.</summary>
    private static string EverywhereBanner()
    {
        if (!_everywhereActive || _everywhereTrack is null)
        {
            return string.Empty;
        }

        int stuck = EverywhereSkipped.Count(s => s.Short == WouldntStart) + EverywhereCarsSkipped.Count + ProfileCarsSkipped.Count;
        return $"track {_everywhereNumber} of {_everywhereTotal}: {_everywhereTrack.Short}{(stuck > 0 ? $"   {stuck} wouldn't start" : string.Empty)}";
    }
}
