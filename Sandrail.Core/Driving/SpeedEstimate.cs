using Sandrail.Core.Cars;

namespace Sandrail.Core.Driving;

/// <summary>
/// How fast a car can actually be along a line (M3-PLAN, stage 3; PLAN suggestion 4): the speed plan, which
/// says how fast each sample may be taken and brakes for what's ahead, met by a pass the other way that
/// accelerates the car from rest at full throttle. Where the two meet is the speed the car can have there.
/// </summary>
/// <remarks>
/// <para>
/// <b>The acceleration</b> is the engine's force (its limit off the line, power ÷ speed above it), held to
/// what the driven tyres can put down, less drag, rolling resistance and the slope. The tyres' share is the
/// grip on half the car's weight, the rear axle's for a rear-drive car: that's what makes the stock car's 0
/// to 100 km/h 5.2 s and not the 4.2 s its 4500 N alone would (the `straight` oracle, 5.17 s).
/// </para>
/// <para>
/// <b>In the air there's no drive</b>: across a jump's gap the line jumps from the lip to the landing, and
/// the car keeps its speed.
/// </para>
/// </remarks>
public static class SpeedEstimate
{
    /// <summary>The share of the car's weight on the driven wheels, for a rear-drive car at rest.</summary>
    private const float DrivenShare = 0.5f;

    /// <summary>Further than this between samples is a gap the car flies.</summary>
    private const float FlightM = 5.0f;

    /// <summary>The speed the car can have at each sample: the plan, met by full throttle from rest at the start.</summary>
    public static float[] Achievable(RacingLine line, float[] plan, CarSpec spec)
    {
        const float G = WheelModel.Gravity;
        float drag = spec.DragNPerMps2 + WheelModel.WingDragPerMps2(spec);
        float rolling = Surfaces.Road.RollingResistance * spec.MassKg * G;
        float traction = spec.Grip * spec.MassKg * G * DrivenShare;
        var speeds = new float[line.Count];
        float v = 0.0f;
        for (int i = 0; i < line.Count; i++)
        {
            if (i > 0)
            {
                float ds = line.DistanceAt(i) - line.DistanceAt(i - 1);
                if (ds < FlightM)
                {
                    float slope = line.ForwardAt(i - 1).Y * spec.MassKg * G;
                    float push = MathF.Min(WheelModel.EngineForce(spec, v), traction) - (drag * v * v) - rolling - slope;
                    v = MathF.Sqrt(MathF.Max(0.0f, (v * v) + (2.0f * push / spec.MassKg * ds)));
                }
            }

            v = MathF.Min(v, plan[i]);
            speeds[i] = v;
        }

        return speeds;
    }
}
