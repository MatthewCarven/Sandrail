using System;
using Godot;

namespace Sandrail.Game;

/// <summary>
/// Game time, and how fast it goes (his ask, 2026-11-24, , and .): a physics tick is always 1/120 s of game time,
/// so the race clock, ghosts, replays and recorded runs, which count ticks, all count game time.
/// </summary>
/// <remarks>
/// <b>Slow motion runs fewer ticks a second, not shorter ones.</b> Godot's time scale alone keeps 120 ticks every
/// real second and makes each shorter: the race clock then counted real time (the shakedown at half speed came out
/// 1:53.46, twice its 56.73), and a ghost's snapshots would have been half as far apart. So the tick rate goes with
/// the scale (60 a second at half speed, 12 at a tenth), which keeps each tick's length, and with it the physics,
/// exactly as at full speed.
/// </remarks>
public static class GameClock
{
    /// <summary>Physics ticks in a second of game time, as the project sets them.</summary>
    public static readonly int TicksPerSecond = (int)ProjectSettings.GetSetting("physics/common/physics_ticks_per_second", 120);

    /// <summary>How fast game time goes against real time: 1 is its own speed.</summary>
    public static double Scale => Engine.TimeScale;

    /// <summary>Sets how fast game time goes: the time scale and the tick rate together, so a tick stays 1/120 s of game time.</summary>
    public static void SetScale(double scale)
    {
        Engine.TimeScale = scale;
        Engine.PhysicsTicksPerSecond = Math.Max(1, (int)Math.Round(TicksPerSecond * scale));
    }
}
