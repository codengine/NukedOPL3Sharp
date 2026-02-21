namespace NukedOPL3Sharp.MidiPlayer.Core.Midi;

public sealed class MidiSequence
{
    private readonly MidiFile _file;
    private readonly int _ticksPerQuarter;
    private int _index;

    private int _tempoUsPerQuarter = 500_000;
    private uint _ticksToNext;

    public MidiSequence(MidiFile file)
    {
        _file = file;

        _ticksPerQuarter = (ushort)file.Division;
        if (_ticksPerQuarter <= 0)
        {
            _ticksPerQuarter = 480;
        }

        Reset();
    }

    public bool AtEnd { get; private set; }

    public void Reset()
    {
        _index = 0;
        _tempoUsPerQuarter = 500_000;
        AtEnd = _file.Events.Count == 0;
        _ticksToNext = AtEnd ? 0 : _file.Events[0].DeltaTicks;
    }

    public uint UpdateAndGetDelaySamples(IMidiSink sink, int sampleRate)
    {
        if (AtEnd)
        {
            return 0;
        }

        while (_index < _file.Events.Count)
        {
            if (_ticksToNext != 0)
            {
                break;
            }

            var ev = _file.Events[_index++];
            ApplyEvent(ev, sink);

            if (AtEnd || _index >= _file.Events.Count)
            {
                AtEnd = true;
                sink.PostUpdate();
                return 0;
            }

            _ticksToNext = _file.Events[_index].DeltaTicks;
        }

        var delayTicks = _ticksToNext;
        _ticksToNext = 0;

        var delaySamples = delayTicks == 0
            ? 0u
            : (uint)Math.Round(delayTicks * SecondsPerTick() * sampleRate, MidpointRounding.AwayFromZero);

        sink.PostUpdate();
        return delaySamples;
    }

    private double SecondsPerTick()
    {
        return _tempoUsPerQuarter / 1_000_000.0 / _ticksPerQuarter;
    }

    private void ApplyEvent(MidiEvent ev, IMidiSink sink)
    {
        switch (ev.Kind)
        {
            case MidiEventKind.NoteOn:
                sink.NoteOn(ev.Channel, ev.Data0, ev.Data1);
                break;
            case MidiEventKind.NoteOff:
                sink.NoteOff(ev.Channel, ev.Data0);
                break;
            case MidiEventKind.ControlChange:
                sink.ControlChange(ev.Channel, ev.Data0, ev.Data1);
                break;
            case MidiEventKind.ProgramChange:
                sink.ProgramChange(ev.Channel, ev.Data0);
                break;
            case MidiEventKind.PitchBend:
            {
                var val14 = (ev.Data0 | (ev.Data1 << 7)) - 8192;
                sink.PitchBend(ev.Channel, val14 / 8192.0);
                break;
            }
            case MidiEventKind.Tempo:
                if (ev.Payload.Length >= 4)
                {
                    _tempoUsPerQuarter = BitConverter.ToInt32(ev.Payload, 0);
                }

                break;
            case MidiEventKind.EndOfTrack:
                AtEnd = true;
                break;
        }
    }
}