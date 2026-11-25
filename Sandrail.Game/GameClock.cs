using System;
using System.Collections.Generic;
using Godot;

namespace Sandrail.Game;

/// <summary>
/// Game time, and how fast it goes (his ask, 2026-11-24, , and .): a physics tick is always 1/120 s of game time,
/// so the race clock, ghosts, replays and recorded runs, which count ticks, all count game time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Slow motion runs fewer ticks a second, not shorter ones.</b> Godot's time scale alone keeps 120 ticks every
/// real second and makes each shorter: the race clock then counted real time (the shakedown at half speed came out
/// 1:53.46, twice its 56.73), and a ghost's snapshots would have been half as far apart. So the tick rate goes with
/// the scale (60 a second at half speed, 12 at a tenth), which keeps each tick's length, and with it the physics,
/// exactly as at full speed.
/// </para>
/// <para>
/// <b>Fast forward runs more ticks a frame, up to ×16</b> (his ask, 2026-11-25: "still does all the ticks required to
/// get there accurately i dont mind if the computer has to run slower just indicate the speed to me"). Godot runs at
/// most 8 ticks a drawn frame unless told, which is ×4 at 60 frames a second; so the most it may run a frame rises
/// with the scale, enough to keep up down to 30 frames a second. A computer that can't keep up runs every tick still,
/// only later: game time then goes slower than asked, never in longer steps. <see cref="Running"/> says how fast it
/// really went.
/// </para>
/// </remarks>
public static class GameClock
{
    /// <summary>Physics ticks in a second of game time, as the project sets them.</summary>
    public static readonly int TicksPerSecond = (int)ProjectSettings.GetSetting("physics/common/physics_ticks_per_second", 120);

    /// <summary>The speeds , and . step through, ×0.1 to ×16.</summary>
    public static readonly double[] Steps = [0.1, 0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 16.0];

    /// <summary>The fastest game time goes: <c>--time=</c> is held to it too.</summary>
    public const double Fastest = 16.0;

    /// <summary>Godot's own most ticks in a frame, kept at ×2 and slower.</summary>
    private const int FewestStepsPerFrame = 8;

    /// <summary>The frame rate fast forward still keeps up at: ×16's 1,920 ticks a second are 64 a frame at 30.</summary>
    private const int KeepUpFps = 30;

    /// <summary>How often <see cref="Running"/> is measured, and over how long.</summary>
    private const ulong SampleEveryUsec = 250_000;
    private const ulong AverageOverUsec = 2_000_000;

    /// <summary>A frame this late is a pause (the Esc menu) or a load, not running slow: the measure starts again.</summary>
    private const ulong GapUsec = 500_000;

    private static readonly Queue<(ulong Usec, ulong Tick)> Samples = new();
    private static ulong _lastSampleUsec;
    private static ulong _lastCallUsec;

    /// <summary>How fast game time goes against real time: 1 is its own speed.</summary>
    public static double Scale => Engine.TimeScale;

    /// <summary>
    /// How fast game time really went over the last two seconds, from the ticks Godot ran: <see cref="Scale"/> when
    /// the computer keeps up, less when it can't. Measured by <see cref="Measure"/>, once a frame.
    /// </summary>
    public static double Running { get; private set; } = 1.0;

    /// <summary>
    /// Whether game time is going noticeably slower than asked, by a tenth or more: the computer can't run the ticks any
    /// faster. Seed 5 at ×16 read ×14.8 to ×16.0 two seconds at a time, and ×15.6 over the whole drive: Godot's frame
    /// timing loses a little, at 130 frames a second and far from the most ticks a frame, and every tick still runs.
    /// </summary>
    public static bool Behind => Running < Scale * 0.9;

    /// <summary>
    /// Sets how fast game time goes, held to ×0.01 to ×16: the time scale and the tick rate together, so a tick stays
    /// 1/120 s of game time, and the most ticks a frame, so fast forward can keep up.
    /// </summary>
    public static void SetScale(double scale)
    {
        scale = Math.Clamp(scale, 0.01, Fastest);
        int ticks = Math.Max(1, (int)Math.Round(TicksPerSecond * scale));
        Engine.TimeScale = scale;
        Engine.PhysicsTicksPerSecond = ticks;
        Engine.MaxPhysicsStepsPerFrame = Math.Max(FewestStepsPerFrame, (ticks + KeepUpFps - 1) / KeepUpFps);
        Running = scale;
        Samples.Clear();
    }

    /// <summary>
    /// Measures <see cref="Running"/>: the ticks Godot ran over the last two seconds against the real time, taken four
    /// times a second. Half a second at a time swung from ×13.6 to ×16.9 on seed 5 at ×16, a frame's hitch and the
    /// catching up after it, and a second at a time from ×14.8 to ×16.3. Called once a frame.
    /// </summary>
    public static void Measure()
    {
        ulong now = Time.GetTicksUsec();
        if (now - _lastCallUsec > GapUsec)
        {
            Samples.Clear();
        }

        _lastCallUsec = now;
        if (Samples.Count > 0 && now - _lastSampleUsec < SampleEveryUsec)
        {
            return;
        }

        ulong tick = Engine.GetPhysicsFrames();
        Samples.Enqueue((now, tick));
        _lastSampleUsec = now;
        while (Samples.Count > 2 && now - Samples.Peek().Usec > AverageOverUsec)
        {
            Samples.Dequeue();
        }

        (ulong fromUsec, ulong fromTick) = Samples.Peek();
        if (now - fromUsec >= 1_000_000)
        {
            Running = (tick - fromTick) / (double)TicksPerSecond / ((now - fromUsec) / 1e6);
        }
    }
}
