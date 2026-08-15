// SPDX-FileCopyrightText: 2013-2026 Nuked-OPL3 by nukeykt
// SPDX-FileCopyrightText: 2026 Tony Gies
// SPDX-License-Identifier: LGPL-2.1-only

using System.Numerics;
using System.Runtime.CompilerServices;
#if NET10_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace NukedOPL3Sharp;

public sealed partial class Opl3Chip
{
    private static void UpdatePhaseIncrement(Opl3Operator slot)
    {
        var chip = slot.Chip ?? throw new InvalidOperationException("Slot chip not assigned.");
        var channel = slot.Channel ?? throw new InvalidOperationException("Slot channel not assigned.");
        var baseFrequency = ((uint)channel.FNumber << channel.Block) >> 1;
        var multiplier = Opl3Tables.ReadFrequencyMultiplier(slot.RegFrequencyMultiplier);
        slot.PhaseIncrement = (baseFrequency * multiplier) >> 1;

        for (byte vibratoPosition = 0; vibratoPosition < slot.VibratoPhaseIncrements.Length; vibratoPosition++)
        {
            var fNumber = channel.FNumber;
            var range = (sbyte)((fNumber >> 7) & 0x07);

            if ((vibratoPosition & 0x03) == 0)
            {
                range = 0;
            }
            else if ((vibratoPosition & 0x01) != 0)
            {
                range >>= 1;
            }

            range >>= chip.VibratoShift;

            if ((vibratoPosition & 0x04) != 0)
            {
                range = (sbyte)-range;
            }

            fNumber = unchecked((ushort)(fNumber + range));
            slot.VibratoPhaseIncrements[vibratoPosition] = (((uint)fNumber << channel.Block) >> 1) * multiplier >> 1;
        }

        slot.CurrentVibratoPhaseIncrement = slot.VibratoPhaseIncrements[chip.VibratoPosition];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PhaseGenerateNormal(Opl3Operator slot)
    {
        var phaseIncrement = slot.RegVibrato != 0 ? slot.CurrentVibratoPhaseIncrement : slot.PhaseIncrement;
        var phase = (ushort)(slot.RegPhaseGeneratorAccumulator >> 9);
        if (slot.RegPhaseResetRequest != 0)
        {
            slot.RegPhaseGeneratorAccumulator = 0;
        }

        slot.RegPhaseGeneratorAccumulator = unchecked(slot.RegPhaseGeneratorAccumulator + phaseIncrement);
        slot.PhaseGeneratorOutput = phase;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PhaseGenerateRhythm(Opl3Operator slot)
    {
        var chip = slot.Chip ?? throw new InvalidOperationException("Slot chip not assigned.");
        var phaseIncrement = slot.RegVibrato != 0 ? slot.CurrentVibratoPhaseIncrement : slot.PhaseIncrement;
        var phase = (ushort)(slot.RegPhaseGeneratorAccumulator >> 9);
        if (slot.RegPhaseResetRequest != 0)
        {
            slot.RegPhaseGeneratorAccumulator = 0;
        }

        slot.RegPhaseGeneratorAccumulator = unchecked(slot.RegPhaseGeneratorAccumulator + phaseIncrement);
        slot.PhaseGeneratorOutput = phase;

        switch (slot.SlotIndex)
        {
            /* hh */
            case 13:
                chip.RhythmHihatBit2 = (byte)((phase >> 2) & 1);
                chip.RhythmHihatBit3 = (byte)((phase >> 3) & 1);
                chip.RhythmHihatBit7 = (byte)((phase >> 7) & 1);
                chip.RhythmHihatBit8 = (byte)((phase >> 8) & 1);
                if ((chip.Rhythm & 0x20) != 0)
                {
                    var rmXor = (byte)((chip.RhythmHihatBit2 ^ chip.RhythmHihatBit7) |
                                       (chip.RhythmHihatBit3 ^ chip.RhythmTomBit5) |
                                       (chip.RhythmTomBit3 ^ chip.RhythmTomBit5));
                    slot.PhaseGeneratorOutput = (ushort)(rmXor << 9);
                    if ((rmXor ^ chip.NoiseHihat) != 0)
                    {
                        slot.PhaseGeneratorOutput = unchecked((ushort)(slot.PhaseGeneratorOutput | 0xd0));
                    }
                    else
                    {
                        slot.PhaseGeneratorOutput = unchecked((ushort)(slot.PhaseGeneratorOutput | 0x34));
                    }
                }

                break;
            case 16: /* sd */
                if ((chip.Rhythm & 0x20) != 0)
                {
                    slot.PhaseGeneratorOutput = (ushort)(((uint)(chip.RhythmHihatBit8 & 0x01) << 9) |
                                                         (((uint)chip.RhythmHihatBit8 ^ chip.NoiseSnare) << 8));
                }

                break;
            case 17: /* tc */
                if ((chip.Rhythm & 0x20) != 0)
                {
                    chip.RhythmTomBit3 = (byte)((phase >> 3) & 1);
                    chip.RhythmTomBit5 = (byte)((phase >> 5) & 1);
                    var rmXor = (byte)((chip.RhythmHihatBit2 ^ chip.RhythmHihatBit7) |
                                       (chip.RhythmHihatBit3 ^ chip.RhythmTomBit5) |
                                       (chip.RhythmTomBit3 ^ chip.RhythmTomBit5));
                    slot.PhaseGeneratorOutput = unchecked((ushort)((rmXor << 9) | 0x80));
                }

                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceNoise()
    {
        var state = Noise;
        var feedback0To8 = (state ^ (state >> 14)) & 0x1ffu;
        var feedback9To17 = ((state >> 9) ^ feedback0To8) & 0x1ffu;
        var feedback18To22 = ((state >> 18) ^ feedback9To17) & 0x1fu;
        var feedback23To31 = feedback0To8 ^ ((feedback9To17 >> 5) | (feedback18To22 << 4));
        var feedback32To35 = (feedback9To17 ^ feedback23To31) & 0x0fu;
        NoiseHihat = (state >> 13) & 1u;
        NoiseSnare = (state >> 16) & 1u;
        Noise = ((feedback9To17 >> 4) & 0x1fu) | (feedback18To22 << 5) | (feedback23To31 << 10) |
                (feedback32To35 << 19);
    }

    /* Original C: static void OPL3_SlotWrite20(opl3_slot *slot, uint8_t data) */
    private static void SlotWrite20(Opl3Operator slot, byte data)
    {
        slot.TremoloEnabled = ((data >> 7) & 0x01) != 0;
        slot.RegVibrato = (byte)((data >> 6) & 0x01);
        slot.RegOperatorType = (byte)((data >> 5) & 0x01);
        slot.EnvelopeRates[(byte)EnvelopeGeneratorStage.Sustain] =
            slot.RegOperatorType != 0 ? (byte)0 : slot.RegReleaseRate;
        slot.RegKeyScaleRate = (byte)((data >> 4) & 0x01);
        slot.RegFrequencyMultiplier = (byte)(data & 0x0f);
        Opl3Envelope.UpdateRates(slot);
        UpdatePhaseIncrement(slot);
    }

    /* Original C: static void OPL3_SlotWrite40(opl3_slot *slot, uint8_t data) */
    private static void SlotWrite40(Opl3Operator slot, byte data)
    {
        slot.RegKeyScaleLevel = (byte)((data >> 6) & 0x03);
        slot.RegTotalLevel = (byte)(data & 0x3f);
        Opl3Envelope.UpdateKeyScaleLevel(slot);
    }

    /* Original C: static void OPL3_SlotWrite60(opl3_slot *slot, uint8_t data) */
    private static void SlotWrite60(Opl3Operator slot, byte data)
    {
        slot.RegAttackRate = (byte)((data >> 4) & 0x0f);
        slot.RegDecayRate = (byte)(data & 0x0f);
        slot.EnvelopeRates[(byte)EnvelopeGeneratorStage.Attack] = slot.RegAttackRate;
        slot.EnvelopeRates[(byte)EnvelopeGeneratorStage.Decay] = slot.RegDecayRate;
        Opl3Envelope.UpdateRates(slot);
    }

    /* Original C: static void OPL3_SlotWrite80(opl3_slot *slot, uint8_t data) */
    private static void SlotWrite80(Opl3Operator slot, byte data)
    {
        slot.RegSustainLevel = (byte)((data >> 4) & 0x0f);
        if (slot.RegSustainLevel == 0x0f)
        {
            slot.RegSustainLevel = 0x1f;
        }

        slot.RegReleaseRate = (byte)(data & 0x0f);
        slot.EnvelopeRates[(byte)EnvelopeGeneratorStage.Sustain] =
            slot.RegOperatorType != 0 ? (byte)0 : slot.RegReleaseRate;
        slot.EnvelopeRates[(byte)EnvelopeGeneratorStage.Release] = slot.RegReleaseRate;
        Opl3Envelope.UpdateRates(slot);
    }

    /* Original C: static void OPL3_SlotWriteE0(opl3_slot *slot, uint8_t data) */
    private static void SlotWriteE0(Opl3Operator slot, byte data)
    {
        slot.RegWaveformSelect = (byte)(data & 0x07);
        if (slot.Chip?.NewM == 0)
        {
            slot.RegWaveformSelect &= 0x03;
        }
    }

    /* Original C: static void OPL3_SlotGenerate(opl3_slot *slot) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SlotGenerate(Opl3Operator slot)
    {
        slot.Out = Opl3Envelope.GenerateWaveform(slot);
    }

    /* Original C: static void OPL3_SlotCalcFB(opl3_slot *slot) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SlotCalcFeedback(Opl3Operator slot, byte feedback)
    {
        if (feedback != 0)
        {
            slot.FeedbackModifiedSignal = (short)((slot.PreviousOutputSample + slot.Out) >> (0x09 - feedback));
        }
        else
        {
            slot.FeedbackModifiedSignal = 0;
        }

        slot.PreviousOutputSample = slot.Out;
    }

    /* Original C: static void OPL3_ChannelSetupAlg(opl3_channel *channel) */
    private static void ChannelSetupAlgorithmBody(Opl3Channel channel)
    {
        if (channel.ChannelType == ChannelType.Drum)
        {
            if (channel.ChannelNumber is 7 or 8)
            {
                channel.Slotz[0].ModulationSource = ShortSignalSource.Zero;
                channel.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                return;
            }

            switch (channel.Algorithm & 0x01)
            {
                case 0x00:
                    channel.Slotz[0].ModulationSource = channel.Slotz[0].FeedbackSignal;
                    channel.Slotz[1].ModulationSource = channel.Slotz[0].OutputSignal;
                    break;
                case 0x01:
                    channel.Slotz[0].ModulationSource = channel.Slotz[0].FeedbackSignal;
                    channel.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                    break;
            }

            return;
        }

        if ((channel.Algorithm & 0x08) != 0)
        {
            return;
        }

        if ((channel.Algorithm & 0x04) != 0)
        {
            var pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
            pair.Out[0] = ShortSignalSource.Zero;
            pair.Out[1] = ShortSignalSource.Zero;
            pair.Out[2] = ShortSignalSource.Zero;
            pair.Out[3] = ShortSignalSource.Zero;
            pair.OutputCount = 0;

            switch (channel.Algorithm & 0x03)
            {
                case 0x00:
                    pair.Slotz[0].ModulationSource = pair.Slotz[0].FeedbackSignal;
                    pair.Slotz[1].ModulationSource = pair.Slotz[0].OutputSignal;
                    channel.Slotz[0].ModulationSource = pair.Slotz[1].OutputSignal;
                    channel.Slotz[1].ModulationSource = channel.Slotz[0].OutputSignal;
                    channel.Out[0] = channel.Slotz[1].OutputSignal;
                    channel.Out[1] = ShortSignalSource.Zero;
                    channel.Out[2] = ShortSignalSource.Zero;
                    channel.Out[3] = ShortSignalSource.Zero;
                    channel.OutputCount = 1;
                    break;
                case 0x01:
                    pair.Slotz[0].ModulationSource = pair.Slotz[0].FeedbackSignal;
                    pair.Slotz[1].ModulationSource = pair.Slotz[0].OutputSignal;
                    channel.Slotz[0].ModulationSource = ShortSignalSource.Zero;
                    channel.Slotz[1].ModulationSource = channel.Slotz[0].OutputSignal;
                    channel.Out[0] = pair.Slotz[1].OutputSignal;
                    channel.Out[1] = channel.Slotz[1].OutputSignal;
                    channel.Out[2] = ShortSignalSource.Zero;
                    channel.Out[3] = ShortSignalSource.Zero;
                    channel.OutputCount = 2;
                    break;
                case 0x02:
                    pair.Slotz[0].ModulationSource = pair.Slotz[0].FeedbackSignal;
                    pair.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                    channel.Slotz[0].ModulationSource = pair.Slotz[1].OutputSignal;
                    channel.Slotz[1].ModulationSource = channel.Slotz[0].OutputSignal;
                    channel.Out[0] = pair.Slotz[0].OutputSignal;
                    channel.Out[1] = channel.Slotz[1].OutputSignal;
                    channel.Out[2] = ShortSignalSource.Zero;
                    channel.Out[3] = ShortSignalSource.Zero;
                    channel.OutputCount = 2;
                    break;
                case 0x03:
                    pair.Slotz[0].ModulationSource = pair.Slotz[0].FeedbackSignal;
                    pair.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                    channel.Slotz[0].ModulationSource = pair.Slotz[1].OutputSignal;
                    channel.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                    channel.Out[0] = pair.Slotz[0].OutputSignal;
                    channel.Out[1] = channel.Slotz[0].OutputSignal;
                    channel.Out[2] = channel.Slotz[1].OutputSignal;
                    channel.Out[3] = ShortSignalSource.Zero;
                    channel.OutputCount = 3;
                    break;
            }
        }
        else
        {
            switch (channel.Algorithm & 0x01)
            {
                case 0x00:
                    channel.Slotz[0].ModulationSource = channel.Slotz[0].FeedbackSignal;
                    channel.Slotz[1].ModulationSource = channel.Slotz[0].OutputSignal;
                    channel.Out[0] = channel.Slotz[1].OutputSignal;
                    channel.Out[1] = ShortSignalSource.Zero;
                    channel.Out[2] = ShortSignalSource.Zero;
                    channel.Out[3] = ShortSignalSource.Zero;
                    channel.OutputCount = 1;
                    break;
                case 0x01:
                    channel.Slotz[0].ModulationSource = channel.Slotz[0].FeedbackSignal;
                    channel.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                    channel.Out[0] = channel.Slotz[0].OutputSignal;
                    channel.Out[1] = channel.Slotz[1].OutputSignal;
                    channel.Out[2] = ShortSignalSource.Zero;
                    channel.Out[3] = ShortSignalSource.Zero;
                    channel.OutputCount = 2;
                    break;
            }
        }
    }

    private static void ChannelSetupAlgorithm(Opl3Channel channel)
    {
        ChannelSetupAlgorithmBody(channel);
        UpdateDelayedOutputs(channel);
        if (channel.Pair is { } pair)
        {
            UpdateDelayedOutputs(pair);
        }

        var chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");
        chip.MixListsDirty = true;
    }

    private static void UpdateDelayedOutputs(Opl3Channel channel)
    {
        for (var output = 0; output < channel.Out.Length; output++)
        {
#if OPL_ENABLE_STEREOEXT
            channel.LeftOutputs[output] = channel.Out[output];
            channel.RightOutputs[output] = channel.Out[output];
#else
            channel.LeftOutputs[output] = channel.Out[output].DelayOutputFrom(15);
            channel.RightOutputs[output] = channel.Out[output].DelayOutputFrom(33);
#endif
        }
    }

    /* Original C: static void OPL3_ChannelUpdateRhythm(opl3_chip *chip, uint8_t data) */
    private static void ChannelUpdateRhythm(Opl3Chip chip, byte data)
    {
        chip.Rhythm = (byte)(data & 0x3f);

        if ((chip.Rhythm & 0x20) != 0)
        {
            var channel6 = chip.Channels[6];
            var channel7 = chip.Channels[7];
            var channel8 = chip.Channels[8];

            channel6.Out[0] = channel6.Slotz[1].OutputSignal;
            channel6.Out[1] = channel6.Slotz[1].OutputSignal;
            channel6.Out[2] = ShortSignalSource.Zero;
            channel6.Out[3] = ShortSignalSource.Zero;
            channel6.OutputCount = 2;

            channel7.Out[0] = channel7.Slotz[0].OutputSignal;
            channel7.Out[1] = channel7.Slotz[0].OutputSignal;
            channel7.Out[2] = channel7.Slotz[1].OutputSignal;
            channel7.Out[3] = channel7.Slotz[1].OutputSignal;
            channel7.OutputCount = 4;

            channel8.Out[0] = channel8.Slotz[0].OutputSignal;
            channel8.Out[1] = channel8.Slotz[0].OutputSignal;
            channel8.Out[2] = channel8.Slotz[1].OutputSignal;
            channel8.Out[3] = channel8.Slotz[1].OutputSignal;
            channel8.OutputCount = 4;

            for (var ch = 6; ch < 9; ch++)
            {
                chip.Channels[ch].ChannelType = ChannelType.Drum;
            }

            ChannelSetupAlgorithm(channel6);
            ChannelSetupAlgorithm(channel7);
            ChannelSetupAlgorithm(channel8);

            if ((chip.Rhythm & 0x01) != 0) /* hh */
            {
                Opl3Envelope.KeyOn(channel7.Slotz[0], EnvelopeKeyType.Drum);
            }
            else
            {
                Opl3Envelope.KeyOff(channel7.Slotz[0], EnvelopeKeyType.Drum);
            }

            if ((chip.Rhythm & 0x02) != 0) /* tc */
            {
                Opl3Envelope.KeyOn(channel8.Slotz[1], EnvelopeKeyType.Drum);
            }
            else
            {
                Opl3Envelope.KeyOff(channel8.Slotz[1], EnvelopeKeyType.Drum);
            }

            if ((chip.Rhythm & 0x04) != 0) /* tom */
            {
                Opl3Envelope.KeyOn(channel8.Slotz[0], EnvelopeKeyType.Drum);
            }
            else
            {
                Opl3Envelope.KeyOff(channel8.Slotz[0], EnvelopeKeyType.Drum);
            }

            if ((chip.Rhythm & 0x08) != 0) /* sd */
            {
                Opl3Envelope.KeyOn(channel7.Slotz[1], EnvelopeKeyType.Drum);
            }
            else
            {
                Opl3Envelope.KeyOff(channel7.Slotz[1], EnvelopeKeyType.Drum);
            }

            if ((chip.Rhythm & 0x10) != 0) /* bd */
            {
                Opl3Envelope.KeyOn(channel6.Slotz[0], EnvelopeKeyType.Drum);
                Opl3Envelope.KeyOn(channel6.Slotz[1], EnvelopeKeyType.Drum);
            }
            else
            {
                Opl3Envelope.KeyOff(channel6.Slotz[0], EnvelopeKeyType.Drum);
                Opl3Envelope.KeyOff(channel6.Slotz[1], EnvelopeKeyType.Drum);
            }
        }
        else
        {
            for (var ch = 6; ch < 9; ch++)
            {
                var channel = chip.Channels[ch];
                channel.ChannelType = ChannelType.TwoOp;
                ChannelSetupAlgorithm(channel);
                Opl3Envelope.KeyOff(channel.Slotz[0], EnvelopeKeyType.Drum);
                Opl3Envelope.KeyOff(channel.Slotz[1], EnvelopeKeyType.Drum);
            }
        }
    }

    /* Original C: static void OPL3_ChannelWriteA0(opl3_channel *channel, uint8_t data) */
    private static void ChannelWriteA0(Opl3Channel channel, byte data)
    {
        var chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");
        if (chip.NewM != 0 && channel.ChannelType == ChannelType.FourOpPair)
        {
            return;
        }

        channel.FNumber = (ushort)((channel.FNumber & 0x300) | data);
        channel.KeyScaleValue = (byte)((channel.Block << 1) | ((channel.FNumber >> (0x09 - chip.Nts)) & 0x01));
        Opl3Envelope.UpdateKeyScaleLevel(channel.Slotz[0]);
        Opl3Envelope.UpdateKeyScaleLevel(channel.Slotz[1]);
        Opl3Envelope.UpdateRates(channel.Slotz[0]);
        Opl3Envelope.UpdateRates(channel.Slotz[1]);
        UpdatePhaseIncrement(channel.Slotz[0]);
        UpdatePhaseIncrement(channel.Slotz[1]);

        if (chip.NewM == 0 || channel.ChannelType != ChannelType.FourOp)
        {
            return;
        }

        var pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
        pair.FNumber = channel.FNumber;
        pair.KeyScaleValue = channel.KeyScaleValue;
        Opl3Envelope.UpdateKeyScaleLevel(pair.Slotz[0]);
        Opl3Envelope.UpdateKeyScaleLevel(pair.Slotz[1]);
        Opl3Envelope.UpdateRates(pair.Slotz[0]);
        Opl3Envelope.UpdateRates(pair.Slotz[1]);
        UpdatePhaseIncrement(pair.Slotz[0]);
        UpdatePhaseIncrement(pair.Slotz[1]);
    }

    /* Original C: static void OPL3_ChannelWriteB0(opl3_channel *channel, uint8_t data) */
    private static void ChannelWriteB0(Opl3Channel channel, byte data)
    {
        var chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");
        if (chip.NewM != 0 && channel.ChannelType == ChannelType.FourOpPair)
        {
            return;
        }

        channel.FNumber = (ushort)((channel.FNumber & 0xff) | ((data & 0x03) << 8));
        channel.Block = (byte)((data >> 2) & 0x07);
        channel.KeyScaleValue = (byte)((channel.Block << 1) | ((channel.FNumber >> (0x09 - chip.Nts)) & 0x01));
        Opl3Envelope.UpdateKeyScaleLevel(channel.Slotz[0]);
        Opl3Envelope.UpdateKeyScaleLevel(channel.Slotz[1]);
        Opl3Envelope.UpdateRates(channel.Slotz[0]);
        Opl3Envelope.UpdateRates(channel.Slotz[1]);
        UpdatePhaseIncrement(channel.Slotz[0]);
        UpdatePhaseIncrement(channel.Slotz[1]);

        if (chip.NewM == 0 || channel.ChannelType != ChannelType.FourOp)
        {
            return;
        }

        var pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
        pair.FNumber = channel.FNumber;
        pair.Block = channel.Block;
        pair.KeyScaleValue = channel.KeyScaleValue;
        Opl3Envelope.UpdateKeyScaleLevel(pair.Slotz[0]);
        Opl3Envelope.UpdateKeyScaleLevel(pair.Slotz[1]);
        Opl3Envelope.UpdateRates(pair.Slotz[0]);
        Opl3Envelope.UpdateRates(pair.Slotz[1]);
        UpdatePhaseIncrement(pair.Slotz[0]);
        UpdatePhaseIncrement(pair.Slotz[1]);
    }

    /* Original C: static void OPL3_ChannelUpdateAlg(opl3_channel *channel) */
    private static void ChannelUpdateAlgorithm(Opl3Channel channel)
    {
        channel.Algorithm = channel.Connection;
        var chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");

        if (chip.NewM != 0)
        {
            switch (channel.ChannelType)
            {
                case ChannelType.FourOp:
                {
                    var pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
                    pair.Algorithm = (byte)(0x04 | (channel.Connection << 1) | pair.Connection);
                    channel.Algorithm = 0x08;
                    ChannelSetupAlgorithm(pair);
                    break;
                }
                case ChannelType.FourOpPair:
                {
                    var primary = channel.Pair ?? throw new InvalidOperationException("Missing 4-op primary.");
                    channel.Algorithm = (byte)(0x04 | (primary.Connection << 1) | channel.Connection);
                    primary.Algorithm = 0x08;
                    ChannelSetupAlgorithm(channel);
                    break;
                }
                default:
                    ChannelSetupAlgorithm(channel);
                    break;
            }
        }
        else
        {
            ChannelSetupAlgorithm(channel);
        }
    }

    /* Original C: static void OPL3_ChannelWriteC0(opl3_channel *channel, uint8_t data) */
    private static void ChannelWriteC0(Opl3Channel channel, byte data)
    {
        channel.Feedback = (byte)((data & 0x0e) >> 1);
        channel.Connection = (byte)(data & 0x01);
        ChannelUpdateAlgorithm(channel);

        if (channel.Chip?.NewM != 0)
        {
            channel.Cha = (ushort)(((data >> 4) & 0x01) != 0 ? 0xffff : 0);
            channel.Chb = (ushort)(((data >> 5) & 0x01) != 0 ? 0xffff : 0);
            channel.Chc = (ushort)(((data >> 6) & 0x01) != 0 ? 0xffff : 0);
            channel.Chd = (ushort)(((data >> 7) & 0x01) != 0 ? 0xffff : 0);
        }
        else
        {
            channel.Cha = 0xffff;
            channel.Chb = 0xffff;
            channel.Chc = 0;
            channel.Chd = 0;
        }
#if OPL_ENABLE_STEREOEXT
        if (channel.Chip is { StereoExtension: 0 }) {
            channel.LeftPan = channel.Cha << 16;
            channel.RightPan = channel.Chb << 16;
        }
#endif
    }

#if OPL_ENABLE_STEREOEXT
    /* Original C: static void OPL3_ChannelWriteD0(opl3_channel *channel, uint8_t data) */
    private static void ChannelWriteD0(Opl3Channel channel, byte data) {
        Opl3Chip chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");

        if (chip.StereoExtension == 0) {
            return;
        }

        ReadOnlySpan<int> panPot = Opl3Tables.StereoPanPotLut;
        int leftIndex = data ^ 0xff;
        channel.LeftPan = panPot[leftIndex];
        channel.RightPan = panPot[data];
        chip.MixListsDirty = true;
    }
#endif

    /* Original C: static void OPL3_ChannelKeyOn(opl3_channel *channel) */
    private static void ChannelKeyOn(Opl3Channel channel)
    {
        var chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");

        if (chip.NewM != 0)
        {
            switch (channel.ChannelType)
            {
                case ChannelType.FourOp:
                {
                    var pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
                    Opl3Envelope.KeyOn(channel.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.KeyOn(channel.Slotz[1], EnvelopeKeyType.Normal);
                    Opl3Envelope.KeyOn(pair.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.KeyOn(pair.Slotz[1], EnvelopeKeyType.Normal);
                    break;
                }
                case ChannelType.TwoOp or ChannelType.Drum:
                    Opl3Envelope.KeyOn(channel.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.KeyOn(channel.Slotz[1], EnvelopeKeyType.Normal);
                    break;
            }
        }
        else
        {
            Opl3Envelope.KeyOn(channel.Slotz[0], EnvelopeKeyType.Normal);
            Opl3Envelope.KeyOn(channel.Slotz[1], EnvelopeKeyType.Normal);
        }
    }

    /* Original C: static void OPL3_ChannelKeyOff(opl3_channel *channel) */
    private static void ChannelKeyOff(Opl3Channel channel)
    {
        var chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");

        if (chip.NewM != 0)
        {
            switch (channel.ChannelType)
            {
                case ChannelType.FourOp:
                {
                    var pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
                    Opl3Envelope.KeyOff(channel.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.KeyOff(channel.Slotz[1], EnvelopeKeyType.Normal);
                    Opl3Envelope.KeyOff(pair.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.KeyOff(pair.Slotz[1], EnvelopeKeyType.Normal);
                    break;
                }
                case ChannelType.TwoOp:
                case ChannelType.Drum:
                    Opl3Envelope.KeyOff(channel.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.KeyOff(channel.Slotz[1], EnvelopeKeyType.Normal);
                    break;
            }
        }
        else
        {
            Opl3Envelope.KeyOff(channel.Slotz[0], EnvelopeKeyType.Normal);
            Opl3Envelope.KeyOff(channel.Slotz[1], EnvelopeKeyType.Normal);
        }
    }

    /* Original C: static void OPL3_ChannelSet4Op(opl3_chip *chip, uint8_t data) */
    private static void ChannelSet4Op(Opl3Chip chip, byte data)
    {
        for (byte bit = 0; bit < 6; bit++)
        {
            var chNum = bit;
            if (bit >= 3)
            {
                chNum = (byte)(bit + 6);
            }

            if (((data >> bit) & 0x01) != 0)
            {
                chip.Channels[chNum].ChannelType = ChannelType.FourOp;
                chip.Channels[chNum + 3].ChannelType = ChannelType.FourOpPair;
                ChannelUpdateAlgorithm(chip.Channels[chNum]);
            }
            else
            {
                chip.Channels[chNum].ChannelType = ChannelType.TwoOp;
                chip.Channels[chNum + 3].ChannelType = ChannelType.TwoOp;
                ChannelUpdateAlgorithm(chip.Channels[chNum]);
                ChannelUpdateAlgorithm(chip.Channels[chNum + 3]);
            }
        }
    }

    /* Original C: static int16_t OPL3_ClipSample(int32_t sample) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short ClipSample(int sample)
    {
        return sample switch
        {
            > short.MaxValue => short.MaxValue,
            < short.MinValue => short.MinValue,
            _ => (short)sample
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessSlot(Opl3Operator slot, byte feedback, bool maybeRhythm)
    {
        var rhythmSlot = maybeRhythm && slot.SlotIndex is 13 or 16 or 17;
        if (slot.RegKeyState == 0 && slot.EnvelopeGeneratorOutput == 0x1ff && !rhythmSlot)
        {
            var chip = slot.Chip ?? throw new InvalidOperationException("Slot chip not assigned.");
            if (feedback == 0 && slot.PhaseIncrement == 0 && slot.Out == 0 && slot.ModulationSource.Read() == 0 &&
                slot.CachedEnvelopeAttenuation == 0 && (!slot.TremoloEnabled || chip.Tremolo == 0) &&
                slot.RegPhaseGeneratorAccumulator == 0 && slot.RegVibrato == 0 && slot.RegWaveformSelect == 0)
            {
                slot.FeedbackModifiedSignal = 0;
                slot.PreviousOutputSample = 0;
                slot.EnvelopeGeneratorLevel = 0x1ff;
                slot.RegPhaseResetRequest = 0;
                slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Release;
                slot.PhaseGeneratorOutput = 0;
                return;
            }

            SlotCalcFeedback(slot, feedback);
            slot.EnvelopeGeneratorLevel = (ushort)(slot.EnvelopeGeneratorOutput + slot.CachedEnvelopeAttenuation +
                                                   (slot.TremoloEnabled ? chip.Tremolo : 0));
            slot.RegPhaseResetRequest = 0;
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Release;

            var phaseIncrement = slot.RegVibrato != 0 ? slot.CurrentVibratoPhaseIncrement : slot.PhaseIncrement;
            var phase = (ushort)(slot.RegPhaseGeneratorAccumulator >> 9);
            slot.RegPhaseGeneratorAccumulator = unchecked(slot.RegPhaseGeneratorAccumulator + phaseIncrement);
            slot.PhaseGeneratorOutput = phase;
            slot.Out = Opl3Envelope.GenerateSilentWaveform(slot);
            return;
        }

        if (slot.EnvelopeGeneratorState == (byte)EnvelopeGeneratorStage.Sustain && slot.RegKeyState != 0 &&
            slot.EnvelopeRates[(byte)EnvelopeGeneratorStage.Sustain] == 0)
        {
            var chip = slot.Chip ?? throw new InvalidOperationException("Slot chip not assigned.");
            SlotCalcFeedback(slot, feedback);
            slot.EnvelopeGeneratorLevel = (ushort)(slot.EnvelopeGeneratorOutput + slot.CachedEnvelopeAttenuation +
                                                   (slot.TremoloEnabled ? chip.Tremolo : 0));
            slot.RegPhaseResetRequest = 0;
            if ((slot.EnvelopeGeneratorOutput & 0x1f8) == 0x1f8)
            {
                slot.EnvelopeGeneratorOutput = 0x1ff;
            }

            if (slot.RegVibrato == 0 && !rhythmSlot)
            {
                var phase = (ushort)(slot.RegPhaseGeneratorAccumulator >> 9);
                slot.RegPhaseGeneratorAccumulator = unchecked(slot.RegPhaseGeneratorAccumulator + slot.PhaseIncrement);
                slot.PhaseGeneratorOutput = phase;
            }
            else if (maybeRhythm)
            {
                PhaseGenerateRhythm(slot);
            }
            else
            {
                PhaseGenerateNormal(slot);
            }

            SlotGenerate(slot);
            return;
        }

        SlotCalcFeedback(slot, feedback);
        Opl3Envelope.Calculate(slot);
        if (maybeRhythm)
        {
            PhaseGenerateRhythm(slot);
        }
        else
        {
            PhaseGenerateNormal(slot);
        }

        SlotGenerate(slot);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessSlotIfActive(Opl3Operator slot, byte feedback, bool maybeRhythm, uint writeGeneration)
    {
        if (slot.DormantGeneration == writeGeneration)
        {
            return;
        }

        var rhythmSlot = maybeRhythm && slot.SlotIndex is 13 or 16 or 17;
        if (slot.RegKeyState == 0 && slot.EnvelopeGeneratorOutput == 0x1ff &&
            slot.EnvelopeGeneratorState == (byte)EnvelopeGeneratorStage.Release && !rhythmSlot && feedback == 0 &&
            slot.PhaseIncrement == 0 && slot.Out == 0 && slot.PreviousOutputSample == 0 &&
            slot.ModulationSource.Read() == 0 && slot.CachedEnvelopeAttenuation == 0 && !slot.TremoloEnabled &&
            slot.RegPhaseGeneratorAccumulator == 0 && slot.RegVibrato == 0 && slot.RegWaveformSelect == 0)
        {
            if (slot.ModulationSource.CanRemainZero(slot, writeGeneration))
            {
                slot.DormantGeneration = writeGeneration;
                var channel = slot.Channel ?? throw new InvalidOperationException("Slot channel not assigned.");
                if (channel.Slotz[0].DormantGeneration == writeGeneration &&
                    channel.Slotz[1].DormantGeneration == writeGeneration)
                {
                    var chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");
                    chip.ActiveChannelMask &= ~(1u << channel.ChannelNumber);
                    chip.MixListsDirty = true;
                }
            }

            return;
        }

        ProcessSlot(slot, feedback, maybeRhythm);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessChannelSlots(Opl3Channel channel, bool maybeRhythm, uint writeGeneration)
    {
        var feedback = channel.Feedback;
        ProcessSlotIfActive(channel.Slotz[0], feedback, maybeRhythm, writeGeneration);
        ProcessSlotIfActive(channel.Slotz[1], feedback, maybeRhythm, writeGeneration);
    }

#if NET10_0_OR_GREATER
    private void RefreshInactiveRhythmPhaseBits()
    {
        var hihatPhase = Slots[13].PhaseGeneratorOutput;
        RhythmHihatBit2 = (byte)((hihatPhase >> 2) & 1);
        RhythmHihatBit3 = (byte)((hihatPhase >> 3) & 1);
        RhythmHihatBit7 = (byte)((hihatPhase >> 7) & 1);
        RhythmHihatBit8 = (byte)((hihatPhase >> 8) & 1);
    }
#endif

#if NET10_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SumMixEntry(ref MixEntry entry, bool right)
    {
        var sum = right ? entry.RightOutput0.Read() : entry.LeftOutput0.Read();
        if (entry.OutputCount > 1)
        {
            sum += right ? entry.RightOutput1.Read() : entry.LeftOutput1.Read();
            if (entry.OutputCount > 2)
            {
                sum += right ? entry.RightOutput2.Read() : entry.LeftOutput2.Read();
                if (entry.OutputCount > 3)
                {
                    sum += right ? entry.RightOutput3.Read() : entry.LeftOutput3.Read();
                }
            }
        }

        return sum;
    }
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SumChannelOutputs(ShortSignalSource[] outputs, byte outputCount)
    {
        var sum = (int)outputs[0].Read();
        if (outputCount > 1)
        {
            sum += outputs[1].Read();
            if (outputCount > 2)
            {
                sum += outputs[2].Read();
                if (outputCount > 3)
                {
                    sum += outputs[3].Read();
                }
            }
        }

        return sum;
    }
#endif

    private void RebuildMixLists()
    {
#if NET10_0_OR_GREATER
        byte count = 0;
#else
        byte leftCount = 0;
        byte rightCount = 0;
#endif
        foreach (var channel in Channels)
        {
            if (channel.OutputCount == 0)
            {
                continue;
            }

            var channelBit = 1u << channel.ChannelNumber;
            if ((ActiveChannelMask & channelBit) == 0 && ((channel.Algorithm & 0x04) == 0 ||
                                                          channel.Pair is not { } pair ||
                                                          (ActiveChannelMask & (1u << pair.ChannelNumber)) == 0))
            {
                continue;
            }

#if NET10_0_OR_GREATER
#if OPL_ENABLE_STEREOEXT
            var leftEnabled = (channel.LeftPan | channel.Chc) != 0;
            var rightEnabled = (channel.RightPan | channel.Chd) != 0;
#else
            var leftEnabled = (channel.Cha | channel.Chc) != 0;
            var rightEnabled = (channel.Chb | channel.Chd) != 0;
#endif
            if (!leftEnabled && !rightEnabled)
            {
                continue;
            }

            ref var entry = ref _mixEntries[count++];
            entry.LeftOutput0 = channel.LeftOutputs[0];
            entry.LeftOutput1 = channel.LeftOutputs[1];
            entry.LeftOutput2 = channel.LeftOutputs[2];
            entry.LeftOutput3 = channel.LeftOutputs[3];
            entry.RightOutput0 = channel.RightOutputs[0];
            entry.RightOutput1 = channel.RightOutputs[1];
            entry.RightOutput2 = channel.RightOutputs[2];
            entry.RightOutput3 = channel.RightOutputs[3];
            entry.Channel = channel;
            entry.OutputCount = channel.OutputCount;
            var sharedOutputs = leftEnabled && rightEnabled
                                && entry.LeftOutput0.ReadsSameSignalAs(entry.RightOutput0)
                                && (entry.OutputCount < 2 || entry.LeftOutput1.ReadsSameSignalAs(entry.RightOutput1))
                                && (entry.OutputCount < 3 || entry.LeftOutput2.ReadsSameSignalAs(entry.RightOutput2))
                                && (entry.OutputCount < 4 || entry.LeftOutput3.ReadsSameSignalAs(entry.RightOutput3));
#if OPL_ENABLE_STEREOEXT
            const bool allMixOutputsEnabled = false;
#else
            var allMixOutputsEnabled = channel.Cha == ushort.MaxValue && channel.Chb == ushort.MaxValue
                                       && channel.Chc == ushort.MaxValue && channel.Chd == ushort.MaxValue;
#endif
            entry.Routes = (byte)((leftEnabled ? LeftMixEnabled : 0) | (rightEnabled ? RightMixEnabled : 0)
                                  | (sharedOutputs ? SharedMixOutputs : 0)
                                  | (allMixOutputsEnabled ? AllMixOutputsEnabled : 0));
#else
#if OPL_ENABLE_STEREOEXT
            if ((channel.LeftPan | channel.Chc) != 0)
#else
            if ((channel.Cha | channel.Chc) != 0)
#endif
            {
                LeftMixChannels[leftCount++] = channel;
            }

#if OPL_ENABLE_STEREOEXT
            if ((channel.RightPan | channel.Chd) != 0)
#else
            if ((channel.Chb | channel.Chd) != 0)
#endif
            {
                RightMixChannels[rightCount++] = channel;
            }
#endif
        }

#if NET10_0_OR_GREATER
        _mixEntryCount = count;
#else
        LeftMixChannelCount = leftCount;
        RightMixChannelCount = rightCount;
#endif
        MixListsDirty = false;
    }

#if NET10_0_OR_GREATER
    private void MixOutputBuses()
    {
        var mix0 = 0;
        var mix1 = 0;
        var mix2 = 0;
        var mix3 = 0;
        var allBusLeftMix = 0;
        var allBusRightMix = 0;
        for (var index = 0; index < _mixEntryCount; index++)
        {
            ref var entry = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_mixEntries), index);
            var channel = entry.Channel;
            var routes = entry.Routes;
            if ((routes & AllMixOutputsEnabled) != 0)
            {
                var allBusLeftSample = (int)(short)SumMixEntry(ref entry, false);
                allBusLeftMix = unchecked(allBusLeftMix + allBusLeftSample);
                if ((routes & SharedMixOutputs) != 0)
                {
                    allBusRightMix = unchecked(allBusRightMix + allBusLeftSample);
                }
                else
                {
                    var allBusRightSample = (int)(short)SumMixEntry(ref entry, true);
                    allBusRightMix = unchecked(allBusRightMix + allBusRightSample);
                }

                continue;
            }

            var leftSample = 0;
            if ((routes & LeftMixEnabled) != 0)
            {
                leftSample = SumMixEntry(ref entry, false);
#if OPL_ENABLE_STEREOEXT
                mix0 = unchecked(mix0 + (short)(((long)(short)leftSample * channel.LeftPan) >> 16));
#else
                mix0 = unchecked(mix0 + (short)(leftSample & channel.Cha));
#endif
                mix2 = unchecked(mix2 + (short)(leftSample & channel.Chc));
            }

            if ((routes & RightMixEnabled) != 0)
            {
                var rightSample = (routes & SharedMixOutputs) != 0
                    ? leftSample
                    : SumMixEntry(ref entry, true);
#if OPL_ENABLE_STEREOEXT
                mix1 = unchecked(mix1 + (short)(((long)(short)rightSample * channel.RightPan) >> 16));
#else
                mix1 = unchecked(mix1 + (short)(rightSample & channel.Chb));
#endif
                mix3 = unchecked(mix3 + (short)(rightSample & channel.Chd));
            }
        }

        MixBuffer[0] = unchecked(mix0 + allBusLeftMix);
        MixBuffer[1] = unchecked(mix1 + allBusRightMix);
        MixBuffer[2] = unchecked(mix2 + allBusLeftMix);
        MixBuffer[3] = unchecked(mix3 + allBusRightMix);
    }
#else
    private void MixRight()
    {
        var front = 0;
        var rear = 0;
        for (var index = 0; index < RightMixChannelCount; index++)
        {
            var channel = RightMixChannels[index];
            var channelSample = SumChannelOutputs(channel.RightOutputs, channel.OutputCount);
#if OPL_ENABLE_STEREOEXT
            front = unchecked(front + (short)(((long)(short)channelSample * channel.RightPan) >> 16));
#else
            front = unchecked(front + (short)(channelSample & channel.Chb));
#endif
            rear = unchecked(rear + (short)(channelSample & channel.Chd));
        }

        MixBuffer[1] = front;
        MixBuffer[3] = rear;
    }
#endif

    /* Original C: void OPL3_Generate4Ch(opl3_chip *chip, int16_t *buf4) */
    private void Generate4ChCore(Span<short> buffer)
    {
#if NET10_0_OR_GREATER
        EnvelopeShiftTableOffset = Opl3Tables.GetEnvelopeShiftTableOffset(EgState, EgAdd, EgTimerLow);
#endif
        if (CachedVibratoPosition != VibratoPosition)
        {
            RefreshCurrentVibratoPhaseIncrements();
        }

        if (ActiveChannelMask == AllChannelsMask)
        {
            Generate4ChActiveCore(buffer);
        }
        else
        {
            Generate4ChSparseCore(buffer);
        }
    }

    private void RefreshCurrentVibratoPhaseIncrements()
    {
        var vibratoPosition = VibratoPosition;
        foreach (var slot in Slots)
        {
            slot.CurrentVibratoPhaseIncrement = slot.VibratoPhaseIncrements[vibratoPosition];
        }

        CachedVibratoPosition = vibratoPosition;
    }

    private void Generate4ChActiveCore(Span<short> buffer)
    {
        if (buffer.Length < 4)
        {
            throw new ArgumentException("Buffer must contain at least four samples.", nameof(buffer));
        }

        buffer[1] = ClipSample(MixBuffer[1]);
        buffer[3] = ClipSample(MixBuffer[3]);

        if (MixListsDirty)
        {
            RebuildMixLists();
        }

        AdvanceNoise();

        var writeGeneration = WriteGeneration;
#if NET10_0_OR_GREATER
        var rhythmActive = (Rhythm & 0x20) != 0;
#else
        const bool rhythmActive = true;
#endif
        for (var channelIndex = 0; channelIndex < 7; channelIndex++)
        {
            ProcessChannelSlots(Channels[channelIndex], false, writeGeneration);
        }

        ProcessChannelSlots(Channels[7], rhythmActive, writeGeneration);
        ProcessChannelSlots(Channels[8], rhythmActive, writeGeneration);
        for (var channelIndex = 9; channelIndex < Channels.Length; channelIndex++)
        {
            ProcessChannelSlots(Channels[channelIndex], false, writeGeneration);
        }
#if NET10_0_OR_GREATER
        if (!rhythmActive)
        {
            RefreshInactiveRhythmPhaseBits();
        }
#endif

#if NET10_0_OR_GREATER
        MixOutputBuses();
#else
        var mix0 = 0;
        var mix1 = 0;
        for (var index = 0; index < LeftMixChannelCount; index++)
        {
            var channel = LeftMixChannels[index];
            var channelSample = SumChannelOutputs(channel.LeftOutputs, channel.OutputCount);
#if OPL_ENABLE_STEREOEXT
            mix0 = unchecked(mix0 + (short)(((long)(short)channelSample * channel.LeftPan) >> 16));
#else
            mix0 = unchecked(mix0 + (short)(channelSample & channel.Cha));
#endif
            mix1 = unchecked(mix1 + (short)(channelSample & channel.Chc));
        }

        MixBuffer[0] = mix0;
        MixBuffer[2] = mix1;
#endif
        buffer[0] = ClipSample(MixBuffer[0]);
        buffer[2] = ClipSample(MixBuffer[2]);

        Opl3Lfo.Advance(this);

        Timer++;

        if (EgState != 0)
        {
            var envelopeTimerLow = (uint)EgTimer & 0x1fffu;
            if (envelopeTimerLow == 0)
            {
                EgAdd = 0;
            }
            else
            {
#if NETSTANDARD2_1
                byte shift = 0;
                while (((envelopeTimerLow >> shift) & 1) == 0)
                {
                    shift++;
                }
#else
                var shift = BitOperations.TrailingZeroCount(envelopeTimerLow);
#endif
                EgAdd = (byte)(shift + 1);
            }

            EgTimerLow = (byte)(EgTimer & 0x03u);
        }

        if (EgTimerRem != 0 || EgState != 0)
        {
            if (EgTimer == 0x0FFFFFFFFFUL)
            {
                EgTimer = 0;
                EgTimerRem = 1;
            }
            else
            {
                EgTimer++;
                EgTimerRem = 0;
            }
        }

        EgState ^= 1;

#if !NET10_0_OR_GREATER
        MixRight();
#endif

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

            var reg = (ushort)(entry.Register & 0x1ff);
            entry.Register = reg;
            WriteRegisterInternal(reg, entry.Data);
            WriteBufferCurrent = (WriteBufferCurrent + 1) % WriteBufferSize;
        }

        WriteBufferSampleCounter++;
    }

    private void Generate4ChSparseCore(Span<short> buffer)
    {
        if (buffer.Length < 4)
        {
            throw new ArgumentException("Buffer must contain at least four samples.", nameof(buffer));
        }

        buffer[1] = ClipSample(MixBuffer[1]);
        buffer[3] = ClipSample(MixBuffer[3]);

        if (MixListsDirty)
        {
            RebuildMixLists();
        }

        AdvanceNoise();

        var writeGeneration = WriteGeneration;
        var activeChannelMask = ActiveChannelMask;
#if NET10_0_OR_GREATER
        var rhythmActive = (Rhythm & 0x20) != 0;
#else
        const bool rhythmActive = true;
#endif
        for (var channelIndex = 0; channelIndex < 7; channelIndex++)
        {
            if ((activeChannelMask & (1u << channelIndex)) != 0)
            {
                ProcessChannelSlots(Channels[channelIndex], false, writeGeneration);
            }
        }

        if ((activeChannelMask & (1u << 7)) != 0)
        {
            ProcessChannelSlots(Channels[7], rhythmActive, writeGeneration);
        }

        if ((activeChannelMask & (1u << 8)) != 0)
        {
            ProcessChannelSlots(Channels[8], rhythmActive, writeGeneration);
        }
#if NET10_0_OR_GREATER
        if (!rhythmActive)
        {
            RefreshInactiveRhythmPhaseBits();
        }
#endif

        for (var channelIndex = 9; channelIndex < Channels.Length; channelIndex++)
        {
            if ((activeChannelMask & (1u << channelIndex)) != 0)
            {
                ProcessChannelSlots(Channels[channelIndex], false, writeGeneration);
            }
        }

#if NET10_0_OR_GREATER
        MixOutputBuses();
#else
        var mix0 = 0;
        var mix1 = 0;
        for (var index = 0; index < LeftMixChannelCount; index++)
        {
            var channel = LeftMixChannels[index];
            var channelSample = SumChannelOutputs(channel.LeftOutputs, channel.OutputCount);
#if OPL_ENABLE_STEREOEXT
            mix0 = unchecked(mix0 + (short)(((long)(short)channelSample * channel.LeftPan) >> 16));
#else
            mix0 = unchecked(mix0 + (short)(channelSample & channel.Cha));
#endif
            mix1 = unchecked(mix1 + (short)(channelSample & channel.Chc));
        }

        MixBuffer[0] = mix0;
        MixBuffer[2] = mix1;
#endif
        buffer[0] = ClipSample(MixBuffer[0]);
        buffer[2] = ClipSample(MixBuffer[2]);

        Opl3Lfo.Advance(this);

        Timer++;

        if (EgState != 0)
        {
            var envelopeTimerLow = (uint)EgTimer & 0x1fffu;
            if (envelopeTimerLow == 0)
            {
                EgAdd = 0;
            }
            else
            {
#if NETSTANDARD2_1
                byte shift = 0;
                while (((envelopeTimerLow >> shift) & 1) == 0)
                {
                    shift++;
                }
#else
                var shift = BitOperations.TrailingZeroCount(envelopeTimerLow);
#endif
                EgAdd = (byte)(shift + 1);
            }

            EgTimerLow = (byte)(EgTimer & 0x03u);
        }

        if (EgTimerRem != 0 || EgState != 0)
        {
            if (EgTimer == 0x0FFFFFFFFFUL)
            {
                EgTimer = 0;
                EgTimerRem = 1;
            }
            else
            {
                EgTimer++;
                EgTimerRem = 0;
            }
        }

        EgState ^= 1;

#if !NET10_0_OR_GREATER
        MixRight();
#endif

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

            var reg = (ushort)(entry.Register & 0x1ff);
            entry.Register = reg;
            WriteRegisterInternal(reg, entry.Data);
            WriteBufferCurrent = (WriteBufferCurrent + 1) % WriteBufferSize;
        }

        WriteBufferSampleCounter++;
    }

    /* Original C: void OPL3_Generate(opl3_chip *chip, int16_t *buf) */
    private void GenerateCore(Span<short> buffer)
    {
        if (buffer.Length < 2)
        {
            throw new ArgumentException("Buffer must contain at least two samples.", nameof(buffer));
        }

        Span<short> temp = stackalloc short[4];
        Generate4ChCore(temp);
        buffer[0] = temp[0];
        buffer[1] = temp[1];
    }

    /* Original C: void OPL3_Generate4ChResampled(opl3_chip *chip, int16_t *buf4) */
    private void Generate4ChResampledCore(Span<short> buffer)
    {
        if (buffer.Length < 4)
        {
            throw new ArgumentException("Buffer must contain at least four samples.", nameof(buffer));
        }

        Generate4ChResampledCore(ref buffer[0], ref buffer[1], ref buffer[2], ref buffer[3]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Generate4ChResampledCore(
        // ReSharper disable RedundantAssignment
        ref short channel0,
        ref short channel1,
        ref short channel2,
        ref short channel3
        // ReSharper restore RedundantAssignment
    )
    {
        while (RateRatio != 0 && SampleCounter >= RateRatio)
        {
            OldSamples[0] = Samples[0];
            OldSamples[1] = Samples[1];
            OldSamples[2] = Samples[2];
            OldSamples[3] = Samples[3];

            Generate4ChCore(Samples.AsSpan());
            SampleCounter -= RateRatio;
        }

        if (RateRatio != 0)
        {
            channel0 = (short)((OldSamples[0] * (RateRatio - SampleCounter) + Samples[0] * SampleCounter) / RateRatio);
            channel1 = (short)((OldSamples[1] * (RateRatio - SampleCounter) + Samples[1] * SampleCounter) / RateRatio);
            channel2 = (short)((OldSamples[2] * (RateRatio - SampleCounter) + Samples[2] * SampleCounter) / RateRatio);
            channel3 = (short)((OldSamples[3] * (RateRatio - SampleCounter) + Samples[3] * SampleCounter) / RateRatio);
        }
        else
        {
            channel0 = Samples[0];
            channel1 = Samples[1];
            channel2 = Samples[2];
            channel3 = Samples[3];
        }

        SampleCounter += 1 << ResampleFractionBits;
    }

    /* Original C: void OPL3_GenerateResampled(opl3_chip *chip, int16_t *buf) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GenerateResampledCore(Span<short> buffer)
    {
        if (buffer.Length < 2)
        {
            throw new ArgumentException("Buffer must contain at least two samples.", nameof(buffer));
        }

        short discardRearLeft = 0;
        short discardRearRight = 0;
        Generate4ChResampledCore(ref buffer[0], ref buffer[1], ref discardRearLeft, ref discardRearRight);
    }

    /* Original C: void OPL3_Reset(opl3_chip *chip, uint32_t samplerate) */
    private void ResetInternal(uint sampleRate)
    {
        Timer = 0;
        EgTimer = 0;
        EgTimerRem = 0;
        EgState = 0;
        EgAdd = 0;
        EgTimerLow = 0;
        NewM = 0;
        Nts = 0;
        Rhythm = 0;
        Opl3Lfo.Reset(this);
        CachedVibratoPosition = 0;
        TremoloDirty = false;
        Noise = 1;
        NoiseHihat = 0;
        NoiseSnare = 0;
        WriteGeneration = 1;
        ActiveChannelMask = AllChannelsMask;
#if NET10_0_OR_GREATER
        _mixEntryCount = 0;
#else
        LeftMixChannelCount = 0;
        RightMixChannelCount = 0;
#endif
        MixListsDirty = true;
        ZeroMod = 0;
        Array.Clear(MixBuffer, 0, MixBuffer.Length);
        RhythmHihatBit2 = 0;
        RhythmHihatBit3 = 0;
        RhythmHihatBit7 = 0;
        RhythmHihatBit8 = 0;
        RhythmTomBit3 = 0;
        RhythmTomBit5 = 0;
#if OPL_ENABLE_STEREOEXT
        StereoExtension = 0;
#endif
        RateRatio = sampleRate == 0 ? 0 : (int)((sampleRate << ResampleFractionBits) / 49716);
        if (RateRatio == 0)
        {
            RateRatio = 1;
        }

        SampleCounter = 0;
        Array.Clear(OldSamples, 0, OldSamples.Length);
        Array.Clear(Samples, 0, Samples.Length);
        WriteBufferSampleCounter = 0;
        WriteBufferCurrent = 0;
        WriteBufferLast = 0;
        WriteBufferLastTime = 0;

        foreach (var entry in WriteBuffer)
        {
            entry.Register = 0;
            entry.Data = 0;
            entry.Time = 0;
        }

        for (var slotIndex = 0; slotIndex < Slots.Length; slotIndex++)
        {
            var slot = Slots[slotIndex];
            slot.Channel = null;
            slot.Chip = this;
            slot.ModulationSource = ShortSignalSource.Zero;
            slot.PreviousOutputSample = 0;
            slot.Out = 0;
            slot.FeedbackModifiedSignal = 0;
            slot.EnvelopeGeneratorOutput = 0x1ff;
            slot.EnvelopeGeneratorLevel = 0x1ff;
            slot.EnvelopeGeneratorIncrement = 0;
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Release;
            slot.EffectiveEnvelopeRateIndex = 0;
            slot.EffectiveKeyScaleLevel = 0;
            slot.CachedEnvelopeAttenuation = 0;
            slot.CachedEnvelopeKeyScale = 0;
            Array.Clear(slot.EnvelopeRates, 0, slot.EnvelopeRates.Length);
#if NET10_0_OR_GREATER
            Array.Clear(slot.ResolvedEnvelopeRates, 0, slot.ResolvedEnvelopeRates.Length);
#else
            Array.Clear(slot.EnvelopeRateHigh, 0, slot.EnvelopeRateHigh.Length);
            Array.Clear(slot.EnvelopeRateLow, 0, slot.EnvelopeRateLow.Length);
#endif
            slot.TremoloEnabled = false;
            slot.RegVibrato = 0;
            slot.RegOperatorType = 0;
            slot.RegKeyScaleRate = 0;
            slot.RegFrequencyMultiplier = 0;
            slot.RegKeyScaleLevel = 0;
            slot.RegTotalLevel = 0;
            slot.RegAttackRate = 0;
            slot.RegDecayRate = 0;
            slot.RegSustainLevel = 0;
            slot.RegReleaseRate = 0;
            slot.RegWaveformSelect = 0;
            slot.RegKeyState = 0;
            slot.RegPhaseResetRequest = 0;
            slot.RegPhaseGeneratorAccumulator = 0;
            slot.PhaseIncrement = 0;
            slot.CurrentVibratoPhaseIncrement = 0;
            Array.Clear(slot.VibratoPhaseIncrements, 0, slot.VibratoPhaseIncrements.Length);
            slot.PhaseGeneratorOutput = 0;
            slot.SlotIndex = (byte)slotIndex;
            slot.DormantGeneration = 0;
        }

        for (var channelIndex = 0; channelIndex < Channels.Length; channelIndex++)
        {
            var channel = Channels[channelIndex];
            var localSlot = Opl3Tables.ReadChannelSlot(channelIndex);
            channel.Slotz[0] = Slots[localSlot];
            channel.Slotz[1] = Slots[localSlot + 3];
            channel.Slotz[0].Channel = channel;
            channel.Slotz[1].Channel = channel;
            channel.Slotz[0].Chip = this;
            channel.Slotz[1].Chip = this;
            channel.Pair = null;

            var mod9 = channelIndex % 9;
            channel.Pair = mod9 switch
            {
                < 3 => Channels[channelIndex + 3],
                < 6 => Channels[channelIndex - 3],
                _ => channel.Pair
            };

            channel.Chip = this;
            channel.Out[0] = ShortSignalSource.Zero;
            channel.Out[1] = ShortSignalSource.Zero;
            channel.Out[2] = ShortSignalSource.Zero;
            channel.Out[3] = ShortSignalSource.Zero;
            channel.LeftOutputs[0] = ShortSignalSource.Zero;
            channel.LeftOutputs[1] = ShortSignalSource.Zero;
            channel.LeftOutputs[2] = ShortSignalSource.Zero;
            channel.LeftOutputs[3] = ShortSignalSource.Zero;
            channel.RightOutputs[0] = ShortSignalSource.Zero;
            channel.RightOutputs[1] = ShortSignalSource.Zero;
            channel.RightOutputs[2] = ShortSignalSource.Zero;
            channel.RightOutputs[3] = ShortSignalSource.Zero;
            channel.OutputCount = 0;
            channel.ChannelType = ChannelType.TwoOp;
            channel.FNumber = 0;
            channel.Block = 0;
            channel.Feedback = 0;
            channel.Connection = 0;
            channel.Algorithm = 0;
            channel.KeyScaleValue = 0;
            channel.Cha = 0xffff;
            channel.Chb = 0xffff;
            channel.Chc = 0;
            channel.Chd = 0;
#if OPL_ENABLE_STEREOEXT
            channel.LeftPan = 0x10000;
            channel.RightPan = 0x10000;
#endif
            channel.ChannelNumber = (byte)channelIndex;
            ChannelSetupAlgorithm(channel);
        }
    }

    /* Original C: void OPL3_WriteReg(opl3_chip *chip, uint16_t reg, uint8_t v) */
    private void WriteRegisterInternal(ushort register, byte value)
    {
        if (ActiveChannelMask != AllChannelsMask)
        {
            ActiveChannelMask = AllChannelsMask;
            MixListsDirty = true;
        }

        WriteGeneration = unchecked(WriteGeneration + 1);
        if (WriteGeneration == 0)
        {
            foreach (var slot in Slots)
            {
                slot.DormantGeneration = 0;
            }

            WriteGeneration = 1;
        }

        var high = (byte)((register >> 8) & 0x01);
        var regm = (byte)(register & 0xff);

        var slotBase = high != 0 ? 18 : 0;
        var channelBase = high != 0 ? 9 : 0;

        switch (regm & 0xf0)
        {
            case 0x00:
                if (high != 0)
                {
                    switch (regm & 0x0f)
                    {
                        case 0x04:
                            ChannelSet4Op(this, value);
                            break;
                        case 0x05:
                            NewM = (byte)(value & 0x01);
#if OPL_ENABLE_STEREOEXT
                            StereoExtension = (byte)((value >> 1) & 0x01);
#endif
                            break;
                    }
                }
                else
                {
                    if ((regm & 0x0f) == 0x08)
                    {
                        Nts = (byte)((value >> 6) & 0x01);
                    }
                }

                break;

            case 0x20:
            case 0x30:
            {
                int slotIndex = Opl3Tables.ReadAddressDecodeSlot(regm & 0x1f);
                if (slotIndex >= 0)
                {
                    SlotWrite20(Slots[slotBase + slotIndex], value);
                }

                break;
            }

            case 0x40:
            case 0x50:
            {
                int slotIndex = Opl3Tables.ReadAddressDecodeSlot(regm & 0x1f);
                if (slotIndex >= 0)
                {
                    SlotWrite40(Slots[slotBase + slotIndex], value);
                }

                break;
            }

            case 0x60:
            case 0x70:
            {
                int slotIndex = Opl3Tables.ReadAddressDecodeSlot(regm & 0x1f);
                if (slotIndex >= 0)
                {
                    SlotWrite60(Slots[slotBase + slotIndex], value);
                }

                break;
            }

            case 0x80:
            case 0x90:
            {
                int slotIndex = Opl3Tables.ReadAddressDecodeSlot(regm & 0x1f);
                if (slotIndex >= 0)
                {
                    SlotWrite80(Slots[slotBase + slotIndex], value);
                }

                break;
            }

            case 0xe0:
            case 0xf0:
            {
                int slotIndex = Opl3Tables.ReadAddressDecodeSlot(regm & 0x1f);
                if (slotIndex >= 0)
                {
                    SlotWriteE0(Slots[slotBase + slotIndex], value);
                }

                break;
            }

            case 0xa0:
                if ((regm & 0x0f) < 9)
                {
                    ChannelWriteA0(Channels[channelBase + (regm & 0x0f)], value);
                }

                break;

            case 0xb0:
                if (regm == 0xbd && high == 0)
                {
                    if (Opl3Lfo.ConfigureDepth(this, value))
                    {
                        foreach (var slot in Slots)
                        {
                            UpdatePhaseIncrement(slot);
                        }
                    }

                    ChannelUpdateRhythm(this, value);
                }
                else if ((regm & 0x0f) < 9)
                {
                    var channel = Channels[channelBase + (regm & 0x0f)];
                    ChannelWriteB0(channel, value);
                    if ((value & 0x20) != 0)
                    {
                        ChannelKeyOn(channel);
                    }
                    else
                    {
                        ChannelKeyOff(channel);
                    }
                }

                break;

            case 0xc0:
                if ((regm & 0x0f) < 9)
                {
                    ChannelWriteC0(Channels[channelBase + (regm & 0x0f)], value);
                }

                break;
#if OPL_ENABLE_STEREOEXT
            case 0xd0:
                if ((regm & 0x0f) < 9) {
                    ChannelWriteD0(Channels[channelBase + (regm & 0x0f)], value);
                }

                break;
#endif
        }
    }

    /* Original C: void OPL3_WriteRegBuffered(opl3_chip *chip, uint16_t reg, uint8_t v) */
    private void WriteRegisterBufferedInternal(ushort register, byte value)
    {
        var writebufLast = (int)WriteBufferLast;
        var entry = WriteBuffer[writebufLast];

        if ((entry.Register & 0x200) != 0)
        {
            WriteRegisterInternal((ushort)(entry.Register & 0x1ff), entry.Data);
            WriteBufferCurrent = (uint)((writebufLast + 1) % WriteBufferSize);
            WriteBufferSampleCounter = entry.Time;
        }

        entry.Register = (ushort)(register | 0x200);
        entry.Data = value;
        var time1 = WriteBufferLastTime + WriteBufferDelay;
        var time2 = WriteBufferSampleCounter;
        if (time1 < time2)
        {
            time1 = time2;
        }

        entry.Time = time1;
        WriteBufferLastTime = time1;
        WriteBufferLast = (uint)((writebufLast + 1) % WriteBufferSize);
    }

    /* Original C: void OPL3_Generate4ChStream(opl3_chip *chip, int16_t *sndptr1, int16_t *sndptr2, uint32_t numsamples) */
    private void Generate4ChStreamCore(Span<short> stream1, Span<short> stream2)
    {
        if ((stream1.Length & 1) != 0 || (stream2.Length & 1) != 0)
        {
            throw new ArgumentException("Stream buffers must contain an even number of elements.");
        }

        var frames = Math.Min(stream1.Length, stream2.Length) / 2;
        if (frames == 0)
        {
            return;
        }

        for (var i = 0; i < frames; i++)
        {
            var offset = i << 1;
            Generate4ChResampledCore(ref stream1[offset],
                ref stream1[offset + 1],
                ref stream2[offset],
                ref stream2[offset + 1]);
        }
    }

    /* Original C: void OPL3_GenerateStream(opl3_chip *chip, int16_t *sndptr, uint32_t numsamples) */
    private void GenerateStreamCore(Span<short> stream)
    {
        if ((stream.Length & 1) != 0)
        {
            throw new ArgumentException("Stream buffer must contain an even number of elements.", nameof(stream));
        }

        var frames = stream.Length / 2;
        if (frames == 0)
        {
            return;
        }

        short discardRearLeft = 0;
        short discardRearRight = 0;
        for (var sampleIndex = 0; sampleIndex < frames; sampleIndex++)
        {
            var offset = sampleIndex << 1;
            Generate4ChResampledCore(ref stream[offset],
                ref stream[offset + 1],
                ref discardRearLeft,
                ref discardRearRight);
        }
    }
}
