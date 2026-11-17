using System.Reflection;
using System.Text.Json;
using Tumbleweed.Core.Cars;

namespace Tumbleweed.Core.Tests;

public class CarSpecTests
{
    private static string ShippedBuggy() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "cars", "buggy.json"));

    [Fact]
    public void TheShippedBuggyLoads()
    {
        CarSpec spec = CarSpec.Parse(ShippedBuggy());

        Assert.Equal("base:buggy", spec.Id);
        Assert.Empty(spec.Problems());
    }

    [Fact]
    public void TheShippedBuggyListsEveryField()
    {
        // A field missing from the file silently takes its default, which is right for an old file
        // and wrong for the shipped one: the panel's Save writes every field, and a hand edit that
        // dropped one would make the file say less than the car does.
        using JsonDocument file = JsonDocument.Parse(ShippedBuggy());
        int fields = file.RootElement.EnumerateObject().Count();

        int properties = typeof(CarSpec).GetProperties(BindingFlags.Public | BindingFlags.Instance).Length;
        Assert.Equal(properties, fields);
    }

    [Fact]
    public void ARoundTripChangesNothing()
    {
        var spec = new CarSpec { MassKg = 712.5f, Grip = 0.95f, Id = "test:heavy" };

        Assert.Equal(spec, CarSpec.Parse(spec.ToJson()));
    }

    [Fact]
    public void AMisspeltFieldIsRefused()
    {
        // The worst playtest finding is "that change did nothing" when the change was a typo.
        var e = Assert.Throws<InvalidDataException>(() => CarSpec.Parse("""{ "massKgg": 700 }"""));
        Assert.Contains("massKgg", e.Message);
    }

    [Fact]
    public void AMissingFieldTakesItsDefault()
    {
        CarSpec spec = CarSpec.Parse("""{ "massKg": 700 }""");

        Assert.Equal(700.0f, spec.MassKg);
        Assert.Equal(new CarSpec().Grip, spec.Grip);
    }

    [Fact]
    public void NaNIsRefusedEvenThoughEveryComparisonWithItIsFalse()
    {
        var spec = new CarSpec { SpringNPerM = float.NaN };

        Assert.Contains(spec.Problems(), p => p.StartsWith(nameof(CarSpec.SpringNPerM), StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => spec.ToJson());
    }

    [Fact]
    public void OutOfRangeNumbersAreRefusedByName()
    {
        var spec = new CarSpec { MassKg = -1.0f, SuspensionTravelM = 0.0f, BrakeFrontShare = 1.5f };
        IReadOnlyList<string> problems = spec.Problems();

        Assert.Contains(problems, p => p.StartsWith(nameof(CarSpec.MassKg), StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith(nameof(CarSpec.SuspensionTravelM), StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith(nameof(CarSpec.BrakeFrontShare), StringComparison.Ordinal));
    }

    [Fact]
    public void SteeringThatWidensWithSpeedIsRefused()
    {
        Assert.NotEmpty(new CarSpec { MinSteerDeg = 35.0f, MaxSteerDeg = 30.0f }.Problems());
    }

    [Fact]
    public void AnUnnamespacedIdIsRefused()
    {
        Assert.NotEmpty(new CarSpec { Id = "buggy" }.Problems());
        Assert.NotEmpty(new CarSpec { Id = "base:" }.Problems());
    }

    [Fact]
    public void TheFingerprintFollowsTheNumbers()
    {
        var spec = new CarSpec();

        Assert.Equal(spec.Fingerprint(), new CarSpec().Fingerprint());
        Assert.NotEqual(spec.Fingerprint(), (spec with { Grip = 1.2f }).Fingerprint());
        Assert.Equal(8, spec.Fingerprint().Length);
    }

    [Fact]
    public void SurfacesAreFoundByFullOrBareName()
    {
        Assert.Same(Surfaces.Sand, Surfaces.Find("sand"));
        Assert.Same(Surfaces.Sand, Surfaces.Find("base:sand"));
        Assert.Null(Surfaces.Find("lava"));
    }
}
