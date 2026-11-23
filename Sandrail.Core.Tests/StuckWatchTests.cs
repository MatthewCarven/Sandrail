using Sandrail.Core.Driving;

namespace Sandrail.Core.Tests;

public class StuckWatchTests
{
    private const float Step = 1.0f / 120.0f;

    /// <summary>Feeds the same state for a while and returns the first reason given, and when.</summary>
    private static (StuckReason? Reason, float At) Hold(StuckWatch watch, float upright, float speed, float height, float seconds)
    {
        int ticks = (int)MathF.Round(seconds / Step);
        for (int i = 1; i <= ticks; i++)
        {
            if (watch.Update(upright, speed, height, Step) is StuckReason reason)
            {
                return (reason, i * Step);
            }
        }

        return (null, seconds);
    }

    [Fact]
    public void OnItsRoofAndStoppedItsPutBackAfterThreeSeconds()
    {
        (StuckReason? reason, float at) = Hold(new StuckWatch(), -1.0f, 0.0f, 1.0f, 5.0f);

        Assert.Equal(StuckReason.TippedOver, reason);
        Assert.Equal(StuckWatch.Seconds, at, 2);
    }

    [Fact]
    public void OnItsSideCountsToo()
    {
        Assert.Equal(StuckReason.TippedOver, Hold(new StuckWatch(), 0.0f, 0.5f, 1.0f, 5.0f).Reason);
    }

    [Fact]
    public void LeaningOnABankDoesNot()
    {
        // The corner lap's steepest bank is 35°: the car's up is 0.82 of the world's.
        Assert.Null(Hold(new StuckWatch(), MathF.Cos(35.0f * MathF.PI / 180.0f), 0.0f, 1.0f, 10.0f).Reason);
    }

    [Fact]
    public void UpsideDownAtSpeedIsTheTopOfALoopNotACrash()
    {
        Assert.Null(Hold(new StuckWatch(), -1.0f, 22.0f, 12.0f, 10.0f).Reason);
    }

    [Fact]
    public void AnyMomentUprightOrMovingStartsTheCountAgain()
    {
        var watch = new StuckWatch();
        Hold(watch, -1.0f, 0.0f, 1.0f, 2.5f);
        Assert.Equal(2.5f, watch.StuckFor, 2);

        watch.Update(-1.0f, 3.0f, 1.0f, Step);
        Assert.Equal(0.0f, watch.StuckFor);

        (StuckReason? reason, float at) = Hold(watch, -1.0f, 0.0f, 1.0f, 5.0f);
        Assert.Equal(StuckReason.TippedOver, reason);
        Assert.Equal(StuckWatch.Seconds, at, 2);
    }

    [Fact]
    public void ItAsksOnceThenCountsAfresh()
    {
        // A respawn lands a tick later; the watch mustn't ask again in between.
        var watch = new StuckWatch();
        Hold(watch, -1.0f, 0.0f, 1.0f, StuckWatch.Seconds);

        Assert.Null(watch.Update(-1.0f, 0.0f, 1.0f, Step));
        Assert.Equal(Step, watch.StuckFor, 5);
    }

    [Fact]
    public void BelowTheWorldItsLostAtOnce()
    {
        var watch = new StuckWatch();

        Assert.Null(watch.Update(1.0f, 30.0f, -29.0f, Step));
        Assert.Equal(StuckReason.Lost, watch.Update(1.0f, 30.0f, -31.0f, Step));
    }

    [Fact]
    public void NaNReadsAsLostNotAsFine()
    {
        Assert.Equal(StuckReason.Lost, new StuckWatch().Update(1.0f, 0.0f, float.NaN, Step));
    }

    [Fact]
    public void ResetStartsTheCountAgain()
    {
        var watch = new StuckWatch();
        Hold(watch, -1.0f, 0.0f, 1.0f, 2.0f);
        watch.Reset();

        Assert.Equal(0.0f, watch.StuckFor);
        Assert.Equal(StuckWatch.Seconds, watch.SecondsLeft);
    }
}
