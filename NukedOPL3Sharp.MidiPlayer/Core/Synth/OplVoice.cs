using NukedOPL3Sharp.MidiPlayer.Core.Patches;

namespace NukedOPL3Sharp.MidiPlayer.Core.Synth;

public sealed class OplVoice
{
    public int Index { get; init; }
    public ushort Num { get; init; }
    public ushort Op { get; init; }

    public bool FourOpPrimary { get; init; }
    public int? FourOpOtherIndex { get; init; }

    public MidiChannel? Channel { get; set; }
    public OplPatch? Patch { get; set; }
    public PatchVoice? PatchVoice { get; set; }

    public bool On { get; set; }
    public bool JustChanged { get; set; }
    public byte Note { get; set; }
    public byte Velocity { get; set; }

    public ushort Freq { get; set; }
    public uint Duration { get; set; } = uint.MaxValue;
}