using NukedOPL3Sharp.MidiPlayer.Core.Synth;

namespace NukedOPL3Sharp.MidiPlayer.Core.Playback;

public interface IPlaybackEngine
{
    TrackInfo Track { get; }
    int SampleRate { get; }

    bool Looping { get; set; }
    float MasterVolume { get; set; }

    long CurrentSample { get; }
    long TotalSamples { get; }

    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    void Reset();
    void SeekTo(TimeSpan position);
    void Render(Span<short> interleavedStereo);

    void SetOpl3Controls(Opl3ControlState controls);
    void SetStereoEnabled(bool enabled);
    void WriteOplRegister(ushort register, byte value);
}
