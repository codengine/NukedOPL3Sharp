// SPDX-FileCopyrightText: 2021-2024 Devin Acker
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace NukedOPL3Sharp.MidiPlayer.Core.Patches;

public static class Op2BankLoader
{
    public static Dictionary<ushort, OplPatch> LoadFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return LoadFromBytes(bytes);
    }

    public static Dictionary<ushort, OplPatch> LoadFromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 175 * (36 + 32) + 8)
        {
            throw new InvalidDataException("OP2 file too small.");
        }

        if (!data[..8].SequenceEqual("#OPL_II#"u8))
        {
            throw new InvalidDataException("Not an OP2 (#OPL_II#) file.");
        }

        var patches = new Dictionary<ushort, OplPatch>();

        // 128 melodic + 47 percussion (notes 35-81)
        for (var i = 0; i < 128 + 47; i++)
        {
            var key = (ushort)(i < 128 ? i : i + 35);

            var patch = new OplPatch();

            var bytes = data.Slice(8 + 36 * i, 36);

            patch.DualTwoOp = (bytes[0] & 4) != 0;
            patch.Voices[1].FineTune = MidiCalcBend((sbyte)(bytes[2] - 128) / 64.0);
            patch.FixedNote = bytes[3];

            var pos = 4;
            for (var j = 0; j < 2; j++)
            {
                var voice = patch.Voices[j];

                for (var op = 0; op < 2; op++)
                {
                    voice.OpMode[op] = bytes[pos++];
                    voice.OpAd[op] = bytes[pos++];
                    voice.OpSr[op] = bytes[pos++];
                    voice.OpWave[op] = bytes[pos++];
                    voice.OpKsr[op] = (byte)(bytes[pos++] & 0xc0);
                    voice.OpLevel[op] = (byte)(bytes[pos++] & 0x3f);

                    if (op == 0)
                    {
                        voice.Conn = bytes[pos];
                    }

                    pos++;
                }

                voice.Tune = unchecked((sbyte)bytes[pos]);
                pos += 2;
            }

            if ((patch.Voices[1].OpAd[0] | patch.Voices[1].OpAd[1]) == 0)
            {
                patch.DualTwoOp = false;
            }

            var nameBytes = data.Slice(8 + 36 * 175 + 32 * i, 32);
            patch.Name = nameBytes[0] != 0 ? ReadCString(nameBytes[..31]) : OplPatchNames.Names[key & 0xff];

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
