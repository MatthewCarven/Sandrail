using System;
using Godot;
using Sandrail.Core.Cars;

namespace Sandrail.Game.Cars;

/// <summary>
/// The car's motion in the numbers a driver talks about, read the same way by the F3 overlay and the
/// F4 recorder so the two never disagree.
/// </summary>
/// <param name="LateralG">Positive while accelerating to the car's right, as in a right-hand turn.</param>
/// <param name="LongitudinalG">Positive while speeding up.</param>
/// <param name="YawDps">Positive while turning left (counter-clockwise from above).</param>
/// <param name="BodySlipDeg">
/// The angle between where the car points and where it's going, positive when it's moving to the right
/// of its nose. In a right-hand corner with the tail out, it goes negative. Zero below 1 m/s, where it
/// means nothing.
/// </param>
/// <param name="RollDeg">Positive with the right side down.</param>
/// <param name="PitchDeg">Positive nose up.</param>
public readonly record struct CarReadout(
    float SpeedKmh,
    float ForwardMps,
    float LateralG,
    float LongitudinalG,
    float YawDps,
    float BodySlipDeg,
    float RollDeg,
    float PitchDeg)
{
    public static CarReadout Of(Buggy car)
    {
        Basis basis = car.GlobalBasis;
        Vector3 up = basis.Y.Normalized();
        Vector3 forward = -basis.Z.Normalized();
        Vector3 right = basis.X.Normalized();
        Vector3 velocity = car.LinearVelocity;
        float forwardSpeed = velocity.Dot(forward);

        // Against the absolute forward speed, so reversing reads as a small slip rather than 180°.
        float bodySlip = velocity.Length() > 1.0f
            ? Mathf.RadToDeg(MathF.Atan2(velocity.Dot(right), MathF.Abs(forwardSpeed)))
            : 0.0f;

        return new CarReadout(
            velocity.Length() * 3.6f,
            forwardSpeed,
            car.Acceleration.Dot(right) / WheelModel.Gravity,
            car.Acceleration.Dot(forward) / WheelModel.Gravity,
            Mathf.RadToDeg(car.AngularVelocity.Dot(up)),
            bodySlip,
            Mathf.RadToDeg(MathF.Asin(Math.Clamp(-right.Y, -1.0f, 1.0f))),
            Mathf.RadToDeg(MathF.Asin(Math.Clamp(forward.Y, -1.0f, 1.0f))));
    }
}
