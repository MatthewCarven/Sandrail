using Godot;
using Sandrail.Core.Driving;
using Sandrail.Core.Profiles;
using Sandrail.Core.Tracks;
using Sandrail.Game.World;

namespace Sandrail.Game.Cars;

/// <summary>
/// The car read for the profile's meter (PROFILE-PLAN, P1): the one rule for what's off the road and which slides
/// are read, so a profile, a run of his and the <c>track</c> oracle all write their figures the same way.
/// </summary>
public static class MeterRead
{
    /// <summary>This tick of the car on the route.</summary>
    /// <param name="car">The car.</param>
    /// <param name="line">The route's racing line.</param>
    /// <param name="index">The line's sample nearest the car.</param>
    /// <param name="here">The route piece the car is on.</param>
    /// <param name="piece">That piece's place in the route.</param>
    /// <param name="lap">Laps done: 0 on a sprint.</param>
    /// <param name="along">How far along the route the car is, laps included.</param>
    public static MeterTick Of(Buggy car, RacingLine line, int index, PlacedPiece here, int piece, int lap, float along)
    {
        bool airborne = car.GroundedCount == 0;
        float speed = car.LinearVelocity.Length();

        // A slide is read in the horizontal plane, so not in the loop, where the car points at the sky.
        float slide = speed > 5.0f && !airborne && !line.StraightAt(index) ? CarReadout.Of(car).BodySlipDeg : float.NaN;

        // Off the road: the car's middle further from the road's middle than the road leaves its tyres, on the ground.
        float carHalf = (car.Spec.TrackM / 2.0f) + 0.15f;
        float roadHalf = (here.Def.WidthM / 2.0f) - 0.15f;
        bool off = !airborne && System.MathF.Abs(line.OffsetFromMiddle(index, car.GlobalPosition.ToCore())) > roadHalf - carHalf + 0.5f;
        return new MeterTick(piece, lap, speed, off, slide, along);
    }
}
