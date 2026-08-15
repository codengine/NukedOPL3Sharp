// SPDX-FileCopyrightText: 2013-2026 Nuked-OPL3 by nukeykt
// SPDX-FileCopyrightText: 2026 Tony Gies
// SPDX-License-Identifier: LGPL-2.1-only

namespace NukedOPL3Sharp;

/// <summary>
///     Encapsulates Nuked-OPL3 LFO state transitions (tremolo and vibrato).
/// </summary>
internal static class Opl3Lfo
{
    /// <summary>
    ///     Restores both low-frequency oscillators to their power-on positions and depths.
    /// </summary>
    internal static void Reset(Opl3Chip chip)
    {
        chip.Tremolo = 0;
        chip.TremoloPosition = 0;
        chip.TremoloShift = 4;
        chip.VibratoPosition = 0;
        chip.VibratoShift = 1;
    }

    /// <summary>
    ///     Advances timer-gated LFO positions and refreshes tremolo only when its visible value can change.
    /// </summary>
    internal static void Advance(Opl3Chip chip)
    {
        var updateTremolo = chip.TremoloDirty;
        if ((chip.Timer & 0x3f) == 0x3f)
        {
            chip.TremoloPosition++;
            if (chip.TremoloPosition == 210)
            {
                chip.TremoloPosition = 0;
            }

            updateTremolo = true;
        }

        if (updateTremolo)
        {
            chip.Tremolo = chip.TremoloPosition < 105
                ? (byte)(chip.TremoloPosition >> chip.TremoloShift)
                : (byte)((210 - chip.TremoloPosition) >> chip.TremoloShift);
            chip.TremoloDirty = false;
        }

        if ((chip.Timer & 0x3ff) == 0x3ff)
        {
            chip.VibratoPosition = (byte)((chip.VibratoPosition + 1) & 0x07);
        }
    }

    /// <summary>
    ///     Applies LFO depth bits and reports whether every vibrato phase-increment cache must be rebuilt.
    /// </summary>
    internal static bool ConfigureDepth(Opl3Chip chip, byte value)
    {
        var tremoloShift = (byte)((((value >> 7) ^ 1) << 1) + 2);
        var vibratoShift = (byte)(((value >> 6) & 0x01) ^ 1);
        if (chip.TremoloShift != tremoloShift)
        {
            chip.TremoloDirty = true;
            chip.TremoloShift = tremoloShift;
        }

        if (chip.VibratoShift == vibratoShift)
        {
            return false;
        }

        chip.VibratoShift = vibratoShift;
        return true;
    }
}
