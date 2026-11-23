using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sandrail.Core.Generation;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;
using Sandrail.Game.Cars;
using Sandrail.Game.World;

namespace Sandrail.Game.Oracles;

/// <summary>
/// M3d's oracle: can a car that has left the road get back onto it? On a generated track (<c>--seed=</c>,
/// seed 1 unless told), from rest at the foot of its three tallest banks, full throttle at the road.
/// </summary>
/// <remarks>
/// His ask, 2026-11-23: "it would be nice if a player who has gone off the track can get back on again",
/// with the banks at 25° so they climb from a standstill. Each bank is found where the road's edge stands
/// furthest above the ground it was laid on, the three at least 60 m of road apart; the car starts 4 m
/// beyond the bank's foot facing the road and is on when its middle is over the road at the road's height.
/// </remarks>
public partial class Oracle
{
    public const string BankClimb = "bank";

    /// <summary>The generated ground, with its bed; null elsewhere.</summary>
    public Terrain? Ground { get; init; }

    public static bool NeedsSeed(string kind) => kind is BankClimb;

    private sealed record BankTrial(Vector3 Foot, Vector3 Facing, Vector3 Edge, float HeightM, string Where);

    private List<BankTrial>? _bankTrials;
    private int _bankTrial;
    private float _bankFrom;
    private float _bankBest;
    private float _bankBestAt;
    private readonly List<string> _bankFigures = new();

    private void RunBank()
    {
        if (_bankTrials is null)
        {
            _bankTrials = FindBanks(Course!, Ground!);
            if (_bankTrials.Count == 0)
            {
                Finish($"{Course!.Track.Id}: no bank taller than 0.5 m to climb");
                return;
            }

            StartBank();
            return;
        }

        BankTrial trial = _bankTrials[_bankTrial];
        float t = Time - _bankFrom;
        if (t < SettleSeconds)
        {
            Car.Driver = default;
            return;
        }

        // Straight at the road: steer against heading away from it.
        Vector3 forward = Forward;
        float heading = MathF.Atan2(forward.Cross(trial.Facing).Dot(Vector3.Up), forward.Dot(trial.Facing));
        Car.Driver = new DriverInput { Throttle = 1.0f, Steer = Math.Clamp(-heading * 2.0f, -1.0f, 1.0f) };

        Vector3 flat = new(Car.GlobalPosition.X - trial.Foot.X, 0.0f, Car.GlobalPosition.Z - trial.Foot.Z);
        float progress = flat.Dot(trial.Facing);
        if (progress > _bankBest + 0.05f)
        {
            _bankBest = progress;
            _bankBestAt = t;
        }

        float toEdge = new Vector3(trial.Edge.X - trial.Foot.X, 0.0f, trial.Edge.Z - trial.Foot.Z).Length();
        bool on = progress > toEdge + 2.0f && Car.GlobalPosition.Y > trial.Edge.Y;
        bool failed = t - _bankBestAt > 3.0f || t > SettleSeconds + 20.0f;
        if (!on && !failed)
        {
            return;
        }

        _bankFigures.Add(on
            ? $"{trial.HeightM:F1} m bank {trial.Where}: ON THE ROAD in {t - SettleSeconds:F1} s"
            : $"{trial.HeightM:F1} m bank {trial.Where}: STOPPED {toEdge + 2.0f - progress:F1} m short of the road");
        _bankTrial++;
        if (_bankTrial < _bankTrials.Count)
        {
            StartBank();
            return;
        }

        Finish($"{Course!.Track.Id}: from rest at the foot of its tallest banks ({Bed.BankDeg:F0}°, dirt), full throttle at the road: {string.Join("; ", _bankFigures)}");
    }

    private void StartBank()
    {
        BankTrial trial = _bankTrials![_bankTrial];
        float ground = Ground!.HeightAt(trial.Foot.X, trial.Foot.Z);
        var at = new Vector3(trial.Foot.X, ground + Buggy.RestHeight(Car.Spec) + 0.05f, trial.Foot.Z);
        Vector3 back = -trial.Facing;
        Car.Respawn(new Transform3D(new Basis(Vector3.Up.Cross(back).Normalized(), Vector3.Up, back), at));
        Car.Driver = default;
        _bankFrom = Time;
        _bankBest = float.MinValue / 2.0f;
        _bankBestAt = SettleSeconds;
    }

    /// <summary>The route's three tallest banks: where an edge of its road stands furthest above the ground it was laid on.</summary>
    private static List<BankTrial> FindBanks(LaidTrack track, Terrain bedded)
    {
        TrackGround ground = track.Track.Ground!;
        var original = new Terrain(ground.Seed, ground.Terrain);
        float tan = MathF.Tan(Bed.BankDeg * (MathF.PI / 180.0f));
        var candidates = new List<(float Height, float Along, BankTrial Trial)>();
        float along = 0.0f;
        foreach (PlacedPiece piece in track.Route)
        {
            float half = piece.Def.WidthM / 2.0f;
            foreach (ICentreline c in piece.Road().Pieces)
            {
                for (float s = 0.0f; s < c.Length; s += 2.0f)
                {
                    Frame f = c.FrameAt(s);
                    foreach (float side in new[] { -1.0f, 1.0f })
                    {
                        System.Numerics.Vector3 edge = f.Across(side * half, 0.0f);
                        float height = edge.Y - original.HeightAt(edge.X, edge.Z);
                        var outward = new Vector3(f.Right.X * side, 0.0f, f.Right.Z * side).Normalized();
                        var e = new Vector3(edge.X, edge.Y, edge.Z);
                        Vector3 foot = e + (outward * ((height / tan) + 4.0f));
                        string where = $"beside {piece.Def.Id[5..]} at {piece.Placement.At}";
                        candidates.Add((height, along + s, new BankTrial(foot, -outward, e, height, where)));
                    }
                }

                along += c.Length;
            }
        }

        var chosen = new List<(float Height, float Along, BankTrial Trial)>();
        foreach (var c in candidates.OrderByDescending(c => c.Height))
        {
            if (c.Height < 0.5f || chosen.Count == 3)
            {
                break;
            }

            // Its foot on open ground: not under another stretch of the same road.
            if (bedded.PaintedByBed(c.Trial.Foot.X, c.Trial.Foot.Z) && bedded.HeightAt(c.Trial.Foot.X, c.Trial.Foot.Z) > c.Trial.Edge.Y - 1.0f)
            {
                continue;
            }

            if (chosen.All(k => MathF.Abs(k.Along - c.Along) > 60.0f))
            {
                chosen.Add(c);
            }
        }

        return chosen.Select(c => c.Trial).ToList();
    }
}
