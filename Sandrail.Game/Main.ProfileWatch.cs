using Godot;

namespace Sandrail.Game;

/// <summary>
/// The watch on a profile's loads (2026-11-27, his ask): each scene a profile starts again, the next car or the next
/// track of every track, must have its first run going within a minute; one that doesn't is tried once more, then
/// skipped, said on the board and in the log, and the profile carries on with the next car or track.
/// </summary>
/// <remarks>
/// <para>
/// <b>The watch is a timer of the scene tree's</b>, which outlives the scenes, on real time rather than the game's (at
/// ×16 a minute of game time is under 4 s). It has gone off when nothing has begun since its load was asked for: the new
/// scene's <c>_Ready</c> stopped on an exception, or the engine couldn't make it (the out of memory that ended his 7.7 h
/// run on 2026-11-27 broke a car's load that way).
/// </para>
/// <para>
/// <b>A track or car the game refuses</b> (a file it can't read) is skipped at once, since trying it again would refuse it
/// again: before, a refusal quit the game, and the whole run with it.
/// </para>
/// <para>
/// <b>It can't save a game that has stopped altogether</b>, since it runs on the main thread: one whose console has stopped
/// taking text, so that a print never returns (the freeze of 2026-11-27), or whose physics waits forever (Jolt's, on
/// 2026-11-28). The watchdog does that, on a thread of its own, for every track (<c>Main.ProfileRestart.cs</c>).
/// </para>
/// </remarks>
public partial class Main
{
    /// <summary>How long a load has to get its first run going, in real seconds: a track with its ground and 220 ghosts is up in two or three.</summary>
    private const double LoadWaitS = 60.0;

    /// <summary>How many times a load is tried before it's skipped.</summary>
    private const int LoadTries = 2;

    /// <summary>The board's word for a track that wouldn't start.</summary>
    private const string WouldntStart = "wouldn't start";

    // The loads the profile has asked for, counted; the last one whose first run began; how many times the one being
    // waited for has been tried, and what it is (a car's name, or a track's).
    private static int _loadSerial;
    private static int _loadGoing;
    private static int _loadTry;
    private static string _loadWhat = string.Empty;

    /// <summary>The scene again with <see cref="NextArgs"/> as they're set, for a profile, watched; <paramref name="what"/> names it in what's said.</summary>
    private static void ChainReload(string what) => ChainReload(what, again: false);

    private static void ChainReload(string what, bool again)
    {
        // A chain over every track that has run long enough carries on in a fresh game instead (2026-11-28).
        if (RestartDue)
        {
            RestartFresh();
            return;
        }

        Alive();
        WatchRun(string.Empty, $"loading {what}");
        _loadSerial++;
        _loadTry = again ? _loadTry + 1 : 1;
        _loadWhat = what;
        int serial = _loadSerial;
        SceneTree tree = Tree;
        tree.CreateTimer(LoadWaitS, processAlways: true, processInPhysics: false, ignoreTimeScale: true).Timeout += () => WatchLoad(serial);
        Error made = tree.ReloadCurrentScene();
        if (made != Error.Ok)
        {
            GD.PrintErr($"Profile: {what} couldn't be started ({made}); looked at again in {LoadWaitS:F0} s.");
        }
    }

    /// <summary>A profile run has begun: the load this scene came from got going, and its watch stands down.</summary>
    private static void LoadGoing() => _loadGoing = _loadSerial;

    /// <summary>A load's minute is up: if nothing has begun since, and nothing newer was asked for, it's tried again or skipped.</summary>
    private static void WatchLoad(int serial)
    {
        if (serial != _loadSerial || _loadGoing >= serial || !(_profileActive || _everywhereActive))
        {
            return;
        }

        if (_loadTry < LoadTries)
        {
            GD.PrintErr($"Profile: {_loadWhat} didn't get going in {LoadWaitS:F0} s ({MemoryWords()}); trying it again.");
            ChainReload(_loadWhat, again: true);
            return;
        }

        ChainGiveUp($"didn't get going in {LoadWaitS:F0} s, twice ({MemoryWords()})");
    }

    /// <summary>Whether a refusal is the profile's to deal with (a load of its own that the game refused) rather than the game's to quit on.</summary>
    private static bool ChainTakesRefusal => _profileActive || _everywhereActive;

    /// <summary>A load the game refused: skipped at once, since trying it again would refuse it again.</summary>
    private static void ChainRefused(string message) => ChainGiveUp($"refused: {message}");

    /// <summary>The load that wouldn't go is skipped and the profile carries on: with the next car, the next track, or its end.</summary>
    private static void ChainGiveUp(string why)
    {
        LoadGoing();
        if (_profileActive)
        {
            // A change of car within a track's profile: the car is left out on this track.
            string car = _loadWhat;
            ProfileCarsSkipped.Add(car);
            GD.PrintErr($"Profile: {car} {why}: skipped on {_profileTrackName}.");
            _profileSaidAfter = $"{car} wouldn't start on {_profileTrackName}: skipped.";
            _profileSaidSeconds = 6.0;
            if (ProfileCars.Count > 0)
            {
                (string next, float from) = ProfileCars.Dequeue();
                _profileCarNumber++;
                _profileFrom = from;
                ProfileLoadCar(next);
                return;
            }

            EndProfileUnseen("done");
            return;
        }

        if (_everywhereActive && _everywhereTrack is EverywhereTrack track)
        {
            GD.PrintErr($"Every track: {track.Label} {why}: skipped.");
            EverywhereSkipped.Add(new SkippedTrack(track, WouldntStart, why));
            _profileSaidAfter = $"{track.Short} wouldn't start: skipped.";
            _profileSaidSeconds = 6.0;
            EverywhereNextTrack();
        }
    }
}
