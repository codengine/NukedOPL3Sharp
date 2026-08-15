// SPDX-FileCopyrightText: 2021-2024 Devin Acker
// SPDX-License-Identifier: BSD-3-Clause

using NukedOPL3Sharp.MidiPlayer.Core.Midi;
using NukedOPL3Sharp.MidiPlayer.Core.Patches;

namespace NukedOPL3Sharp.MidiPlayer.Core.Synth;

public sealed class OplMidiSynth : IMidiSink
{
    private const ushort REG_OP_MODE = 0x20;
    private const ushort REG_OP_LEVEL = 0x40;
    private const ushort REG_OP_AD = 0x60;
    private const ushort REG_OP_SR = 0x80;
    private const ushort REG_VOICE_FREQL = 0xA0;
    private const ushort REG_VOICE_FREQH = 0xB0;
    private const ushort REG_VOICE_CNT = 0xC0;
    private const ushort REG_OP_WAVEFORM = 0xE0;
    private const ushort REG_4OP = 0x104;
    private const ushort REG_NEW = 0x105;

    private static readonly ushort[] VoiceNum =
    [
        0x0, 0x1, 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x8,
        0x100, 0x101, 0x102, 0x103, 0x104, 0x105, 0x106, 0x107, 0x108
    ];

    private static readonly ushort[] OperNum =
    [
        0x0, 0x1, 0x2, 0x8, 0x9, 0xA, 0x10, 0x11, 0x12,
        0x100, 0x101, 0x102, 0x108, 0x109, 0x10A, 0x110, 0x111, 0x112
    ];

    private static readonly ushort[] NoteFreq =
    [
        345, 365, 387, 410, 435, 460, 488, 517, 547, 580, 615, 651
    ];

    private static readonly byte[] OplVolumeMap =
    [
        80, 63, 40, 36, 32, 28, 23, 21,
        19, 17, 15, 14, 13, 12, 11, 10,
        9, 8, 7, 6, 5, 5, 4, 4,
        3, 3, 2, 2, 1, 1, 0, 0
    ];

    private readonly MidiChannel[] _channels = new MidiChannel[16];
    private readonly short[] _discard;

    private readonly Dictionary<ushort, OplPatch> _patches;

    private readonly int _sampleRate;
    private readonly OplVoice[] _voices;
    private Opl3ControlState _controls = Opl3ControlState.Default;

    public OplMidiSynth(Dictionary<ushort, OplPatch> patches, int sampleRate)
    {
        _patches = patches;
        _sampleRate = sampleRate;
        _discard = new short[48 * 2];

        for (byte i = 0; i < _channels.Length; i++)
        {
            _channels[i] = new MidiChannel { Num = i, Percussion = i == 9 };
        }

        _voices = new OplVoice[18];
        for (var i = 0; i < _voices.Length; i++)
        {
            var mod9 = i % 9;
            var fourOpPrimary = mod9 is >= 0 and < 3;
            int? other = mod9 switch
            {
                0 or 1 or 2 => i + 3,
                3 or 4 or 5 => i - 3,
                _ => null
            };

            _voices[i] = new OplVoice
            {
                Index = i,
                Num = VoiceNum[i],
                Op = OperNum[i],
                FourOpPrimary = fourOpPrimary,
                FourOpOtherIndex = other
            };
        }

        Reset();
    }

    public Opl3Chip Chip { get; } = new();
    public bool Stereo { get; private set; } = true;

    public void PostUpdate()
    {
        foreach (var voice in _voices)
        {
            if (voice.Duration < uint.MaxValue)
            {
                voice.Duration++;
            }

            voice.JustChanged = false;
        }
    }

    public void NoteOn(byte channel, byte note, byte velocity)
    {
        note &= 0x7f;
        velocity &= 0x7f;

        if (FindVoice(channel, note, true) is not null)
        {
            return;
        }

        if (velocity == 0)
        {
            NoteOff(channel, note);
            return;
        }

        var patch = FindPatch(channel, note);
        if (patch is null)
        {
            return;
        }

        var numVoices = patch.FourOp || patch.DualTwoOp ? 2 : 1;
        OplVoice? voice = null;

        for (var i = 0; i < numVoices; i++)
        {
            if (voice is not null && patch.FourOp && voice.FourOpOtherIndex is { } otherIndex)
            {
                voice = _voices[otherIndex];
            }
            else
            {
                voice = FindVoice(channel, patch, note);
            }

            if (voice is null)
            {
                continue;
            }

            UpdatePatch(voice, patch, (byte)i);

            var ch = _channels[channel & 15];

            voice.Channel = ch;
            voice.On = true;
            voice.JustChanged = true;
            voice.Note = note;
            voice.Velocity = (byte)Math.Clamp(velocity + patch.Velocity, 0, 127);
            voice.Duration = 0;

            UpdateVolume(voice);
            UpdatePanning(voice);

            if (!patch.FourOp)
            {
                UpdateFrequency(voice);
            }
            else if (i > 0 && voice.FourOpOtherIndex is { } other)
            {
                UpdateFrequency(_voices[other]);
            }
        }
    }

    public void NoteOff(byte channel, byte note)
    {
        note &= 0x7f;

        while (true)
        {
            var voice = FindVoice(channel, note, false);
            if (voice is null)
            {
                break;
            }

            voice.JustChanged = voice.On;
            voice.On = false;

            Write(REG_VOICE_FREQH, voice.Num, (byte)(voice.Freq >> 8));
        }
    }

    public void PitchBend(byte channel, double normalizedMinus1To1)
    {
        var ch = _channels[channel & 15];
        ch.BasePitch = normalizedMinus1To1;
        ch.Pitch = MidiCalcBend(normalizedMinus1To1 * ch.BendRange);
        UpdateChannelVoices(channel, UpdateFrequency);
    }

    public void ProgramChange(byte channel, byte program)
    {
        _channels[channel & 15].PatchNum = (byte)(program & 0x7f);
    }

    public void ControlChange(byte channel, byte control, byte value)
    {
        channel &= 15;
        control &= 0x7f;
        value &= 0x7f;

        var ch = _channels[channel];
        switch (control)
        {
            case 0:
                // SysEx mode switching is intentionally not supported; ignore CC0 mode-dependent behavior.
                break;

            case 32:
                // SysEx mode switching is intentionally not supported; ignore CC32 mode-dependent behavior.
                break;

            case 6: // data entry (RPN 0 -> pitch bend range)
                if (ch.Rpn == 0)
                {
                    ch.BendRange = value;
                    PitchBend(channel, ch.BasePitch);
                }

                break;

            case 7: // volume
                ch.Volume = value;
                UpdateChannelVoices(channel, UpdateVolume);
                break;

            case 10: // pan
                ch.Pan = value;
                if (Stereo)
                {
                    UpdateChannelVoices(channel, UpdatePanning);
                }

                break;

            case 98:
            case 99:
                ch.Rpn = 0x3fff;
                break;

            case 100:
                ch.Rpn = (ushort)((ch.Rpn & 0x3f80) | value);
                break;

            case 101:
                ch.Rpn = (ushort)((ch.Rpn & 0x7f) | (value << 7));
                break;
        }
    }

    public void Reset()
    {
        Chip.Reset((uint)_sampleRate);
        Chip.WriteRegister(REG_NEW, 0x01);
        Chip.WriteRegister(REG_4OP, 0x00);
        ApplyControlsToChip();

        foreach (var ch in _channels)
        {
            ch.Bank = 0;
            ch.PatchNum = 0;
            ch.Volume = 127;
            ch.Expression = 127;
            ch.Pan = 64;
            ch.BasePitch = 0;
            ch.Pitch = 1.0;
            ch.Rpn = 0x3fff;
            ch.BendRange = 2;
            ch.Percussion = ch.Num == 9;
        }

        foreach (var v in _voices)
        {
            v.Channel = null;
            v.Patch = null;
            v.PatchVoice = null;
            v.On = false;
            v.JustChanged = false;
            v.Note = 0;
            v.Velocity = 0;
            v.Freq = 0;
            v.Duration = uint.MaxValue;
        }

        // quick silence
        foreach (var v in _voices)
        {
            SilenceVoice(v);
        }
    }

    public void SetOpl3Controls(Opl3ControlState controls)
    {
        _controls = controls.WithDrumMask(controls.DrumMask);
        ApplyControlsToChip();
    }

    public void SetStereoEnabled(bool enabled)
    {
        Stereo = enabled;
        UpdateChannelVoices(-1, UpdatePanning);
    }

    public void WriteRegister(ushort register, byte value)
    {
        Chip.WriteRegister(register, value);
    }

    private void ApplyControlsToChip()
    {
        // 0x08: NTS (note select) is bit 6.
        Chip.WriteRegister(0x08, (byte)(_controls.NoteSelect ? 0x40 : 0x00));

        // 0xBD: AM/VIB depth + rhythm mode + drum enables.
        var bd = 0;
        if (_controls.TremoloDepth)
        {
            bd |= 1 << 7;
        }

        if (_controls.VibratoDepth)
        {
            bd |= 1 << 6;
        }

        if (_controls.RhythmMode)
        {
            bd |= 1 << 5;
        }

        if (_controls.RhythmMode)
        {
            bd |= _controls.DrumMask & 0x1F;
        }

        Chip.WriteRegister(0xBD, (byte)bd);
    }

    private void Write(ushort regBase, ushort index, byte value)
    {
        Chip.WriteRegister((ushort)(regBase + index), value);
    }

    private static double MidiCalcBend(double semitones)
    {
        return Math.Pow(2, semitones / 12.0);
    }

    private OplVoice? FindVoice(byte channel, OplPatch patch, byte note)
    {
        OplVoice? found = null;
        var duration = 0u;

        foreach (var voice in _voices)
        {
            if (patch.FourOp && !voice.FourOpPrimary)
            {
                continue;
            }

            if (voice.Channel is null)
            {
                return voice;
            }

            if (voice is not { On: false, JustChanged: false })
            {
                continue;
            }

            if (voice.Channel.Num == (channel & 15) && voice.Note == note && voice.Duration < uint.MaxValue)
            {
                SilenceVoice(voice);
                if (voice.Patch?.FourOp == true && voice.FourOpOtherIndex is { } other)
                {
                    SilenceVoice(_voices[other]);
                }
            }
            else if (voice.Duration > duration)
            {
                found = voice;
                duration = voice.Duration;
            }
        }

        if (found is not null)
        {
            return found;
        }

        foreach (var voice in _voices)
        {
            if (patch.FourOp && !voice.FourOpPrimary)
            {
                continue;
            }

            if (ReferenceEquals(voice.Patch, patch) && voice.Duration > duration)
            {
                found = voice;
                duration = voice.Duration;
            }
        }

        if (found is not null)
        {
            return found;
        }

        foreach (var voice in _voices)
        {
            switch (patch.FourOp)
            {
                case true when !voice.FourOpPrimary:
                case false when voice is { On: true, Patch.FourOp: true }:
                    continue;
            }

            if (voice.Duration > duration)
            {
                found = voice;
                duration = voice.Duration;
            }
        }

        return found;
    }

    private OplVoice? FindVoice(byte channel, byte note, bool justChanged)
    {
        channel &= 15;
        foreach (var voice in _voices)
        {
            if (voice.On && voice.JustChanged == justChanged && voice.Channel == _channels[channel] &&
                voice.Note == note)
            {
                return voice;
            }
        }

        return null;
    }

    private OplPatch? FindPatch(byte channel, byte note)
    {
        var ch = _channels[channel & 15];

        ushort key;
        if (ch.Percussion)
        {
            key = (ushort)(0x80 | note | (ch.PatchNum << 8));
        }
        else
        {
            key = (ushort)(ch.PatchNum | (ch.Bank << 8));
        }

        if (!_patches.ContainsKey(key))
        {
            key &= 0x00ff;
        }

        if (!_patches.ContainsKey(key))
        {
            key &= 0x0080;
        }

        return _patches.TryGetValue(key, out var p) ? p : null;
    }

    private void UpdateChannelVoices(int channel, Action<OplVoice> update)
    {
        foreach (var voice in _voices)
        {
            if (voice.Channel is null)
            {
                continue;
            }

            if (channel < 0 || voice.Channel == _channels[channel & 15])
            {
                update(voice);
            }
        }
    }

    private void UpdatePatch(OplVoice voice, OplPatch newPatch, byte numVoice)
    {
        var patchVoice = newPatch.Voices[numVoice];
        if (!ReferenceEquals(voice.PatchVoice, patchVoice))
        {
            var oldFourOp = voice.Patch?.FourOp ?? false;
            voice.Patch = newPatch;
            voice.PatchVoice = patchVoice;

            if (newPatch.FourOp != oldFourOp)
            {
                var otherIndex = voice.FourOpOtherIndex;
                if (otherIndex is { } other && _voices[other].Patch?.FourOp == true && !newPatch.FourOp)
                {
                    SilenceVoice(_voices[other]);
                }

                byte enable = 0;
                byte bit = 1;
                foreach (var t in _voices)
                {
                    if (!t.FourOpPrimary)
                    {
                        continue;
                    }

                    if (t.Patch?.FourOp == true)
                    {
                        enable |= bit;
                    }

                    bit <<= 1;
                }

                Chip.WriteRegister(REG_4OP, enable);
            }

            SilenceVoice(voice);
            Chip.GenerateStream(_discard);

            Write(REG_OP_MODE, voice.Op, patchVoice.OpMode[0]);
            Write(REG_OP_MODE, (ushort)(voice.Op + 3), patchVoice.OpMode[1]);

            Write(REG_OP_AD, voice.Op, patchVoice.OpAd[0]);
            Write(REG_OP_AD, (ushort)(voice.Op + 3), patchVoice.OpAd[1]);

            Write(REG_OP_WAVEFORM, voice.Op, patchVoice.OpWave[0]);
            Write(REG_OP_WAVEFORM, (ushort)(voice.Op + 3), patchVoice.OpWave[1]);
        }

        Write(REG_OP_SR, voice.Op, patchVoice.OpSr[0]);
        Write(REG_OP_SR, (ushort)(voice.Op + 3), patchVoice.OpSr[1]);
    }

    private static (bool ScaleOp1, bool ScaleOp2) ActiveCarriers(OplVoice voice)
    {
        var patchVoice = voice.PatchVoice;
        if (patchVoice is null || voice.Patch is null)
        {
            return (false, false);
        }

        if (!voice.Patch.FourOp)
        {
            return ((patchVoice.Conn & 1) != 0, true);
        }

        if (voice.FourOpPrimary)
        {
            var scale1 = (voice.Patch.Voices[0].Conn & 1) != 0;
            var scale2 = (voice.Patch.Voices[1].Conn & 1) != 0 && !scale1;
            return (scale1, scale2);
        }

        var s1 = (voice.Patch.Voices[0].Conn & 1) != 0 && (voice.Patch.Voices[1].Conn & 1) != 0;
        return (s1, true);
    }

    private void UpdateVolume(OplVoice voice)
    {
        if (voice.Patch is null || voice.Channel is null || voice.PatchVoice is null)
        {
            return;
        }

        var index = (voice.Velocity * voice.Channel.Volume) >> 9;
        var atten = OplVolumeMap[Math.Clamp(index, 0, 31)];

        var scale = ActiveCarriers(voice);
        byte level;

        if (scale.ScaleOp1)
        {
            level = (byte)Math.Min(0x3f, voice.PatchVoice.OpLevel[0] + atten);
        }
        else
        {
            level = voice.PatchVoice.OpLevel[0];
        }

        Write(REG_OP_LEVEL, voice.Op, (byte)(level | voice.PatchVoice.OpKsr[0]));

        if (scale.ScaleOp2)
        {
            level = (byte)Math.Min(0x3f, voice.PatchVoice.OpLevel[1] + atten);
        }
        else
        {
            level = voice.PatchVoice.OpLevel[1];
        }

        Write(REG_OP_LEVEL, (ushort)(voice.Op + 3), (byte)(level | voice.PatchVoice.OpKsr[1]));
    }

    private void UpdatePanning(OplVoice voice)
    {
        if (voice.Patch is null || voice.Channel is null || voice.PatchVoice is null)
        {
            return;
        }

        byte pan = 0x30;
        if (Stereo)
        {
            pan = voice.Channel.Pan switch
            {
                < 32 => 0x10,
                >= 96 => 0x20,
                _ => pan
            };
        }

        Write(REG_VOICE_CNT, voice.Num, (byte)(voice.PatchVoice.Conn | pan));
    }

    private void UpdateFrequency(OplVoice voice)
    {
        if (voice.Patch is null || voice.Channel is null || voice.PatchVoice is null)
        {
            return;
        }

        if (voice.Patch.FourOp && !voice.FourOpPrimary)
        {
            return;
        }

        var baseNote = !voice.Channel.Percussion ? voice.Note : voice.Patch.FixedNote;
        var note = baseNote + voice.PatchVoice.Tune;

        var octave = note / 12;
        note %= 12;

        var freq = note >= 0 ? NoteFreq[note] : (ushort)(NoteFreq[note + 12] >> 1);

        freq = octave switch
        {
            < 0 => (ushort)(freq >> -octave),
            > 0 => (ushort)(freq << octave),
            _ => freq
        };

        var freqD = freq * voice.Channel.Pitch * voice.PatchVoice.FineTune;
        var f = freqD <= 0 ? 0u : freqD >= uint.MaxValue ? uint.MaxValue : (uint)freqD;

        octave = 0;
        while (f > 0x3ff)
        {
            f >>= 1;
            octave++;
        }

        octave = Math.Min(7, octave);
        voice.Freq = (ushort)(f | ((uint)octave << 10));

        Write(REG_VOICE_FREQL, voice.Num, (byte)(voice.Freq & 0xff));
        Write(REG_VOICE_FREQH, voice.Num, (byte)((voice.Freq >> 8) | (voice.On ? 1 << 5 : 0)));
    }

    private void SilenceVoice(OplVoice voice)
    {
        voice.On = false;
        voice.JustChanged = true;
        voice.Duration = uint.MaxValue;

        Write(REG_OP_SR, voice.Op, 0xff);
        Write(REG_OP_SR, (ushort)(voice.Op + 3), 0xff);
        Write(REG_VOICE_FREQH, voice.Num, (byte)(voice.Freq >> 8));
    }
}
