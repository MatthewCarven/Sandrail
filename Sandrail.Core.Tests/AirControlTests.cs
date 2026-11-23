using Sandrail.Core.Cars;

namespace Sandrail.Core.Tests;

public class AirControlTests
{
    private const float Step = 1.0f / 120.0f;
    private const float Radians = MathF.PI / 180.0f;

    [Fact]
    public void NoInputLeavesTheSpinAlone()
    {
        // A jump's own rotation is the physics'; only what the player does changes it.
        Assert.Equal(0.0f, WheelModel.AirControlStep(0.0f, 40.0f, 1.0f, 90.0f, Step));
    }

    [Fact]
    public void OffIsOff()
    {
        Assert.Equal(0.0f, WheelModel.AirControlStep(1.0f, 0.0f, 0.0f, 90.0f, Step));
    }

    [Fact]
    public void FullInputAddsTheSetSpinEachSecondAndHalfInputHalf()
    {
        Assert.Equal(40.0f * Radians * Step, WheelModel.AirControlStep(1.0f, 40.0f, 0.0f, 90.0f, Step), 6);
        Assert.Equal(-20.0f * Radians * Step, WheelModel.AirControlStep(-0.5f, 40.0f, 0.0f, 90.0f, Step), 6);
    }

    [Fact]
    public void ItAddsNoSpinPastTheCapAndStopsExactlyAtIt()
    {
        float cap = 90.0f * Radians;

        Assert.Equal(0.0f, WheelModel.AirControlStep(1.0f, 40.0f, cap, 90.0f, Step));
        Assert.Equal(0.0f, WheelModel.AirControlStep(1.0f, 40.0f, cap * 2.0f, 90.0f, Step));

        float justUnder = cap - 1e-4f;
        Assert.Equal(1e-4f, WheelModel.AirControlStep(1.0f, 40.0f, justUnder, 90.0f, Step), 6);
    }

    [Fact]
    public void ItAlwaysSlowsASpinHoweverFast()
    {
        // Spinning nose-down at four times the cap, holding nose-up still pushes at full strength: a car
        // flung off a lip can always be caught.
        float fast = -360.0f * Radians;
        Assert.Equal(40.0f * Radians * Step, WheelModel.AirControlStep(1.0f, 40.0f, fast, 90.0f, Step), 6);
    }

    [Fact]
    public void HeldForASecondFromRestItTurnsHalfTheSpinRate()
    {
        float rate = 0.0f;
        float angle = 0.0f;
        for (int i = 0; i < 120; i++)
        {
            rate += WheelModel.AirControlStep(1.0f, 40.0f, rate, 90.0f, Step);
            angle += rate * Step;
        }

        Assert.Equal(40.0f, rate / Radians, 1);
        Assert.Equal(20.0f, angle / Radians, 0);
    }

    [Fact]
    public void ANaNInputDoesNothing()
    {
        Assert.Equal(0.0f, WheelModel.AirControlStep(float.NaN, 40.0f, 0.0f, 90.0f, Step));
    }
}
