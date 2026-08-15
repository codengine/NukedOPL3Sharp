using BenchmarkDotNet.Attributes;

namespace NukedOPL3Sharp.Benchmarks;

/// <summary>
///     Measures the chip-core rendering regimes targeted by Nuked-OPL3-fast.
/// </summary>
[MemoryDiagnoser]
public class Opl3Benchmarks
{
    private const int FramesPerInvoke = 4_096;
    private static readonly byte[] SlotOffsets =
    [
        0, 1, 2, 3, 4, 5,
        8, 9, 10, 11, 12, 13,
        16, 17, 18, 19, 20, 21
    ];
    private static readonly RegisterWrite[] BroadWriteStream = BuildBroadWriteStream();

    private readonly short[] _buffer = new short[4];
    private Opl3Chip _broadChip = null!;
    private int _broadWriteIndex;
    private Opl3Chip _denseChip = null!;
    private Opl3Chip _fourOpChip = null!;
    private Opl3Chip _rhythmChip = null!;
    private Opl3Chip _silentChip = null!;
    private Opl3Chip _singleVoiceChip = null!;

    /// <summary>
    ///     Creates stable starting states once so each measured loop contains only the operations named by that workload.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _silentChip = CreateChip();
        _singleVoiceChip = CreateSingleVoiceChip();
        _denseChip = CreateDenseChip();
        _fourOpChip = CreateFourOpChip();
        _rhythmChip = CreateRhythmChip();
        _broadChip = CreateChip(48_000);
    }

    /// <summary>
    ///     Measures a reset chip whose operators can remain dormant.
    /// </summary>
    [Benchmark(OperationsPerInvoke = FramesPerInvoke)]
    public short GenerateSilent()
    {
        return GenerateFrames(_silentChip);
    }

    /// <summary>
    ///     Measures one active 2-op voice while the remaining operators stay dormant.
    /// </summary>
    [Benchmark(OperationsPerInvoke = FramesPerInvoke)]
    public short GenerateSingleVoice()
    {
        return GenerateFrames(_singleVoiceChip);
    }

    /// <summary>
    ///     Measures all 18 2-op channels with both operators active and routed to every output.
    /// </summary>
    [Benchmark(OperationsPerInvoke = FramesPerInvoke)]
    public short GenerateDense()
    {
        return GenerateFrames(_denseChip);
    }

    /// <summary>
    ///     Measures six enabled 4-op pairs spanning all four algorithms alongside the remaining 2-op channels.
    /// </summary>
    [Benchmark(OperationsPerInvoke = FramesPerInvoke)]
    public short GenerateFourOp()
    {
        return GenerateFrames(_fourOpChip);
    }

    /// <summary>
    ///     Measures active percussion operators and the rhythm-specific phase paths.
    /// </summary>
    [Benchmark(OperationsPerInvoke = FramesPerInvoke)]
    public short GenerateRhythm()
    {
        return GenerateFrames(_rhythmChip);
    }

    /// <summary>
    ///     Measures buffered writes across all register groups while exercising mode transitions and resampling.
    /// </summary>
    [Benchmark(OperationsPerInvoke = FramesPerInvoke)]
    public short GenerateBroadBufferedResampled()
    {
        for (var frame = 0; frame < FramesPerInvoke; frame++)
        {
            if ((frame & 1) == 0)
            {
                var write = BroadWriteStream[_broadWriteIndex++];
                if (_broadWriteIndex == BroadWriteStream.Length)
                {
                    _broadWriteIndex = 0;
                }

                _broadChip.WriteRegisterBuffered(write.Register, write.Value);
            }

            _broadChip.Generate4ChannelsResampled(_buffer);
        }

        return (short)(_buffer[0] ^ _buffer[1] ^ _buffer[2] ^ _buffer[3]);
    }

    private short GenerateFrames(Opl3Chip chip)
    {
        for (var frame = 0; frame < FramesPerInvoke; frame++)
        {
            chip.Generate4Channels(_buffer);
        }

        return (short)(_buffer[0] ^ _buffer[1] ^ _buffer[2] ^ _buffer[3]);
    }

    private static Opl3Chip CreateChip(uint sampleRate = 49_716)
    {
        var chip = new Opl3Chip();
        chip.Reset(sampleRate);
        return chip;
    }

    private static Opl3Chip CreateSingleVoiceChip()
    {
        var chip = CreateChip();
        chip.WriteRegister(0x105, 0x01);
        chip.WriteRegister(0x020, 0xf1);
        chip.WriteRegister(0x023, 0xf1);
        chip.WriteRegister(0x040, 0x00);
        chip.WriteRegister(0x043, 0x00);
        chip.WriteRegister(0x060, 0xf4);
        chip.WriteRegister(0x063, 0xf4);
        chip.WriteRegister(0x080, 0x45);
        chip.WriteRegister(0x083, 0x45);
        chip.WriteRegister(0x0e0, 0x00);
        chip.WriteRegister(0x0e3, 0x00);
        chip.WriteRegister(0x0c0, 0xf1);
        chip.WriteRegister(0x0a0, 0x98);
        chip.WriteRegister(0x0b0, 0x31);
        return chip;
    }

    private static Opl3Chip CreateDenseChip()
    {
        var chip = CreateChip();
        chip.WriteRegister(0x105, 0x01);

        for (ushort bank = 0; bank <= 0x100; bank += 0x100)
        {
            foreach (var slot in SlotOffsets)
            {
                chip.WriteRegister((ushort)(bank + 0x20 + slot), (byte)(0xf1 + (slot & 0x03)));
                chip.WriteRegister((ushort)(bank + 0x40 + slot), (byte)(slot & 0x0f));
                chip.WriteRegister((ushort)(bank + 0x60 + slot), (byte)(0xf4 + (slot & 0x03)));
                chip.WriteRegister((ushort)(bank + 0x80 + slot), (byte)(0x45 + (slot & 0x03)));
                chip.WriteRegister((ushort)(bank + 0xe0 + slot), (byte)(slot & 0x07));
            }

            for (ushort channel = 0; channel < 9; channel++)
            {
                chip.WriteRegister((ushort)(bank + 0xc0 + channel), 0xf1);
                chip.WriteRegister((ushort)(bank + 0xa0 + channel), (byte)(0x40 + channel * 17));
                chip.WriteRegister((ushort)(bank + 0xb0 + channel),
                    (byte)(0x20 | ((channel & 0x07) << 2) | (channel & 0x03)));
            }
        }

        return chip;
    }

    private static Opl3Chip CreateFourOpChip()
    {
        var chip = CreateDenseChip();
        byte[][] connections =
        [
            [0, 0, 1, 0, 1, 0],
            [1, 0, 0, 1, 0, 1]
        ];

        for (ushort bank = 0; bank <= 0x100; bank += 0x100)
        {
            var bankConnections = connections[bank >> 8];
            for (ushort channel = 0; channel < 6; channel++)
            {
                chip.WriteRegister((ushort)(bank + 0xc0 + channel), (byte)(0xf0 | bankConnections[channel]));
            }
        }

        chip.WriteRegister(0x104, 0x3f);
        return chip;
    }

    private static Opl3Chip CreateRhythmChip()
    {
        var chip = CreateDenseChip();
        chip.WriteRegister(0x0bd, 0xff);
        return chip;
    }

    private static RegisterWrite[] BuildBroadWriteStream()
    {
        var writes = new List<RegisterWrite>
        {
            new(0x105, 0x01),
            new(0x104, 0x3f),
            new(0x008, 0x40),
            new(0x0bd, 0xc0)
        };

        for (ushort bank = 0; bank <= 0x100; bank += 0x100)
        {
            foreach (var slot in SlotOffsets)
            {
                writes.Add(new RegisterWrite((ushort)(bank + 0x20 + slot), (byte)(0xf1 + (slot & 0x03))));
                writes.Add(new RegisterWrite((ushort)(bank + 0x40 + slot), (byte)((slot * 3) & 0x3f)));
                writes.Add(new RegisterWrite((ushort)(bank + 0x60 + slot), (byte)(0xf4 + (slot & 0x03))));
                writes.Add(new RegisterWrite((ushort)(bank + 0x80 + slot), (byte)(0x45 + (slot & 0x03))));
                writes.Add(new RegisterWrite((ushort)(bank + 0xe0 + slot), (byte)(slot & 0x07)));
            }

            for (ushort channel = 0; channel < 9; channel++)
            {
                writes.Add(new RegisterWrite((ushort)(bank + 0xc0 + channel), (byte)(0xf0 | (channel & 0x01))));
                writes.Add(new RegisterWrite((ushort)(bank + 0xa0 + channel), (byte)(0x30 + channel * 19)));
                writes.Add(new RegisterWrite((ushort)(bank + 0xb0 + channel),
                    (byte)(0x20 | ((channel & 0x07) << 2) | (channel & 0x03))));
            }
        }

        writes.Add(new RegisterWrite(0x0bd, 0xff));
        for (ushort bank = 0; bank <= 0x100; bank += 0x100)
        {
            for (ushort channel = 0; channel < 9; channel++)
            {
                writes.Add(new RegisterWrite((ushort)(bank + 0xa0 + channel), (byte)(0xe0 - channel * 11)));
                writes.Add(new RegisterWrite((ushort)(bank + 0xb0 + channel),
                    (byte)(((channel & 0x07) << 2) | (channel & 0x03))));
                writes.Add(new RegisterWrite((ushort)(bank + 0xc0 + channel), (byte)(0x10 << (channel & 0x03))));
            }
        }

        writes.Add(new RegisterWrite(0x104, 0x00));
        writes.Add(new RegisterWrite(0x0bd, 0x00));
        writes.Add(new RegisterWrite(0x105, 0x00));
        writes.Add(new RegisterWrite(0x105, 0x01));
        writes.Add(new RegisterWrite(0x104, 0x15));
        writes.Add(new RegisterWrite(0x0bd, 0xe0));
        return writes.ToArray();
    }

    private readonly record struct RegisterWrite(ushort Register, byte Value);
}
