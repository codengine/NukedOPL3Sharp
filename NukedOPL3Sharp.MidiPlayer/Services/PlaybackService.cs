using System.Collections.Concurrent;
using NukedOPL3Sharp.MidiPlayer.Audio;
using NukedOPL3Sharp.MidiPlayer.Core.Patches;
using NukedOPL3Sharp.MidiPlayer.Core.Playback;
using NukedOPL3Sharp.MidiPlayer.Core.Synth;
using NukedOPL3Sharp.MidiPlayer.Models;

namespace NukedOPL3Sharp.MidiPlayer.Services;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused
}

public sealed class PlaybackService : IDisposable
{
    private const int SampleRate = 44100;
    private const int FramesPerBuffer = 2048;
    private const int NumBuffers = 4;

    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);

    private IPlaybackEngine? _engine;
    private Opl3ControlState _opl3Controls = Opl3ControlState.Default;
    private volatile string _patchBankDisplayName = "(none)";

    private volatile Dictionary<ushort, OplPatch> _patches;

    private long _positionSamples;
    private volatile bool _shutdown;

    private volatile PlaybackState _state = PlaybackState.Stopped;
    private volatile bool _stereoEnabled = true;

    public PlaybackService()
    {
        _patches = new Dictionary<ushort, OplPatch>();
        StatusChanged?.Invoke(this, "No patch bank loaded. Select one (.wopl / .op2).");

        _thread = new Thread(AudioThreadMain)
        {
            IsBackground = true,
            Name = "OpenAL Audio Thread"
        };
        _thread.Start();
    }

    public TrackInfo? CurrentTrack { get; private set; }

    public string PatchBankDisplayName => _patchBankDisplayName;

    public bool IsPlayingOrPaused => _state is PlaybackState.Playing or PlaybackState.Paused;

    public void Dispose()
    {
        _shutdown = true;
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
        _wake.Dispose();
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<TrackInfo?>? TrackChanged;
    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<string>? PatchBankChanged;
    public event EventHandler<TimeSpan>? SeekCompleted;

    public void LoadTrack(TrackItem track)
    {
        if (CurrentTrack?.Path == track.Path)
        {
            return;
        }

        if (_patches.Count == 0)
        {
            StatusChanged?.Invoke(this, "Cannot load track: patch bank not loaded.");
            return;
        }

        try
        {
            var patches = _patches;
            var engine = CreateEngine(track.Path, track.DisplayName, patches, SampleRate);
            Enqueue(new Command.Load(engine));
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Load failed: {ex.Message}");
        }
    }

    private static MidiPlaybackEngine CreateEngine(string path, string displayName, Dictionary<ushort, OplPatch> patches,
        int sampleRate)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = Path.GetFileName(path);
        return ext switch
        {
            ".mid" or ".midi" => new MidiPlaybackEngine(path, displayName, patches, sampleRate),
            _ when name.Contains(".mid", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(".midi", StringComparison.OrdinalIgnoreCase)
                => new MidiPlaybackEngine(path, displayName, patches, sampleRate),
            _ => throw new NotSupportedException("Unsupported music file type (expected .mid, .midi).")
        };
    }

    public void SetPatchBank(string patchPath)
    {
        Dictionary<ushort, OplPatch> patches;
        try
        {
            patches = PatchBankLoader.LoadFromFile(patchPath);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Patch bank load failed: {ex.Message}");
            return;
        }

        _patches = patches;
        _patchBankDisplayName = Path.GetFileName(patchPath);
        PatchBankChanged?.Invoke(this, _patchBankDisplayName);
        StatusChanged?.Invoke(this, $"Loaded patch bank: {_patchBankDisplayName} ({patches.Count} patches).");

        Enqueue(new Command.ReloadCurrent());
    }

    public void Play()
    {
        Enqueue(new Command.Play());
    }

    public void Pause()
    {
        Enqueue(new Command.Pause());
    }

    public void Stop()
    {
        Enqueue(new Command.Stop());
    }

    public void Seek(TimeSpan position)
    {
        Enqueue(new Command.Seek(position));
    }

    public void SetLoop(bool loop)
    {
        Enqueue(new Command.SetLoop(loop));
    }

    public void SetVolume(float volume)
    {
        Enqueue(new Command.SetVolume(volume));
    }

    public void SetOpl3Controls(Opl3ControlState controls)
    {
        _opl3Controls = controls.WithDrumMask(controls.DrumMask);
        Enqueue(new Command.SetOpl3Controls(_opl3Controls));
    }

    public void SetStereoEnabled(bool enabled)
    {
        _stereoEnabled = enabled;
        Enqueue(new Command.SetStereoEnabled(enabled));
    }

    public void WriteOplRegister(ushort register, byte value)
    {
        Enqueue(new Command.WriteOplRegister(register, value));
    }

    public TimeSpan GetPosition()
    {
        var samples = Interlocked.Read(ref _positionSamples);
        return TimeSpan.FromSeconds(samples / (double)SampleRate);
    }

    private void Enqueue(Command cmd)
    {
        _commands.Enqueue(cmd);
        _wake.Set();
    }

    private void SetState(PlaybackState state)
    {
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private void AudioThreadMain()
    {
        OpenAlStreamingOutput? output = null;
        try
        {
            output = new OpenAlStreamingOutput(SampleRate, NumBuffers);
            StatusChanged?.Invoke(this, "OpenAL initialized.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"OpenAL init failed: {ex.Message}");
        }

        // If OpenAL couldn't init, just drain commands and do nothing.
        if (output is null)
        {
            while (!_shutdown)
            {
                DrainCommandsWithoutAudio();
                _wake.WaitOne(50);
            }

            return;
        }

        using (output)
        {
            var pcm = new short[FramesPerBuffer * 2];
            var free = new Queue<uint>(output.BufferIds);
            var processed = new uint[NumBuffers];

            output.StopAndClear();

            while (!_shutdown)
            {
                ProcessCommands(output, free);

                if (_state == PlaybackState.Playing && _engine is not null)
                {
                    var unqueued = output.UnqueueProcessed(processed);
                    for (var i = 0; i < unqueued; i++)
                    {
                        free.Enqueue(processed[i]);
                    }

                    while (free.Count > 0)
                    {
                        var id = free.Dequeue();
                        _engine.Render(pcm);
                        Interlocked.Exchange(ref _positionSamples, _engine.CurrentSample);

                        output.QueueBuffer(id, pcm);

                        if (_engine.CurrentSample >= _engine.TotalSamples && !_engine.Looping)
                        {
                            StatusChanged?.Invoke(this, "Reached end of track.");
                            SetState(PlaybackState.Stopped);
                            break;
                        }
                    }

                    output.EnsurePlaying();
                    _wake.WaitOne(2);
                }
                else
                {
                    _wake.WaitOne(25);
                }
            }
        }
    }

    private void DrainCommandsWithoutAudio()
    {
        while (_commands.TryDequeue(out var cmd))
        {
            switch (cmd)
            {
                case Command.Load load:
                    _engine = load.Engine;
                    _engine.Reset();
                    _engine.SetOpl3Controls(_opl3Controls);
                    CurrentTrack = _engine.Track;
                    TrackChanged?.Invoke(this, CurrentTrack);
                    Interlocked.Exchange(ref _positionSamples, 0);
                    SetState(PlaybackState.Stopped);
                    break;
                case Command.ReloadCurrent:
                    // No audio output available; drop loaded engine so the next LoadTrack uses the new patch bank.
                    _engine = null;
                    CurrentTrack = null;
                    Interlocked.Exchange(ref _positionSamples, 0);
                    TrackChanged?.Invoke(this, null);
                    SetState(PlaybackState.Stopped);
                    break;
            }
        }
    }

    private void ProcessCommands(OpenAlStreamingOutput output, Queue<uint> free)
    {
        while (_commands.TryDequeue(out var cmd))
        {
            switch (cmd)
            {
                case Command.Load load:
                    output.StopAndClear();
                    free.Clear();
                    foreach (var id in output.BufferIds)
                    {
                        free.Enqueue(id);
                    }

                    _engine = load.Engine;
                    _engine.Reset();
                    _engine.SetOpl3Controls(_opl3Controls);
                    _engine.SetStereoEnabled(_stereoEnabled);
                    CurrentTrack = _engine.Track;
                    TrackChanged?.Invoke(this, CurrentTrack);
                    Interlocked.Exchange(ref _positionSamples, 0);
                    SetState(PlaybackState.Stopped);
                    break;

                case Command.ReloadCurrent:
                {
                    if (CurrentTrack is null)
                    {
                        break;
                    }

                    var priorState = _state;
                    var priorVolume = _engine?.MasterVolume ?? 1.0f;
                    var priorLoop = _engine?.Looping ?? false;
                    var priorPosSamples = _engine?.CurrentSample ?? Interlocked.Read(ref _positionSamples);
                    var priorPos = TimeSpan.FromSeconds(priorPosSamples / (double)SampleRate);

                    output.StopAndClear();
                    free.Clear();
                    foreach (var id in output.BufferIds)
                    {
                        free.Enqueue(id);
                    }

                    var patches = _patches;
                    var newEngine = CreateEngine(CurrentTrack.Path, CurrentTrack.DisplayName, patches, SampleRate);
                    newEngine.MasterVolume = priorVolume;
                    newEngine.Looping = priorLoop;
                    newEngine.SetOpl3Controls(_opl3Controls);
                    newEngine.SetStereoEnabled(_stereoEnabled);
                    newEngine.SeekTo(priorPos);

                    _engine = newEngine;
                    CurrentTrack = newEngine.Track;
                    TrackChanged?.Invoke(this, CurrentTrack);
                    Interlocked.Exchange(ref _positionSamples, newEngine.CurrentSample);

                    switch (priorState)
                    {
                        case PlaybackState.Playing:
                            output.Play();
                            SetState(PlaybackState.Playing);
                            break;
                        case PlaybackState.Paused:
                            SetState(PlaybackState.Paused);
                            break;
                        default:
                            SetState(PlaybackState.Stopped);
                            break;
                    }

                    break;
                }

                case Command.Play:
                    if (_engine is null)
                    {
                        StatusChanged?.Invoke(this, "No track loaded.");
                        break;
                    }

                    output.Play();
                    SetState(PlaybackState.Playing);
                    break;

                case Command.Pause:
                    if (_state == PlaybackState.Playing)
                    {
                        output.Pause();
                        SetState(PlaybackState.Paused);
                    }

                    break;

                case Command.Stop:
                    output.StopAndClear();
                    free.Clear();
                    foreach (var id in output.BufferIds)
                    {
                        free.Enqueue(id);
                    }

                    _engine?.Reset();
                    Interlocked.Exchange(ref _positionSamples, 0);
                    SetState(PlaybackState.Stopped);
                    break;

                case Command.Seek seek:
                    if (_engine is null)
                    {
                        break;
                    }

                    output.StopAndClear();
                    free.Clear();
                    foreach (var id in output.BufferIds)
                    {
                        free.Enqueue(id);
                    }

                    _engine.SeekTo(seek.Position);
                    Interlocked.Exchange(ref _positionSamples, _engine.CurrentSample);
                    if (_state == PlaybackState.Playing)
                    {
                        output.Play();
                    }

                    SeekCompleted?.Invoke(this, GetPosition());
                    break;

                case Command.SetLoop loop:
                    _engine?.Looping = loop.Enabled;

                    break;

                case Command.SetVolume vol:
                    _engine?.MasterVolume = vol.Volume;

                    break;

                case Command.SetOpl3Controls ctl:
                    _engine?.SetOpl3Controls(ctl.Controls);
                    break;

                case Command.SetStereoEnabled stereo:
                    _engine?.SetStereoEnabled(stereo.Enabled);
                    break;

                case Command.WriteOplRegister wr:
                    _engine?.WriteOplRegister(wr.Register, wr.Value);
                    break;
            }
        }
    }

    private abstract record Command
    {
        public sealed record Load(IPlaybackEngine Engine) : Command;

        public sealed record Play : Command;

        public sealed record Pause : Command;

        public sealed record Stop : Command;

        public sealed record Seek(TimeSpan Position) : Command;

        public sealed record SetLoop(bool Enabled) : Command;

        public sealed record SetVolume(float Volume) : Command;

        public sealed record ReloadCurrent : Command;

        public sealed record SetOpl3Controls(Opl3ControlState Controls) : Command;

        public sealed record SetStereoEnabled(bool Enabled) : Command;

        public sealed record WriteOplRegister(ushort Register, byte Value) : Command;
    }
}