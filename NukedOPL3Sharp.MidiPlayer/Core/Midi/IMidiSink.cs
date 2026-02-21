namespace NukedOPL3Sharp.MidiPlayer.Core.Midi;

public interface IMidiSink
{
    void NoteOn(byte channel, byte note, byte velocity);
    void NoteOff(byte channel, byte note);
    void PitchBend(byte channel, double normalizedMinus1To1);
    void ProgramChange(byte channel, byte program);
    void ControlChange(byte channel, byte control, byte value);
    void PostUpdate();
}