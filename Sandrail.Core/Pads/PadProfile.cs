using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandrail.Core.Pads;

/// <summary>One game control bound to one thing on a pad: a button, or an axis pushed one way.</summary>
/// <param name="Action">The game's action name, such as <c>throttle</c>.</param>
/// <param name="Kind"><c>axis</c> or <c>button</c>.</param>
/// <param name="Index">Godot's axis or button number, as the pad reports it.</param>
/// <param name="Direction">For an axis, −1 or 1: which way it's pushed. 0 for a button.</param>
public sealed record PadBinding(string Action, string Kind, int Index, int Direction)
{
    public const string Axis = "axis";
    public const string Button = "button";
}

/// <summary>
/// A pad's own layout, recorded by the binding run (<c>--bindpad</c>) and loaded at start-up in place of the
/// default pad bindings, for the pad with this GUID.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists: his pad is a generic clone</b> whose button numbers don't match their labels and whose
/// triggers are plain buttons (worklog, 2026-11-22). The defaults assume a real PlayStation or Xbox layout;
/// this records what the pad in hand actually sends, whatever it is.
/// </para>
/// <para>
/// Kept per machine, in <c>user://pads/</c>, not in the repository: a pad is the player's, not the game's.
/// Strict like the car spec: an unknown field is refused, and so is a binding that can't mean anything.
/// </para>
/// </remarks>
public sealed record PadProfile(string Guid, string Name, IReadOnlyList<PadBinding> Bindings)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    /// <summary>The layout's file name for a pad's GUID, refusing anything but hex so it can't name a path.</summary>
    /// <exception cref="ArgumentException">The GUID isn't 32 hex digits.</exception>
    public static string FileName(string guid)
    {
        if (guid.Length != 32 || !guid.All(Uri.IsHexDigit))
        {
            throw new ArgumentException($"\"{guid}\" isn't a pad GUID (32 hex digits).", nameof(guid));
        }

        return guid.ToLowerInvariant() + ".json";
    }

    public static PadProfile Parse(string json)
    {
        PadProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<PadProfile>(json, Options);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Not a pad layout: {e.Message}", e);
        }

        if (profile is null)
        {
            throw new InvalidDataException("Not a pad layout: the file is JSON null.");
        }

        IReadOnlyList<string> problems = profile.Problems();
        if (problems.Count > 0)
        {
            throw new InvalidDataException($"Pad layout for {profile.Name} refused: " + string.Join("; ", problems));
        }

        return profile;
    }

    public string ToJson()
    {
        IReadOnlyList<string> problems = Problems();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"Pad layout for {Name} not written: " + string.Join("; ", problems));
        }

        return JsonSerializer.Serialize(this, Options);
    }

    /// <summary>Everything wrong with the layout, in words. Empty means it can be loaded.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        try
        {
            FileName(Guid);
        }
        catch (ArgumentException e)
        {
            problems.Add(e.Message);
        }

        foreach (PadBinding b in Bindings)
        {
            if (b.Kind == PadBinding.Axis)
            {
                if (b.Direction is not (-1 or 1))
                {
                    problems.Add($"{b.Action}: an axis needs a direction of -1 or 1, not {b.Direction}");
                }

                if (b.Index is < 0 or > 9)
                {
                    problems.Add($"{b.Action}: axis {b.Index} doesn't exist");
                }
            }
            else if (b.Kind == PadBinding.Button)
            {
                if (b.Index is < 0 or > 127)
                {
                    problems.Add($"{b.Action}: button {b.Index} doesn't exist");
                }
            }
            else
            {
                problems.Add($"{b.Action}: \"{b.Kind}\" is neither axis nor button");
            }
        }

        foreach (IGrouping<string, PadBinding> twice in Bindings.GroupBy(b => b.Action).Where(g => g.Count() > 1))
        {
            problems.Add($"{twice.Key} is bound more than once");
        }

        // Loaded, a layout takes every default pad binding away; one with nothing in it leaves a dead pad.
        if (Bindings.Count == 0)
        {
            problems.Add("it binds nothing, which would leave the pad doing nothing at all");
        }

        return problems;
    }
}
