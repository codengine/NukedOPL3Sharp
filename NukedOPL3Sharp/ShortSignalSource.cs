// SPDX-FileCopyrightText: 2013-2026 Nuked-OPL3 by nukeykt
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
        Feedback = 2
    }

    private readonly Opl3Operator? _source;
    private readonly SourceKind _kind;

    private ShortSignalSource(Opl3Operator? source, SourceKind kind)
    {
        _source = source;
        _kind = kind;
    }

    public static ShortSignalSource Zero => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short Read()
    {
        return _kind switch
        {
            SourceKind.Output => _source!.Out,
            SourceKind.Feedback => _source!.FeedbackModifiedSignal,
            _ => 0
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ShortSignalSource FromOutput(Opl3Operator source)
    {
        return source is null
            ? throw new ArgumentNullException(nameof(source))
            : new ShortSignalSource(source, SourceKind.Output);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ShortSignalSource FromFeedback(Opl3Operator source)
    {
        return source is null
            ? throw new ArgumentNullException(nameof(source))
            : new ShortSignalSource(source, SourceKind.Feedback);
    }
}
