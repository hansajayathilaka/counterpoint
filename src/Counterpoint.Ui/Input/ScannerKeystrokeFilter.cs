using System;
using System.Text;

namespace Counterpoint.Ui.Input;

/// <summary>
/// Tells a barcode scanner's keystrokes apart from a cashier typing, by how fast they arrive
/// (SRS FR-3.2, UI-01, P1-T09 "inter-key timing under 30 ms plus configurable prefix/suffix").
/// </summary>
/// <remarks>
/// <para>
/// A barcode scanner is a keyboard wedge: to the operating system it is just another keyboard,
/// typing very fast. Nobody types faster than about one character every 80-100 ms even in a
/// burst; a scanner types a whole barcode in a few milliseconds. So a run of characters that all
/// arrive within <see cref="MaxInterKeyGap"/> of the one before it is a scan; anything with a
/// slower gap in the middle is a cashier typing, however long the resulting text is.
/// </para>
/// <para>
/// Deliberately free of anything from <c>Avalonia</c>: this is the one part of the scanner filter
/// a test can drive with plain <see cref="DateTimeOffset"/> values and no window, no focus and no
/// dispatcher. The Avalonia-facing half - reading real key events, stealing focus back from
/// whatever control they landed in, and applying <c>PeripheralSettings.ScannerSuffix</c> - is
/// <c>Counterpoint.Ui.Views.SalesWindow</c>'s code-behind, which has nothing left to decide once
/// this class has spoken.
/// </para>
/// </remarks>
public sealed class ScannerKeystrokeFilter
{
    private readonly int _minimumLength;
    private readonly TimeSpan _maxInterKeyGap;
    private readonly StringBuilder _buffer = new();

    private DateTimeOffset? _lastKeystrokeAt;
    private bool _confirmed;

    /// <param name="minimumLength">
    /// The shortest run the till treats as a scan rather than as typing
    /// (<c>PeripheralSettings.ScannerMinimumLength</c>).
    /// </param>
    /// <param name="maxInterKeyGap">
    /// The longest gap between two keystrokes that still counts as scanner speed. SRS names
    /// 30 ms; kept as a parameter rather than a constant so a test can use a gap it can actually
    /// simulate without a real clock.
    /// </param>
    public ScannerKeystrokeFilter(int minimumLength, TimeSpan maxInterKeyGap)
    {
        if (minimumLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLength), minimumLength, "A scan is at least one character.");
        }

        _minimumLength = minimumLength;
        _maxInterKeyGap = maxInterKeyGap;
    }

    /// <summary>How many characters are buffered right now, confirmed or not.</summary>
    public int BufferedLength => _buffer.Length;

    /// <summary>
    /// True once the run buffered so far has been believed to be a scan - from
    /// <see cref="JustBecameConfirmedScan"/>'s keystroke onward, until the next
    /// <see cref="Commit"/> or <see cref="Reset"/>. What the caller checks on every keystroke to
    /// decide whether to keep stealing focus back for the rest of the run.
    /// </summary>
    public bool IsConfirmedScanInProgress => _confirmed;

    /// <summary>
    /// True from the keystroke that pushed the buffered run's length to
    /// <see cref="_minimumLength"/> while every gap so far has been scanner-speed - the moment
    /// the caller should start stealing focus back, because what has already leaked into whatever
    /// control was focused is now known to be a scan rather than typing.
    /// </summary>
    public bool JustBecameConfirmedScan { get; private set; }

    /// <summary>
    /// Feeds one typed character, arriving at <paramref name="at"/>.
    /// </summary>
    /// <returns>
    /// True while the run buffered so far is still, keystroke by keystroke, consistent with
    /// scanner speed (every gap seen has been at or under the configured maximum) - not yet
    /// necessarily long enough to be believed, see <see cref="JustBecameConfirmedScan"/>.
    /// </returns>
    public bool Push(char character, DateTimeOffset at)
    {
        JustBecameConfirmedScan = false;

        if (_lastKeystrokeAt is { } last && at - last > _maxInterKeyGap)
        {
            // The gap was too long: whatever was buffered was typing, not a scan. A scan starts
            // fresh from this keystroke - the till does not carry a slow keystroke into a fast
            // run that happens to follow it.
            Reset();
        }

        _buffer.Append(character);
        _lastKeystrokeAt = at;

        var stillScannerSpeed = _buffer.Length > 0;

        if (stillScannerSpeed && !_confirmed && _buffer.Length >= _minimumLength)
        {
            _confirmed = true;
            JustBecameConfirmedScan = true;
        }

        return stillScannerSpeed;
    }

    /// <summary>
    /// The scanner's suffix arrived (Enter, Tab, or - when the shop's scanner sends no suffix at
    /// all - an idle timeout the caller measured itself). Returns the scanned code when the
    /// buffered run was long enough and every gap in it was scanner speed; null otherwise. Either
    /// way, the buffer is cleared for the next scan.
    /// </summary>
    public string? Commit()
    {
        var code = _confirmed && _buffer.Length >= _minimumLength ? _buffer.ToString() : null;
        Reset();
        return code;
    }

    /// <summary>Discards whatever has been buffered, without deciding anything about it.</summary>
    public void Reset()
    {
        _buffer.Clear();
        _lastKeystrokeAt = null;
        _confirmed = false;
        JustBecameConfirmedScan = false;
    }
}
