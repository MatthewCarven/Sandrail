using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Sandrail.Core.Cars;

namespace Sandrail.Game.Cars;

/// <summary>
/// The garage (PROFILE-PLAN, P2, decision 5): the stock car and every car in <c>user://tunes/</c>, by name. A car's
/// name is its file's (fact 7); the stock car is never written by the game, so a car tuned from it is saved as a
/// new one.
/// </summary>
/// <remarks>
/// <c>--tunes=</c> points it at another folder, which the checks use so they never touch his cars.
/// </remarks>
public static class Garage
{
    /// <summary>The stock car: what every player starts with, and what "differences from stock" are against.</summary>
    public const string StockPath = "res://content/base/cars/buggy.json";

    public const string StockName = "stock";

    /// <summary>Where his cars are kept: <c>user://tunes</c> unless <c>--tunes=</c> says otherwise.</summary>
    public static string TunesPath { get; set; } = "user://tunes";

    /// <summary>A car in the garage: its name, its file, and its numbers, or why its file couldn't be read.</summary>
    public sealed record Car(string Name, string Path, CarSpec? Spec, string? Problem);

    public static bool IsStock(string path) => path == StockPath;

    /// <summary>A car's name: "stock" for the stock car, else its file's name without <c>.json</c>.</summary>
    public static string NameOf(string path) => IsStock(path) ? StockName : System.IO.Path.GetFileNameWithoutExtension(path);

    /// <summary>The stock car first, then his, the newest first, as the Load list has saved tracks.</summary>
    public static IReadOnlyList<Car> All()
    {
        var cars = new List<Car> { Read(StockPath) };
        string[] files = DirAccess.DirExistsAbsolute(TunesPath) ? DirAccess.GetFilesAt(TunesPath) : [];
        cars.AddRange(files
            .Where(f => f.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            .Select(f => $"{TunesPath}/{f}")
            .OrderByDescending(FileAccess.GetModifiedTime)
            .ThenBy(p => p, System.StringComparer.Ordinal)
            .Select(Read));
        return cars;
    }

    /// <summary>A car's file read, or why it couldn't be.</summary>
    public static Car Read(string path)
    {
        try
        {
            return new Car(NameOf(path), path, CarSpec.Parse(FileAccess.GetFileAsString(path)), null);
        }
        catch (System.IO.InvalidDataException e)
        {
            return new Car(NameOf(path), path, null, e.Message);
        }
    }

    /// <summary>A typed name as a file name: lower case, letters and digits, a dash for the rest, as saved tracks are.</summary>
    public static string Slug(string name)
    {
        string slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "car" : slug;
    }

    /// <summary>Where a car of this name is kept.</summary>
    public static string PathFor(string name) => $"{TunesPath}/{Slug(name)}.json";

    /// <summary>A name no car has yet, from a start: <c>my-car</c>, then <c>my-car-2</c> and on.</summary>
    public static string FreeName(string start)
    {
        string stem = Slug(start);
        for (int n = 1; ; n++)
        {
            string name = n == 1 ? stem : $"{stem}-{n}";
            if (!FileAccess.FileExists(PathFor(name)))
            {
                return name;
            }
        }
    }

    /// <summary>Writes a car to its file; null if it was written, else why not. Refuses the stock car's file.</summary>
    public static string? Write(string path, CarSpec spec)
    {
        if (IsStock(path))
        {
            return "the stock car is never written: save yours as a new car";
        }

        DirAccess.MakeDirRecursiveAbsolute(path.GetBaseDir());
        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            return FileAccess.GetOpenError().ToString();
        }

        file.StoreString(spec.ToJson() + "\n");
        return null;
    }
}
