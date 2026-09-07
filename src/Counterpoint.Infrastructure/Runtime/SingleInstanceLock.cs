using System;
using System.Globalization;
using System.Threading;

namespace Counterpoint.Infrastructure.Runtime;

/// <summary>
/// One Counterpoint per machine, held by a named mutex (C-01, CLAUDE.md "Single named-mutex
/// instance").
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it matters more here than in most desktop applications.</b> Two copies of this process
/// would be two SQLite writers on one file, each with its own <c>BEGIN IMMEDIATE</c> gate and its
/// own five-second busy timeout - and the second one would not fail cleanly, it would take turns.
/// Two tills would then draw bill numbers from the same sequence, chain two hashes onto the same
/// <c>prev_hash</c>, and both open a shift. Refusing to start is the only correct answer, and it
/// has to happen before anything opens the database.
/// </para>
/// <para>
/// <b>Why it is a class and not four lines in <c>Program.Main</c>.</b> Because those four lines
/// need a test. A named mutex is owned by a <em>thread</em>, so a second acquisition attempt from
/// another thread in this same process is refused exactly as a second process would be - which
/// makes the rule provable on the Linux development machine, with no second process, no window
/// and no Windows. <c>SingleInstanceLockTests</c> does that.
/// </para>
/// <para>
/// <b>Global, not session-local.</b> The <c>Global\</c> prefix covers every Windows terminal
/// services session, so a second copy started under a different logged-in user is refused too -
/// it would reach the same database file. On Linux the runtime supports named mutexes without the
/// prefix; the name is used verbatim there, which is all the development-time test needs.
/// </para>
/// </remarks>
public sealed class SingleInstanceLock : IDisposable
{
    /// <summary>
    /// The name of the machine-wide mutex. A GUID rather than a word, so nothing else on the
    /// machine can hold it by coincidence.
    /// </summary>
    public const string DefaultName = "Counterpoint-Till-8f2b1c46-4c3a-4e02-9a3f-5f8a5a5c2b41";

    /// <summary>What to tell whoever started the second copy (SRS UI-06).</summary>
    public const string AlreadyRunningMessage =
        "Counterpoint is already running on this computer. Switch to the window that is already open - "
        + "a second copy would be a second till writing to the same database, and the shop has one till.";

    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceLock(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// Takes the lock, or returns null when another copy already holds it.
    /// </summary>
    /// <param name="name">The mutex name. Tests pass a unique one so they cannot collide.</param>
    /// <remarks>
    /// A zero timeout, deliberately: the answer to "is another copy running" must be immediate,
    /// and waiting would mean a second copy silently springing to life the moment the first one
    /// closed.
    /// </remarks>
    public static SingleInstanceLock? TryAcquire(string name = DefaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var mutex = new Mutex(initiallyOwned: false, QualifiedName(name));

        try
        {
            // AbandonedMutexException means the previous holder died without releasing - a crash
            // or a kill. The lock is ours and the machine has no other copy running, which is
            // exactly the state we are testing for, so it is an acquisition and not a failure.
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero, exitContext: false);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }

            return new SingleInstanceLock(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The platform-qualified mutex name. Windows gets the <c>Global\</c> prefix so the lock spans
    /// terminal-services sessions; other platforms take the name as given.
    /// </summary>
    private static string QualifiedName(string name) => OperatingSystem.IsWindows()
        ? string.Create(CultureInfo.InvariantCulture, $@"Global\{name}")
        : name;

    /// <summary>Releases the lock. The next copy of Counterpoint may start.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owning thread, or already released. Nothing left to do but let go of the
            // handle - and an exception on the way out of a shutdown helps nobody.
        }
        finally
        {
            _mutex.Dispose();
        }
    }
}
