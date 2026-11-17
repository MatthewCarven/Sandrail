namespace Sandrail.Core.Cars;

/// <summary>What a wheel is standing on: how much grip it lends and how much it drags.</summary>
/// <param name="Id">Namespaced, like every content ID (<c>base:road</c>).</param>
/// <param name="Grip">Multiplies the car's own grip. Road is 1.</param>
/// <param name="RollingResistance">
/// Rolling force as a fraction of the wheel's load. This, not grip, is what makes sand slow: at 0.08
/// it costs a buggy about 500 N, a quarter of its drag at top speed.
/// </param>
public sealed record Surface(string Id, float Grip, float RollingResistance);

/// <summary>
/// The surfaces that exist, in code until there is a second set of them to express, the same rule
/// Overburden's strata table followed.
/// </summary>
public static class Surfaces
{
    public static readonly Surface Road = new("base:road", 1.0f, 0.015f);

    public static readonly Surface Dirt = new("base:dirt", 0.75f, 0.03f);

    public static readonly Surface Grass = new("base:grass", 0.65f, 0.04f);

    public static readonly Surface Sand = new("base:sand", 0.6f, 0.08f);

    public static IReadOnlyList<Surface> All { get; } = [Road, Dirt, Grass, Sand];

    /// <summary>The surface with this ID, or null. A bare name (<c>sand</c>) is read as <c>base:</c>.</summary>
    public static Surface? Find(string id)
    {
        string full = id.Contains(':', StringComparison.Ordinal) ? id : "base:" + id;
        return All.FirstOrDefault(s => s.Id == full);
    }
}
