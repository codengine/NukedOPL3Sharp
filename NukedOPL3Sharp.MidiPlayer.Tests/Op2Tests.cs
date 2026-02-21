using NukedOPL3Sharp.MidiPlayer.Core.Patches;

namespace NukedOPL3Sharp.MidiPlayer.Tests;

public sealed class Op2Tests
{
    [Fact]
    public void Op2BankLoader_LoadsMinimalOp2()
    {
        var bytes = BuildMinimalOp2();
        var patches = Op2BankLoader.LoadFromBytes(bytes);

        Assert.NotEmpty(patches);
        Assert.True(patches.ContainsKey(0));
        Assert.Equal("OP2Test", patches[0].Name);
    }

    private static byte[] BuildMinimalOp2()
    {
        // OP2 layout (as used in ymfmidi):
        // - 8 byte header "#OPL_II#"
        // - 175 patches * 36 bytes
        // - 175 names * 32 bytes
        const int patchCount = 175;
        const int patchSize = 36;
        const int nameSize = 32;
        const int headerSize = 8;

        const int total = headerSize + patchCount * patchSize + patchCount * nameSize;
        var data = new byte[total];

        "#OPL_II#"u8.CopyTo(data);

        // Patch 0 name
        const int nameOffset = headerSize + patchCount * patchSize;
        "OP2Test"u8.CopyTo(data.AsSpan(nameOffset));

        return data;
    }
}