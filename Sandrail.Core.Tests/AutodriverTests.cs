using System.Numerics;
using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tests;

public class AutodriverTests
{
    private static readonly CarSpec Spec = new();

    private static readonly TestGroundLayout Layout = new();

    /// <summary>Half the lap's flat road, and the buggy's middle to the outside of its tyres.</summary>
    private const float RoadHalfWidth = 4.85f;

    private static readonly float CarHalfWidth = (Spec.TrackM / 2.0f) + 0.15f;

    // Built once: the fast line takes tens of milliseconds, and the tests ask for it over and over.
    private static readonly Lazy<RacingLine> TheLap = new(() =>
        new RacingLine(Layout.Runs.Single(r => r.Name == "corners").Pieces, closed: true));

    private static readonly Lazy<RacingLine> TheFastLine = new(() => Lap().MinimumCurvature(RoadHalfWidth, CarHalfWidth));

    private static RacingLine Lap() => TheLap.Value;

    private static RacingLine Fast() => TheFastLine.Value;

    /// <summary>The middle sample of one of the lap's corners.</summary>
    private static int MiddleOfCorner(RacingLine line, int corner)
    {
        int piece = (2 * corner) + 1;
        int[] on = Enumerable.Range(0, line.Count).Where(i => line.PieceAt(i) == piece).ToArray();
        return on[on.Length / 2];
    }

    [Fact]
    public void TheLineIsAsLongAsTheLap()
    {
        RacingLine line = Lap();
        float lap = Layout.Runs.Single(r => r.Name == "corners").Pieces.Sum(p => p.Length);

        // A little longer than the pieces' lengths, which are measured flat: the banked corners lift the
        // middle of the road up to 2.9 m and bring it back down, and the line measures the climb.
        Assert.InRange(line.Length, lap, lap * 1.005f);
        Assert.InRange(line.Count, (int)lap, (int)lap + 20);
    }

    [Fact]
    public void CurvatureIsOneOverTheRadiusInACornerAndNothingOnAStraight()
    {
        RacingLine line = Lap();

        for (int c = 0; c < TestGroundLayout.Corners.Length; c++)
        {
            (float angle, float radius, _) = TestGroundLayout.Corners[c];
            float expected = MathF.Sign(angle) / radius;
            Assert.Equal(expected, line.CurvatureAt(MiddleOfCorner(line, c)), 3);
        }

        int straight = Enumerable.Range(0, line.Count).First(i => line.PieceAt(i) == 0 && line.DistanceAt(i) > 40.0f);
        Assert.Equal(0.0f, line.CurvatureAt(straight), 4);
    }

    [Fact]
    public void TheFastLineKeepsAllFourWheelsOnTheRoad()
    {
        RacingLine line = Fast();
        float room = RoadHalfWidth - CarHalfWidth - 0.25f;

        for (int i = 0; i < line.Count; i++)
        {
            Assert.True(MathF.Abs(line.OffsetAt(i)) <= room + 1e-4f, $"sample {i} is {line.OffsetAt(i):F2} m off the middle; the room is {room:F2}");
        }

        Assert.True(Enumerable.Range(0, line.Count).Max(i => MathF.Abs(line.OffsetAt(i))) > room - 0.05f, "it never used the road's width");
    }

    [Fact]
    public void TheFastLineTurnsLessThanTheMiddleOfTheRoad()
    {
        RacingLine middle = Lap();
        RacingLine fast = Fast();
        float Squared(RacingLine l) => Enumerable.Range(0, l.Count).Sum(i => l.CurvatureAt(i) * l.CurvatureAt(i));

        // Measured 2026-11-23: 0.311 on the middle, 0.245 on the fast line, 21% less. It minimises the
        // lap's total, not its worst point, so the long 135° and the steep 35° each end up with one point
        // tighter than the middle's, traded for straighter entries and exits; both are still faster
        // corners (TheFastLineIsFasterThroughEveryCorner).
        Assert.True(Squared(fast) < 0.85f * Squared(middle), $"{Squared(fast):F4} against the middle's {Squared(middle):F4}");
    }

    [Fact]
    public void TheFastLineClipsTheInsideOfACorner()
    {
        // Corner 1 turns right, so its inside is to the right: positive offset at the apex.
        Assert.True(Fast().OffsetAt(MiddleOfCorner(Fast(), 0)) > 1.5f);

        // Corner 4 is the left-hander: its inside is to the left.
        Assert.True(Fast().OffsetAt(MiddleOfCorner(Fast(), 3)) < -1.5f);
    }

    [Fact]
    public void TheFastLineIsFasterThroughEveryCorner()
    {
        RacingLine lap = Lap();
        RacingLine line = Fast();
        float[] middle = lap.SpeedProfile(Spec, 0.85f);
        float[] fast = line.SpeedProfile(Spec, 0.85f);

        for (int c = 0; c < TestGroundLayout.Corners.Length; c++)
        {
            float slowestMiddle = Enumerable.Range(0, lap.Count).Where(i => lap.PieceAt(i) == (2 * c) + 1).Min(i => middle[i]);
            float slowestFast = Enumerable.Range(0, line.Count).Where(i => line.PieceAt(i) == (2 * c) + 1).Min(i => fast[i]);
            Assert.True(slowestFast > slowestMiddle, $"corner {c + 1}: {slowestFast * 3.6f:F0} km/h against the middle's {slowestMiddle * 3.6f:F0}");
        }
    }

    [Fact]
    public void TheBankIsReadOffTheRoad()
    {
        RacingLine line = Lap();

        Assert.Equal(35.0f, line.BankDegAt(MiddleOfCorner(line, 4)), 1);
        Assert.Equal(0.0f, line.BankDegAt(MiddleOfCorner(line, 0)), 1);
    }

    [Fact]
    public void CornerSpeedsFollowTheBankedCurveFormula()
    {
        RacingLine line = Lap();
        float[] speeds = line.SpeedProfile(Spec, caution: 1.0f);
        float mu = Spec.Grip;

        // Flat, 25 m: √(g R μ).
        Assert.Equal(MathF.Sqrt(9.81f * 25.0f * mu), speeds[MiddleOfCorner(line, 0)], 0);

        // 20°, 18 m, corner 3: nothing slower follows closely, so its own limit is what the plan says.
        float tan = MathF.Tan(20.0f * MathF.PI / 180.0f);
        float banked = MathF.Sqrt(9.81f * 18.0f * (tan + mu) / (1.0f - (mu * tan)));
        Assert.Equal(banked, speeds[MiddleOfCorner(line, 2)], 0);
    }

    [Fact]
    public void ThePlanBrakesBeforeACornerNotInIt()
    {
        // On the 85 m straight into corner 1 the plan starts far above the corner's speed and falls steadily
        // to it by the time the corner starts: the braking is done on the straight. (It's too short to reach
        // top speed: braking from 50 m/s to the corner's 17 takes about 130 m.)
        RacingLine line = Lap();
        float[] speeds = line.SpeedProfile(Spec, caution: 0.85f);
        int[] straight = Enumerable.Range(0, line.Count).Where(i => line.PieceAt(i) == 0).Skip(5).ToArray();
        float cornerSpeed = Enumerable.Range(0, line.Count).Where(i => line.PieceAt(i) == 1).Min(i => speeds[i]);

        Assert.True(speeds[straight[0]] > cornerSpeed + 10.0f, $"{speeds[straight[0]]:F1} m/s early on against the corner's {cornerSpeed:F1}");
        Assert.True(speeds[straight[^1]] < cornerSpeed + 3.0f, $"{speeds[straight[^1]]:F1} at the corner's start against {cornerSpeed:F1} in it");
        for (int k = 1; k < straight.Length; k++)
        {
            Assert.True(speeds[straight[k]] <= speeds[straight[k - 1]] + 1e-3f, $"the plan speeds up again at sample {straight[k]}");
        }
    }

    [Fact]
    public void ThePlanNeverAsksForMoreBrakingThanTheGripGives()
    {
        RacingLine line = Lap();
        const float Caution = 0.85f;
        float[] speeds = line.SpeedProfile(Spec, Caution);
        float braking = 9.81f * Spec.Grip * Caution * 0.9f;

        for (int i = 0; i < line.Count; i++)
        {
            int next = (i + 1) % line.Count;
            float ds = line.DistanceAt(next) - line.DistanceAt(i);
            ds = ds < 0.0f ? ds + line.Length : ds;
            float drop = (speeds[i] * speeds[i]) - (speeds[next] * speeds[next]);
            Assert.True(drop <= (2.0f * braking * ds) + 0.01f, $"sample {i}: asks to shed {drop:F2} m²/s² in {ds:F2} m");
        }
    }

    [Fact]
    public void ItSteersBackTowardTheLine()
    {
        RacingLine line = Lap();
        var driver = new Autodriver(line, Spec);
        Frame start = Layout.CornerLapStart;
        Vector3 velocity = start.Forward * 10.0f;

        DriveCommand rightOfLine = driver.Drive(start.Position + (start.Right * 2.0f), start.Forward, start.Up, velocity);
        driver = new Autodriver(line, Spec);
        DriveCommand leftOfLine = driver.Drive(start.Position - (start.Right * 2.0f), start.Forward, start.Up, velocity);
        driver = new Autodriver(line, Spec);
        DriveCommand onLine = driver.Drive(start.Position, start.Forward, start.Up, velocity);

        Assert.True(rightOfLine.Steer < 0.0f, $"2 m right, it steered {rightOfLine.Steer}");
        Assert.True(leftOfLine.Steer > 0.0f, $"2 m left, it steered {leftOfLine.Steer}");
        // Not quite zero: the lap starts just out of corner 6, and the line still carries a trace of its bend.
        Assert.InRange(onLine.Steer, -0.05f, 0.05f);
    }

    [Fact]
    public void ItHoldsACircleToAFewCentimetres()
    {
        // Steady speed round a 30 m circle, the kinematic car of the lap test. Reading the line's direction
        // ahead of the front axle left it a metre inside at 15 m/s; at the axle, with the body compared at
        // its middle, it measured 1-3 cm (2026-11-23).
        var circle = new RacingLine([new TurnCentreline(Frame.Level(Vector3.Zero, 0.0f), 360.0f, 30.0f)], closed: true);
        foreach (float speed in new[] { 5.0f, 15.0f })
        {
            var driver = new Autodriver(circle, Spec);
            const float Dt = 1.0f / 120.0f;
            Vector3 position = Vector3.Zero;
            float heading = 0.0f;
            float steerDeg = 0.0f;
            for (float t = 0.0f; t < 10.0f; t += Dt)
            {
                var forward = new Vector3(MathF.Sin(heading), 0.0f, -MathF.Cos(heading));
                DriveCommand cmd = driver.Drive(position, forward, Vector3.UnitY, forward * speed);
                steerDeg = MoveToward(steerDeg, cmd.Steer * WheelModel.SteerLimitDeg(Spec, speed), Spec.SteerRateDegPerS * Dt);
                heading += speed / Spec.WheelbaseM * MathF.Tan(steerDeg * MathF.PI / 180.0f) * Dt;
                position += forward * (speed * Dt);
            }

            Assert.True(MathF.Abs(driver.Lateral) < 0.05f, $"at {speed} m/s it settled {driver.Lateral:F3} m off the line");
        }
    }

    [Fact]
    public void ItAcceleratesWhenSlowAndNeverBrakesToAStandstill()
    {
        RacingLine line = Lap();
        var driver = new Autodriver(line, Spec);
        Frame start = Layout.CornerLapStart;

        DriveCommand fromRest = driver.Drive(start.Position, start.Forward, start.Up, Vector3.Zero);

        Assert.Equal(1.0f, fromRest.Throttle);
        Assert.Equal(0.0f, fromRest.Brake);
    }

    [Fact]
    public void ACarThatGoesWhereItsPointedLapsTheFastLineWithItsWheelsOnTheRoad()
    {
        // A kinematic car: no slip, the bicycle model's turn rate, the spec's engine and drag, the same
        // steering rate limit. It can't test grip, which only Godot can; it tests that the pursuit and
        // the speed plan get round the lap on the road, closed loop.
        RacingLine line = Fast();
        var driver = new Autodriver(line, Spec);
        Frame start = Layout.CornerLapStart;
        const float Dt = 1.0f / 120.0f;

        Vector3 position = start.Position;
        float heading = 0.0f;
        float speed = 0.0f;
        float steerDeg = 0.0f;
        float worst = 0.0f;
        float worstFromLine = 0.0f;
        float travelled = 0.0f;
        float time = 0.0f;

        while (travelled < line.Length + 20.0f && time < 120.0f)
        {
            var forward = new Vector3(MathF.Sin(heading), 0.0f, -MathF.Cos(heading));
            DriveCommand cmd = driver.Drive(position, forward, Vector3.UnitY, forward * speed);

            // This car drives on the flat, so on a banked corner, where the road is lifted and tilted, the
            // height between them reads as sideways and the measure means nothing; Godot judges the banks.
            if (line.BankDegAt(driver.Index) < 1.0f)
            {
                worst = MathF.Max(worst, MathF.Abs(line.OffsetFromMiddle(driver.Index, position)));
            }

            if (travelled > 30.0f)
            {
                worstFromLine = MathF.Max(worstFromLine, MathF.Abs(driver.Lateral));
            }

            float limit = WheelModel.SteerLimitDeg(Spec, speed);
            steerDeg = MoveToward(steerDeg, cmd.Steer * limit, Spec.SteerRateDegPerS * Dt);
            float push = cmd.Throttle * WheelModel.EngineForce(Spec, speed);
            float accel = ((push - (Spec.DragNPerMps2 * speed * speed)) / Spec.MassKg) - (cmd.Brake * 9.0f);
            speed = MathF.Max(0.0f, speed + (accel * Dt));
            heading += speed / Spec.WheelbaseM * MathF.Tan(steerDeg * MathF.PI / 180.0f) * Dt;
            position += forward * (speed * Dt);
            travelled += speed * Dt;
            time += Dt;
        }

        // Its middle never further from the road's middle than leaves its tyres on the 9.7 m surface.
        Assert.True(travelled >= line.Length, $"only {travelled:F0} m of {line.Length:F0} in {time:F0} s");
        Assert.True(worst < RoadHalfWidth - CarHalfWidth, $"strayed {worst:F2} m from the middle; the tyres leave the road past {RoadHalfWidth - CarHalfWidth:F2}");

        // Once away from the start, where it begins off the line: measured 0.40 m, 2026-11-23.
        Assert.True(worstFromLine < 0.6f, $"strayed {worstFromLine:F2} m from the line it was following");
    }

    private static float MoveToward(float from, float to, float delta) =>
        MathF.Abs(to - from) <= delta ? to : from + (MathF.Sign(to - from) * delta);
}
