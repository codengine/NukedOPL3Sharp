namespace NukedOPL3Sharp.MidiPlayer.Core.Synth;

public sealed class MidiChannel
{
    public byte Num { get; init; }

    public bool Percussion { get; set; }
    public byte Bank { get; set; }
    public byte PatchNum { get; set; }
    public byte Volume { get; set; } = 127;
    public byte Expression { get; set; } = 127;
    public byte Pan { get; set; } = 64;
    public double BasePitch { get; set; } // pitch wheel position
    public double Pitch { get; set; } = 1.0; // frequency multiplier

    public ushort Rpn { get; set; } = 0x3fff;
    public byte BendRange { get; set; } = 2;
}