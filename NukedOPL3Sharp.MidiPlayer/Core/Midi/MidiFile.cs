using System.Buffers.Binary;

namespace NukedOPL3Sharp.MidiPlayer.Core.Midi;

public sealed class MidiFile
{
    private MidiFile(ushort format, ushort trackCount, short division, List<MidiEvent> events)
    {
        Format = format;
        TrackCount = trackCount;
        Division = division;
        Events = events;
    }

    public ushort Format { get; }
    public ushort TrackCount { get; }
    public short Division { get; }

    public IReadOnlyList<MidiEvent> Events { get; }

    public static MidiFile Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return FromBytes(bytes);
    }

    public static MidiFile FromBytes(ReadOnlySpan<byte> data)
    {
        var r = new MidiReader(data);

        if (!r.TryReadChunkHeader(out var type, out var len) || type != MidiChunkType.MThd)
        {
            throw new InvalidDataException("Invalid MIDI file: missing MThd header.");
        }

        if (len < 6)
        {
            throw new InvalidDataException("Invalid MIDI file: MThd chunk too small.");
        }

        var header = r.ReadBytes(len);
        var format = BinaryPrimitives.ReadUInt16BigEndian(header[..2]);
        var nTracks = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(2, 2));
        var division = unchecked((short)BinaryPrimitives.ReadUInt16BigEndian(header.Slice(4, 2)));

        var trackEvents = new List<(long Tick, int Order, MidiEvent Ev)>();
        var order = 0;

        for (var t = 0; t < nTracks; t++)
        {
            if (!r.TryReadChunkHeader(out var tType, out var tLen) || tType != MidiChunkType.MTrk)
            {
                throw new InvalidDataException("Invalid MIDI file: missing MTrk chunk.");
            }

            var trackData = r.ReadBytes(tLen);
            ParseTrack(trackData, ref order, trackEvents);
        }

        // Merge all tracks by absolute tick; preserve parse order for same tick.
        trackEvents.Sort((a, b) =>
        {
            var c = a.Tick.CompareTo(b.Tick);
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        });

        var merged = new List<MidiEvent>(trackEvents.Count);
        long lastTick = 0;
        foreach (var (tick, _, ev) in trackEvents)
        {
            var delta = tick - lastTick;
            if (delta < 0)
            {
                delta = 0;
            }

            lastTick = tick;
            merged.Add(ev with { DeltaTicks = (uint)delta });
        }

        return new MidiFile(format, nTracks, division, merged);
    }

    private static void ParseTrack(ReadOnlySpan<byte> trackData,
        ref int order,
        List<(long Tick, int Order, MidiEvent Ev)> output)
    {
        var r = new MidiReader(trackData);

        long tick = 0;
        byte runningStatus = 0;

        while (!r.EndOfData)
        {
            var delta = r.ReadVarLen();
            tick += delta;

            var statusOrData = r.ReadByte();
            byte status;
            if ((statusOrData & 0x80) != 0)
            {
                status = statusOrData;
                if (status < 0xF0)
                {
                    runningStatus = status;
                }
            }
            else
            {
                if (runningStatus == 0)
                {
                    throw new InvalidDataException("Invalid MIDI track: running status without prior status.");
                }

                status = runningStatus;
                r.UnreadByte();
            }

            if (status == 0xFF)
            {
                var metaType = r.ReadByte();
                var metaLen = (int)r.ReadVarLen();
                var meta = r.ReadBytes(metaLen);

                if (metaType == 0x2F)
                {
                    break;
                }

                if (metaType == 0x51 && metaLen == 3)
                {
                    var tempo = (meta[0] << 16) | (meta[1] << 8) | meta[2];
                    output.Add((tick, order++,
                        new MidiEvent(0, MidiEventKind.Tempo, 0, 0, 0, BitConverter.GetBytes(tempo))));
                }

                continue;
            }

            if (status is 0xF0 or 0xF7)
            {
                var syxLen = (int)r.ReadVarLen();
                _ = r.ReadBytes(syxLen);
                continue;
            }

            var kindNibble = (byte)(status >> 4);
            var channel = (byte)(status & 0x0F);

            switch (kindNibble)
            {
                case 0x8:
                {
                    var note = r.ReadByte();
                    var vel = r.ReadByte();
                    output.Add((tick, order++, new MidiEvent(0, MidiEventKind.NoteOff, channel, note, vel, [])));
                    break;
                }
                case 0x9:
                {
                    var note = r.ReadByte();
                    var vel = r.ReadByte();
                    var kind = vel == 0 ? MidiEventKind.NoteOff : MidiEventKind.NoteOn;
                    output.Add((tick, order++, new MidiEvent(0, kind, channel, note, vel, [])));
                    break;
                }
                case 0xA:
                    _ = r.ReadByte();
                    _ = r.ReadByte();
                    break;
                case 0xB:
                {
                    var cc = r.ReadByte();
                    var val = r.ReadByte();
                    output.Add((tick, order++, new MidiEvent(0, MidiEventKind.ControlChange, channel, cc, val, [])));
                    break;
                }
                case 0xC:
                {
                    var program = r.ReadByte();
                    output.Add((tick, order++, new MidiEvent(0, MidiEventKind.ProgramChange, channel, program, 0, [])));
                    break;
                }
                case 0xD:
                    _ = r.ReadByte();
                    break;
                case 0xE:
                {
                    var lsb = r.ReadByte();
                    var msb = r.ReadByte();
                    output.Add((tick, order++, new MidiEvent(0, MidiEventKind.PitchBend, channel, lsb, msb, [])));
                    break;
                }
                default:
                    throw new InvalidDataException($"Unsupported MIDI event status: 0x{status:X2}.");
            }
        }
    }

    private enum MidiChunkType
    {
        MThd,
        MTrk
    }

    private ref struct MidiReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _pos;

        public MidiReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _pos = 0;
        }

        public bool EndOfData => _pos >= _data.Length;

        public bool TryReadChunkHeader(out MidiChunkType type, out int length)
        {
            type = default;
            length = 0;
            if (_data.Length - _pos < 8)
            {
                return false;
            }

            var t = BinaryPrimitives.ReadUInt32BigEndian(_data.Slice(_pos, 4));
            _pos += 4;
            length = (int)BinaryPrimitives.ReadUInt32BigEndian(_data.Slice(_pos, 4));
            _pos += 4;

            type = t switch
            {
                0x4D546864 => MidiChunkType.MThd, // "MThd"
                0x4D54726B => MidiChunkType.MTrk, // "MTrk"
                _ => throw new InvalidDataException("Invalid MIDI file: unknown chunk type.")
            };

            return _data.Length - _pos < length
                ? throw new InvalidDataException("Invalid MIDI file: chunk length exceeds file size.")
                : true;
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (_data.Length - _pos < count)
            {
                throw new InvalidDataException("Unexpected end of MIDI data.");
            }

            var s = _data.Slice(_pos, count);
            _pos += count;
            return s;
        }

        public byte ReadByte()
        {
            return _pos >= _data.Length
                ? throw new InvalidDataException("Unexpected end of MIDI data.")
                : _data[_pos++];
        }

        public void UnreadByte()
        {
            if (_pos == 0)
            {
                throw new InvalidOperationException("Cannot unread at start.");
            }

            _pos--;
        }

        public uint ReadVarLen()
        {
            uint value = 0;
            for (var i = 0; i < 4; i++)
            {
                var b = ReadByte();
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    return value;
                }
            }

            throw new InvalidDataException("Invalid MIDI: variable-length quantity too long.");
        }
    }
}
