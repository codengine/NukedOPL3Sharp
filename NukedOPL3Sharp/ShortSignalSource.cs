// SPDX-FileCopyrightText: 2013-2026 Nuked-OPL3 by nukeykt
// SPDX-FileCopyrightText: 2026 Tony Gies
// SPDX-License-Identifier: LGPL-2.1-only

using System.Runtime.CompilerServices;

namespace NukedOPL3Sharp;

/// <summary>
///     Lightweight indirection used to read either zero, operator output, or operator feedback without allocating
///     delegates.
/// </summary>
public readonly struct ShortSignalSource
{
    private enum SourceKind : byte
    {
        Zero = 0,
        Output = 1,
        Feedback = 2,
        PreviousOutput = 3
    }

    private readonly Opl3Operator? _source;
#if NET8_0_OR_GREATER
    private readonly nint _fieldOffset;
#else
    private readonly SourceKind _kind;
#endif

    private ShortSignalSource(Opl3Operator? source, SourceKind kind)
    {
        _source = source;
#if NET8_0_OR_GREATER
        if (source is null)
        {
            _fieldOffset = 0;
            return;
        }

        _fieldOffset = kind switch
        {
            SourceKind.Feedback => Unsafe.ByteOffset(ref source.Out, ref source.FeedbackModifiedSignal),
            SourceKind.PreviousOutput => Unsafe.ByteOffset(ref source.Out, ref source.PreviousOutputSample),
            _ => 0
        };
#else
        _kind = kind;
#endif
    }

    /// <summary>
    ///     Provides a source that always reads zero.
    /// </summary>
    public static ShortSignalSource Zero => default;

    /// <summary>
    ///     Reads the currently selected signal without allocating a delegate.
    /// </summary>
#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public short Read()
    {
#if NET8_0_OR_GREATER
        var source = _source;
        return source is null ? (short)0 : Unsafe.AddByteOffset(ref source.Out, _fieldOffset);
#else
        return _kind switch
        {
            SourceKind.Output => _source!.Out,
            SourceKind.Feedback => _source!.FeedbackModifiedSignal,
            SourceKind.PreviousOutput => _source!.PreviousOutputSample,
            _ => 0
        };
#endif
    }

#if NET10_0_OR_GREATER
    /// <summary>
    ///     Confirms that two delayed mixer views read the same operator field.
    /// </summary>
    internal bool ReadsSameSignalAs(ShortSignalSource other)
    {
        return ReferenceEquals(_source, other._source) && _fieldOffset == other._fieldOffset;
    }
#endif

    /// <summary>
    ///     Redirects outputs at or beyond the delay boundary to the operator's previous sample.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ShortSignalSource DelayOutputFrom(byte firstDelayedSlot)
    {
#if NET8_0_OR_GREATER
        return _source is { } source && _fieldOffset == 0 && source.SlotIndex >= firstDelayedSlot
#else
        return _kind == SourceKind.Output && _source!.SlotIndex >= firstDelayedSlot
#endif
            ? new ShortSignalSource(_source, SourceKind.PreviousOutput)
            : this;
    }

    /// <summary>
    ///     Confirms that a modulation source cannot become nonzero before the next register write.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool CanRemainZero(Opl3Operator owner, uint writeGeneration)
    {
#if NET8_0_OR_GREATER
        var source = _source;
        if (source is null)
        {
            return true;
        }

        if (_fieldOffset == 0)
        {
            return source.DormantGeneration == writeGeneration;
        }

        return ReferenceEquals(source, owner)
               && _fieldOffset == Unsafe.ByteOffset(ref source.Out, ref source.FeedbackModifiedSignal);
#else
        return _kind switch
        {
            SourceKind.Zero => true,
            SourceKind.Feedback => ReferenceEquals(_source, owner),
            SourceKind.Output => _source!.DormantGeneration == writeGeneration,
            _ => false
        };
#endif
    }

    /// <summary>
    ///     Selects an operator's current output.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ShortSignalSource FromOutput(Opl3Operator source)
    {
        return source is null
            ? throw new ArgumentNullException(nameof(source))
            : new ShortSignalSource(source, SourceKind.Output);
    }

    /// <summary>
    ///     Selects an operator's feedback signal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ShortSignalSource FromFeedback(Opl3Operator source)
    {
        return source is null
            ? throw new ArgumentNullException(nameof(source))
            : new ShortSignalSource(source, SourceKind.Feedback);
    }
}
