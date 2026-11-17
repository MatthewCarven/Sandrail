using System.Numerics;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tests;

public class CentrelineTests
{
    private const float Millimetre = 0.001f;

    private static readonly Frame Origin = Frame.Level(Vector3.Zero, 0.0f);

    internal static void Near(Vector3 expected, Vector3 actual, float tolerance, string what = "")
    {
        float off = Vector3.Distance(expected, actual);
        Assert.True(off <= tolerance, $"{what} expected {expected}, got {actual} ({off:F5} off)");
    }

    private static IEnumerable<ICentreline> EveryKind()
    {
        yield return new LineCentreline(Origin, 25.0f);
        yield return new PitchCentreline(Origin, 20.0f, 12.0f);
        yield return new PitchCentreline(Origin, -15.0f, 30.0f);
        yield return new TurnCentreline(Origin, 90.0f, 20.0f);
        yield return new TurnCentreline(Origin, -180.0f, 35.0f, 30.0f, 5.0f);
        yield return new LoopCentreline(Origin, 18.0f, 7.0f, 13.0f);
    }

    [Fact]
    public void EveryCentrelineStartsExactlyAtItsStart()
    {
        foreach (ICentreline line in EveryKind())
        {
            Frame first = line.FrameAt(0.0f);
            Near(Origin.Position, first.Position, Millimetre, line.GetType().Name);
            Near(Origin.Up, first.Up, Millimetre, line.GetType().Name);
        }
    }

    [Fact]
    public void FramesStayUnitAndSquareWhereTheyMustBe()
    {
        // Forward and up are square everywhere; right is square to up everywhere, and to forward too
        // except on the loop, whose right is fixed on purpose (see LoopCentreline).
        foreach (ICentreline line in EveryKind())
        {
            for (int k = 0; k <= 50; k++)
            {
                Frame f = line.FrameAt(line.Length * k / 50.0f);
                string at = $"{line.GetType().Name} at {k}/50";
                Assert.True(MathF.Abs(f.Forward.Length() - 1.0f) < 1e-4f, at);
                Assert.True(MathF.Abs(f.Up.Length() - 1.0f) < 1e-4f, at);
                Assert.True(MathF.Abs(Vector3.Dot(f.Forward, f.Up)) < 1e-4f, at);
                Assert.True(MathF.Abs(Vector3.Dot(f.Right, f.Up)) < 1e-4f, at);
                if (line is not LoopCentreline)
                {
                    Assert.True(MathF.Abs(Vector3.Dot(f.Right, f.Forward)) < 1e-4f, at);
                }
            }
        }
    }

    [Fact]
    public void ARightTurnOfNinetyEndsWhereTheGeometrySays()
    {
        Frame end = new TurnCentreline(Origin, 90.0f, 20.0f).FrameAt(float.MaxValue);

        Near(new Vector3(20.0f, 0.0f, -20.0f), end.Position, Millimetre);
        Near(Vector3.UnitX, end.Forward, 1e-4f);
    }

    [Fact]
    public void ALeftTurnGoesLeft()
    {
        Frame end = new TurnCentreline(Origin, -90.0f, 20.0f).FrameAt(float.MaxValue);

        Near(new Vector3(-20.0f, 0.0f, -20.0f), end.Position, Millimetre);
        Near(-Vector3.UnitX, end.Forward, 1e-4f);
    }

    [Fact]
    public void ABankTiltsTowardTheInsideAndKeepsTheInnerEdgeOnTheGround()
    {
        var turn = new TurnCentreline(Origin, 180.0f, 35.0f, 30.0f, 5.0f);
        Frame middle = turn.FrameAt(turn.Length / 2.0f);

        // A right turn: the surface faces right of up, the inside (right) edge sits where it started, and
        // the outside edge is lifted by the road's width times sin 30°.
        Assert.True(Vector3.Dot(middle.Up, middle.Right) == 0.0f || middle.Up.Y < 1.0f);
        Assert.Equal(MathF.Cos(MathF.PI / 6.0f), middle.Up.Y, 3);
        Assert.Equal(0.0f, middle.Across(5.0f, 0.0f).Y, 3);
        Assert.Equal(5.0f, middle.Across(-5.0f, 0.0f).Y, 3);

        Near(Vector3.UnitY, turn.FrameAt(0.0f).Up, 1e-4f, "start");
        Near(Vector3.UnitY, turn.FrameAt(turn.Length).Up, 1e-4f, "end");
    }

    [Fact]
    public void UpThenDownByTheSameAngleComesBackLevel()
    {
        Chain chain = new Chain(Origin).Pitch(20.0f, 10.0f).Pitch(-20.0f, 10.0f);
        float rise = 2.0f * 10.0f * (1.0f - MathF.Cos(20.0f * MathF.PI / 180.0f));

        Near(Origin.Forward, chain.End.Forward, 1e-4f, "forward");
        Assert.Equal(rise, chain.End.Position.Y, 3);
    }

    [Fact]
    public void ARampIsAPitchThenAStraight()
    {
        Chain chain = new Chain(Origin).Pitch(20.0f, 15.0f).Line(6.0f);
        float rise = (15.0f * (1.0f - MathF.Cos(20.0f * MathF.PI / 180.0f))) + (6.0f * MathF.Sin(20.0f * MathF.PI / 180.0f));

        Assert.Equal(rise, chain.End.Position.Y, 3);
        Assert.Equal(MathF.Sin(20.0f * MathF.PI / 180.0f), chain.End.Forward.Y, 4);
    }

    [Fact]
    public void ALoopEndsLevelFacingTheWayItStartedAndOffsetByItsOffset()
    {
        var loop = new LoopCentreline(Origin, 18.0f, 7.0f, 13.0f);
        Frame start = loop.FrameAt(0.0f);
        Frame end = loop.FrameAt(loop.Length);

        Assert.Equal(0.0f, end.Position.Y, 3);
        Near(start.Forward, end.Forward, 1e-4f, "forward");
        Near(Vector3.UnitY, end.Up, 1e-4f, "up");
        Assert.Equal(13.0f, end.Position.X, 3);
    }

    [Fact]
    public void ALoopIsUpsideDownAtTheTopAndTallerThanItIsWide()
    {
        var loop = new LoopCentreline(Origin, 18.0f, 7.0f, 13.0f);
        Frame top = loop.FrameAt(loop.Length / 2.0f);

        Assert.True(top.Up.Y < -0.999f, $"up at the top is {top.Up}");
        Assert.True(top.Position.Y > 2.0f * 7.0f, $"the top is only {top.Position.Y} m up");
        Assert.True(loop.RadiusAt(MathF.PI) < loop.RadiusAt(0.0f));
    }

    [Fact]
    public void ALoopsRoadIsAStraightLineWhenUnrolled()
    {
        // The property that lets a car with straight wheels follow it: the sideways shift grows evenly
        // with distance, so the direction of travel's sideways part never changes.
        var loop = new LoopCentreline(Origin, 18.0f, 7.0f, 13.0f);
        float sideways = loop.FrameAt(0.0f).Forward.X;
        for (int k = 1; k <= 20; k++)
        {
            Assert.Equal(sideways, loop.FrameAt(loop.Length * k / 20.0f).Forward.X, 4);
        }

        Assert.Equal(MathF.Atan(13.0f / loop.PlanarLength) * 180.0f / MathF.PI, loop.EntryHeadingDeg, 3);
    }

    [Fact]
    public void EachPieceOfAChainStartsWhereTheLastEnded()
    {
        Chain chain = new Chain(Origin).Line(20.0f).Pitch(8.0f, 50.0f).Line(35.0f).Pitch(-8.0f, 50.0f)
            .Turn(-90.0f, 30.0f, 20.0f, 5.0f).Line(10.0f);

        for (int i = 1; i < chain.Pieces.Count; i++)
        {
            ICentreline before = chain.Pieces[i - 1];
            Frame end = before.FrameAt(before.Length);
            Frame start = chain.Pieces[i].FrameAt(0.0f);
            Near(end.Position, start.Position, Millimetre, $"piece {i}");
            Near(end.Forward, start.Forward, 1e-4f, $"piece {i}");
        }
    }
}
