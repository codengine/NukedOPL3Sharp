namespace NukedOPL3Sharp.MidiPlayer.Core.Midi;

public enum MidiEventKind : byte
{
    NoteOff,
    NoteOn,
    ControlChange,
    ProgramChange,
    PitchBend,
    Tempo,
    EndOfTrack
}

public readonly record struct MidiEvent(
    uint DeltaTicks,
    MidiEventKind Kind,
    byte Channel,
    byte Data0,
    byte Data1,
    byte[] Payload);