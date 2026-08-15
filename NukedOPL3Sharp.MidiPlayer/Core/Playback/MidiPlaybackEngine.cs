using NukedOPL3Sharp.MidiPlayer.Core.Midi;
using NukedOPL3Sharp.MidiPlayer.Core.Patches;
using NukedOPL3Sharp.MidiPlayer.Core.Synth;

namespace NukedOPL3Sharp.MidiPlayer.Core.Playback;

public sealed class MidiPlaybackEngine : IPlaybackEngine
{
    private readonly short[] _discard = new short[32768 * 2];
    private readonly MidiSequence _sequence;
    private readonly OplMidiSynth _synth;
    private DcHighPassFilter _hpFilter;

    private float _masterVolume = 1.0f;

    private uint _samplesUntilNextEvent;

    public MidiPlaybackEngine(string path, string displayName, Dictionary<ushort, OplPatch> patches, int sampleRate)
    {
        SampleRate = sampleRate;

        var midi = MidiFile.Load(path);
        _sequence = new MidiSequence(midi);
        _synth = new OplMidiSynth(patches, sampleRate);
        _hpFilter = new DcHighPassFilter(sampleRate, 5.0);

        TotalSamples = ComputeTotalSamples(midi, sampleRate);
        Track = new TrackInfo(path, displayName, Duration, TotalSamples);
    }

    public TrackInfo Track { get; }

    public bool Looping { get; set; }

    public float MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Math.Clamp(value, 0, 1);
    }

    public int SampleRate { get; }

    public TimeSpan Position => TimeSpan.FromSeconds(CurrentSample / (double)SampleRate);
    public TimeSpan Duration => TimeSpan.FromSeconds(TotalSamples / (double)SampleRate);

    public long CurrentSample { get; private set; }

    public long TotalSamples { get; }

    public void Reset()
    {
        _sequence.Reset();
        _synth.Reset();
        _hpFilter.Reset(SampleRate, 5.0);
        _samplesUntilNextEvent = 0;
        CurrentSample = 0;
    }

    public void SeekTo(TimeSpan position)
    {
        var targetSample = (long)Math.Round(position.TotalSeconds * SampleRate);
        var delta = Math.Abs(targetSample - CurrentSample);
        var fastThreshold = SampleRate * 2L;
        if (delta > fastThreshold)
        {
            FastSeekToSample(targetSample);
        }
        else
        {
            AccurateSeekToSample(targetSample);
        }
    }

    public void Render(Span<short> interleavedStereo)
    {
        if ((interleavedStereo.Length & 1) != 0)
        {
            throw new ArgumentException("Audio buffer must be interleaved stereo (even length).",
                nameof(interleavedStereo));
        }

        var framesRemaining = interleavedStereo.Length / 2;
        var frameOffset = 0;

        while (framesRemaining > 0)
        {
            if (_samplesUntilNextEvent == 0)
            {
                _samplesUntilNextEvent = _sequence.UpdateAndGetDelaySamples(_synth, SampleRate);

                if (_sequence.AtEnd && _samplesUntilNextEvent == 0)
                {
                    if (Looping)
                    {
                        Reset();
                        continue;
                    }

                    interleavedStereo[(frameOffset * 2)..].Clear();
                    CurrentSample = TotalSamples;
                    return;
                }
            }

            var chunk = (int)Math.Min((uint)framesRemaining, _samplesUntilNextEvent);
            var slice = interleavedStereo.Slice(frameOffset * 2, chunk * 2);

            _synth.Chip.GenerateStream(slice);
            ApplyGainInPlace(slice, _masterVolume);
            _hpFilter.ProcessInPlace(slice);

            _samplesUntilNextEvent -= (uint)chunk;
            CurrentSample += chunk;
            frameOffset += chunk;
            framesRemaining -= chunk;
        }
    }

    public void SetOpl3Controls(Opl3ControlState controls)
    {
        _synth.SetOpl3Controls(controls);
    }

    public void SetStereoEnabled(bool enabled)
    {
        _synth.SetStereoEnabled(enabled);
    }

    public void WriteOplRegister(ushort register, byte value)
    {
        _synth.WriteRegister(register, value);
    }

    private static long ComputeTotalSamples(MidiFile midi, int sampleRate)
    {
        var seq = new MidiSequence(midi);
        long total = 0;
        var guard = 0;
        while (!seq.AtEnd && guard++ < 5_000_000)
        {
            total += seq.UpdateAndGetDelaySamples(NullMidiSink.Instance, sampleRate);
        }

        return total;
    }

    private void AccurateSeekToSample(long targetSample)
    {
        targetSample = Math.Clamp(targetSample, 0, TotalSamples);
        if (targetSample < CurrentSample)
        {
            Reset();
        }

        if (targetSample == CurrentSample)
        {
            return;
        }

        while (CurrentSample < targetSample)
        {
            if (_samplesUntilNextEvent == 0)
            {
                _samplesUntilNextEvent = _sequence.UpdateAndGetDelaySamples(_synth, SampleRate);
                if (_sequence.AtEnd && _samplesUntilNextEvent == 0)
                {
                    CurrentSample = TotalSamples;
                    return;
                }
            }

            var step = (int)Math.Min(_samplesUntilNextEvent,
                (uint)Math.Min(int.MaxValue, targetSample - CurrentSample));
            Discard(step);
            _samplesUntilNextEvent -= (uint)step;
            CurrentSample += step;
        }
    }

    private void FastSeekToSample(long targetSample)
    {
        targetSample = Math.Clamp(targetSample, 0, TotalSamples);
        Reset();

        if (targetSample == 0)
        {
            return;
        }

        var recorder = new SeekStateRecorder();
        long advanced = 0;

        while (!_sequence.AtEnd && advanced < targetSample)
        {
            var delay = _sequence.UpdateAndGetDelaySamples(recorder, SampleRate);
            if (_sequence.AtEnd)
            {
                _samplesUntilNextEvent = 0;
                break;
            }

            if (advanced + delay <= targetSample)
            {
                advanced += delay;
                continue;
            }

            var remaining = targetSample - advanced;
            _samplesUntilNextEvent = (uint)(delay - remaining);
            advanced = targetSample;
            break;
        }

        recorder.ApplyTo(_synth);
        CurrentSample = advanced;
    }

    private void Discard(int frames)
    {
        var remaining = frames;
        while (remaining > 0)
        {
            var chunk = Math.Min(remaining, _discard.Length / 2);
            var slice = _discard.AsSpan(0, chunk * 2);
            _synth.Chip.GenerateStream(slice);
            ApplyGainInPlace(slice, _masterVolume);
            _hpFilter.ProcessInPlace(slice);
            remaining -= chunk;
        }
    }

    private static void ApplyGainInPlace(Span<short> pcm, float gain)
    {
        if (gain >= 0.999f)
        {
            return;
        }

        for (var i = 0; i < pcm.Length; i++)
        {
            var v = (int)Math.Round(pcm[i] * gain);
            if (v > short.MaxValue)
            {
                v = short.MaxValue;
            }

            if (v < short.MinValue)
            {
                v = short.MinValue;
            }

            pcm[i] = (short)v;
        }
    }

    private struct DcHighPassFilter
    {
        private double _coef;
        private int _lastInL;
        private int _lastInR;
        private int _lastOutL;
        private int _lastOutR;

        public DcHighPassFilter(int sampleRate, double cutoffHz)
        {
            _coef = ComputeCoef(sampleRate, cutoffHz);
            _lastInL = _lastInR = 0;
            _lastOutL = _lastOutR = 0;
        }

        public void Reset(int sampleRate, double cutoffHz)
        {
            _coef = ComputeCoef(sampleRate, cutoffHz);
            _lastInL = _lastInR = 0;
            _lastOutL = _lastOutR = 0;
        }

        public void ProcessInPlace(Span<short> interleavedStereo)
        {
            if (_coef >= 1.0)
            {
                return;
            }

            for (var i = 0; i < interleavedStereo.Length; i += 2)
            {
                var inL = (int)interleavedStereo[i];
                var inR = (int)interleavedStereo[i + 1];

                var lastInL = _lastInL;
                var lastInR = _lastInR;
                _lastInL = inL;
                _lastInR = inR;

                _lastOutL = (int)(_coef * (_lastOutL + inL - lastInL));
                _lastOutR = (int)(_coef * (_lastOutR + inR - lastInR));

                interleavedStereo[i] = (short)Math.Clamp(_lastOutL, short.MinValue, short.MaxValue);
                interleavedStereo[i + 1] = (short)Math.Clamp(_lastOutR, short.MinValue, short.MaxValue);
            }
        }

        private static double ComputeCoef(int sampleRate, double cutoffHz)
        {
            if (cutoffHz <= 0.0)
            {
                return 1.0;
            }

            const double pi = 3.14159265358979323846;
            return 1.0 / (2 * pi * cutoffHz / sampleRate + 1);
        }
    }

    private sealed class SeekStateRecorder : IMidiSink
    {
        private readonly Dictionary<(byte Ch, byte Note), byte> _activeNotes = new();
        private readonly ChannelState[] _channels = new ChannelState[16];

        public SeekStateRecorder()
        {
            for (var i = 0; i < _channels.Length; i++)
            {
                _channels[i] = new ChannelState();
            }
        }

        public void NoteOn(byte channel, byte note, byte velocity)
        {
            channel &= 15;
            note &= 0x7f;
            velocity &= 0x7f;
            if (velocity == 0)
            {
                NoteOff(channel, note);
                return;
            }

            _activeNotes[(channel, note)] = velocity;
        }

        public void NoteOff(byte channel, byte note)
        {
            channel &= 15;
            note &= 0x7f;
            _activeNotes.Remove((channel, note));
        }

        public void PitchBend(byte channel, double normalizedMinus1To1)
        {
            channel &= 15;
            _channels[channel].PitchBend = normalizedMinus1To1;
        }

        public void ProgramChange(byte channel, byte program)
        {
            channel &= 15;
            _channels[channel].Program = (byte)(program & 0x7f);
        }

        public void ControlChange(byte channel, byte control, byte value)
        {
            channel &= 15;
            control &= 0x7f;
            value &= 0x7f;

            ref var st = ref _channels[channel];
            switch (control)
            {
                case 0: st.Cc0 = value; break;
                case 6:
                    if (st.Rpn == 0)
                    {
                        st.BendRange = value;
                    }

                    break;
                case 7: st.Volume = value; break;
                case 10: st.Pan = value; break;
                case 32: st.Cc32 = value; break;
                case 98:
                case 99:
                    st.Rpn = 0x3fff;
                    break;
                case 100:
                    st.Rpn = (ushort)((st.Rpn & 0x3f80) | value);
                    break;
                case 101:
                    st.Rpn = (ushort)((st.Rpn & 0x7f) | (value << 7));
                    break;
            }
        }

        public void PostUpdate()
        {
        }

        public void ApplyTo(OplMidiSynth synth)
        {
            synth.Reset();

            for (byte ch = 0; ch < 16; ch++)
            {
                ref var st = ref _channels[ch];
                synth.ControlChange(ch, 0, st.Cc0);
                synth.ControlChange(ch, 32, st.Cc32);
                synth.ProgramChange(ch, st.Program);
                synth.ControlChange(ch, 7, st.Volume);
                synth.ControlChange(ch, 10, st.Pan);
                synth.ControlChange(ch, 101, 0);
                synth.ControlChange(ch, 100, 0);
                synth.ControlChange(ch, 6, st.BendRange);
                synth.ControlChange(ch, 101, (byte)(st.Rpn >> 7));
                synth.ControlChange(ch, 100, (byte)(st.Rpn & 0x7f));
                synth.PitchBend(ch, st.PitchBend);
            }

            foreach (var ((ch, note), vel) in _activeNotes)
            {
                synth.NoteOn(ch, note, vel);
            }
        }

        private struct ChannelState
        {
            public byte Program;
            public byte Volume;
            public byte Pan;
            public double PitchBend;
            public byte Cc0;
            public byte Cc32;
            public ushort Rpn;
            public byte BendRange;

            public ChannelState()
            {
                Program = 0;
                Volume = 127;
                Pan = 64;
                PitchBend = 0;
                Cc0 = 0;
                Cc32 = 0;
                Rpn = 0x3fff;
                BendRange = 2;
            }
        }
    }
}
