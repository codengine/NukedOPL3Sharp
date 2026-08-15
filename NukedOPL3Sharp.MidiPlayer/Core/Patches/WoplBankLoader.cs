// SPDX-FileCopyrightText: 2021-2024 Devin Acker
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace NukedOPL3Sharp.MidiPlayer.Core.Patches;

public static class WoplBankLoader
{
    public static Dictionary<ushort, OplPatch> LoadFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return LoadFromBytes(bytes);
    }

    public static Dictionary<ushort, OplPatch> LoadFromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 19)
        {
            throw new InvalidDataException("WOPL file too small.");
        }

        // "WOPL3-BANK\0"
        if (!data[..10].SequenceEqual("WOPL3-BANK"u8) || data[10] != 0)
        {
            throw new InvalidDataException("Not a WOPL3-BANK file.");
        }

        var version = (ushort)(data[11] | (data[12] << 8));
        var numMelody = (ushort)((data[13] << 8) | data[14]);
        var numPerc = (ushort)((data[15] << 8) | data[16]);

        if (version > 3)
        {
            throw new InvalidDataException($"Unsupported WOPL version: {version}.");
        }

        const uint bankOffset = 19;
        var patchOffset = bankOffset + 34u * (uint)(numMelody + numPerc);

        var instSize = version >= 3 ? 66u : 62u;
        var bankInfoSize = version >= 2 ? 34u : 0u;

        var expectedMin = (uint)(numMelody + numPerc) * (128u * instSize + bankInfoSize);
        if (data.Length < expectedMin)
        {
            throw new InvalidDataException("Truncated WOPL bank.");
        }

        var patches = new Dictionary<ushort, OplPatch>();

        for (var i = 0u; i < 128u * (uint)(numMelody + numPerc); i++)
        {
            var key = (ushort)(i & 0x7f);

            if (version >= 2)
            {
                var bank = i >> 7;
                var bankInfoPos = (int)(bankOffset + 34u * bank);
                var bankInfo = data.Slice(bankInfoPos, 34);

                if (bank >= numMelody)
                {
                    key |= (ushort)((bankInfo[32] << 8) | 0x80);
                }
                else if (bankInfo[32] != 0)
                {
                    key |= (ushort)(bankInfo[32] << 8);
                }
                else if (bankInfo[33] != 0)
                {
                    key |= (ushort)(bankInfo[33] << 8);
                }
            }

            var patchPos = (int)(patchOffset + instSize * i);
            var bytes = data.Slice(patchPos, (int)instSize);

            // skip blank or unsupported rhythm-mode patches
            if ((bytes[39] & 0x3c) != 0)
            {
                continue;
            }

            var patch = new OplPatch
            {
                Name = bytes[0] != 0 ? ReadCString(bytes[..31]) : OplPatchNames.Names[key & 0xff]
            };

            patch.Voices[0].Tune = (sbyte)bytes[33];
            patch.Voices[0].Tune -= 12;
            patch.Voices[1].Tune = (sbyte)bytes[35];
            patch.Voices[1].Tune -= 12;
            patch.Velocity = (sbyte)bytes[36];
            patch.Voices[1].FineTune = MidiCalcBend((sbyte)bytes[37] / 64.0);
            patch.FixedNote = bytes[38];
            patch.FourOp = (bytes[39] & 3) == 1;
            patch.DualTwoOp = (bytes[39] & 3) == 3;
            patch.Voices[0].Conn = bytes[40];
            patch.Voices[1].Conn = bytes[41];

            var pos = 42;
            for (var op = 0; op < 4; op++)
            {
                var voice = patch.Voices[op / 2];
                var n = (op % 2) ^ 1;

                voice.OpMode[n] = bytes[pos++];
                voice.OpKsr[n] = (byte)(bytes[pos] & 0xc0);
                voice.OpLevel[n] = (byte)(bytes[pos++] & 0x3f);
                voice.OpAd[n] = bytes[pos++];
                voice.OpSr[n] = bytes[pos++];
                voice.OpWave[n] = bytes[pos++];
            }

            patches[key] = patch;
        }

        return patches;
    }

    private static string ReadCString(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        if (end < 0)
        {
            end = bytes.Length;
        }

        return Encoding.ASCII.GetString(bytes[..end]);
    }

    private static double MidiCalcBend(double semitones)
    {
        return Math.Pow(2, semitones / 12.0);
    }
}
