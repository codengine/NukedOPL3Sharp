namespace NukedOPL3Sharp.MidiPlayer.Core.Playback;

public sealed record TrackInfo(string Path, string DisplayName, TimeSpan Duration, long TotalSamples);