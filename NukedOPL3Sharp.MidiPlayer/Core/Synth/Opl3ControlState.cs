namespace NukedOPL3Sharp.MidiPlayer.Core.Synth;

public readonly record struct Opl3ControlState(
    bool TremoloDepth,
    bool VibratoDepth,
    bool RhythmMode,
    byte DrumMask,
    bool NoteSelect)
{
    public static Opl3ControlState Default => new(false, false, false, 0, false);

    public Opl3ControlState WithDrumMask(byte drumMask)
    {
        return this with { DrumMask = (byte)(drumMask & 0x1F) };
    }
}
