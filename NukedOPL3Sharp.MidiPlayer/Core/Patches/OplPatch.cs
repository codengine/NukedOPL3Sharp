namespace NukedOPL3Sharp.MidiPlayer.Core.Patches;

public sealed class OplPatch
{
    public string Name { get; set; } = string.Empty;
    public bool FourOp { get; set; }
    public bool DualTwoOp { get; set; }
    public byte FixedNote { get; set; }
    public sbyte Velocity { get; set; }

    public PatchVoice[] Voices { get; } = [new(), new()];
}
