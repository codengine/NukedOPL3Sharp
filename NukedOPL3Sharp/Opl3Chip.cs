// SPDX-FileCopyrightText: 2013-2026 Nuked-OPL3 by nukeykt
// SPDX-FileCopyrightText: 2026 Tony Gies
// SPDX-License-Identifier: LGPL-2.1-only

namespace NukedOPL3Sharp;

/// <summary>
///     Emulates one YMF262-compatible OPL3 chip and owns all channel, operator, timer, and buffered-write state.
/// </summary>
public sealed partial class Opl3Chip
{
    public const int WriteBufferSize = 1024;
    public const int WriteBufferDelay = 2;
    private const int ResampleFractionBits = 10;

    /// <summary>
    ///     Holds channels that can contribute to the next left-side mix.
    /// </summary>
    internal Opl3Channel[] LeftMixChannels { get; } = new Opl3Channel[18];

    /// <summary>
    ///     Holds channels that can contribute to the next right-side mix.
    /// </summary>
    internal Opl3Channel[] RightMixChannels { get; } = new Opl3Channel[18];

    /// <summary>
    ///     Counts valid entries in <see cref="LeftMixChannels"/>.
    /// </summary>
    internal byte LeftMixChannelCount;

    /// <summary>
    ///     Counts valid entries in <see cref="RightMixChannels"/>.
    /// </summary>
    internal byte RightMixChannelCount;

    /// <summary>
    ///     Requests a mix-list rebuild after routing or algorithm state changes.
    /// </summary>
    internal bool MixListsDirty;

    /// <summary>
    ///     Invalidates dormant operators whenever a register write occurs.
    /// </summary>
    internal uint WriteGeneration;

    /// <summary>
    ///     Holds the noise bit observed by the hi-hat operator during the current sample.
    /// </summary>
    internal uint NoiseHihat;

    /// <summary>
    ///     Holds the noise bit observed by the snare operator during the current sample.
    /// </summary>
    internal uint NoiseSnare;

    /// <summary>
    ///     Requests a tremolo refresh after its depth changes between timer steps.
    /// </summary>
    internal bool TremoloDirty;

    public Opl3Channel[] Channels { get; } = new Opl3Channel[18];
    public Opl3Operator[] Slots { get; } = new Opl3Operator[36];
    public ushort Timer;
    public ulong EgTimer;
    public byte EgTimerRem;
    public byte EgState;
    public byte EgAdd;
    public byte EgTimerLow;
    public byte NewM;
    public byte Nts;
    public byte Rhythm;
    public byte VibratoPosition;
    public byte VibratoShift;
    public byte Tremolo;
    public byte TremoloPosition;
    public byte TremoloShift;
    public uint Noise;
    public short ZeroMod;
    public int[] MixBuffer { get; } = new int[4];
    public byte RhythmHihatBit2;
    public byte RhythmHihatBit3;
    public byte RhythmHihatBit7;
    public byte RhythmHihatBit8;
    public byte RhythmTomBit3;
    public byte RhythmTomBit5;
#if OPL_ENABLE_STEREOEXT
    public byte StereoExtension;
#endif
    public int RateRatio;
    public int SampleCounter;
    public short[] OldSamples { get; } = new short[4];
    public short[] Samples { get; } = new short[4];
    public ulong WriteBufferSampleCounter;
    public uint WriteBufferCurrent;
    public uint WriteBufferLast;
    public ulong WriteBufferLastTime;
    public Opl3WriteBufferEntry[] WriteBuffer { get; } = new Opl3WriteBufferEntry[WriteBufferSize];

    public Opl3Chip()
    {
        for (var i = 0; i < Channels.Length; i++)
        {
            Channels[i] = new Opl3Channel
            {
                Chip = this,
                ChannelNumber = (byte)i,
                ChannelType = ChannelType.TwoOp
            };
            for (var j = 0; j < Channels[i].Out.Length; j++)
            {
                Channels[i].Out[j] = ShortSignalSource.Zero;
            }
        }

        for (var i = 0; i < Slots.Length; i++)
        {
            Slots[i] = new Opl3Operator
            {
                Chip = this,
                SlotIndex = (byte)i,
                ModulationSource = ShortSignalSource.Zero,
                TremoloEnabled = false
            };
        }

        for (var i = 0; i < WriteBuffer.Length; i++)
        {
            WriteBuffer[i] = new Opl3WriteBufferEntry();
        }
    }


    /* Original C: void OPL3_Reset(opl3_chip *chip, uint32_t samplerate); */
    public void Reset(uint sampleRate)
    {
        ResetInternal(sampleRate);
    }

    /* Original C: void OPL3_WriteReg(opl3_chip *chip, uint16_t reg, uint8_t v); */
    public void WriteRegister(ushort register, byte value)
    {
        WriteRegisterInternal(register, value);
    }

    /* Original C: void OPL3_WriteRegBuffered(opl3_chip *chip, uint16_t reg, uint8_t v); */
    public void WriteRegisterBuffered(ushort register, byte value)
    {
        WriteRegisterBufferedInternal(register, value);
    }

    /* Original C: void OPL3_Generate4Ch(opl3_chip *chip, int16_t *buf4); */
    public void Generate4Channels(Span<short> buffer)
    {
        Generate4ChCore(buffer);
    }

    /* Original C: void OPL3_Generate(opl3_chip *chip, int16_t *buf); */
    public void Generate(Span<short> buffer)
    {
        GenerateCore(buffer);
    }

    /* Original C: void OPL3_Generate4ChResampled(opl3_chip *chip, int16_t *buf4); */
    public void Generate4ChannelsResampled(Span<short> buffer)
    {
        Generate4ChResampledCore(buffer);
    }

    /* Original C: void OPL3_GenerateResampled(opl3_chip *chip, int16_t *buf); */
    public void GenerateResampled(Span<short> buffer)
    {
        GenerateResampledCore(buffer);
    }

    /* Original C: void OPL3_Generate4ChStream(opl3_chip *chip, int16_t *sndptr1, int16_t *sndptr2, uint32_t numsamples); */
    public void Generate4ChannelStream(Span<short> stream1, Span<short> stream2)
    {
        Generate4ChStreamCore(stream1, stream2);
    }

    /* Original C: void OPL3_GenerateStream(opl3_chip *chip, int16_t *sndptr, uint32_t numsamples); */
    public void GenerateStream(Span<short> stream)
    {
        GenerateStreamCore(stream);
    }

    public ulong GetWriteBufferSampleCounter()
    {
        return WriteBufferSampleCounter;
    }

    public ulong? PeekNextBufferedWriteSample()
    {
        var entry = WriteBuffer[(int)WriteBufferCurrent];
        return (entry.Register & 0x200) != 0 ? entry.Time : null;
    }

    public void ProcessWriteBufferUntil(ulong inclusiveSampleIndex)
    {
        if (WriteBufferSampleCounter > inclusiveSampleIndex)
        {
            return;
        }

        do
        {
            while (true)
            {
                var entry = WriteBuffer[(int)WriteBufferCurrent];
                if (entry.Time > WriteBufferSampleCounter)
                {
                    break;
                }

                if ((entry.Register & 0x200) == 0)
                {
                    break;
                }

                var register = (ushort)(entry.Register & 0x1ff);
                entry.Register = register;
                WriteRegisterInternal(register, entry.Data);
                WriteBufferCurrent = (WriteBufferCurrent + 1) % WriteBufferSize;
            }

            WriteBufferSampleCounter++;
        } while (WriteBufferSampleCounter <= inclusiveSampleIndex);
    }
}

public enum ChannelType : byte
{
    TwoOp = 0,
    FourOp = 1,
    FourOpPair = 2,
    Drum = 3
}

internal enum EnvelopeKeyType : byte
{
    Normal = 0x01,
    Drum = 0x02
}

public enum EnvelopeGeneratorStage : byte
{
    Attack = 0,
    Decay = 1,
    Sustain = 2,
    Release = 3
}

public sealed class Opl3WriteBufferEntry
{
    public byte Data;
    public ushort Register;
    public ulong Time;
}
