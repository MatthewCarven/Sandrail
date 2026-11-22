using Sandrail.Core.Cars;

namespace Sandrail.Core.Tests;

public class WheelModelTests
{
    private const float Step = 1.0f / 120.0f;

    private static readonly CarSpec Spec = new();

    private static float CornerLoad => Spec.MassKg * WheelModel.Gravity / 4.0f;

    [Fact]
    public void ASpringAtFullStretchPushesNothing()
    {
        Assert.Equal(0.0f, WheelModel.SuspensionForce(Spec, 0.0f, -3.0f));
    }

    [Fact]
    public void ASpringNeverPullsTheCarDown()
    {
        // Extending fast, the rebound damper would pull; the clamp is what stops a wheel leaving a
        // crest from yanking the chassis after it.
        Assert.Equal(0.0f, WheelModel.SuspensionForce(Spec, 0.02f, -5.0f));
    }

    [Fact]
    public void BumpAndReboundDampDifferently()
    {
        float spring = Spec.SpringNPerM * 0.1f;

        Assert.Equal(spring + (Spec.BumpDampingNsPerM * 0.2f), WheelModel.SuspensionForce(Spec, 0.1f, 0.2f), 3);
        Assert.Equal(spring - (Spec.ReboundDampingNsPerM * 0.2f), WheelModel.SuspensionForce(Spec, 0.1f, -0.2f), 3);
    }

    [Fact]
    public void TheBumpStopStiffensTheLastOfTheTravel()
    {
        float travel = Spec.SuspensionTravelM;
        float stopLength = travel * Spec.BumpStopFraction;
        float justBefore = travel - stopLength - 0.001f;

        // At the starting numbers the stop doubles the force at full travel: 11.2 kN a corner, about
        // seven times the car's static share. Whether that's enough for a big landing is the play's
        // question; this pins what it is.
        float atTheEnd = (Spec.SpringNPerM * travel) + (Spec.SpringNPerM * Spec.BumpStopStiffness * stopLength);

        Assert.Equal(Spec.SpringNPerM * justBefore, WheelModel.SuspensionForce(Spec, justBefore, 0.0f), 2);
        Assert.Equal(atTheEnd, WheelModel.SuspensionForce(Spec, travel, 0.0f), 1);
    }

    [Fact]
    public void RestSagIsAboutATenthOfAMetre()
    {
        Assert.InRange(WheelModel.RestCompression(Spec), 0.09f, 0.11f);
    }

    [Fact]
    public void TheSlipCurvePeaksThenEasesIntoTheSlide()
    {
        float peak = Spec.PeakSlipDeg;

        Assert.Equal(0.5f, WheelModel.SlipCurve(peak / 2.0f, peak, 0.8f), 5);
        Assert.Equal(1.0f, WheelModel.SlipCurve(peak, peak, 0.8f), 5);
        Assert.Equal(0.9f, WheelModel.SlipCurve(peak * 1.5f, peak, 0.8f), 5);
        Assert.Equal(0.8f, WheelModel.SlipCurve(peak * 3.0f, peak, 0.8f), 5);
        Assert.Equal(WheelModel.SlipCurve(-peak, peak, 0.8f), WheelModel.SlipCurve(peak, peak, 0.8f));
    }

    [Fact]
    public void SideForceOpposesTheSlide()
    {
        TyreForces right = WheelModel.Tyre(Spec, Surfaces.Road, CornerLoad, 20.0f, 1.0f, 0.0f, 0.0f, Step);
        TyreForces left = WheelModel.Tyre(Spec, Surfaces.Road, CornerLoad, 20.0f, -1.0f, 0.0f, 0.0f, Step);

        Assert.True(right.Side < 0.0f);
        Assert.True(left.Side > 0.0f);
        Assert.Equal(-right.Side, left.Side, 3);
    }

    [Fact]
    public void AWheelInTheAirGripsNothing()
    {
        Assert.Equal(default, WheelModel.Tyre(Spec, Surfaces.Road, 0.0f, 20.0f, 5.0f, 2_000.0f, 0.0f, Step));
    }

    [Fact]
    public void NoTyreGivesMoreThanItsGrip()
    {
        float limit = Spec.Grip * Surfaces.Road.Grip * CornerLoad;

        foreach (float priority in new[] { 0.0f, 0.3f, 1.0f })
        {
            CarSpec spec = Spec with { ForwardPriority = priority };
            TyreForces f = WheelModel.Tyre(spec, Surfaces.Road, CornerLoad, 10.0f, 4.0f, 5_000.0f, 0.0f, Step);
            float total = MathF.Sqrt((f.Forward * f.Forward) + (f.Side * f.Side));

            Assert.True(f.Saturated);
            Assert.True(total <= limit * 1.0001f, $"priority {priority}: {total} N against a limit of {limit} N");
        }
    }

    [Fact]
    public void PushingTakesGripFromTurning()
    {
        // The rule that makes power oversteer: the same slide holds less hard with the throttle down.
        TyreForces coasting = WheelModel.Tyre(Spec, Surfaces.Road, CornerLoad, 15.0f, 3.0f, 0.0f, 0.0f, Step);
        TyreForces pushing = WheelModel.Tyre(Spec, Surfaces.Road, CornerLoad, 15.0f, 3.0f, 2_000.0f, 0.0f, Step);

        Assert.True(MathF.Abs(pushing.Side) < MathF.Abs(coasting.Side));
    }

    [Fact]
    public void ForwardPriorityOneIsThePlansForwardFirst()
    {
        CarSpec spec = Spec with { ForwardPriority = 1.0f };
        float limit = spec.Grip * CornerLoad;
        TyreForces f = WheelModel.Tyre(spec, Surfaces.Road, CornerLoad, 15.0f, 3.0f, 10_000.0f, 0.0f, Step);

        Assert.Equal(limit, f.Forward, 1);
        Assert.Equal(0.0f, f.Side, 1);
    }

    [Fact]
    public void SandGripsLessAndDragsMore()
    {
        TyreForces road = WheelModel.Tyre(Spec, Surfaces.Road, CornerLoad, 20.0f, 5.0f, 0.0f, 0.0f, Step);
        TyreForces sand = WheelModel.Tyre(Spec, Surfaces.Sand, CornerLoad, 20.0f, 5.0f, 0.0f, 0.0f, Step);

        Assert.True(MathF.Abs(sand.Side) < MathF.Abs(road.Side));
        Assert.True(sand.Forward < road.Forward);
    }

    [Fact]
    public void NearAStandstillSidewaysSpeedIsCancelledRatherThanModelled()
    {
        // 0.05 m/s sideways at rest: the slip angle model, floored at 3 m/s, asks for about 210 N and
        // the car would creep. The cancelling term asks for enough to stop it in one tick, and at
        // 0.05 m/s the blend is still 97.5% cancel.
        TyreForces f = WheelModel.Tyre(Spec, Surfaces.Road, CornerLoad, 0.0f, 0.05f, 0.0f, 0.0f, Step);
        float cancel = 0.05f * (CornerLoad / WheelModel.Gravity) / Step;

        Assert.InRange(-f.Side, cancel * 0.97f, cancel);
    }

    [Fact]
    public void BrakesFadeAtAStandstillInsteadOfFlippingSign()
    {
        TyreForces rolling = WheelModel.Tyre(Spec, Surfaces.Road, CornerLoad, 5.0f, 0.0f, 0.0f, 1_000.0f, Step);
        TyreForces stopped = WheelModel.Tyre(Spec, Surfaces.Road, CornerLoad, 0.0f, 0.0f, 0.0f, 1_000.0f, Step);

        Assert.True(rolling.Forward < -900.0f);
        Assert.Equal(0.0f, stopped.Forward);
    }

    [Fact]
    public void TheEngineIsForceLimitedOffTheLineAndPowerLimitedAbove()
    {
        float crossover = Spec.PowerW / Spec.DriveForceN;

        Assert.Equal(Spec.DriveForceN, WheelModel.EngineForce(Spec, 0.0f));
        Assert.Equal(Spec.DriveForceN, WheelModel.EngineForce(Spec, crossover * 0.9f));
        Assert.Equal(Spec.PowerW / 40.0f, WheelModel.EngineForce(Spec, 40.0f), 2);
    }

    [Fact]
    public void BrakeSlowsYouWhileRollingAndReversesAtAStandstill()
    {
        (float driveRolling, float brakeRolling) = WheelModel.ResolvePedals(Spec, 0.0f, 1.0f, 10.0f);
        (float driveStopped, float brakeStopped) = WheelModel.ResolvePedals(Spec, 0.0f, 1.0f, 0.0f);
        (float driveFlatOut, _) = WheelModel.ResolvePedals(Spec, 0.0f, 1.0f, -Spec.ReverseTopSpeedMps - 1.0f);

        Assert.Equal((0.0f, 1.0f), (driveRolling, brakeRolling));
        Assert.True(driveStopped < 0.0f);
        Assert.Equal(0.0f, brakeStopped);
        Assert.Equal(0.0f, driveFlatOut);
    }

    [Fact]
    public void ThrottleWhileRollingBackwardBrakes()
    {
        Assert.Equal((0.0f, 0.7f), WheelModel.ResolvePedals(Spec, 0.7f, 0.0f, -5.0f));
    }

    [Fact]
    public void SteeringNarrowsWithSpeed()
    {
        Assert.Equal(Spec.MaxSteerDeg, WheelModel.SteerLimitDeg(Spec, 0.0f));
        Assert.Equal(Spec.MinSteerDeg, WheelModel.SteerLimitDeg(Spec, 80.0f));
        Assert.Equal(19.0f, WheelModel.SteerLimitDeg(Spec, 25.0f), 3);
    }

    [Fact]
    public void WideLockMultipliesTheLockAtEverySpeed()
    {
        foreach (float speed in new[] { 0.0f, 25.0f, 80.0f })
        {
            Assert.Equal(
                WheelModel.SteerLimitDeg(Spec, speed) * Spec.WideLockFactor,
                WheelModel.SteerLimitDeg(Spec, speed, wideLock: true),
                4);
        }

        Assert.Equal(60.0f, WheelModel.SteerLimitDeg(Spec, 0.0f, wideLock: true), 4);
        Assert.Equal(1.0f, WheelModel.SteerScale(Spec, wideLock: false));
        Assert.Equal(1.0f, WheelModel.SteerScale(Spec with { WideLockFactor = 1.0f }, wideLock: true));
    }

    [Fact]
    public void AckermannTurnsTheInsideWheelMoreAndBothRoundOneCentre()
    {
        foreach (float steer in new[] { 10.0f, 28.4f, 60.0f })
        {
            (float left, float right) = WheelModel.FrontWheelsDeg(Spec, steer);
            Assert.True(right > steer && steer > left, $"at {steer}°: left {left}°, right {right}°");

            // Both wheels' axles pass through the same point on the rear axle's line.
            float half = Spec.TrackM / 2.0f;
            float fromLeft = (Spec.WheelbaseM / MathF.Tan(left * MathF.PI / 180.0f)) - half;
            float fromRight = (Spec.WheelbaseM / MathF.Tan(right * MathF.PI / 180.0f)) + half;
            Assert.Equal(fromLeft, fromRight, 3);

            (float mirrorLeft, float mirrorRight) = WheelModel.FrontWheelsDeg(Spec, -steer);
            Assert.Equal(-right, mirrorLeft, 4);
            Assert.Equal(-left, mirrorRight, 4);
        }
    }

    [Fact]
    public void AckermannShareZeroIsParallelSteering()
    {
        Assert.Equal((25.0f, 25.0f), WheelModel.FrontWheelsDeg(Spec with { AckermannShare = 0.0f }, 25.0f));
        Assert.Equal((0.0f, 0.0f), WheelModel.FrontWheelsDeg(Spec, 0.0f));
    }

    [Fact]
    public void TheStartingNumbersTopOutNearFiftyMetresASecondOnRoad()
    {
        // The plan's target for the straight oracle, checked before Godot is involved. Traction can
        // only lower what the oracle reads; it can't raise it above this.
        Assert.InRange(WheelModel.TopSpeedEstimate(Spec, Surfaces.Road), 48.0f, 52.0f);
    }

    [Fact]
    public void AntiRollPushesTheCompressedSideUp()
    {
        Assert.True(WheelModel.AntiRollForce(Spec.AntiRollFrontNPerM, 0.2f, 0.1f) > 0.0f);
        Assert.Equal(0.0f, WheelModel.AntiRollForce(Spec.AntiRollFrontNPerM, 0.1f, 0.1f));
    }
}
