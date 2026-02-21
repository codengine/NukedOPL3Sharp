using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NukedOPL3Sharp.MidiPlayer.Core.Synth;
using NukedOPL3Sharp.MidiPlayer.Models;
using NukedOPL3Sharp.MidiPlayer.Services;

namespace NukedOPL3Sharp.MidiPlayer.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly DispatcherTimer _uiTimer;
    [ObservableProperty] private bool _canPause;

    [ObservableProperty] private bool _canPlay;
    [ObservableProperty] private bool _canSeek;
    [ObservableProperty] private bool _canStop;

    [ObservableProperty] private bool _loopEnabled;
    [ObservableProperty] private string _nowPlayingText = "No track loaded.";

    [ObservableProperty] private bool _oplDrumBd;
    [ObservableProperty] private bool _oplDrumCy;
    [ObservableProperty] private bool _oplDrumHh;
    [ObservableProperty] private bool _oplDrumSd;
    [ObservableProperty] private bool _oplDrumTt;
    [ObservableProperty] private bool _oplNoteSelect;

    [ObservableProperty] private string _oplRegisterHex = "BD";
    [ObservableProperty] private bool _oplRhythmMode;
    [ObservableProperty] private bool _oplStereoEnabled = true;

    [ObservableProperty] private bool _oplTremoloDepth;
    [ObservableProperty] private string _oplValueHex = "00";
    [ObservableProperty] private bool _oplVibratoDepth;
    [ObservableProperty] private string _patchBankText = "Patch bank: (loading…)";
    private CancellationTokenSource? _seekCts;

    [ObservableProperty] private double _seekMaximumSeconds;
    private bool _seekPending;
    [ObservableProperty] private double _seekPositionSeconds;

    [ObservableProperty] private TrackItem? _selectedTrack;
    [ObservableProperty] private string _statusText = "Ready.";

    private bool _suppressOplChanged;

    private bool _suppressSeekChanged;
    [ObservableProperty] private float _volume = 1.0f;

    public MainWindowViewModel()
    {
        _playback = new PlaybackService();
        PatchBankText = $"Patch bank: {_playback.PatchBankDisplayName}";
        _playback.StatusChanged += (_, s) =>
            Dispatcher.UIThread.Post(() => StatusText = s);

        _playback.TrackChanged += (_, info) =>
            Dispatcher.UIThread.Post(() =>
            {
                NowPlayingText = info is null ? "No track loaded." : $"Now playing: {info.DisplayName}";
                SeekMaximumSeconds = info?.Duration.TotalSeconds ?? 0;
                CanSeek = info is not null;
                OnPropertyChanged(nameof(TimeText));
            });

        _playback.StateChanged += (_, st) =>
            Dispatcher.UIThread.Post(() =>
            {
                CanPlay = st is PlaybackState.Stopped or PlaybackState.Paused;
                CanPause = st is PlaybackState.Playing;
                CanStop = st is PlaybackState.Playing or PlaybackState.Paused;
            });

        _playback.PatchBankChanged += (_, name) =>
            Dispatcher.UIThread.Post(() => PatchBankText = $"Patch bank: {name}");

        _playback.SeekCompleted += (_, pos) =>
            Dispatcher.UIThread.Post(() =>
            {
                _seekPending = false;
                _suppressSeekChanged = true;
                try
                {
                    SeekPositionSeconds = Math.Clamp(pos.TotalSeconds, 0, SeekMaximumSeconds);
                }
                finally
                {
                    _suppressSeekChanged = false;
                }

                OnPropertyChanged(nameof(TimeText));
            });

        _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
        {
            var pos = _playback.GetPosition();
            if (!_seekPending &&
                pos.TotalSeconds >= 0 && pos.TotalSeconds <= SeekMaximumSeconds &&
                _playback.IsPlayingOrPaused)
            {
                _suppressSeekChanged = true;
                try
                {
                    SeekPositionSeconds = pos.TotalSeconds;
                }
                finally
                {
                    _suppressSeekChanged = false;
                }

                OnPropertyChanged(nameof(TimeText));
            }

            OnPropertyChanged(nameof(VolumeText));
        });
        _uiTimer.Start();

        CanPlay = true;
    }

    public ObservableCollection<TrackItem> Tracks { get; } = [];

    public string TimeText
    {
        get
        {
            var pos = TimeSpan.FromSeconds(SeekPositionSeconds);
            var max = TimeSpan.FromSeconds(SeekMaximumSeconds);
            return $"{FormatTime(pos)} / {FormatTime(max)}";
        }
    }

    public string VolumeText => $"Volume: {(int)Math.Round(Volume * 100)}%";

    public void Dispose()
    {
        _seekCts?.Cancel();
        _seekCts?.Dispose();
        _uiTimer.Stop();
        _playback.Dispose();
    }

    partial void OnLoopEnabledChanged(bool value)
    {
        _playback.SetLoop(value);
    }

    partial void OnVolumeChanged(float value)
    {
        _playback.SetVolume(value);
    }

    partial void OnOplTremoloDepthChanged(bool value)
    {
        if (!_suppressOplChanged)
        {
            PushOplControls();
        }
    }

    partial void OnOplVibratoDepthChanged(bool value)
    {
        if (!_suppressOplChanged)
        {
            PushOplControls();
        }
    }

    partial void OnOplRhythmModeChanged(bool value)
    {
        if (!_suppressOplChanged)
        {
            PushOplControls();
        }
    }

    partial void OnOplNoteSelectChanged(bool value)
    {
        if (!_suppressOplChanged)
        {
            PushOplControls();
        }
    }

    partial void OnOplDrumBdChanged(bool value)
    {
        if (!_suppressOplChanged)
        {
            PushOplControls();
        }
    }

    partial void OnOplDrumSdChanged(bool value)
    {
        if (!_suppressOplChanged)
        {
            PushOplControls();
        }
    }

    partial void OnOplDrumTtChanged(bool value)
    {
        if (!_suppressOplChanged)
        {
            PushOplControls();
        }
    }

    partial void OnOplDrumCyChanged(bool value)
    {
        if (!_suppressOplChanged)
        {
            PushOplControls();
        }
    }

    partial void OnOplDrumHhChanged(bool value)
    {
        if (!_suppressOplChanged)
        {
            PushOplControls();
        }
    }

    partial void OnOplStereoEnabledChanged(bool value)
    {
        _playback.SetStereoEnabled(value);
    }

    partial void OnSeekPositionSecondsChanged(double value)
    {
        if (_suppressSeekChanged || !CanSeek)
        {
            OnPropertyChanged(nameof(TimeText));
            return;
        }

        _seekPending = true;
        _seekCts?.Cancel();
        _seekCts?.Dispose();
        _seekCts = new CancellationTokenSource();
        var token = _seekCts.Token;

        var target = TimeSpan.FromSeconds(Math.Clamp(value, 0, SeekMaximumSeconds));
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                _playback.Seek(target);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);

        OnPropertyChanged(nameof(TimeText));
    }

    [RelayCommand]
    private void WriteOplRegister()
    {
        if (!TryParseHexU16(OplRegisterHex, out var reg))
        {
            StatusText = "Invalid OPL register hex (expected 0-1FF).";
            return;
        }

        if (!TryParseHexByte(OplValueHex, out var val))
        {
            StatusText = "Invalid OPL value hex (expected 00-FF).";
            return;
        }

        _playback.WriteOplRegister(reg, val);
        StatusText = $"Wrote OPL reg 0x{reg:X3} = 0x{val:X2}.";
    }

    [RelayCommand]
    private void ResetOplControls()
    {
        _suppressOplChanged = true;
        try
        {
            OplTremoloDepth = false;
            OplVibratoDepth = false;
            OplRhythmMode = false;
            OplNoteSelect = false;
            OplDrumBd = false;
            OplDrumSd = false;
            OplDrumTt = false;
            OplDrumCy = false;
            OplDrumHh = false;
        }
        finally
        {
            _suppressOplChanged = false;
        }

        PushOplControls();
        OplStereoEnabled = true;
        StatusText = "OPL3 controls reset.";
    }

    private void PushOplControls()
    {
        byte drums = 0;
        if (OplDrumBd)
        {
            drums |= 1 << 4;
        }

        if (OplDrumSd)
        {
            drums |= 1 << 3;
        }

        if (OplDrumTt)
        {
            drums |= 1 << 2;
        }

        if (OplDrumCy)
        {
            drums |= 1 << 1;
        }

        if (OplDrumHh)
        {
            drums |= 1 << 0;
        }

        var state = new Opl3ControlState(
            OplTremoloDepth,
            OplVibratoDepth,
            OplRhythmMode,
            drums,
            OplNoteSelect);

        _playback.SetOpl3Controls(state);
    }

    private static bool TryParseHexByte(string? text, out byte value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseHexU16(string? text, out ushort value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (!ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return value <= 0x1FF;
    }

    [RelayCommand]
    private void ClearTracks()
    {
        Tracks.Clear();
        SelectedTrack = null;
        StatusText = "Cleared track list.";
    }

    [RelayCommand]
    private void Play()
    {
        if (SelectedTrack is null)
        {
            StatusText = "Select a track first.";
            return;
        }

        _playback.LoadTrack(SelectedTrack);
        _playback.Play();
    }

    [RelayCommand]
    private void Pause()
    {
        _playback.Pause();
    }

    [RelayCommand]
    private void Stop()
    {
        _playback.Stop();
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            StatusText = "No storage provider available (are we running with a desktop lifetime?).";
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open music file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Music")
                {
                    Patterns = ["*.mid", "*.MID", "*.mid*", "*.MID*", "*.midi", "*.MIDI", "*.midi*", "*.MIDI*"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "Selected file is not a local path.";
            return;
        }

        var track = new TrackItem(path, Path.GetFileName(path));
        SelectedTrack = track;
        Tracks.Add(track);

        StatusText = $"Selected {track.DisplayName}.";
    }

    [RelayCommand]
    private async Task OpenPatchBank()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            StatusText = "No storage provider available.";
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open patch bank (.wopl / .op2)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("OPL patch bank")
                {
                    Patterns = ["*.wopl", "*.WOPL", "*.op2", "*.OP2"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "Selected patch bank is not a local path.";
            return;
        }

        _playback.SetPatchBank(path);
    }

    private static IStorageProvider? GetStorageProvider()
    {
        var lifetime = Application.Current?.ApplicationLifetime;
        return lifetime is not IClassicDesktopStyleApplicationLifetime desktop
            ? null
            : desktop.MainWindow?.StorageProvider;
    }

    private static string FormatTime(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero)
        {
            ts = TimeSpan.Zero;
        }

        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : ts.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
