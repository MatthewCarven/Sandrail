using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Game.Cars;
using Sandrail.Game.Ui;

namespace Sandrail.Game;

/// <summary>
/// The garage in the game (PROFILE-PLAN, P2): the Esc menu's Change car list. Picking a car starts the scene
/// again with it, on the same track, so nothing built from the old car is left over: the racing line and the
/// autodriver's plan are the new car's, and F2's Save writes the new car's file.
/// </summary>
/// <remarks>
/// Each of his cars has a lock beside it, which unlocks Set inactive (or Set active): an inactive car stays in the
/// list and can be driven, but Profile every car leaves it out (2026-11-25, at his ask, in place of deleting).
/// </remarks>
public partial class Main
{
    /// <summary>Every car in the garage for the Esc menu, the one being driven marked; a car whose file is refused says why and can't be picked.</summary>
    private List<SeedMenu.CarRow> CarsToDrive(string[] args) =>
        Garage.All().Select(car =>
        {
            bool driving = car.Path == _tuning?.SpecPath;
            string inactive = car.Inactive ? ", inactive" : string.Empty;
            Func<string>? toggle = Garage.IsStock(car.Path) ? null : () => SetCarInactive(car.Path, !car.Inactive);
            if (car.Spec is null)
            {
                return new SeedMenu.CarRow($"{car.Name}{inactive}: can't be read ({car.Problem})", null, car.Inactive, toggle);
            }

            string label = $"{car.Name}   {car.Spec.Fingerprint()}{inactive}{(driving ? ", driving now" : string.Empty)}";
            return new SeedMenu.CarRow(label, driving ? null : () => StartAgainWithCar(args, car.Path), car.Inactive, toggle);
        }).ToList();

    /// <summary>
    /// Sets a car inactive or active again, in words for the menu. The car being driven can be set either way: F2's Save
    /// follows its file to where it's been moved, and so does the next start of the scene.
    /// </summary>
    private string SetCarInactive(string path, bool inactive)
    {
        string name = Garage.NameOf(path);
        (string? now, string? problem) = Garage.SetInactive(path, inactive);
        if (now is null)
        {
            return $"Couldn't set {name} {(inactive ? "inactive" : "active")}: {problem}";
        }

        if (_tuning is not null && _tuning.SpecPath == path)
        {
            _tuning.SpecPath = now;
        }

        GD.Print($"{name} set {(inactive ? "inactive" : "active")}: {ProjectSettings.GlobalizePath(path)} moved to {ProjectSettings.GlobalizePath(now)}");
        return inactive
            ? $"{name} is inactive: Profile every car leaves it out. It's still here to drive, and its file is in {ProjectSettings.GlobalizePath(Garage.InactivePath)}."
            : $"{name} is active again: Profile every car drives it.";
    }

    /// <summary>The scene again with another car, everything else as it was: the same track, from its start.</summary>
    private void StartAgainWithCar(string[] args, string path)
    {
        NextArgs = args.Where(a => !a.StartsWith("--spec=", StringComparison.Ordinal) && a != "--autodrive")
            .Concat(Garage.IsStock(path) ? [] : [$"--spec={path}"])
            .ToArray();
        GD.Print($"Starting again with {Garage.NameOf(path)}: {string.Join(' ', NextArgs)}");
        GetTree().ReloadCurrentScene();
    }

    /// <summary>The car random cars are made around, in words, for the Esc menu: the one being driven, as it's set now.</summary>
    private string RandomAroundWords() =>
        _car is null || _tuning is null ? "the car being driven" : $"{CarName} ({_car.Spec.Fingerprint()})";

    /// <summary>
    /// Random cars (2026-11-26, his ask): cars from consecutive seeds around the car being driven, as F2 has it now, into
    /// the garage as <c>random-&lt;seed&gt;</c>, each noted in <see cref="Garage.RandomListPath"/> with what made it; what
    /// was made, in words, for the menu. A seed whose car is there already, the same car, isn't made twice; a name taken
    /// by another car gets a number after it, as F2's Save as a new car does.
    /// </summary>
    private string MakeRandomCars(SeedMenu.RandomCarsAsk ask)
    {
        if (_car is null || _tuning is null)
        {
            return "There's no car being driven to make cars around.";
        }

        CarSpec around = _car.Spec;
        string aroundName = CarName;
        string aroundPrint = around.Fingerprint();
        string groups = string.Join("; ", ask.Groups);
        string spread = (ask.Spread * 100.0f).ToString("0", CultureInfo.InvariantCulture);
        var made = new List<string>();
        var there = new List<string>();
        for (int i = 0; i < ask.Count; i++)
        {
            ulong seed = ask.FirstSeed + (ulong)i;
            CarSpec car = RandomCars.Make(around, seed, ask.Spread, ask.Groups);
            string name = RandomCars.NameFor(seed);
            if (Garage.Find(name) is string had)
            {
                if (Garage.Read(had).Spec == car)
                {
                    there.Add(name);
                    continue;
                }

                name = Garage.FreeName(name);
            }

            string path = Garage.PathFor(name);
            if (Garage.Write(path, car) is string why)
            {
                return $"Couldn't write {name}: {why}.{(made.Count > 0 ? $" Made before it: {string.Join(", ", made)}." : string.Empty)}";
            }

            string print = car.Fingerprint();
            string when = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            if (Garage.NoteRandom($"{when},{name},{print},{seed},{aroundName},{aroundPrint},{spread},{groups}") is string unnoted)
            {
                GD.PrintErr($"Couldn't note {name} in {ProjectSettings.GlobalizePath(Garage.RandomListPath)}: {unnoted}");
            }

            GD.Print($"Made {name} ({print}), seed {seed} around {aroundName} ({aroundPrint}) at {spread}% over {groups}: {ProjectSettings.GlobalizePath(path)}");
            made.Add(name);
        }

        // Kept short, so the form stays in sight above it: the fingerprints are in Change car and the log.
        string thereWords = there.Count == 0 ? string.Empty
            : $" {Names(there)} {(there.Count == 1 ? "was" : "were")} there already, the same car{(there.Count == 1 ? string.Empty : "s")}.";
        if (made.Count == 0)
        {
            return $"Nothing new made:{thereWords}";
        }

        return $"Made {made.Count} car{(made.Count == 1 ? string.Empty : "s")} around {aroundName} at {spread}%: {Names(made)}.{thereWords} "
            + "Change car lists them; Profile every car drives them.";

        // Three or more in a run of seeds as a range, random-48213 to random-48217; anything else name by name.
        string Names(List<string> names)
        {
            int at = Enumerable.Range(0, ask.Count).FirstOrDefault(i => RandomCars.NameFor(ask.FirstSeed + (ulong)i) == names[0]);
            bool run = names.Count > 2 && names.Select((n, i) => n == RandomCars.NameFor(ask.FirstSeed + (ulong)(at + i))).All(b => b);
            return run ? $"{names[0]} to {names[^1]}" : string.Join(", ", names);
        }
    }

    /// <summary>The car being driven in words, for the Esc menu: its name and fingerprint.</summary>
    private string CarWords() =>
        _car is null || _tuning is null ? string.Empty : $"Car: {Garage.NameOf(_tuning.SpecPath)}, fingerprint {CarPrint}";
}
