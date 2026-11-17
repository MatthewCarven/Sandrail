using System.Reflection;

namespace Tumbleweed.Core.Cars;

/// <summary>Which group a number's slider sits in on the tuning panel, and where the slider ends.</summary>
/// <remarks>
/// Narrower than <see cref="CarSpec.Problems"/>: validation refuses what can't be driven, while this is
/// the range worth exploring by hand. A number typed into the panel's box can go past the slider's ends,
/// as far as validation allows.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TuneAttribute(string group, float min, float max) : Attribute
{
    public string Group { get; } = group;

    public float Min { get; } = min;

    public float Max { get; } = max;
}

/// <summary>One number in a <see cref="CarSpec"/> as the tuning panel and the recorder see it.</summary>
public sealed record Tunable(string Name, string Group, float Min, float Max, PropertyInfo Property)
{
    public float Get(CarSpec spec) => (float)Property.GetValue(spec)!;
}
