using System;
using System.Collections.Generic;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Game.Cars;

namespace Sandrail.Game.Oracles;

/// <summary>
/// M3a's oracle: how steep a slope the car climbs on each surface (M3-PLAN fact 3), so the terrain's
/// dials know what counts as a wall. The arithmetic said about 17° on sand; this measures it.
/// </summary>
/// <remarks>
/// A 40 m slope of one surface is laid at the foot of the bare plain, and the car pulls away from rest 3 m
/// short of it at full throttle, steering only to stay straight. It's made the top when its middle is 3 m
/// from the end; it hasn't when it stalls, slides back, or runs out of time. A search between 0° and 45°
/// halves the gap six times a surface, so each figure is within about 0.7°.
/// </remarks>
public partial class Oracle
{
    public const string Climb = "climb";

    private const float ClimbLengthM = 40.0f;
    private const float ClimbFootZ = -3.0f;
    private const int ClimbSteps = 6;

    private static readonly Surface[] ClimbSurfaces = [Surfaces.Road, Surfaces.Dirt, Surfaces.Grass, Surfaces.Sand];

    private int _climbSurface;
    private int _climbStep;
    private float _climbLow;
    private float _climbHigh;
    private float _climbAngle;
    private float _climbFrom;
    private float _climbBest;
    private float _climbBestAt;
    private StaticBody3D? _climbRamp;
    private readonly List<string> _climbFigures = new();

    private void RunClimb()
    {
        if (_climbRamp is null)
        {
            _home = Car.GlobalTransform;
            _climbLow = 0.0f;
            _climbHigh = 45.0f;
            StartClimb();
            return;
        }

        float t = Time - _climbFrom;
        if (t < SettleSeconds)
        {
            Car.Driver = default;
            return;
        }

        // Straight up the slope (north, -Z): steer against heading off it and drifting sideways.
        Vector3 forward = Forward;
        float heading = MathF.Atan2(forward.X, -forward.Z);
        float steer = Math.Clamp((-heading * 2.0f) - (Car.GlobalPosition.X * 0.1f), -1.0f, 1.0f);
        Car.Driver = new DriverInput { Throttle = 1.0f, Steer = steer };

        float angle = Mathf.DegToRad(_climbAngle);
        var up = new Vector3(0.0f, MathF.Sin(angle), -MathF.Cos(angle));
        float along = (Car.GlobalPosition - new Vector3(0.0f, 0.0f, ClimbFootZ)).Dot(up);
        if (along > _climbBest + 0.05f)
        {
            _climbBest = along;
            _climbBestAt = t;
        }

        bool made = along >= ClimbLengthM - 3.0f;
        bool stalled = t - _climbBestAt > 2.0f || along < _climbBest - 1.0f || t > SettleSeconds + 20.0f;
        if (!made && !stalled)
        {
            return;
        }

        if (made)
        {
            _climbLow = _climbAngle;
        }
        else
        {
            _climbHigh = _climbAngle;
        }

        _climbStep++;
        if (_climbStep < ClimbSteps)
        {
            StartClimb();
            return;
        }

        Surface surface = ClimbSurfaces[_climbSurface];
        _climbFigures.Add($"{surface.Id[5..]} {_climbLow:F1}°");
        _climbSurface++;
        if (_climbSurface < ClimbSurfaces.Length)
        {
            _climbStep = 0;
            _climbLow = 0.0f;
            _climbHigh = 45.0f;
            StartClimb();
            return;
        }

        _climbRamp.QueueFree();
        Finish($"from rest at the foot, the steepest 40 m slope it makes the top of: {string.Join(", ", _climbFigures)} (each within 0.7°)");
    }

    /// <summary>The next trial: the slope halfway between what it has climbed and what it hasn't, and the car back at its foot, at rest.</summary>
    private void StartClimb()
    {
        _climbAngle = (_climbLow + _climbHigh) / 2.0f;
        _climbRamp?.QueueFree();
        _climbRamp = Ramp(_climbAngle, ClimbSurfaces[_climbSurface]);
        GetParent().AddChild(_climbRamp);
        Car.Respawn(_home);
        Car.Driver = default;
        _climbFrom = Time;

        // It starts short of the foot, a negative distance up the slope: the furthest so far starts below that.
        _climbBest = float.MinValue / 2.0f;
        _climbBestAt = SettleSeconds;
    }

    /// <summary>
    /// A slab 12 m wide whose top rises northward from the plain at an angle, its foot exactly at the
    /// plain's surface (the rest of the slab is underground there), tagged with a surface.
    /// </summary>
    private static StaticBody3D Ramp(float angleDeg, Surface surface)
    {
        const float Thickness = 2.0f;
        float a = Mathf.DegToRad(angleDeg);
        var basis = new Basis(Vector3.Right, a);
        Vector3 foot = basis * new Vector3(0.0f, Thickness / 2.0f, ClimbLengthM / 2.0f);
        var body = new StaticBody3D
        {
            Name = $"slope {angleDeg:F1} on {surface.Id}",
            CollisionLayer = Buggy.WorldLayer,
            Transform = new Transform3D(basis, new Vector3(0.0f, -foot.Y, ClimbFootZ - foot.Z)),
        };
        body.SetMeta("surface", surface.Id);
        var size = new Vector3(12.0f, Thickness, ClimbLengthM);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = World.TerrainView.Colour(surface) },
        });
        return body;
    }
}
