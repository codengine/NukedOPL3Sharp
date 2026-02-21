using NukedOPL3Sharp.MidiPlayer.Core.Midi;

namespace NukedOPL3Sharp.MidiPlayer.Core.Playback;

public sealed class NullMidiSink : IMidiSink
{
    public static readonly NullMidiSink Instance = new();

    private NullMidiSink()
    {
    }

    public void NoteOn(byte channel, byte note, byte velocity)
    {
    }

    public void NoteOff(byte channel, byte note)
    {
    }

    public void PitchBend(byte channel, double normalizedMinus1To1)
    {
    }

    public void ProgramChange(byte channel, byte program)
    {
    }

    public void ControlChange(byte channel, byte control, byte value)
    {
    }

    public void PostUpdate()
    {
    }
}