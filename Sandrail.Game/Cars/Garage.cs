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
/// <para>
/// <c>--tunes=</c> points it at another folder, which the checks use so they never touch his cars.
/// </para>
/// <para>
/// <b>A car set inactive</b> is moved into <c>inactive/</c> inside the garage, its file unchanged: still listed and still
/// driven when picked, but Profile every car leaves it out (2026-11-25, his ask: "do not use for further testing"). A
/// car's name is unique across both folders, so it can always go back.
/// </para>
/// </remarks>
public static class Garage
{
    /// <summary>The stock car: what every player starts with, and what "differences from stock" are against.</summary>
    public const string StockPath = "res://content/base/cars/buggy.json";

    public const string StockName = "stock";

    /// <summary>Where his cars are kept: <c>user://tunes</c> unless <c>--tunes=</c> says otherwise.</summary>
    public static string TunesPath { get; set; } = "user://tunes";

    /// <summary>Where cars set inactive are kept: a folder inside the garage, which Profile every car doesn't drive.</summary>
    public static string InactivePath => $"{TunesPath}/inactive";

    /// <summary>A car in the garage: its name, its file, its numbers or why its file couldn't be read, and whether it's set inactive.</summary>
    public sealed record Car(string Name, string Path, CarSpec? Spec, string? Problem, bool Inactive);

    public static bool IsStock(string path) => path == StockPath;

    /// <summary>Whether a car's file is among the inactive ones.</summary>
    public static bool IsInactive(string path) => path.GetBaseDir() == InactivePath;

    /// <summary>A car's name: "stock" for the stock car, else its file's name without <c>.json</c>.</summary>
    public static string NameOf(string path) => IsStock(path) ? StockName : System.IO.Path.GetFileNameWithoutExtension(path);

    /// <summary>The stock car first, then his, the newest first, as the Load list has saved tracks, then the inactive ones the same way.</summary>
    public static IReadOnlyList<Car> All()
    {
        var cars = new List<Car> { Read(StockPath) };
        cars.AddRange(CarsIn(TunesPath));
        cars.AddRange(CarsIn(InactivePath));
        return cars;
    }

    /// <summary>The cars whose files are in a folder (not its folders inside), the newest first.</summary>
    private static IEnumerable<Car> CarsIn(string folder)
    {
        string[] files = DirAccess.DirExistsAbsolute(folder) ? DirAccess.GetFilesAt(folder) : [];
        return files
            .Where(f => f.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            .Select(f => $"{folder}/{f}")
            .OrderByDescending(FileAccess.GetModifiedTime)
            .ThenBy(p => p, System.StringComparer.Ordinal)
            .Select(Read);
    }

    /// <summary>A car's file read, or why it couldn't be.</summary>
    public static Car Read(string path)
    {
        try
        {
            return new Car(NameOf(path), path, CarSpec.Parse(FileAccess.GetFileAsString(path)), null, IsInactive(path));
        }
        catch (System.IO.InvalidDataException e)
        {
            return new Car(NameOf(path), path, null, e.Message, IsInactive(path));
        }
    }

    /// <summary>
    /// Sets a car inactive, moving its file into <see cref="InactivePath"/>, or active again, moving it back: where its
    /// file is now, or why it couldn't be moved. The file itself is never changed. Refuses the stock car, which is what
    /// every other car is measured against.
    /// </summary>
    public static (string? Path, string? Problem) SetInactive(string path, bool inactive)
    {
        if (IsStock(path))
        {
            return (null, "the stock car stays in: it's what the others are measured against");
        }

        if (IsInactive(path) == inactive)
        {
            return (path, null);
        }

        if (!FileAccess.FileExists(path))
        {
            return (null, "its file isn't there any more");
        }

        string folder = inactive ? InactivePath : TunesPath;
        string to = $"{folder}/{path.GetFile()}";
        if (FileAccess.FileExists(to))
        {
            return (null, $"there's a car called {NameOf(path)} there already");
        }

        DirAccess.MakeDirRecursiveAbsolute(folder);
        Error moved = DirAccess.RenameAbsolute(path, to);
        return moved == Error.Ok ? (to, null) : (null, moved.ToString());
    }

    /// <summary>A typed name as a file name: lower case, letters and digits, a dash for the rest, as saved tracks are.</summary>
    public static string Slug(string name)
    {
        string slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "car" : slug;
    }

    /// <summary>Where a car of this name is kept.</summary>
    public static string PathFor(string name) => $"{TunesPath}/{Slug(name)}.json";

    /// <summary>Whether a car of this name is in the garage, active or not.</summary>
    public static bool Taken(string name) =>
        FileAccess.FileExists(PathFor(name)) || FileAccess.FileExists($"{InactivePath}/{Slug(name)}.json");

    /// <summary>A name no car has yet, active or not, from a start: <c>my-car</c>, then <c>my-car-2</c> and on.</summary>
    public static string FreeName(string start)
    {
        string stem = Slug(start);
        for (int n = 1; ; n++)
        {
            string name = n == 1 ? stem : $"{stem}-{n}";
            if (!Taken(name))
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
