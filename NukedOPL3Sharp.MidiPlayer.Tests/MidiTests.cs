using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NukedOPL3Sharp.MidiPlayer.Core.Midi;
using NukedOPL3Sharp.MidiPlayer.Core.Playback;

namespace NukedOPL3Sharp.MidiPlayer.Tests;

public sealed class MidiTests
{
    [Fact]
    public void MidiFile_FromBytes_ParsesAndMergesEvents()
    {
        var bytes = BuildMinimalMidi();
        var midi = MidiFile.FromBytes(bytes);

        Assert.NotEmpty(midi.Events);
        Assert.Equal(1, midi.TrackCount);
        Assert.Contains(midi.Events, e => e.Kind == MidiEventKind.NoteOn);
    }

    [Fact]
    public void MidiFile_FromBytes_DoesNotEndPlaybackOnFirstTrackEot()
    {
        var bytes = BuildTwoTrackEotThenNote();
        var midi = MidiFile.FromBytes(bytes);

        Assert.Contains(midi.Events, e => e.Kind == MidiEventKind.NoteOn);
        Assert.DoesNotContain(midi.Events, e => e.Kind == MidiEventKind.EndOfTrack);
        Assert.Equal(MidiEventKind.NoteOn, midi.Events[0].Kind);
    }

    [Fact]
    public void MidiSequence_ProducesDelaySamples()
    {
        var bytes = BuildMinimalMidi();
        var midi = MidiFile.FromBytes(bytes);
        var seq = new MidiSequence(midi);

        var delay = seq.UpdateAndGetDelaySamples(NullMidiSink.Instance, 44100);
        Assert.True(delay > 0);
    }

    [Fact]
    public void MidiSequence_UsesRoundForDelaySamples()
    {
        var bytes = BuildOneTickMidi();
        var midi = MidiFile.FromBytes(bytes);
        var seq = new MidiSequence(midi);

        var delay = seq.UpdateAndGetDelaySamples(NullMidiSink.Instance, 44100);
        Assert.Equal(46u, delay); // 1 tick at 500000us/qn, 480 TPQ => 45.9375 samples at 44100Hz -> round() = 46
    }

    private static byte[] BuildMinimalMidi()
    {
        // Format 0, 1 track, division 480.
        // Track:
        //   delta 0: Note On ch0 note60 vel64
        //   delta 480: Note Off ch0 note60 vel64
        //   delta 0: End of track

        var track = new List<byte>
        {
            0x00,
            0x90,
            60,
            64
        };

        track.AddRange(VarLen(480));
        track.Add(0x80);
        track.Add(60);
        track.Add(64);

        track.Add(0x00);
        track.Add(0xFF);
        track.Add(0x2F);
        track.Add(0x00);

        var bytes = new List<byte>();
        bytes.AddRange("MThd"u8.ToArray());
        bytes.AddRange([0x00, 0x00, 0x00, 0x06]);
        bytes.AddRange([0x00, 0x00]); // format 0
        bytes.AddRange([0x00, 0x01]); // one track
        bytes.AddRange([0x01, 0xE0]); // 480

        bytes.AddRange("MTrk"u8.ToArray());
        var lenPos = bytes.Count;
        bytes.AddRange([0, 0, 0, 0]);
        bytes.AddRange(track);

        var trackLen = track.Count;
        BinaryPrimitives.WriteUInt32BigEndian(CollectionsMarshal.AsSpan(bytes).Slice(lenPos, 4), (uint)trackLen);

        return bytes.ToArray();
    }

    private static byte[] BuildOneTickMidi()
    {
        var track = new List<byte>
        {
            0x00,
            0x90,
            60,
            64
        };

        track.AddRange(VarLen(1));
        track.Add(0x80);
        track.Add(60);
        track.Add(64);

        track.Add(0x00);
        track.Add(0xFF);
        track.Add(0x2F);
        track.Add(0x00);

        return BuildMidiFile(0, 480, track.ToArray());
    }

    private static byte[] BuildTwoTrackEotThenNote()
    {
        // Format 1, 2 tracks, division 480.
        // Track 0: immediate end-of-track
        // Track 1: note on/off
        var t0 = new List<byte> { 0x00, 0xFF, 0x2F, 0x00 };

        var t1 = new List<byte>
        {
            0x00,
            0x90,
            60,
            64
        };

        t1.AddRange(VarLen(480));
        t1.Add(0x80);
        t1.Add(60);
        t1.Add(64);

        t1.Add(0x00);
        t1.Add(0xFF);
        t1.Add(0x2F);
        t1.Add(0x00);

        return BuildMidiFile(1, 480, t0.ToArray(), t1.ToArray());
    }

    private static byte[] BuildMidiFile(ushort format, ushort division, params byte[][] tracks)
    {
        var bytes = new List<byte>();
        bytes.AddRange("MThd"u8.ToArray());
        bytes.AddRange([0x00, 0x00, 0x00, 0x06]);

        bytes.Add(0);
        bytes.Add(0);
        BinaryPrimitives.WriteUInt16BigEndian(CollectionsMarshal.AsSpan(bytes).Slice(bytes.Count - 2, 2), format);

        bytes.Add(0);
        bytes.Add(0);
        BinaryPrimitives.WriteUInt16BigEndian(CollectionsMarshal.AsSpan(bytes).Slice(bytes.Count - 2, 2),
            (ushort)tracks.Length);

        bytes.Add(0);
        bytes.Add(0);
        BinaryPrimitives.WriteUInt16BigEndian(CollectionsMarshal.AsSpan(bytes).Slice(bytes.Count - 2, 2), division);

        foreach (var track in tracks)
        {
            bytes.AddRange("MTrk"u8.ToArray());
            var lenPos = bytes.Count;
            bytes.AddRange([0, 0, 0, 0]);
            bytes.AddRange(track);
            BinaryPrimitives.WriteUInt32BigEndian(CollectionsMarshal.AsSpan(bytes).Slice(lenPos, 4),
                (uint)track.Length);
        }

        return bytes.ToArray();
    }

    private static IEnumerable<byte> VarLen(int value)
    {
        var buffer = value & 0x7F;
        while ((value >>= 7) > 0)
        {
            buffer <<= 8;
            buffer |= (value & 0x7F) | 0x80;
        }

        while (true)
        {
            yield return (byte)buffer;
            if ((buffer & 0x80) != 0)
            {
                buffer >>= 8;
            }
            else
            {
                break;
            }
        }
    }
}
