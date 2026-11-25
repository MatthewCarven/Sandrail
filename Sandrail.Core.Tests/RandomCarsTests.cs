using Sandrail.Core.Cars;

namespace Sandrail.Core.Tests;

public class RandomCarsTests
{
    private static readonly string[] All = [.. RandomCars.Groups];

    private static CarSpec Stock() =>
        CarSpec.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "cars", "buggy.json")));

    [Fact]
    public void TheSameSeedMakesTheSameCar()
    {
        CarSpec once = RandomCars.Make(Stock(), 48213, 0.25f, RandomCars.DefaultGroups);
        CarSpec again = RandomCars.Make(Stock(), 48213, 0.25f, RandomCars.DefaultGroups);

        Assert.Equal(once, again);
        Assert.Equal(once.Fingerprint(), again.Fingerprint());
        Assert.NotEqual(Stock(), once);
    }

    [Fact]
    public void KnownSeedsMakeKnownNumbers()
    {
        // Pinned so a change to the draw, or to Hash64, shows here rather than as his random-48213 quietly becoming
        // another car. Each number draws from its own name, so adding a number to the spec leaves these alone.
        CarSpec car = RandomCars.Make(new CarSpec(), 1, 1.0f, All);

        Assert.Equal(1432.0f, car.MassKg);
        Assert.Equal(1.128f, car.Grip);
        Assert.Equal(49_000.0f, car.PowerW);
    }

    [Fact]
    public void DifferentSeedsMakeDifferentCars()
    {
        var prints = Enumerable.Range(1, 200)
            .Select(s => RandomCars.Make(Stock(), (ulong)s, 0.25f, RandomCars.DefaultGroups).Fingerprint())
            .ToHashSet();

        Assert.Equal(200, prints.Count);
    }

    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.25f)]
    [InlineData(0.6f)]
    [InlineData(1.0f)]
    public void EveryNumberStaysOnItsSliderAndEveryCarCanBeDriven(float spread)
    {
        CarSpec stock = Stock();
        for (ulong seed = 1; seed <= 400; seed++)
        {
            CarSpec car = RandomCars.Make(stock, seed, spread, All);

            Assert.Empty(car.Problems());
            Assert.Equal(car, CarSpec.Parse(car.ToJson()));
            foreach (Tunable t in CarSpec.Tunables)
            {
                Assert.InRange(t.Get(car), t.Min, t.Max);
            }
        }
    }

    [Fact]
    public void ANumberLandsWithinTheSpreadOfItsOwnAndOnTheSlidersSteps()
    {
        // Without the steering, whose two locks are swapped if they cross, the one exception to the spread.
        CarSpec stock = Stock();
        for (ulong seed = 1; seed <= 200; seed++)
        {
            CarSpec car = RandomCars.Make(stock, seed, 0.1f, RandomCars.DefaultGroups);
            foreach (Tunable t in CarSpec.Tunables.Where(t => t.Group != RandomCars.SteeringGroup))
            {
                float was = t.Get(stock);
                float now = t.Get(car);
                Assert.True(MathF.Abs(now - was) <= (0.1f * (t.Max - t.Min)) + (t.Step / 2.0f) + 1e-4f,
                    $"seed {seed}: {t.Name} went from {was} to {now}, past 10% of {t.Min} to {t.Max}");
                double steps = (now - (double)t.Min) / t.Step;
                Assert.True(now == t.Max || Math.Abs(steps - Math.Round(steps)) < 1e-3,
                    $"seed {seed}: {t.Name} is {now}, off its slider's steps of {t.Step} from {t.Min}");
            }
        }
    }

    [Fact]
    public void OnlyTheTickedGroupsVary()
    {
        CarSpec stock = Stock();
        string[] tyres = ["Tyres"];
        for (ulong seed = 1; seed <= 100; seed++)
        {
            CarSpec car = RandomCars.Make(stock, seed, 1.0f, tyres);
            foreach (Tunable t in CarSpec.Tunables.Where(t => t.Group != "Tyres"))
            {
                Assert.Equal(t.Get(stock), t.Get(car));
            }

            Assert.Equal(stock.WingId, car.WingId);
            Assert.Equal(stock.Id, car.Id);
        }

        Assert.Contains(Enumerable.Range(1, 100), s => RandomCars.Make(stock, (ulong)s, 1.0f, tyres).Grip != stock.Grip);
    }

    [Fact]
    public void TheSteeringIsLeftAloneToStart()
    {
        Assert.DoesNotContain(RandomCars.SteeringGroup, RandomCars.DefaultGroups);
        Assert.Equal(RandomCars.Groups.Count - 1, RandomCars.DefaultGroups.Count);

        CarSpec stock = Stock();
        for (ulong seed = 1; seed <= 100; seed++)
        {
            CarSpec car = RandomCars.Make(stock, seed, 1.0f, RandomCars.DefaultGroups);
            Assert.Equal(stock.MaxSteerDeg, car.MaxSteerDeg);
            Assert.Equal(stock.MinSteerDeg, car.MinSteerDeg);
            Assert.Equal(stock.WideLockFactor, car.WideLockFactor);
        }
    }

    [Fact]
    public void WithNoSpreadTheCarIsTheOneItsMadeAround()
    {
        Assert.Equal(Stock(), RandomCars.Make(Stock(), 7, 0.0f, All));
    }

    [Fact]
    public void TheWingGoesWithItsGroupAndTheSpread()
    {
        CarSpec stock = Stock();
        var wings = Enumerable.Range(1, 400).Select(s => RandomCars.Make(stock, (ulong)s, 1.0f, All).WingId).ToList();

        // At 100% any wing: each of the four turns up about a quarter of the time.
        foreach (Wing wing in Wings.All)
        {
            Assert.InRange(wings.Count(w => w == wing.Id), 60, 140);
        }

        // Unticked, the wing fitted stays; at a small spread it nearly always does.
        string[] noWing = [.. RandomCars.Groups.Where(g => g != RandomCars.WingGroup)];
        Assert.All(Enumerable.Range(1, 100), s => Assert.Equal(stock.WingId, RandomCars.Make(stock, (ulong)s, 1.0f, noWing).WingId));
        int changed = Enumerable.Range(1, 400).Count(s => RandomCars.Make(stock, (ulong)s, 0.05f, All).WingId != stock.WingId);
        Assert.InRange(changed, 1, 40);
    }

    [Fact]
    public void ANumberPastItsSliderIsBroughtBack()
    {
        // F2's box can take a number past the slider's end, as far as validation allows.
        CarSpec heavy = Stock() with { MassKg = 2400.0f };
        string[] body = ["Body"];

        for (ulong seed = 1; seed <= 50; seed++)
        {
            Assert.InRange(RandomCars.Make(heavy, seed, 0.1f, body).MassKg, 1500.0f - 120.0f, 1500.0f);
        }
    }

    [Fact]
    public void ASpreadOrAGroupThatIsntOneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RandomCars.Make(Stock(), 1, 1.5f, All));
        Assert.Throws<ArgumentOutOfRangeException>(() => RandomCars.Make(Stock(), 1, float.NaN, All));
        Assert.Throws<ArgumentException>(() => RandomCars.Make(Stock(), 1, 0.5f, ["Paint"]));
    }

    [Fact]
    public void ARandomCarIsNamedByItsSeed()
    {
        Assert.Equal("random-48213", RandomCars.NameFor(48213));
    }
}
