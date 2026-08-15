using Silk.NET.OpenAL;

namespace NukedOPL3Sharp.MidiPlayer.Audio;

public sealed class OpenAlStreamingOutput : IDisposable
{
    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly uint[] _buffers;

    private readonly int _sampleRate;
    private readonly uint[] _singleBuffer = new uint[1];
    private unsafe Context* _context;
    private unsafe Device* _device;

    private uint _source;

    public OpenAlStreamingOutput(int sampleRate, int numBuffers)
    {
        _sampleRate = sampleRate;
        _buffers = new uint[numBuffers];

        _al = AL.GetApi(true);
        _alc = ALContext.GetApi(true);

        unsafe
        {
            _device = _alc.OpenDevice(null);
            if (_device == null)
            {
                throw new InvalidOperationException("OpenAL: failed to open device.");
            }

            _context = _alc.CreateContext(_device, null);
            if (_context == null)
            {
                throw new InvalidOperationException("OpenAL: failed to create context.");
            }

            if (!_alc.MakeContextCurrent(_context))
            {
                throw new InvalidOperationException("OpenAL: failed to make context current.");
            }
        }

        _source = _al.GenSource();
        if (_buffers.Length > 0)
        {
            var ids = _al.GenBuffers(_buffers.Length);
            Array.Copy(ids, _buffers, _buffers.Length);
        }
    }

    public IReadOnlyList<uint> BufferIds => _buffers;

    public void Dispose()
    {
        try
        {
            StopAndClear();
        }
        catch
        {
            // ignored
        }

        if (_source != 0)
        {
            _al.DeleteSource(_source);
            _source = 0;
        }

        if (_buffers.Length > 0)
        {
            _al.DeleteBuffers(_buffers);
        }

        unsafe
        {
            if (_context != null)
            {
                _alc.DestroyContext(_context);
                _context = null;
            }

            if (_device != null)
            {
                _alc.CloseDevice(_device);
                _device = null;
            }
        }
    }

    public void Play()
    {
        _al.SourcePlay(_source);
    }

    public void Pause()
    {
        _al.SourcePause(_source);
    }

    public void StopAndClear()
    {
        _al.SourceStop(_source);

        _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out var queued);
        while (queued-- > 0)
        {
            _singleBuffer[0] = 0;
            _al.SourceUnqueueBuffers(_source, _singleBuffer);
        }
    }

    public unsafe int UnqueueProcessed(uint[] processed)
    {
        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out var count);
        var toUnqueue = Math.Min(count, processed.Length);
        if (toUnqueue <= 0)
        {
            return 0;
        }

        fixed (uint* p = processed)
        {
            _al.SourceUnqueueBuffers(_source, toUnqueue, p);
        }

        return toUnqueue;
    }

    public unsafe void QueueBuffer(uint bufferId, ReadOnlySpan<short> interleavedStereo)
    {
        fixed (short* p = interleavedStereo)
        {
            _al.BufferData(bufferId, BufferFormat.Stereo16, p, interleavedStereo.Length * sizeof(short), _sampleRate);
        }

        _singleBuffer[0] = bufferId;
        _al.SourceQueueBuffers(_source, _singleBuffer);
    }

    public bool EnsurePlaying()
    {
        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out var state);
        var playing = (SourceState)state == SourceState.Playing;
        if (!playing)
        {
            _al.SourcePlay(_source);
        }

        return true;
    }
}
