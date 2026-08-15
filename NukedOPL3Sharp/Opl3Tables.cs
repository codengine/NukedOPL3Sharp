/* Nuked OPL3
 * Copyright (C) 2013-2020 Nuke.YKT
 *
 * This file is part of Nuked OPL3.
 *
 * Nuked OPL3 is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as
 * published by the Free Software Foundation, either version 2.1
 * of the License, or (at your option) any later version.
 *
 * Nuked OPL3 is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with Nuked OPL3. If not, see <https://www.gnu.org/licenses/>.

 *  Nuked OPL3 emulator.
 *  Thanks:
 *      MAME Development Team(Jarek Burczynski, Tatsuyuki Satoh):
 *          Feedback and Rhythm part calculation information.
 *      forums.submarine.org.uk(carbon14, opl3):
 *          Tremolo and phase generator calculation information.
 *      OPLx decapsulated(Matthew Gambrell, Olli Niemitalo):
 *          OPL2 ROMs.
 *      siliconpr0n.org(John McMaster, digshadow):
 *          YMF262 and VRC VII decaps and die shots.
 *
 * version: 1.8
 */

// SPDX-FileCopyrightText: 2026 Tony Gies
// SPDX-License-Identifier: LGPL-2.1-only

using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Diagnostics;
using System.Runtime.InteropServices;
#endif

namespace NukedOPL3Sharp;

/// <summary>
///     Owns immutable ROM data and decoded lookup tables used by the chip core.
/// </summary>
internal static partial class Opl3Tables
{
#if NET10_0_OR_GREATER
    private const int LinearEnvelopeLevelCount = 384;
#endif
    private static readonly ushort[] WaveformData = WaveformRom.Data;
    private static readonly ushort[] ExpRomData = BuildExpRomData();
#if NET10_0_OR_GREATER
    private static readonly short[] LinearWaveformData = BuildLinearWaveformData();
#endif

    private static ushort[] BuildExpRomData()
    {
        ushort[] data =
        [
            0x7fa, 0x7f5, 0x7ef, 0x7ea, 0x7e4, 0x7df, 0x7da, 0x7d4,
            0x7cf, 0x7c9, 0x7c4, 0x7bf, 0x7b9, 0x7b4, 0x7ae, 0x7a9,
            0x7a4, 0x79f, 0x799, 0x794, 0x78f, 0x78a, 0x784, 0x77f,
            0x77a, 0x775, 0x770, 0x76a, 0x765, 0x760, 0x75b, 0x756,
            0x751, 0x74c, 0x747, 0x742, 0x73d, 0x738, 0x733, 0x72e,
            0x729, 0x724, 0x71f, 0x71a, 0x715, 0x710, 0x70b, 0x706,
            0x702, 0x6fd, 0x6f8, 0x6f3, 0x6ee, 0x6e9, 0x6e5, 0x6e0,
            0x6db, 0x6d6, 0x6d2, 0x6cd, 0x6c8, 0x6c4, 0x6bf, 0x6ba,
            0x6b5, 0x6b1, 0x6ac, 0x6a8, 0x6a3, 0x69e, 0x69a, 0x695,
            0x691, 0x68c, 0x688, 0x683, 0x67f, 0x67a, 0x676, 0x671,
            0x66d, 0x668, 0x664, 0x65f, 0x65b, 0x657, 0x652, 0x64e,
            0x649, 0x645, 0x641, 0x63c, 0x638, 0x634, 0x630, 0x62b,
            0x627, 0x623, 0x61e, 0x61a, 0x616, 0x612, 0x60e, 0x609,
            0x605, 0x601, 0x5fd, 0x5f9, 0x5f5, 0x5f0, 0x5ec, 0x5e8,
            0x5e4, 0x5e0, 0x5dc, 0x5d8, 0x5d4, 0x5d0, 0x5cc, 0x5c8,
            0x5c4, 0x5c0, 0x5bc, 0x5b8, 0x5b4, 0x5b0, 0x5ac, 0x5a8,
            0x5a4, 0x5a0, 0x59c, 0x599, 0x595, 0x591, 0x58d, 0x589,
            0x585, 0x581, 0x57e, 0x57a, 0x576, 0x572, 0x56f, 0x56b,
            0x567, 0x563, 0x560, 0x55c, 0x558, 0x554, 0x551, 0x54d,
            0x549, 0x546, 0x542, 0x53e, 0x53b, 0x537, 0x534, 0x530,
            0x52c, 0x529, 0x525, 0x522, 0x51e, 0x51b, 0x517, 0x514,
            0x510, 0x50c, 0x509, 0x506, 0x502, 0x4ff, 0x4fb, 0x4f8,
            0x4f4, 0x4f1, 0x4ed, 0x4ea, 0x4e7, 0x4e3, 0x4e0, 0x4dc,
            0x4d9, 0x4d6, 0x4d2, 0x4cf, 0x4cc, 0x4c8, 0x4c5, 0x4c2,
            0x4be, 0x4bb, 0x4b8, 0x4b5, 0x4b1, 0x4ae, 0x4ab, 0x4a8,
            0x4a4, 0x4a1, 0x49e, 0x49b, 0x498, 0x494, 0x491, 0x48e,
            0x48b, 0x488, 0x485, 0x482, 0x47e, 0x47b, 0x478, 0x475,
            0x472, 0x46f, 0x46c, 0x469, 0x466, 0x463, 0x460, 0x45d,
            0x45a, 0x457, 0x454, 0x451, 0x44e, 0x44b, 0x448, 0x445,
            0x442, 0x43f, 0x43c, 0x439, 0x436, 0x433, 0x430, 0x42d,
            0x42a, 0x428, 0x425, 0x422, 0x41f, 0x41c, 0x419, 0x416,
            0x414, 0x411, 0x40e, 0x40b, 0x408, 0x406, 0x403, 0x400
        ];

        for (var index = 0; index < data.Length; index++)
        {
            data[index] <<= 1;
        }

        return data;
    }

#if NET10_0_OR_GREATER
    private static short[] BuildLinearWaveformData()
    {
        var data = GC.AllocateUninitializedArray<short>(LinearEnvelopeLevelCount * WaveformData.Length);
        for (var envelope = 0; envelope < LinearEnvelopeLevelCount; envelope++)
        {
            var destinationOffset = envelope * WaveformData.Length;
            for (var waveformIndex = 0; waveformIndex < WaveformData.Length; waveformIndex++)
            {
                var waveform = WaveformData[waveformIndex];
                var negativeMask = (ushort)((short)waveform >> 15);
                var level = (uint)((waveform & 0x7fff) + (envelope << 3));
                if (level > 0x1fff)
                {
                    level = 0x1fff;
                }

                var sample = (ushort)(ExpRomData[level & 0xff] >> (int)(level >> 8));
                data[destinationOffset + waveformIndex] = unchecked((short)(sample ^ negativeMask));
            }
        }

        return data;
    }
#endif

    private static readonly byte[] FrequencyMultiplierData =
    [
        1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 20, 24, 24, 30, 30
    ];

    private static readonly byte[] KeyScaleLevelData =
    [
        0, 32, 40, 45, 48, 51, 53, 55, 56, 58, 59, 60, 61, 62, 63, 64
    ];

    private static readonly byte[] KeyScaleShiftData =
    [
        8, 1, 2, 0
    ];

    private static readonly sbyte[] AddressDecodeSlotData =
    [
        0, 1, 2, 3, 4, 5, -1, -1, 6, 7, 8, 9, 10, 11, -1, -1,
        12, 13, 14, 15, 16, 17, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1
    ];

    private static readonly byte[] ChannelSlotData =
    [
        0, 1, 2, 6, 7, 8, 12, 13, 14, 18, 19, 20, 24, 25, 26, 30, 31, 32
    ];

#if OPL_ENABLE_STEREOEXT
    /*
        stereo extension panpot lookup table
    */
    internal static ReadOnlySpan<int> StereoPanPotLut => StereoPanPotLutData;

    private static readonly int[] StereoPanPotLutData = BuildStereoPanPotLut();

    private static int[] BuildStereoPanPotLut() {
        int[] table = new int[256];
        for (int i = 0; i < table.Length; i++) {
            double angle = i * Math.PI / 512.0;
            table[i] = (int)(Math.Sin(angle) * 65536.0);
        }

        return table;
    }
#endif

    /*
        envelope generator constants
    */
    internal static byte[,] EgIncrementSteps { get; } = new byte[,]
    {
        { 0, 0, 0, 0 },
        { 1, 0, 0, 0 },
        { 1, 0, 1, 0 },
        { 1, 1, 1, 0 }
    };

#if NET10_0_OR_GREATER
    private const int EnvelopeRateDescriptorCount = 65;
    private const int EnvelopeTransitionCount = 1 << 20;
    private static readonly byte[] EnvelopeShiftData = BuildEnvelopeShiftData();
    private static readonly ushort[] EnvelopeTransitionData = BuildEnvelopeTransitionData();

    private static byte[] BuildEnvelopeShiftData()
    {
        var data = new byte[2 * 14 * 4 * EnvelopeRateDescriptorCount];
        for (var envelopeState = 0; envelopeState < 2; envelopeState++)
        {
            for (var envelopeAdd = 0; envelopeAdd < 14; envelopeAdd++)
            {
                for (var timerLow = 0; timerLow < 4; timerLow++)
                {
                    var destinationOffset = (((envelopeState * 14) + envelopeAdd) * 4 + timerLow)
                                            * EnvelopeRateDescriptorCount;
                    for (var descriptor = 1; descriptor < EnvelopeRateDescriptorCount; descriptor++)
                    {
                        var rate = descriptor - 1;
                        var rateHigh = rate >> 2;
                        var rateLow = rate & 0x03;
                        var shift = 0;
                        if (rateHigh < 12)
                        {
                            if (envelopeState != 0)
                            {
                                shift = (rateHigh + envelopeAdd) switch
                                {
                                    12 => 1,
                                    13 => (rateLow >> 1) & 0x01,
                                    14 => rateLow & 0x01,
                                    _ => 0
                                };
                            }
                        }
                        else
                        {
                            shift = (rateHigh & 0x03) + EgIncrementSteps[rateLow, timerLow];
                            if ((shift & 0x04) != 0)
                            {
                                shift = 0x03;
                            }

                            if (shift == 0)
                            {
                                shift = envelopeState;
                            }
                        }

                        data[destinationOffset + descriptor] = (byte)shift;
                    }
                }
            }
        }

        return data;
    }

    private static ushort[] BuildEnvelopeTransitionData()
    {
        var data = GC.AllocateUninitializedArray<ushort>(EnvelopeTransitionCount);
        for (var index = 0; index < data.Length; index++)
        {
            var currentOutput = index & 0x1ff;
            var stage = (index >> 9) & 0x03;
            var keyOn = ((index >> 11) & 0x01) != 0;
            var shift = (index >> 12) & 0x03;
            var rateHighIsMaximum = ((index >> 14) & 0x01) != 0;
            var sustainLevel = (index >> 15) & 0x1f;
            var reset = keyOn && stage == (byte)EnvelopeGeneratorStage.Release;
            var envelopeOutput = currentOutput;
            var envelopeIncrement = 0;
            var envelopeOff = (currentOutput & 0x1f8) == 0x1f8;

            if (reset && rateHighIsMaximum)
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
                    if (currentOutput == 0)
                    {
                        stage = (byte)EnvelopeGeneratorStage.Decay;
                    }
                    else if (keyOn && shift > 0 && !rateHighIsMaximum)
                    {
                        envelopeIncrement = ~currentOutput >> (4 - shift);
                    }

                    break;

                case EnvelopeGeneratorStage.Decay:
                    if (currentOutput >> 4 == sustainLevel)
                    {
                        stage = (byte)EnvelopeGeneratorStage.Sustain;
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

            envelopeOutput = (envelopeOutput + envelopeIncrement) & 0x1ff;
            if (reset)
            {
                stage = (byte)EnvelopeGeneratorStage.Attack;
            }

            if (!keyOn)
            {
                stage = (byte)EnvelopeGeneratorStage.Release;
            }

            data[index] = (ushort)(envelopeOutput | (stage << 9) | (reset ? 1 << 11 : 0));
        }

        return data;
    }

    /// <summary>
    ///     Encodes the global envelope clock into the base offset shared by all operator-rate lookups for one sample.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetEnvelopeShiftTableOffset(byte envelopeState, byte envelopeAdd, byte timerLow)
    {
        return (((envelopeState * 14) + envelopeAdd) * 4 + timerLow) * EnvelopeRateDescriptorCount;
    }

    /// <summary>
    ///     Reads the envelope increment shift for a proven clock offset and resolved rate descriptor.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte ReadEnvelopeShift(int index)
    {
        Debug.Assert((uint)index < EnvelopeShiftData.Length);
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(EnvelopeShiftData), index);
    }

    /// <summary>
    ///     Reads a packed next envelope output, stage, and phase-reset flag for a proven transition index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ushort ReadEnvelopeTransition(int index)
    {
        Debug.Assert((uint)index < EnvelopeTransitionData.Length);
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(EnvelopeTransitionData), index);
    }
#endif

    /// <summary>
    ///     Reads one logarithmic waveform entry for a ten-bit phase.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ushort ReadWaveform(int waveform, int phase)
    {
        return WaveformData[(waveform << 10) + phase];
    }

#if NET10_0_OR_GREATER
    /// <summary>
    ///     Reads the final linear sample for a proven audible envelope level and ten-bit waveform phase.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static short ReadLinearWaveform(int envelope, int waveform, int phase)
    {
        var index = (envelope << 13) + (waveform << 10) + phase;
        Debug.Assert((uint)index < LinearWaveformData.Length);
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(LinearWaveformData), index);
    }
#endif

    /// <summary>
    ///     Reads one pre-shifted exponential entry for a proven eight-bit index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ushort ReadExp(int index)
    {
#if NET8_0_OR_GREATER
        Debug.Assert((uint)index < ExpRomData.Length);
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(ExpRomData), index);
#else
        return ExpRomData[index];
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte ReadKeyScaleLevel(int index)
    {
        return KeyScaleLevelData[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte ReadKeyScaleShift(int index)
    {
        return KeyScaleShiftData[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte ReadFrequencyMultiplier(int index)
    {
        return FrequencyMultiplierData[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte ReadChannelSlot(int index)
    {
        return ChannelSlotData[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static sbyte ReadAddressDecodeSlot(int index)
    {
        return AddressDecodeSlotData[index];
    }
}
