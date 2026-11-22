using Sandrail.Core.Pads;

namespace Sandrail.Core.Tests;

public class PadProfileTests
{
    private const string Guid = "0300be79790000000600000000000000";

    private static PadProfile Sample() => new(Guid, "Microntek USB Joystick",
    [
        new PadBinding("steer_left", PadBinding.Axis, 0, -1),
        new PadBinding("steer_right", PadBinding.Axis, 0, 1),
        new PadBinding("throttle", PadBinding.Axis, 3, -1),
        new PadBinding("wide_lock", PadBinding.Button, 9, 0),
    ]);

    [Fact]
    public void ARoundTripChangesNothing()
    {
        PadProfile before = Sample();
        PadProfile after = PadProfile.Parse(before.ToJson());

        Assert.Equal(before.Guid, after.Guid);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Bindings, after.Bindings);
    }

    [Fact]
    public void TheFileNameIsTheGuidAndNothingElse()
    {
        Assert.Equal(Guid + ".json", PadProfile.FileName(Guid.ToUpperInvariant()));
        Assert.Throws<ArgumentException>(() => PadProfile.FileName("../../evil"));
        Assert.Throws<ArgumentException>(() => PadProfile.FileName("0300"));
    }

    [Fact]
    public void BindingsThatCantMeanAnythingAreRefused()
    {
        Assert.NotEmpty((Sample() with { Bindings = [new PadBinding("throttle", PadBinding.Axis, 3, 0)] }).Problems());
        Assert.NotEmpty((Sample() with { Bindings = [new PadBinding("throttle", "trigger", 3, 1)] }).Problems());
        Assert.NotEmpty((Sample() with { Bindings = [new PadBinding("throttle", PadBinding.Axis, 12, 1)] }).Problems());
        Assert.NotEmpty((Sample() with
        {
            Bindings = [new PadBinding("camera", PadBinding.Button, 1, 0), new PadBinding("camera", PadBinding.Button, 2, 0)],
        }).Problems());
    }

    [Fact]
    public void AnEmptyLayoutIsRefused()
    {
        // The first binding run, with nobody at the pad, saved one: loaded, it would have taken every
        // default pad binding away and put nothing back.
        string empty = """{ "guid": "0300be79790000000600000000000000", "name": "Microntek USB Joystick", "bindings": [] }""";

        Assert.NotEmpty((Sample() with { Bindings = [] }).Problems());
        Assert.Throws<InvalidDataException>(() => PadProfile.Parse(empty));
    }

    [Fact]
    public void AMisspeltFieldIsRefused()
    {
        string json = Sample().ToJson().Replace("\"bindings\"", "\"bindingz\"");

        Assert.Throws<InvalidDataException>(() => PadProfile.Parse(json));
    }
}
