using System;
using System.Collections.Generic;
using Godot;
using Sandrail.Core.Cars;

namespace Sandrail.Game.Cars;

/// <summary>
/// The buggy's grey boxes (M4-PLAN fact 7): the body, the cockpit hoop, the orange nose, four wheels and the
/// wing, built from a spec and posed wheel by wheel. The <see cref="Buggy"/> wears one, posed by its physics;
/// a ghost or a replay car wears another, posed by a snapshot, with no physics at all.
/// </summary>
/// <remarks>
/// Moved out of <see cref="Buggy"/> unchanged (M4b): the same boxes, the same sizes and colours, the same
/// rules for rebuilding (the body when a dimension changes, the wing when anything it shows does, since the
/// angle slider moves it many times a second).
/// </remarks>
public partial class CarLooks : Node3D
{
    private readonly List<Node> _body = new();
    private readonly Node3D[] _pivots = new Node3D[4];
    private readonly Node3D[] _spinners = new Node3D[4];
    private readonly float[] _spin = new float[4];
    private CarSpec? _shown;
    private Node3D? _wingLook;
    private (string, float, float, float, float) _wingKey;
    private float _opacity = 1.0f;

    /// <summary>Where a wheel's mount is on the body: Godot's forward is -Z, so the front axle is at -Z.</summary>
    public static Vector3 Mount(CarSpec spec, int wheel)
    {
        float halfTrack = spec.TrackM / 2.0f;
        float halfBase = spec.WheelbaseM / 2.0f;
        return new Vector3(wheel % 2 == 0 ? -halfTrack : halfTrack, spec.MountHeightM, wheel < 2 ? -halfBase : halfBase);
    }

    /// <summary>Shows a spec: the body rebuilt if a dimension changed, the wing if anything it shows did.</summary>
    public void Show(CarSpec spec)
    {
        BuildWing(spec);
        if (_shown is not null && !Buggy.ShapeDiffers(_shown, spec))
        {
            _shown = spec;
            return;
        }

        _shown = spec;
        foreach (Node part in _body)
        {
            RemoveChild(part);
            part.QueueFree();
        }

        _body.Clear();
        BuildBody(spec, new Vector3(spec.ChassisWidthM, spec.ChassisHeightM, spec.ChassisLengthM));
        Fade(_opacity);
    }

    /// <summary>
    /// Poses one wheel: hanging <paramref name="droopM"/> below its mount, turned <paramref name="steerDeg"/>
    /// (positive right), and spun on by its rolling speed over <paramref name="seconds"/>.
    /// </summary>
    public void Wheel(int wheel, float droopM, float steerDeg, float rollingMps, float seconds)
    {
        CarSpec spec = _shown!;
        _pivots[wheel].Position = Mount(spec, wheel) + (Vector3.Down * droopM);
        _pivots[wheel].Rotation = new Vector3(0.0f, -Mathf.DegToRad(steerDeg), 0.0f);
        _spin[wheel] = Mathf.Wrap(_spin[wheel] - (rollingMps / spec.WheelRadiusM * seconds), -Mathf.Pi, Mathf.Pi);
        _spinners[wheel].Rotation = new Vector3(_spin[wheel], 0.0f, 0.0f);
    }

    /// <summary>How solid it looks: 1 is the car, less is a ghost, see-through and unshaded by its own shadow.</summary>
    public void Fade(float opacity)
    {
        _opacity = opacity;
        foreach (Node part in GetChildren())
        {
            Faded(part, opacity);
        }
    }

    /// <summary>A part and everything under it at an opacity. Each car's materials are its own, so a ghost's fading leaves the car alone.</summary>
    private static void Faded(Node node, float opacity)
    {
        if (node is MeshInstance3D mesh)
        {
            mesh.CastShadow = opacity >= 1.0f ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
            if (mesh.MaterialOverride is StandardMaterial3D m)
            {
                m.Transparency = opacity < 1.0f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
                m.AlbedoColor = m.AlbedoColor with { A = opacity };
            }
        }

        foreach (Node child in node.GetChildren())
        {
            Faded(child, opacity);
        }
    }

    private static StandardMaterial3D Material(Color colour, float roughness) => new() { AlbedoColor = colour, Roughness = roughness };

    private void BuildBody(CarSpec spec, Vector3 chassisSize)
    {
        StandardMaterial3D body = Material(new Color(0.55f, 0.56f, 0.58f), 0.6f);
        StandardMaterial3D dark = Material(new Color(0.12f, 0.12f, 0.13f), 0.9f);
        StandardMaterial3D nose = Material(new Color(0.95f, 0.45f, 0.1f), 0.5f);

        Part(new MeshInstance3D { Mesh = new BoxMesh { Size = chassisSize }, MaterialOverride = body });

        // A cockpit hoop, and an orange nose so the front is never in doubt.
        Part(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(chassisSize.X * 0.8f, 0.7f, 0.15f) },
            MaterialOverride = dark,
            Position = new Vector3(0.0f, (chassisSize.Y / 2.0f) + 0.35f, chassisSize.Z * 0.1f),
        });
        Part(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(chassisSize.X * 0.6f, 0.2f, 0.4f) },
            MaterialOverride = nose,
            Position = new Vector3(0.0f, chassisSize.Y * 0.2f, -chassisSize.Z / 2.0f),
        });

        var wheel = new CylinderMesh
        {
            TopRadius = spec.WheelRadiusM,
            BottomRadius = spec.WheelRadiusM,
            Height = 0.3f,
            RadialSegments = 20,
        };

        for (int i = 0; i < 4; i++)
        {
            var pivot = new Node3D { Position = Mount(spec, i) + (Vector3.Down * spec.SuspensionTravelM) };
            var spinner = new Node3D();

            // The cylinder's axis is Y; lying it on its side puts the axle across the car.
            spinner.AddChild(new MeshInstance3D
            {
                Mesh = wheel,
                MaterialOverride = dark,
                Rotation = new Vector3(0.0f, 0.0f, Mathf.Pi / 2.0f),
            });
            pivot.AddChild(spinner);
            Part(pivot);
            _pivots[i] = pivot;
            _spinners[i] = spinner;
        }
    }

    /// <summary>
    /// The wing's grey box: a plate as wide as three times its depth, tilted trailing edge up by its
    /// angle, on two struts from the chassis's top, at the point its forces act. The twin-plane wing has
    /// a second, smaller plate behind and above the first.
    /// </summary>
    private void BuildWing(CarSpec spec)
    {
        var key = (spec.WingId, spec.WingAngleDeg, spec.WingBackM, spec.WingHeightM, spec.ChassisHeightM);
        if (_wingLook is not null && _wingKey == key)
        {
            return;
        }

        _wingKey = key;
        if (_wingLook is not null)
        {
            RemoveChild(_wingLook);
            _wingLook.QueueFree();
            _wingLook = null;
        }

        Wing wing = spec.FittedWing();
        _wingLook = new Node3D { Position = new Vector3(0.0f, spec.WingHeightM, spec.WingBackM) };
        AddChild(_wingLook);
        if (wing.AreaM2 <= 0.0f)
        {
            return;
        }

        StandardMaterial3D plate = Material(new Color(0.9f, 0.9f, 0.86f), 0.5f);
        StandardMaterial3D dark = Material(new Color(0.12f, 0.12f, 0.13f), 0.9f);
        bool twin = wing == Wings.TwinPlane;
        float span = MathF.Sqrt(3.0f * wing.AreaM2);
        float mainChord = wing.AreaM2 / span * (twin ? 0.6f : 1.0f);
        float tilt = -Mathf.DegToRad(spec.WingAngleDeg);

        var main = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(span, 0.04f, mainChord) },
            MaterialOverride = plate,
            Rotation = new Vector3(tilt, 0.0f, 0.0f),
        };
        _wingLook.AddChild(main);

        if (twin)
        {
            float flapChord = wing.AreaM2 / span * 0.4f;
            _wingLook.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(span, 0.04f, flapChord) },
                MaterialOverride = plate,
                Position = new Vector3(0.0f, 0.12f + (MathF.Sin(-tilt) * mainChord / 2.0f), (mainChord / 2.0f) + (flapChord / 2.0f) - 0.05f),
                Rotation = new Vector3(tilt * 1.6f, 0.0f, 0.0f),
            });
        }

        // End plates, which make it read as a wing from any angle.
        if (wing != Wings.Lip)
        {
            float plateDepth = twin ? mainChord * 1.9f : mainChord * 1.2f;
            foreach (float side in new[] { -1.0f, 1.0f })
            {
                _wingLook.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.3f, plateDepth) },
                    MaterialOverride = dark,
                    Position = new Vector3(side * span / 2.0f, 0.05f, twin ? mainChord * 0.3f : 0.0f),
                });
            }
        }

        // Struts down to the chassis's top, if the wing stands above it.
        float drop = spec.WingHeightM - (spec.ChassisHeightM / 2.0f);
        if (drop > 0.02f)
        {
            foreach (float side in new[] { -1.0f, 1.0f })
            {
                _wingLook.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.05f, drop, 0.12f) },
                    MaterialOverride = dark,
                    Position = new Vector3(side * span * 0.3f, -drop / 2.0f, 0.0f),
                });
            }
        }

        Fade(_opacity);
    }

    /// <summary>Adds a body part, remembered so a reshape can take it away again.</summary>
    private void Part(Node3D node)
    {
        AddChild(node);
        _body.Add(node);
    }
}
