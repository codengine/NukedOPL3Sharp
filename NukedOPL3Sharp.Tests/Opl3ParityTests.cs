using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NukedOPL3Sharp.Tests;

/// <summary>
///     Locks synthesized output to the Nuked OPL3 1.8 reference implementation.
/// </summary>
public sealed class Opl3ParityTests
{
    /// <summary>
    ///     Preserves all eight waveform equations and the reference feedback timing.
    /// </summary>
    [Theory]
    [InlineData(0, "daeeccac43b2463b55f201bcc5032927645c0441084c44438daa2168f67acb86")]
    [InlineData(1, "e5f9ca6680a75db74557e768130b670c74f1953ac1898671ecf9acffe431a9b3")]
    [InlineData(2, "004eafc7b0238ee2802f43a7e0959e7d039c20cd0e9a5e49e868600bc9162688")]
    [InlineData(3, "cb38e92548785af05d4860f404f241c984dc86aecd0bdbd8584e547a4d5e511e")]
    [InlineData(4, "f3ef92f96d42e22d8dbffb7dc04c5959d81e1eb3ef8971a418858beef6b3d5eb")]
    [InlineData(5, "c7816f4f0c8419f58888f752047164bb1a3a8151b3729e1892c0832e70727352")]
    [InlineData(6, "f79fa9419c8abec32aea243a1c7dc771c267b9d892100ca5a8d4490aea0754a6")]
    [InlineData(7, "21d87e16aa336c99fa11f851fc8080e1475243ab0cabca8c883d45d4bd72eadd")]
    public void Generate4Channels_MatchesReferenceWaveform(byte waveform, string expectedHash)
    {
        var chip = ConfigureWaveform(waveform);

        Assert.Equal(expectedHash, HashSamples(chip, 8_192));
    }

    /// <summary>
    ///     Preserves shared 4-op frequency state, A0 KSL updates, and drum key release when rhythm mode ends.
    /// </summary>
    [Fact]
    public void Generate4Channels_MatchesReferenceModeTransitions()
    {
        var chip = ConfigureFourOpChannels();

        Assert.Equal("9afc3727fd353d3de8eef99cf8ea3098327cca10364857d0dad531d8f0817807", HashSamples(chip, 16_384));

        chip.WriteRegister(0x1a1, 0xff);
        chip.WriteRegister(0x1a4, 0x00);
        Assert.Equal("6df96a7f54eca9c8bf0ff3bcc41d00a1f83e3e0862e05a71776103829c9def16", HashSamples(chip, 8_192));

        chip.WriteRegister(0x0bd, 0xff);
        Assert.Equal("29ec198d5d42d37d4b8f27f7f0375e6b12c9da9a95d821762411c7b87f40e213", HashSamples(chip, 8_192));

        chip.WriteRegister(0x0bd, 0x00);
        Assert.Equal("e5168aaab696c0c30bdd1ecf44ec0de2e248b1bfeb5dc8110b8b3dfae67eb3e6", HashSamples(chip, 8_192));
    }

    private static Opl3Chip ConfigureWaveform(byte waveform)
    {
        var chip = new Opl3Chip();
        chip.Reset(49_716);
        chip.WriteRegister(0x105, 0x01);
        chip.WriteRegister(0x020, 0x01);
        chip.WriteRegister(0x023, 0x01);
        chip.WriteRegister(0x040, 0x00);
        chip.WriteRegister(0x043, 0x3f);
        chip.WriteRegister(0x060, 0xf0);
        chip.WriteRegister(0x063, 0xf0);
        chip.WriteRegister(0x080, 0x00);
        chip.WriteRegister(0x083, 0x00);
        chip.WriteRegister(0x0e0, waveform);
        chip.WriteRegister(0x0e3, 0x00);
        chip.WriteRegister(0x0c0, 0x3f);
        chip.WriteRegister(0x0a0, 0xff);
        chip.WriteRegister(0x0b0, 0x31);
        return chip;
    }

    private static Opl3Chip ConfigureFourOpChannels()
    {
        var chip = new Opl3Chip();
        chip.Reset(49_716);
        chip.WriteRegister(0x105, 0x01);
        chip.WriteRegister(0x104, 0x3f);

        for (ushort bank = 0; bank <= 0x100; bank += 0x100)
        {
            for (ushort slot = 0; slot < 0x20; slot++)
            {
                chip.WriteRegister((ushort)(bank + 0x20 + slot), (byte)(0x80 | (slot & 0x0f)));
                chip.WriteRegister((ushort)(bank + 0x40 + slot), (byte)(slot * 3));
                chip.WriteRegister((ushort)(bank + 0x60 + slot), (byte)(0xf0 | (slot & 0x0f)));
                chip.WriteRegister((ushort)(bank + 0x80 + slot), (byte)(slot & 0x0f));
                chip.WriteRegister((ushort)(bank + 0xe0 + slot), (byte)(slot & 0x07));
            }
        }

        for (ushort bank = 0; bank <= 0x100; bank += 0x100)
        {
            for (ushort channel = 0; channel < 9; channel++)
            {
                chip.WriteRegister((ushort)(bank + 0xc0 + channel), (byte)(0xf0 | (channel & 0x0f)));
                chip.WriteRegister((ushort)(bank + 0xa0 + channel), (byte)(0x40 + channel * 13));
                chip.WriteRegister((ushort)(bank + 0xb0 + channel),
                    (byte)(0x20 | ((channel & 0x07) << 2) | (channel & 0x03)));
            }
        }

        return chip;
    }

    private static string HashSamples(Opl3Chip chip, int frameCount)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<short> samples = stackalloc short[4];
        Span<byte> frame = stackalloc byte[8];
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            chip.Generate4Channels(samples);
            for (var channel = 0; channel < samples.Length; channel++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(frame.Slice(channel * 2, 2), samples[channel]);
            }

            hash.AppendData(frame);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
