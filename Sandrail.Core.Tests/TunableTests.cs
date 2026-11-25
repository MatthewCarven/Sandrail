using System.Reflection;
using Sandrail.Core.Cars;

namespace Sandrail.Core.Tests;

public class TunableTests
{
    [Fact]
    public void EveryNumberHasATuneRange()
    {
        // A number without one still gets a slider, but a guessed range; this is the nag to give it a real one.
        var missing = typeof(CarSpec).GetProperties()
            .Where(p => p.PropertyType == typeof(float) && p.GetCustomAttribute<TuneAttribute>() is null)
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryNumberIsTunableAndNothingElseIs()
    {
        int numbers = typeof(CarSpec).GetProperties().Count(p => p.PropertyType == typeof(float));

        Assert.Equal(numbers, CarSpec.Tunables.Count);
        Assert.DoesNotContain(CarSpec.Tunables, t => t.Name == nameof(CarSpec.Id));
    }

    [Fact]
    public void TheShippedNumbersSitInsideTheirSliders()
    {
        var defaults = new CarSpec();
        foreach (Tunable t in CarSpec.Tunables)
        {
            Assert.True(t.Min < t.Max, $"{t.Name}: {t.Min} is not below {t.Max}");
            Assert.InRange(t.Get(defaults), t.Min, t.Max);
        }
    }

    [Fact]
    public void TheStepIsTheSlidersAndANumberGoesOnIt()
    {
        Tunable mass = CarSpec.Tunables.Single(t => t.Name == nameof(CarSpec.MassKg));
        Tunable grip = CarSpec.Tunables.Single(t => t.Name == nameof(CarSpec.Grip));

        // 1,200 kg over 400 steps is 3, rounded down to 2; 1.7 over 400 is 0.00425, rounded down to 0.002.
        Assert.Equal(2.0f, mass.Step);
        Assert.Equal(0.002f, grip.Step);
        Assert.Equal(812.0f, mass.OnStep(812.9f));
        Assert.Equal(1.234f, grip.OnStep(1.2341f));
        Assert.Equal("1.234", grip.OnStep(1.2341f).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1500.0f, mass.OnStep(9000.0f));
        Assert.Equal(0.3f, grip.OnStep(-1.0f));
    }

    [Fact]
    public void TunablesComeInTheOrderTheyreDeclared()
    {
        Assert.Equal(nameof(CarSpec.MassKg), CarSpec.Tunables[0].Name);
        Assert.Equal(nameof(CarSpec.PedalRatePerS), CarSpec.Tunables[^1].Name);
    }

    [Fact]
    public void WithValueChangesOneNumberAndLeavesTheOriginalAlone()
    {
        var spec = new CarSpec();
        CarSpec changed = spec.WithValue(nameof(CarSpec.Grip), 0.9f);

        Assert.Equal(0.9f, changed.Grip);
        Assert.Equal(new CarSpec().Grip, spec.Grip);
        Assert.Equal(spec with { Grip = 0.9f }, changed);
    }

    [Fact]
    public void WithValueRefusesANameThatIsntANumber()
    {
        Assert.Throws<ArgumentException>(() => new CarSpec().WithValue("Gripp", 1.0f));
        Assert.Throws<ArgumentException>(() => new CarSpec().WithValue(nameof(CarSpec.Id), 1.0f));
    }

    [Fact]
    public void WithValueDoesNotValidate()
    {
        // The panel shows the problem and refuses to apply it; the copy itself has to exist to be asked.
        CarSpec broken = new CarSpec().WithValue(nameof(CarSpec.MassKg), -5.0f);

        Assert.NotEmpty(broken.Problems());
    }
}
