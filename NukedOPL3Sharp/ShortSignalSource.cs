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
    private readonly SourceKind _kind;

    private ShortSignalSource(Opl3Operator? source, SourceKind kind)
    {
        _source = source;
        _kind = kind;
    }

    /// <summary>
    ///     Provides a source that always reads zero.
    /// </summary>
    public static ShortSignalSource Zero => default;

    /// <summary>
    ///     Reads the currently selected signal without allocating a delegate.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short Read()
    {
        return _kind switch
        {
            SourceKind.Output => _source!.Out,
            SourceKind.Feedback => _source!.FeedbackModifiedSignal,
            SourceKind.PreviousOutput => _source!.PreviousOutputSample,
            _ => 0
        };
    }

    /// <summary>
    ///     Redirects outputs at or beyond the delay boundary to the operator's previous sample.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ShortSignalSource DelayOutputFrom(byte firstDelayedSlot)
    {
        return _kind == SourceKind.Output && _source!.SlotIndex >= firstDelayedSlot
            ? new ShortSignalSource(_source, SourceKind.PreviousOutput)
            : this;
    }

    /// <summary>
    ///     Confirms that a modulation source cannot become nonzero before the next register write.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool CanRemainZero(Opl3Operator owner, uint writeGeneration)
    {
        return _kind switch
        {
            SourceKind.Zero => true,
            SourceKind.Feedback => ReferenceEquals(_source, owner),
            SourceKind.Output => _source!.DormantGeneration == writeGeneration,
            _ => false
        };
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
