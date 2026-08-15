namespace NukedOPL3Sharp.MidiPlayer.Core.Patches;

public sealed class PatchVoice
{
    public byte[] OpMode { get; } = new byte[2]; // regs 0x20+
    public byte[] OpKsr { get; } = new byte[2]; // regs 0x40+ (upper bits)
    public byte[] OpLevel { get; } = new byte[2]; // regs 0x40+ (lower bits)
    public byte[] OpAd { get; } = new byte[2]; // regs 0x60+
    public byte[] OpSr { get; } = new byte[2]; // regs 0x80+
    public byte Conn { get; set; } // regs 0xC0+
    public byte[] OpWave { get; } = new byte[2]; // regs 0xE0+

    public sbyte Tune { get; set; } // MIDI note offset
    public double FineTune { get; set; } = 1.0; // frequency multiplier
}
