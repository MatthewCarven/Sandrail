using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tests;

public class WingTests
{
    private static readonly CarSpec NoWing = new();

    private static CarSpec With(Wing wing, float angleDeg) => new() { WingId = wing.Id, WingAngleDeg = angleDeg };

    private static readonly Lazy<RacingLine> TheLap = new(() =>
        new RacingLine(new TestGroundLayout().Runs.Single(r => r.Name == "corners").Pieces, closed: true));

    [Fact]
    public void TheCarComesWithNoWingAndNoWingDoesNothing()
    {
        Assert.Equal(Wings.None.Id, NoWing.WingId);
        Assert.Equal(0.0f, WheelModel.DownforcePerMps2(NoWing));
        Assert.Equal(0.0f, WheelModel.WingDragPerMps2(NoWing));
        Assert.Equal((0.0f, 0.0f), WheelModel.WingForces(NoWing, 50.0f));
    }

    [Fact]
    public void DownforceGrowsWithAngleUpToTheStallAndFallsAwayPastIt()
    {
        foreach (Wing wing in Wings.All.Where(w => w.AreaM2 > 0.0f))
        {
            float atStall = wing.LiftCoefficient(wing.StallDeg);
            Assert.True(wing.LiftCoefficient(wing.StallDeg - 2.0f) < atStall, wing.Id);
            Assert.True(wing.LiftCoefficient(wing.StallDeg + 5.0f) < atStall, wing.Id);
            Assert.Equal(atStall / 2.0f, wing.LiftCoefficient(wing.StallDeg + Wing.StallFadeDeg + 5.0f), 4);

            // Met from underneath, it lifts as much as it would press.
            Assert.Equal(-wing.LiftCoefficient(5.0f), wing.LiftCoefficient(-5.0f));
        }
    }

    [Fact]
    public void AirFromBelowPressesLessAndPastTheSetAngleLifts()
    {
        // A strut wing set at 12°, at 30 m/s: the car level, its nose 6° up of where it's going (half the
        // angle left), and 20° up (8° the wrong way, so it lifts: in the air, that turns the nose back down).
        CarSpec spec = With(Wings.Strut, 12.0f);
        float Down(float noseUpDeg) =>
            WheelModel.WingForces(spec, 30.0f * MathF.Cos(noseUpDeg * MathF.PI / 180.0f), -30.0f * MathF.Sin(noseUpDeg * MathF.PI / 180.0f)).Downforce;

        Assert.Equal(WheelModel.DownforcePerMps2(spec) * 900.0f, Down(0.0f), 1);
        Assert.Equal(Down(0.0f) / 2.0f, Down(6.0f), 1);
        Assert.True(Down(20.0f) < 0.0f, $"{Down(20.0f):F0} N");
    }

    [Fact]
    public void DragClimbsWithAngleAndSteeplyPastTheStall()
    {
        Wing wing = Wings.Strut;
        float before = wing.DragCoefficient(wing.StallDeg - 1.0f);
        float at = wing.DragCoefficient(wing.StallDeg);
        float after = wing.DragCoefficient(wing.StallDeg + 1.0f);

        Assert.True(wing.DragCoefficient(0.0f) < before);
        Assert.True(before < at && at < after);
        Assert.True(after - at > at - before, "past the stall a degree costs more than before it");
    }

    [Fact]
    public void ABiggerWingPressesMoreAndDragsMore()
    {
        float[] down = [.. Wings.All.Select(w => WheelModel.DownforcePerMps2(With(w, w.StallDeg)))];
        float[] drag = [.. Wings.All.Select(w => WheelModel.WingDragPerMps2(With(w, w.StallDeg)))];

        for (int i = 1; i < down.Length; i++)
        {
            Assert.True(down[i] > down[i - 1], $"{Wings.All[i].Id} presses {down[i]:F3} against {down[i - 1]:F3}");
            Assert.True(drag[i] > drag[i - 1], $"{Wings.All[i].Id} drags {drag[i]:F3} against {drag[i - 1]:F3}");
        }

        // The twin-plane wing at its stall, at 30 m/s: about 1,000 N, a sixth of the buggy's weight.
        Assert.InRange(WheelModel.DownforcePerMps2(With(Wings.TwinPlane, 18.0f)) * 900.0f, 950.0f, 1100.0f);
    }

    [Fact]
    public void TheForcesGoWithSpeedSquaredAndOnlyWithAirFromTheFront()
    {
        CarSpec spec = With(Wings.Strut, 12.0f);
        (float down20, float drag20) = WheelModel.WingForces(spec, 20.0f);
        (float down40, float drag40) = WheelModel.WingForces(spec, 40.0f);

        Assert.Equal(4.0f * down20, down40, 2);
        Assert.Equal(4.0f * drag20, drag40, 2);
        Assert.Equal((0.0f, 0.0f), WheelModel.WingForces(spec, -20.0f));
    }

    [Fact]
    public void AWingCostsTopSpeedAndABiggerOneCostsMore()
    {
        float bare = WheelModel.TopSpeedEstimate(NoWing, Surfaces.Road);
        float lip = WheelModel.TopSpeedEstimate(With(Wings.Lip, 10.0f), Surfaces.Road);
        float twin = WheelModel.TopSpeedEstimate(With(Wings.TwinPlane, 18.0f), Surfaces.Road);

        Assert.True(lip < bare && twin < lip, $"{bare * 3.6f:F0}, {lip * 3.6f:F0}, {twin * 3.6f:F0} km/h");
    }

    [Fact]
    public void AnUnknownOrBareWingIsRefusedAndFittedAsNone()
    {
        Assert.NotEmpty(new CarSpec { WingId = "base:wing-jet" }.Problems());

        // A file names content in full; only the command line's flags may leave off base:.
        Assert.NotEmpty(new CarSpec { WingId = "wing-lip" }.Problems());
        Assert.Same(Wings.None, new CarSpec { WingId = "base:wing-jet" }.FittedWing());
        Assert.Empty(With(Wings.TwinPlane, 18.0f).Problems());
    }

    [Fact]
    public void WingsAreFoundByFullOrBareName()
    {
        Assert.Same(Wings.Lip, Wings.Find("wing-lip"));
        Assert.Same(Wings.Lip, Wings.Find("base:wing-lip"));
        Assert.Null(Wings.Find("wing-jet"));
    }

    [Fact]
    public void AWingedCarRoundTripsThroughItsFile()
    {
        CarSpec spec = With(Wings.Strut, 12.5f) with { WingBackM = 1.2f };

        Assert.Equal(spec, CarSpec.Parse(spec.ToJson()));
        Assert.Contains("\"wingId\": \"base:wing-strut\"", spec.ToJson());
        Assert.NotEqual(NoWing.Fingerprint(), (NoWing with { WingId = Wings.Lip.Id }).Fingerprint());
    }

    [Fact]
    public void DownforceRaisesEveryCornersPlannedSpeedByTheFormula()
    {
        RacingLine line = TheLap.Value;
        CarSpec winged = With(Wings.TwinPlane, 18.0f);
        float[] bare = line.SpeedProfile(NoWing, caution: 1.0f);
        float[] pressed = line.SpeedProfile(winged, caution: 1.0f);

        for (int c = 0; c < TestGroundLayout.Corners.Length; c++)
        {
            int middle = Middle(line, c);
            Assert.True(pressed[middle] > bare[middle], $"corner {c + 1}: {pressed[middle] * 3.6f:F1} against {bare[middle] * 3.6f:F1} km/h");
        }

        // Corner 1, flat and 25 m: v² = gμ / (1/R − μk/m).
        float mu = winged.Grip;
        float k = WheelModel.DownforcePerMps2(winged) / winged.MassKg;
        Assert.Equal(MathF.Sqrt(9.81f * mu / ((1.0f / 25.0f) - (mu * k))), pressed[Middle(line, 0)], 0);
    }

    [Fact]
    public void AWinglessPlanIsTheOldPlanExactly()
    {
        // The formula was rewritten for downforce so that with none it's the old one to the last bit: the
        // lap oracle's figures for the shipped car must not move.
        RacingLine line = TheLap.Value;
        float[] speeds = line.SpeedProfile(NoWing, caution: 1.0f);
        float tan = MathF.Tan(20.0f * MathF.PI / 180.0f);
        float bend = 1.0f / 18.0f;
        float old = MathF.Sqrt(9.81f / bend * (tan + NoWing.Grip) / MathF.Max(1.0f - (NoWing.Grip * tan), 0.05f));

        Assert.Equal(old, speeds[Middle(line, 2)], 0);
    }

    [Fact]
    public void AWingedPlanNeverBrakesHarderThanTheGripAndDownforceGive()
    {
        RacingLine line = TheLap.Value;
        CarSpec winged = With(Wings.TwinPlane, 18.0f);
        const float Caution = 0.85f;
        float[] speeds = line.SpeedProfile(winged, Caution);
        float k = WheelModel.DownforcePerMps2(winged) / winged.MassKg;

        for (int i = 0; i < line.Count; i++)
        {
            int next = (i + 1) % line.Count;
            float ds = line.DistanceAt(next) - line.DistanceAt(i);
            ds = ds < 0.0f ? ds + line.Length : ds;
            float braking = (9.81f + (k * speeds[next] * speeds[next])) * winged.Grip * Caution * 0.9f;
            float drop = (speeds[i] * speeds[i]) - (speeds[next] * speeds[next]);
            Assert.True(drop <= (2.0f * braking * ds) + 0.01f, $"sample {i}: asks to shed {drop:F2} m²/s² in {ds:F2} m");
        }
    }

    private static int Middle(RacingLine line, int corner)
    {
        int piece = (2 * corner) + 1;
        int[] on = Enumerable.Range(0, line.Count).Where(i => line.PieceAt(i) == piece).ToArray();
        return on[on.Length / 2];
    }
}
