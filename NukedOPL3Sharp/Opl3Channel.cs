// SPDX-FileCopyrightText: 2013-2026 Nuked-OPL3 by nukeykt
// SPDX-License-Identifier: LGPL-2.1-only

namespace NukedOPL3Sharp;

// struct _opl3_channel {
//     opl3_slot *slotz[2];/*Don't use "slots" keyword to avoid conflict with Qt applications*/
//     opl3_channel *pair;
//     opl3_chip *chip;
//     int16_t *out[4];
//
// #if OPL_ENABLE_STEREOEXT
//     int32_t leftpan;
//     int32_t rightpan;
// #endif
//
//     uint8_t chtype;
//     uint16_t f_num;
//     uint8_t block;
//     uint8_t fb;
//     uint8_t con;
//     uint8_t alg;
//     uint8_t ksv;
//     uint16_t cha, chb;
//     uint16_t chc, chd;
//     uint8_t ch_num;
// };
public sealed class Opl3Channel
{
    public Opl3Operator[] Slotz { get; } = new Opl3Operator[2];
    public Opl3Channel? Pair { get; set; }
    public Opl3Chip? Chip { get; set; }
    public ShortSignalSource[] Out { get; } = new ShortSignalSource[4];
#if OPL_ENABLE_STEREOEXT
    public int LeftPan;
    public int RightPan;
#endif
    public ChannelType ChannelType;
    public ushort FNumber;
    public byte Block;
    public byte Feedback;
    public byte Connection;
    public byte Algorithm;
    public byte KeyScaleValue;
    public ushort Cha;
    public ushort Chb;
    public ushort Chc;
    public ushort Chd;
    public byte ChannelNumber;
}
