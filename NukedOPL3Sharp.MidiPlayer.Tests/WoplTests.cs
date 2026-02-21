using NukedOPL3Sharp.MidiPlayer.Core.Patches;

namespace NukedOPL3Sharp.MidiPlayer.Tests;

public sealed class WoplTests
{
    [Fact]
    public void WoplBankLoader_LoadsMinimalWopl()
    {
        var bytes = BuildMinimalWopl();
        var patches = WoplBankLoader.LoadFromBytes(bytes);

        Assert.NotEmpty(patches);
        Assert.True(patches.ContainsKey(0), "Expected patch 0 to exist (bank 0, program 0).");
        Assert.Equal("TestPatch", patches[0].Name);
    }

    private static byte[] BuildMinimalWopl()
    {
        // Minimal WOPL3-BANK v3 with 1 melodic bank, 0 percussion banks.
        const ushort version = 3;
        const ushort numMelody = 1;
        const ushort numPerc = 0;
        const int instSize = 66;
        const int bankInfoSize = 34;
        const int headerSize = 19;

        const int patchOffset = headerSize + bankInfoSize * (numMelody + numPerc);
        const int patchBytes = instSize * 128 * (numMelody + numPerc);

        var data = new byte[patchOffset + patchBytes];

        // "WOPL3-BANK\0"
        var sig = "WOPL3-BANK"u8;
        sig.CopyTo(data);
        data[10] = 0;

        data[11] = version & 0xff;
        data[12] = version >> 8;

        // mixed endianness for counts
        data[13] = numMelody >> 8;
        data[14] = numMelody & 0xff;
        data[15] = numPerc >> 8;
        data[16] = numPerc & 0xff;

        // bank info: bytes[32]=LSB, bytes[33]=MSB, leave 0
        // patch 0: simple named patch, 2-op.
        "TestPatch"u8.CopyTo(data.AsSpan(patchOffset));
        data[patchOffset + 39] = 0; // 2-op, not rhythm
        data[patchOffset + 40] = 0; // conn/feedback voice 0
        data[patchOffset + 41] = 0; // conn/feedback voice 1

        // Operator bytes start at +42; leave defaults (0) but ensure they don't trigger rhythm skip.
        return data;
    }
}