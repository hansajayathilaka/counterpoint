using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Device.Tests.Support;

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that keeps what it was told, so a test can assert
/// that a degraded device actually warned somebody (SRS FR-7.8).
/// </summary>
/// <typeparam name="T">The logger category.</typeparam>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LoggedEntry> _entries = [];

    /// <summary>Everything logged, in order.</summary>
    public IReadOnlyList<LoggedEntry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _entries.Add(new LoggedEntry(logLevel, eventId, formatter(state, exception), exception));
    }

    /// <summary>One logged line.</summary>
    internal sealed record LoggedEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception);
}
