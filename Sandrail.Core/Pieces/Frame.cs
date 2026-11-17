using System.Numerics;

namespace Sandrail.Core.Pieces;

/// <summary>
/// Where a point on a centreline is and which way it faces: the road's forward, its up (the way its
/// surface faces) and its right (the way across it).
/// </summary>
/// <remarks>
/// <para>
/// <b>Godot's axes</b>, so nothing is converted on the way across: Y is up and forward at heading 0
/// is -Z, which makes right +X. Right is <c>Forward × Up</c>.
/// </para>
/// <para>
/// <b>Right is carried rather than always derived</b>, because one kind of road needs it not to be
/// square to forward: a loop whose exit is beside its entry (see <see cref="LoopCentreline"/>). Its
/// cross-sections keep one fixed right all the way round, which is what lets a car with its wheels
/// straight follow it. Everywhere else, <see cref="Of"/> makes the three square.
/// </para>
/// </remarks>
public readonly record struct Frame(Vector3 Position, Vector3 Forward, Vector3 Up, Vector3 Right)
{
    /// <summary>A square frame: right is derived from forward and up.</summary>
    public static Frame Of(Vector3 position, Vector3 forward, Vector3 up)
    {
        Vector3 f = Vector3.Normalize(forward);
        Vector3 u = Vector3.Normalize(up);
        return new Frame(position, f, u, Vector3.Normalize(Vector3.Cross(f, u)));
    }

    /// <summary>A level frame facing a heading: 0 is -Z, and positive turns right, toward +X.</summary>
    public static Frame Level(Vector3 position, float headingDeg) => Pitched(position, headingDeg, 0.0f);

    /// <summary>A frame facing a heading and pitched nose-up by an angle (negative is nose-down).</summary>
    public static Frame Pitched(Vector3 position, float headingDeg, float pitchDeg)
    {
        float h = headingDeg * (MathF.PI / 180.0f);
        float p = pitchDeg * (MathF.PI / 180.0f);
        var forward = new Vector3(MathF.Sin(h) * MathF.Cos(p), MathF.Sin(p), -MathF.Cos(h) * MathF.Cos(p));
        var up = new Vector3(-MathF.Sin(h) * MathF.Sin(p), MathF.Cos(p), MathF.Cos(h) * MathF.Sin(p));
        return Of(position, forward, up);
    }

    /// <summary>A point given in this frame's right and up, as on a cross-section.</summary>
    public Vector3 Across(float right, float up) => Position + (Right * right) + (Up * up);
}
