using System.Collections.Generic;
using Godot;
using Sandrail.Core.Generation;

namespace Sandrail.Game.World;

/// <summary>
/// The rocks off a generated track's road (M3-PLAN, M3d: "rocks as grey boxes with colliders"), as Core
/// scattered them. Grey-box like the rest of the world until there's art; hard, so they drive as road.
/// </summary>
public static class RockView
{
    private static readonly Color[] Shades =
    [
        new(0.46f, 0.44f, 0.41f),
        new(0.52f, 0.50f, 0.46f),
        new(0.40f, 0.38f, 0.36f),
        new(0.57f, 0.54f, 0.49f),
    ];

    public static Node3D Build(IReadOnlyList<Rock> rocks)
    {
        var root = new Node3D { Name = "Rocks" };
        var materials = new StandardMaterial3D[Shades.Length];
        for (int i = 0; i < Shades.Length; i++)
        {
            materials[i] = new StandardMaterial3D { AlbedoColor = Shades[i], Roughness = 0.95f };
        }

        for (int k = 0; k < rocks.Count; k++)
        {
            Rock rock = rocks[k];
            var size = new Vector3(rock.Size.X, rock.Size.Y, rock.Size.Z);
            var body = new StaticBody3D
            {
                Name = $"Rock {k}",
                Transform = new Transform3D(
                    new Basis(Vector3.Up, Mathf.DegToRad(rock.YawDeg)),
                    new Vector3(rock.Position.X, rock.Position.Y + (rock.Size.Y / 2.0f), rock.Position.Z)),
            };
            body.SetMeta("surface", "base:road");
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            body.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = materials[k % materials.Length],
            });
            root.AddChild(body);
        }

        return root;
    }
}
