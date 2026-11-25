using System.Text;
using Sandrail.Core.Generation;

namespace Sandrail.Core.Cars;

/// <summary>
/// Random cars (2026-11-26, his ask: a "generate random cars dialog"): cars made from seeds around another car, for the
/// garage, so the profile can try cars nobody would have set up by hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>From a seed, through <see cref="Hash64"/></b> (non-negotiable 2): the same seed, around the same car, with the same
/// spread and groups, makes the same car on every machine and every launch. The seed is in the car's name,
/// <c>random-48213</c>.
/// </para>
/// <para>
/// <b>Each number draws from its own name</b>, not its place in the list, so a number added to the spec later leaves
/// every other number's draw where it was, as a seed per stage does for the generator (non-negotiable 4).
/// </para>
/// <para>
/// <b>The spread</b> is a share of each number's F2 slider range, either side of the car it's made around (his pick):
/// at 25% a number lands within a quarter of its range of where it was, and at 100% anywhere between the slider's ends.
/// A number drawn goes on the slider's steps (<see cref="Tunable.OnStep"/>), so it reads as one F2 could have set, and
/// a number past its slider's end (typed into F2's box) is brought back within it.
/// </para>
/// <para>
/// <b>Only the groups ticked vary</b> (his pick), F2's groups; Steering and pedals is left out to start, since the
/// steering is his to open. <b>The wing goes with the Wing group</b>: the spread is also the chance of a wing drawn
/// afresh, any of the four (the one fitted among them), so at 100% it's any wing and at 5% nearly always the car's own.
/// </para>
/// <para>
/// <b>Not breeding</b> (PROFILE-PLAN): nothing here looks at how a car drove. He chooses the car, the spread and the
/// groups; the profile only measures.
/// </para>
/// </remarks>
public static class RandomCars
{
    /// <summary>The group a random car leaves alone unless it's ticked: the steering is his to open.</summary>
    public const string SteeringGroup = "Steering and pedals";

    /// <summary>The group the wing goes with: ticked, the wing fitted can change too.</summary>
    public const string WingGroup = "Wing";

    /// <summary>F2's groups, in its order: what can be ticked.</summary>
    public static IReadOnlyList<string> Groups { get; } = CarSpec.Tunables.Select(t => t.Group).Distinct().ToList();

    /// <summary>The groups ticked to start: all of them but the steering.</summary>
    public static IReadOnlyList<string> DefaultGroups { get; } = Groups.Where(g => g != SteeringGroup).ToList();

    /// <summary>A random car's name, its seed in it: <c>random-48213</c>.</summary>
    public static string NameFor(ulong seed) => $"random-{seed}";

    /// <summary>
    /// The car this seed makes around <paramref name="around"/>: each number in a ticked group drawn within
    /// <paramref name="spread"/> of its own (a share of its slider range, 0 to 1), the rest left as they are.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The spread isn't from 0 to 1.</exception>
    /// <exception cref="ArgumentException">A group that isn't one of F2's.</exception>
    public static CarSpec Make(CarSpec around, ulong seed, float spread, IReadOnlyCollection<string> groups)
    {
        if (!(spread >= 0.0f && spread <= 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(spread), spread, "The spread is a share of each slider's range, from 0 to 1.");
        }

        if (groups.FirstOrDefault(g => !Groups.Contains(g)) is string unknown)
        {
            throw new ArgumentException($"There's no group called {unknown}: it's one of {string.Join(", ", Groups)}.", nameof(groups));
        }

        CarSpec car = around;
        foreach (Tunable tunable in CarSpec.Tunables.Where(t => groups.Contains(t.Group)))
        {
            float was = tunable.Get(around);
            float reach = spread * (tunable.Max - tunable.Min);
            float lo = Math.Clamp(was - reach, tunable.Min, tunable.Max);
            float hi = Math.Clamp(was + reach, tunable.Min, tunable.Max);
            float value = lo == hi ? lo : tunable.OnStep(lo + (Draw(seed, tunable.Name) * (hi - lo)));
            car = car.WithValue(tunable.Name, value);
        }

        if (groups.Contains(WingGroup) && Draw(seed, nameof(CarSpec.WingId)) < spread)
        {
            int which = (int)(Draw(seed, nameof(CarSpec.WingId) + " which") * Wings.All.Count);
            car = car with { WingId = Wings.All[which].Id };
        }

        // The one rule between two numbers: the lock left at speed can't be more than the lock at a standstill. Swapped,
        // both stay on their sliders, since the least lock's slider tops out where the most lock's starts above it.
        if (car.MinSteerDeg > car.MaxSteerDeg)
        {
            car = car with { MinSteerDeg = car.MaxSteerDeg, MaxSteerDeg = car.MinSteerDeg };
        }

        return car;
    }

    /// <summary>A draw from 0 to 1 for one thing about the car this seed makes, keyed by its name.</summary>
    private static float Draw(ulong seed, string what)
    {
        ulong key = 0;
        foreach (byte b in Encoding.UTF8.GetBytes(what))
        {
            key = Hash64.Mix(key ^ b);
        }

        return Hash64.ToUnitFloat(Hash64.Mix(Hash64.Mix(seed) ^ key));
    }
}
