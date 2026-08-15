// SPDX-FileCopyrightText: 2013-2026 Nuked-OPL3 by nukeykt
// SPDX-FileCopyrightText: 2026 Tony Gies
// SPDX-License-Identifier: LGPL-2.1-only

using System.Runtime.CompilerServices;

namespace NukedOPL3Sharp;

/// <summary>
///     Advances operator envelopes and converts their logarithmic waveform level into a sample.
/// </summary>
internal static class Opl3Envelope
{
    /// <summary>
    ///     Refreshes key-scale level and the combined attenuation used for every generated sample.
    /// </summary>
    internal static void UpdateKeyScaleLevel(Opl3Operator slot)
    {
        var channel = slot.Channel ?? throw new InvalidOperationException("Channel not assigned.");
        var value = (short)((Opl3Tables.ReadKeyScaleLevel(channel.FNumber >> 6) << 2)
                            - ((0x08 - channel.Block) << 5));
        if (value < 0)
        {
            value = 0;
        }

        slot.EffectiveKeyScaleLevel = (byte)value;
        slot.CachedEnvelopeAttenuation = (ushort)((slot.RegTotalLevel << 2)
                                                   + (slot.EffectiveKeyScaleLevel >>
                                                      Opl3Tables.ReadKeyScaleShift(slot.RegKeyScaleLevel)));
    }

    /// <summary>
    ///     Resolves all stage rates after a register or channel pitch change so sample generation only selects a cache entry.
    /// </summary>
    internal static void UpdateRates(Opl3Operator slot)
    {
        var channel = slot.Channel ?? throw new InvalidOperationException("Channel not assigned.");
        slot.CachedEnvelopeKeyScale = (byte)(channel.KeyScaleValue >> ((slot.RegKeyScaleRate ^ 1) << 1));

        for (var stage = 0; stage < slot.EnvelopeRates.Length; stage++)
        {
            var rate = (byte)(slot.CachedEnvelopeKeyScale + (slot.EnvelopeRates[stage] << 2));
            var rateHigh = (byte)(rate >> 2);
            if ((rateHigh & 0x10) != 0)
            {
                rateHigh = 0x0f;
            }

            slot.EnvelopeRateHigh[stage] = rateHigh;
            slot.EnvelopeRateLow[stage] = (byte)(rate & 0x03);
        }
    }

    /// <summary>
    ///     Advances one operator's envelope while preserving the upstream timer and key-transition rules.
    /// </summary>
    internal static void Calculate(Opl3Operator slot)
    {
        var chip = slot.Chip ?? throw new InvalidOperationException("Chip not assigned.");
        var stage = slot.EnvelopeGeneratorState;
        var reset = slot.RegKeyState != 0 && stage == (byte)EnvelopeGeneratorStage.Release;
        var rateStage = reset ? (byte)EnvelopeGeneratorStage.Attack : stage;
        var registerRate = slot.EnvelopeRates[rateStage];
        var rateHigh = slot.EnvelopeRateHigh[rateStage];
        var rateLow = slot.EnvelopeRateLow[rateStage];
        byte shift = 0;

        slot.EnvelopeGeneratorLevel = (ushort)(slot.EnvelopeGeneratorOutput + slot.CachedEnvelopeAttenuation
                                                + (slot.TremoloEnabled ? chip.Tremolo : 0));
        slot.RegPhaseResetRequest = reset ? 1u : 0u;

        if (registerRate != 0)
        {
            if (rateHigh < 12)
            {
                if (chip.EgState != 0)
                {
                    shift = (byte)(rateHigh + chip.EgAdd) switch
                    {
                        12 => 1,
                        13 => (byte)((rateLow >> 1) & 0x01),
                        14 => (byte)(rateLow & 0x01),
                        _ => 0
                    };
                }
            }
            else
            {
                shift = (byte)((rateHigh & 0x03) + Opl3Tables.EgIncrementSteps[rateLow, chip.EgTimerLow]);
                if ((shift & 0x04) != 0)
                {
                    shift = 0x03;
                }

                if (shift == 0)
                {
                    shift = chip.EgState;
                }
            }
        }

        var envelopeOutput = slot.EnvelopeGeneratorOutput;
        var envelopeIncrement = 0;
        var envelopeOff = (slot.EnvelopeGeneratorOutput & 0x1f8) == 0x1f8;

        if (reset && rateHigh == 0x0f)
        {
            envelopeOutput = 0;
        }

        if (stage != (byte)EnvelopeGeneratorStage.Attack && !reset && envelopeOff)
        {
            envelopeOutput = 0x1ff;
        }

        switch ((EnvelopeGeneratorStage)stage)
        {
            case EnvelopeGeneratorStage.Attack:
                if (slot.EnvelopeGeneratorOutput == 0)
                {
                    slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Decay;
                }
                else if (slot.RegKeyState != 0 && shift > 0 && rateHigh != 0x0f)
                {
                    envelopeIncrement = ~slot.EnvelopeGeneratorOutput >> (4 - shift);
                }

                break;

            case EnvelopeGeneratorStage.Decay:
                if (slot.EnvelopeGeneratorOutput >> 4 == slot.RegSustainLevel)
                {
                    slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Sustain;
                }
                else if (!envelopeOff && !reset && shift > 0)
                {
                    envelopeIncrement = 1 << (shift - 1);
                }

                break;

            case EnvelopeGeneratorStage.Sustain:
            case EnvelopeGeneratorStage.Release:
                if (!envelopeOff && !reset && shift > 0)
                {
                    envelopeIncrement = 1 << (shift - 1);
                }

                break;
        }

        slot.EnvelopeGeneratorOutput = (ushort)((envelopeOutput + envelopeIncrement) & 0x1ff);
        if (reset)
        {
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Attack;
        }

        if (slot.RegKeyState == 0)
        {
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Release;
        }
    }

    /// <summary>
    ///     Adds a key source without disturbing any other active key source.
    /// </summary>
    internal static void KeyOn(Opl3Operator slot, EnvelopeKeyType type)
    {
        slot.RegKeyState = (byte)(slot.RegKeyState | (byte)type);
    }

    /// <summary>
    ///     Removes one key source while preserving any other active key source.
    /// </summary>
    internal static void KeyOff(Opl3Operator slot, EnvelopeKeyType type)
    {
        slot.RegKeyState = (byte)(slot.RegKeyState & ~(byte)type);
    }

    /// <summary>
    ///     Generates a sample through the precomputed waveform and pre-shifted exponential tables.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static short GenerateWaveform(Opl3Operator slot)
    {
        var phase = unchecked((ushort)(slot.PhaseGeneratorOutput + (ushort)slot.ModulationSource.Read()));
        var waveform = Opl3Tables.ReadWaveform(slot.RegWaveformSelect, phase & 0x3ff);
        var negativeMask = (ushort)((short)waveform >> 15);
        var level = (uint)((waveform & 0x7fff) + (slot.EnvelopeGeneratorLevel << 3));
        if (level > 0x1fff)
        {
            level = 0x1fff;
        }

        var sample = (ushort)(Opl3Tables.ReadExp((int)(level & 0xff)) >> (int)(level >> 8));
        return unchecked((short)(sample ^ negativeMask));
    }

    /// <summary>
    ///     Generates only the waveform sign after attenuation has proven the exponential magnitude is zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static short GenerateSilentWaveform(Opl3Operator slot)
    {
        var phase = unchecked((ushort)(slot.PhaseGeneratorOutput + (ushort)slot.ModulationSource.Read()));
        var waveform = Opl3Tables.ReadWaveform(slot.RegWaveformSelect, phase & 0x3ff);
        return (short)((short)waveform >> 15);
    }
}
