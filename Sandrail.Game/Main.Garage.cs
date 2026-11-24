using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
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

    /// <summary>The car being driven in words, for the Esc menu: its name and fingerprint.</summary>
    private string CarWords() =>
        _car is null || _tuning is null ? string.Empty : $"Car: {Garage.NameOf(_tuning.SpecPath)}, fingerprint {CarPrint}";
}
