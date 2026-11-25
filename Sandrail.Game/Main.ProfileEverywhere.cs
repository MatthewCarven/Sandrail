using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Sandrail.Core.Tracks;
using Sandrail.Game.Cars;
using Sandrail.Game.World;

namespace Sandrail.Game;

/// <summary>
/// Profile every car on every track (2026-11-25, his ask): Profile every car, track after track, unattended, then back
/// to the track and car he started from. The tracks are Load a track's, the game's own and every saved one; the cars are
/// Profile every car's, the stock car and his active ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>It chains what's there</b>: each track is started as Load a track starts one, with the garage's first car, and
/// Profile every car runs on it as it always does (the sweep, the rows in that track's own sheet, the profile ghosts, the
/// rows held while a spreadsheet has the file). When that ends, the next track. Statics carry it across the scenes, as
/// the profile's own do.
/// </para>
/// <para>
/// <b>A track that won't lay soundly is skipped</b> (a half-built editor track), and so is <b>a track already profiled
/// under the same fingerprint</b>: his saved <c>gen-5.json</c> (Seed 5, saved before codes) is the game's <c>gen:5v1</c>,
/// the same sheet. The fingerprint is the scene's own, made as the track is built, so a duplicate costs a track's load
/// and nothing more. Esc stops the lot, and the end of it says how it went, on the board and in the log.
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
    // and where to go back to.
    private static bool _everywhereActive;
    private static readonly Queue<EverywhereTrack> EverywhereTracks = new();
    private static readonly Dictionary<string, EverywhereTrack> EverywhereDone = new();
    private static readonly List<SkippedTrack> EverywhereSkipped = new();
    private static int _everywhereTotal;
    private static int _everywhereNumber;
    private static EverywhereTrack? _everywhereTrack;
    private static int _everywhereFinished;
    private static int _everywhereRuns;
    private static int _everywhereGhosts;
    private static string[] _everywhereReturnArgs = [];
    private static string? _everywhereReturnCar;
    private static readonly System.Diagnostics.Stopwatch EverywhereClock = new();

    /// <summary>
    /// Flags the tracks are started without, beside the place flags: a run recorded from the start, a ghost chosen by a
    /// car's name (refused on a track where it hasn't been profiled), the autodriver handed the car. The way back puts
    /// them back.
    /// </summary>
    private static readonly string[] EverywhereDropped = ["--record", "--autodrive", "--ghost=", "--spec="];

    /// <summary>The Esc menu's Profile every car on every track, and <c>--profile=everywhere</c>.</summary>
    private void StartProfileEverywhere()
    {
        if (_car is null || _tuning is null || _hud is null || _testDriveFrom is not null)
        {
            _hud?.Flash("Profile every car on every track starts from a drive, not a test drive.", 4.0);
            return;
        }

        PieceCatalogue catalogue = _menuCatalogue ??= TrackView.LoadCatalogue();
        EverywhereTracks.Clear();
        EverywhereDone.Clear();
        EverywhereSkipped.Clear();
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

        _everywhereTotal = EverywhereTracks.Count;
        _everywhereNumber = 0;
        _everywhereTrack = null;
        _everywhereFinished = 0;
        _everywhereRuns = 0;
        _everywhereGhosts = 0;
        _everywhereReturnArgs = _args;
        _everywhereReturnCar = _tuning.SpecPath;
        _everywhereActive = true;
        EverywhereClock.Restart();
        int cars = Garage.All().Count(c => c.Spec is not null && !c.Inactive);
        GD.Print($"Profiling every car ({cars}) on every track ({_everywhereTotal}"
            + $"{(EverywhereSkipped.Count > 0 ? $", {EverywhereSkipped.Count} unfinished left out" : string.Empty)}), into {_profilesFolder}");
        foreach (SkippedTrack skipped in EverywhereSkipped)
        {
            GD.Print($"  Left out: {skipped.Track.Label}, {skipped.Long}");
        }

        EverywhereNextTrack();
    }

    /// <summary>The next track, started as Load a track starts one, with the garage's first car so its sweep needs no change of car; or the end.</summary>
    private void EverywhereNextTrack()
    {
        if (EverywhereTracks.Count == 0)
        {
            EndEverywhere("done");
            return;
        }

        EverywhereTrack next = EverywhereTracks.Dequeue();
        _everywhereNumber++;
        _everywhereTrack = next;
        string first = Garage.All().FirstOrDefault(c => c.Spec is not null && !c.Inactive)?.Path ?? _tuning!.SpecPath;
        NextArgs = _args.Where(a => !PlaceFlags.Concat(EverywhereDropped).Any(p => a.StartsWith(p, StringComparison.Ordinal)))
            .Append($"--track={next.Path}")
            .Concat(Garage.IsStock(first) ? [] : [$"--spec={first}"])
            .ToArray();
        GD.Print($"Every track: {_everywhereNumber} of {_everywhereTotal}, {next.Label}, {Took(EverywhereClock.Elapsed)} in: {string.Join(' ', NextArgs)}");
        GetTree().ReloadCurrentScene();
    }

    /// <summary>
    /// A track's scene is up: Profile every car on it, unless its fingerprint has been profiled already in this run (the
    /// same track saved twice), when it's skipped and the count of tracks goes down by one.
    /// </summary>
    private void EverywhereArrived()
    {
        if (!_everywhereActive || _everywhereTrack is not EverywhereTrack here)
        {
            return;
        }

        if (_track is null || _race is null || _trackPrint is null || _testDriveFrom is not null)
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
        StartProfile(true);
    }

    /// <summary>A track's Profile every car has ended: its runs counted, then the next track, or, stopped, the way back.</summary>
    private void EverywhereTrackEnded(string how)
    {
        _everywhereGhosts += ProfileGhostsKept.Count;
        if (how == "done")
        {
            _everywhereFinished++;
            EverywhereNextTrack();
            return;
        }

        EndEverywhere(how);
    }

    /// <summary>The run's end: how it went, on the board and in the log, and back to the track and car it began on.</summary>
    private void EndEverywhere(string how)
    {
        _everywhereActive = false;
        EverywhereTracks.Clear();
        EverywhereClock.Stop();
        string time = Took(EverywhereClock.Elapsed);
        string stoppedOn = how == "done" || _everywhereTrack is null ? string.Empty
            : $", stopped on {_everywhereTrack.Short} (track {_everywhereNumber} of {_everywhereTotal})";
        string tracks = $"{_everywhereFinished} track{(_everywhereFinished == 1 ? string.Empty : "s")}";
        string runs = $"{_everywhereRuns} run{(_everywhereRuns == 1 ? string.Empty : "s")}";
        string ghosts = _everywhereGhosts > 0 ? $"; {_everywhereGhosts} new best ghost{(_everywhereGhosts == 1 ? string.Empty : "s")}" : string.Empty;
        string waiting = PendingRows.Count > 0 ? $"; {PendingRows.Count} row{(PendingRows.Count == 1 ? string.Empty : "s")} waiting for a sheet to be closed" : string.Empty;
        string[] skips = EverywhereSkipped.Select(s => $"{s.Track.Short} ({s.Short})").ToArray();
        string skipped = skips.Length == 0 ? "none skipped" : $"{skips.Length} skipped: {string.Join(", ", skips)}";
        string head = $"Every car on every track {how} in {time}{stoppedOn}";
        string counts = $"{tracks} profiled, {runs}{ghosts}{waiting}";
        GD.Print($"{head}: {counts}; {skipped}.");

        // On the board in lines, since one ran off the default window, the skipped tracks wrapped at the commas between them.
        var lines = new List<string> { $"{head}.", $"{char.ToUpperInvariant(counts[0])}{counts[1..]}." };
        string line = skips.Length == 0 ? "None skipped." : $"{skips.Length} skipped:";
        for (int i = 0; i < skips.Length; i++)
        {
            string piece = skips[i] + (i < skips.Length - 1 ? "," : ".");
            if (line.Length + piece.Length + 1 > 100)
            {
                lines.Add(line);
                line = "  ";
            }

            line += $" {piece}";
        }

        lines.Add(line);
        string said = string.Join("\n", lines);
        foreach (SkippedTrack s in EverywhereSkipped)
        {
            GD.Print($"  Skipped: {s.Track.Label}, {s.Long}");
        }

        if (_profileFromFlag)
        {
            HoldPendingRows();
            GetTree().Quit();
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
        GetTree().ReloadCurrentScene();
    }

    /// <summary>Real time in words: 45 s, 12 min 4 s.</summary>
    private static string Took(TimeSpan took) =>
        took.TotalMinutes >= 1.0 ? $"{(int)took.TotalMinutes} min {took.Seconds} s" : $"{took.TotalSeconds:F0} s";

    /// <summary>The board's line for the track, while every track is being profiled: which, and how far through.</summary>
    private static string EverywhereBanner() =>
        _everywhereActive && _everywhereTrack is not null ? $"track {_everywhereNumber} of {_everywhereTotal}: {_everywhereTrack.Short}" : string.Empty;
}
