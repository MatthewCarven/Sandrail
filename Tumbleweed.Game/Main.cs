using System.Reflection;
using Godot;
using Tumbleweed.Core.Generation;

namespace Tumbleweed.Game;

/// <summary>
/// M0 entry point: a sunlit 640 m plain (one 32 × 32 map of 20 m tiles), a camera, and proof
/// that Core is linked.
/// </summary>
/// <remarks>
/// The plain has collision already, a world boundary, so M1's buggy has something to land on.
/// The sun and camera are aimed here rather than in the scene, because a hand-written
/// <c>Transform3D</c> is twelve numbers nobody can check by eye.
/// </remarks>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        string version = typeof(Main).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        GD.Print($"Tumbleweed {version}");

        // The published first output of splitmix64, which Hash64Tests pins. Printed from the Godot
        // side, it says this binary links the same Core the tests ran against.
        GD.Print($"Core linked: Hash64.Mix(0) = 0x{Hash64.Mix(0UL):X16} (expected 0xE220A8397B1DCDAF)");

        GetNode<DirectionalLight3D>("Sun").RotationDegrees = new Vector3(-50.0f, 30.0f, 0.0f);
        GetNode<Camera3D>("Camera").LookAtFromPosition(new Vector3(0.0f, 8.0f, 20.0f), Vector3.Zero, Vector3.Up);
    }
}
