using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Godot;
using Sandrail.Core.Profiles;

namespace Sandrail.Game;

/// <summary>
/// A fresh game for the rest of a long profile (2026-11-28): Profile every car on every track, and Profile unprofiled
/// cars, carry on in a new process before Godot 4.7.1's Jolt stops, and whenever the profile stops moving.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why</b>: his overnight run froze after 13.6 hours. Jolt's job barrier, as Godot 4.7.1 has it, counts the jobs that
/// pass through it in 32 bits and lets them go only while its read count is less than its write count. After 2^32 jobs
/// the write count wraps past zero, so it never lets a job go again, its 2,048 job slots fill, and the next physics step
/// waits for a slot forever (the dump: read count 0xFFFFFFE5, write count 0x7C9). Fixed in Jolt (jrouwe/JoltPhysics#2206,
/// 2026-11-06, "!= instead of &lt;"), not yet in Godot. A tick of one car makes 2 jobs a worker thread and 17 more, 49
/// with his 16 threads, so it comes after about 88 million ticks, some 200 hours of game time. The count is the
/// process's, not the scene's: only a new process starts it again.
/// </para>
/// <para>
/// <b>Before it comes</b>: at a load the profile makes (the next car, the next track), once the game has run half the
/// ticks it would stop at (<see cref="TickBudget"/>), it hands the rest to a fresh one, every five or six hours at ×16.
/// </para>
/// <para>
/// <b>Anything else that stops it</b>: a watchdog on a thread of its own, since a main thread that's stuck can't watch
/// itself. When nothing has moved for three minutes (no tick of a run, no load), it does the same from outside. A run
/// that freezes the game twice running, on the same car and track, is left out the third time, so a freeze that comes
/// back every time can't hold the profile in a loop.
/// </para>
/// <para>
/// <b>How the next game comes</b>: with <c>--restart-file=</c> (as <c>profile.cmd</c> passes it), its flags go in that
/// file and the game ends with 75, for the launcher to start. Without, the game starts it itself, never in a console:
/// a console holds a game's every print while its window is scrolled or has text selected (the freeze of 2026-11-27),
/// so the next game writes where this one did if that was a file, and otherwise only to its log, and a launcher
/// sends the game's output to a file too. Either way it's <c>--profile=unprofiled</c>,
/// with <c>--since=</c> the time Profile every car on every track began, so it drives what this run hasn't, and a note
/// in the profiles folder says why it was started.
/// </para>
/// </remarks>
public partial class Main
{
    /// <summary>The exit code that asks a launcher to start the next game (EX_TEMPFAIL): what <c>profile.cmd</c> loops on.</summary>
    public const int RestartExitCode = 75;

    /// <summary>How long a profile may go with nothing moving before the watchdog restarts it, in real seconds: a load has a minute, twice.</summary>
    private const double StallS = 180.0;

    /// <summary>The watchdog's wait as it's set: <see cref="StallS"/>, or <c>--stall-after=</c> for checks.</summary>
    private static double _stallS = StallS;

    /// <summary>How often the watchdog looks, in milliseconds.</summary>
    private const int WatchEveryMs = 5000;

    /// <summary>A look that came this many seconds late was the machine asleep, not the game: the game gets its time again.</summary>
    private const double AsleepS = 60.0;

    /// <summary>The note a restart leaves in the profiles folder, for the next game to say.</summary>
    private const string RestartNoteName = "restart note.txt";

    /// <summary>The time format of <c>--since=</c>, without a space so it needs no quotes.</summary>
    private const string SinceFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>The user flags the next game doesn't start with: the profile's own, set afresh, and ones that would take it elsewhere.</summary>
    private static readonly string[] RestartDropped =
        ["--profile=", "--since=", "--edit=", "--drive-from=", "--replay=", "--replay-at=", "--replay-camera=", "--screenshot=", "--oracle=", "--record", "--autodrive"];

    // What the watchdog reads, set on the main thread: whether it's watching, the last sign of life (a Stopwatch
    // timestamp), the run going on (its track's and car's fingerprints, and in words), and the next game, worked out in
    // advance, since a game that's stuck can't be asked anything.
    private static Thread? _watchdog;
    private static volatile bool _watchArmed;
    private static long _alive;
    private static volatile string _watchPlace = string.Empty;
    private static volatile string _watchWhat = string.Empty;
    private static string[] _nextGameArgs = [];
    private static string _nextGameExe = string.Empty;
    private static string[] _nextGameEngineArgs = [];
    private static string? _restartFile;
    private static string _restartNote = string.Empty;
    private static ulong _tickBudget = ulong.MaxValue;

    /// <summary>
    /// The physics ticks this game profiles for before handing the rest to a fresh one: half of 2^32 barrier jobs, at a
    /// generous count of what a tick makes. Jolt plans for as many jobs as Godot's worker pool has threads, a thread a
    /// logical processor unless the project says otherwise. <c>--restart-after=</c> sets it, for checks.
    /// </summary>
    private static ulong TickBudget(string[] args, out int threads)
    {
        threads = ProjectSettings.GetSetting("threading/worker_pool/max_threads", -1).AsInt32();
        if (threads <= 0)
        {
            threads = OS.GetProcessorCount();
        }

        if (Flag(args, "--restart-after=") is string forced && ulong.TryParse(forced, out ulong ticks))
        {
            return ticks;
        }

        return (1UL << 31) / (ulong)(2 * threads + 24);
    }

    /// <summary>
    /// A chain over every track has begun: the next game worked out, the tick budget set, the watchdog watching. Says
    /// what the last game left in its note, if this one was started by a restart.
    /// </summary>
    private static void ArmRestart(string[] args, DateTime? since)
    {
        _tickBudget = TickBudget(args, out int threads);
        _restartFile = Flag(args, "--restart-file=");
        _stallS = FloatFlag(args, "--stall-after=") is float stall && stall > 0.0f ? stall : StallS;
        _restartNote = Path.Combine(_profilesFolder, RestartNoteName);
        string[] user = OS.GetCmdlineUserArgs()
            .Where(a => !RestartDropped.Any(d => a.StartsWith(d, StringComparison.Ordinal)))
            .Append("--profile=unprofiled")
            .Concat(since is DateTime s ? [$"--since={s.ToString(SinceFormat, System.Globalization.CultureInfo.InvariantCulture)}"] : [])
            .ToArray();
        _nextGameArgs = user;
        _nextGameEngineArgs = EngineArgs();
        _nextGameExe = OS.GetExecutablePath();

        if (File.Exists(_restartNote))
        {
            try
            {
                GD.Print($"{EverywhereName}: carried on from the last game, which said: {File.ReadAllText(_restartNote).Trim()}");
                File.Delete(_restartNote);
            }
            catch (IOException e)
            {
                GD.PrintErr($"{EverywhereName}: the last game's note couldn't be read: {e.Message}");
            }
        }

        double wrap = 4294967296.0 / (2 * threads + 17);
        GD.Print($"{EverywhereName}: a fresh game carries on after {_tickBudget / 1e6:F1} million physics ticks, by "
            + $"{(_restartFile is null ? "starting it itself" : "asking the launcher")} (Godot 4.7.1's Jolt stops near "
            + $"{wrap / 1e6:F0} million with {threads} worker threads), or after {_stallS:F0} s with nothing moving.");

        Alive();
        _watchArmed = true;
        if (_watchdog is null)
        {
            _watchdog = new Thread(Watch) { IsBackground = true, Name = "Sandrail profile watchdog" };
            _watchdog.Start();
        }
    }

    /// <summary>The chain is over: nothing to watch.</summary>
    private static void DisarmRestart() => _watchArmed = false;

    /// <summary>A sign of life: a tick of a run, or a load asked for.</summary>
    private static void Alive() => Interlocked.Exchange(ref _alive, Stopwatch.GetTimestamp());

    /// <summary>The run going on, for a freeze to name: its track's and car's fingerprints, and in words.</summary>
    private static void WatchRun(string place, string what)
    {
        _watchPlace = place;
        _watchWhat = what;
    }

    /// <summary>Whether this game has profiled long enough to hand the rest to a fresh one, at the next load.</summary>
    private static bool RestartDue => _watchArmed && Engine.GetPhysicsFrames() >= _tickBudget;

    /// <summary>
    /// The rest in a fresh game, at a load (the main thread): rows a spreadsheet holds kept beside their sheet, said in the
    /// log, then the next game and this one's end.
    /// </summary>
    private static void RestartFresh()
    {
        HoldPendingRows();
        string why = $"{Engine.GetPhysicsFrames() / 1e6:F1} million physics ticks, past this game's {_tickBudget / 1e6:F1} million";
        WriteRestartNote($"it had run {why} (Godot 4.7.1's Jolt stops further on, so a fresh game carries on before it).");
        GD.Print($"{EverywhereName}: {why}: carrying on in a fresh game, {(_restartFile is null ? "started now" : "which the launcher starts")}: "
            + CommandLine(_restartFile is null ? [.. _nextGameEngineArgs, "--", .. _nextGameArgs] : _nextGameArgs));
        StartNextGame(_nextGameArgs);
    }

    /// <summary>The watchdog's thread: once every five seconds, whether anything has moved in the last three minutes.</summary>
    private static void Watch()
    {
        long looked = Stopwatch.GetTimestamp();
        while (true)
        {
            Thread.Sleep(WatchEveryMs);
            long now = Stopwatch.GetTimestamp();
            double late = (now - looked) / (double)Stopwatch.Frequency;
            looked = now;
            if (!_watchArmed)
            {
                continue;
            }

            if (late > AsleepS)
            {
                Alive();
                continue;
            }

            double quiet = (now - Interlocked.Read(ref _alive)) / (double)Stopwatch.Frequency;
            if (quiet > _stallS)
            {
                Stalled(quiet);
                return;
            }
        }
    }

    /// <summary>
    /// Nothing has moved for three minutes (the watchdog's thread): the next game started, and this one ended at once.
    /// Nothing here touches Godot or the console, which may be what's stuck: the note goes in a file, and the run that
    /// froze is named in the next game's flags so a run that freezes every time is left out at last.
    /// </summary>
    private static void Stalled(double quiet)
    {
        string place = _watchPlace;
        string what = _watchWhat;
        WriteRestartNote($"nothing had moved for {quiet:F0} s{(what.Length > 0 ? $" ({what})" : string.Empty)}, "
            + "so it was ended and a fresh game carried on.");
        StartNextGame(place.Length > 0 ? [.. _nextGameArgs, $"--froze={place}"] : _nextGameArgs);
    }

    private static void WriteRestartNote(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_restartNote)!);
            File.WriteAllText(_restartNote, $"{DateTime.Now.ToString(CarSheet.WhenFormat, System.Globalization.CultureInfo.InvariantCulture)}: {text}{System.Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A note that can't be written costs only the next game's line about it.
        }
    }

    /// <summary>The next game: its flags in the launcher's file, or started here; then this game ends with 75, at once.</summary>
    private static void StartNextGame(string[] user)
    {
        try
        {
            if (_restartFile is string file)
            {
                File.WriteAllText(file, CommandLine(user) + System.Environment.NewLine);
            }
            else
            {
                // The game itself, with no console: its output goes on into this one's when that's a file (handed
                // over by asking for its input, the one way .NET passes the others on), and otherwise into pipes this
                // game closes as it ends, so it's only in the log, never in a console that could hold it.
                bool toFile = OperatingSystem.IsWindows() && GetFileType(GetStdHandle(StdOutputHandle)) == FileTypeDisk;
                var start = new ProcessStartInfo(_nextGameExe, CommandLine([.. _nextGameEngineArgs, "--", .. user]))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = !toFile,
                    RedirectStandardError = !toFile,
                    WorkingDirectory = System.Environment.CurrentDirectory,
                };
                Process.Start(start)?.Dispose();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            WriteRestartNote($"the next game couldn't be started ({e.Message}); start it with --profile=unprofiled.");
        }

        // At once, not a quit: a quit runs on the main thread, which may be what's stuck, and a fresh game started
        // here mustn't find this one still writing the log it takes over.
        if (OperatingSystem.IsWindows())
        {
            TerminateProcess(GetCurrentProcess(), RestartExitCode);
        }

        System.Environment.Exit(RestartExitCode);
    }

    /// <summary>
    /// The engine's flags for the next game: this one's, with the project's folder made whole, since Godot moved into it.
    /// Read from the process's own command line, since Godot's list leaves out the flags it takes for itself
    /// (<c>--headless</c>, <c>--log-file</c>): a next game made from it opened a window and wrote his log.
    /// </summary>
    private static string[] EngineArgs()
    {
        string project = ProjectSettings.GlobalizePath("res://").TrimEnd('/');
        var args = new List<string>();
        string[] mine = OperatingSystem.IsWindows() ? ProcessArgs() : OS.GetCmdlineArgs();
        bool hasPath = false;
        for (int i = 0; i < mine.Length; i++)
        {
            if (mine[i] == "--path" && i + 1 < mine.Length)
            {
                args.Add("--path");
                args.Add(project);
                hasPath = true;
                i++;
                continue;
            }

            args.Add(mine[i]);
        }

        if (!hasPath)
        {
            args.InsertRange(0, ["--path", project]);
        }

        return args.ToArray();
    }

    /// <summary>Flags as one command line, each quoted as Windows reads them back, and as cmd passes them on.</summary>
    private static string CommandLine(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    private static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"', '&', '|', '<', '>', '^', '(', ')']) < 0)
        {
            return arg;
        }

        var quoted = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }

            quoted.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            quoted.Append(c);
        }

        quoted.Append('\\', slashes * 2).Append('"');
        return quoted.ToString();
    }

    /// <summary>
    /// The runs that froze the game twice running, as track and car fingerprints: each restart after a freeze names the
    /// run in the next game's flags (<c>--froze=track:car</c>), and a run named twice is left out.
    /// </summary>
    private static HashSet<string> FrozeTwice(string[] args) =>
        args.Where(a => a.StartsWith("--froze=", StringComparison.Ordinal))
            .GroupBy(a => a["--froze=".Length..], StringComparer.Ordinal)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The engine's part of this process's command line, as Windows splits it: after the program, up to the bare <c>--</c>.</summary>
    private static string[] ProcessArgs()
    {
        IntPtr argv = CommandLineToArgvW(GetCommandLineW(), out int count);
        if (argv == IntPtr.Zero)
        {
            return OS.GetCmdlineArgs();
        }

        try
        {
            var all = new string[count];
            for (int i = 0; i < count; i++)
            {
                all[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? string.Empty;
            }

            return all.Skip(1).TakeWhile(a => a is not ("--" or "++")).ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    private const int StdOutputHandle = -11;
    private const int FileTypeDisk = 1;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll")]
    private static extern int GetFileType(IntPtr file);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCommandLineW();

    [DllImport("shell32.dll")]
    private static extern IntPtr CommandLineToArgvW(IntPtr commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, int exitCode);
}
