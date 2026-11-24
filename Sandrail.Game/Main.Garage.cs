using System;
using System.Collections.Generic;
using System.Linq;
using Sandrail.Game.Cars;

namespace Sandrail.Game;

/// <summary>
/// The garage in the game (PROFILE-PLAN, P2): the Esc menu's Change car list. Picking a car starts the scene
/// again with it, on the same track, so nothing built from the old car is left over: the racing line and the
/// autodriver's plan are the new car's, and F2's Save writes the new car's file.
/// </summary>
public partial class Main
{
    /// <summary>Every car in the garage for the Esc menu, the one being driven marked; a car whose file is refused says why and can't be picked.</summary>
    private List<(string Label, Action? Pick)> CarsToDrive(string[] args) =>
        Garage.All().Select(car =>
        {
            bool driving = car.Path == _tuning?.SpecPath;
            if (car.Spec is null)
            {
                return ($"{car.Name}: can't be read ({car.Problem})", (Action?)null);
            }

            string label = $"{car.Name}   {car.Spec.Fingerprint()}{(driving ? ", driving now" : string.Empty)}";
            return (label, driving ? null : () => StartAgainWithCar(args, car.Path));
        }).ToList();

    /// <summary>The scene again with another car, everything else as it was: the same track, from its start.</summary>
    private void StartAgainWithCar(string[] args, string path)
    {
        NextArgs = args.Where(a => !a.StartsWith("--spec=", StringComparison.Ordinal) && a != "--autodrive")
            .Concat(Garage.IsStock(path) ? [] : [$"--spec={path}"])
            .ToArray();
        Godot.GD.Print($"Starting again with {Garage.NameOf(path)}: {string.Join(' ', NextArgs)}");
        GetTree().ReloadCurrentScene();
    }

    /// <summary>The car being driven in words, for the Esc menu: its name and fingerprint.</summary>
    private string CarWords() =>
        _car is null || _tuning is null ? string.Empty : $"Car: {Garage.NameOf(_tuning.SpecPath)}, fingerprint {CarPrint}";
}
