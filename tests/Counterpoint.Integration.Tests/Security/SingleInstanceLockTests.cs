using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Runtime;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// One Counterpoint per machine (C-01, CLAUDE.md "Single named-mutex instance").
/// </summary>
/// <remarks>
/// <para>
/// <b>How a second instance is proved to be refused without starting a second process.</b> A
/// named mutex is owned by the <em>thread</em> that took it, not by the process. A second
/// acquisition from another thread is therefore refused by exactly the same mechanism, and for
/// exactly the same reason, as a second copy of the application would be - so the rule is
/// provable here, on Linux, with no window, no Windows and no second process. What a second
/// process would additionally exercise is the operating system's named-mutex implementation,
/// which is not this code.
/// </para>
/// <para>
/// Every test uses its own mutex name. The production name is a constant that
/// <see cref="Counterpoint.App"/>'s <c>Program.Main</c> uses by default; a test that took it
/// would fight the developer's own running copy.
/// </para>
/// </remarks>
public sealed class SingleInstanceLockTests
{
    [Fact]
    public void C_01_TheFirstInstanceGetsTheLock()
    {
        var name = UniqueName();

        using var first = SingleInstanceLock.TryAcquire(name);

        first.Should().NotBeNull();
    }

    [Fact]
    public async Task C_01_ASecondInstanceIsRefused()
    {
        var name = UniqueName();
        var held = new SemaphoreSlim(0, 1);
        var release = new SemaphoreSlim(0, 1);
        SingleInstanceLock? holder = null;

        // A dedicated thread, because mutex ownership is per thread: this stands in for the copy
        // of Counterpoint that is already running.
        var owner = new Thread(() =>
        {
            holder = SingleInstanceLock.TryAcquire(name);
            held.Release();
            release.Wait();
            holder?.Dispose();
        })
        {
            IsBackground = true,
        };

        owner.Start();
        await held.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            holder.Should().NotBeNull("the first instance must start normally");

            var second = SingleInstanceLock.TryAcquire(name);

            second.Should().BeNull(
                "two copies would be two SQLite writers on one file, two bill-number sequences "
                + "and two hash chains - so the second one refuses to start");
        }
        finally
        {
            release.Release();
            owner.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task C_01_TheLockIsReleasedWhenTheFirstInstanceCloses()
    {
        var name = UniqueName();
        var held = new SemaphoreSlim(0, 1);
        var released = new SemaphoreSlim(0, 1);

        var owner = new Thread(() =>
        {
            using (SingleInstanceLock.TryAcquire(name))
            {
                held.Release();
            }

            released.Release();
        })
        {
            IsBackground = true,
        };

        owner.Start();
        await held.WaitAsync(TimeSpan.FromSeconds(5));
        await released.WaitAsync(TimeSpan.FromSeconds(5));
        owner.Join(TimeSpan.FromSeconds(5));

        // Closing the till and opening it again has to work, or the shop needs a reboot to trade.
        using var next = SingleInstanceLock.TryAcquire(name);

        next.Should().NotBeNull();
    }

    [Fact]
    public void TheRefusalMessageSaysWhatToDoAboutIt()
    {
        // UI-06: plain language with a next step. It is what Program.Main prints before it exits
        // with a non-zero code.
        SingleInstanceLock.AlreadyRunningMessage.Should().Contain("already running");
        SingleInstanceLock.AlreadyRunningMessage.Should().Contain("window that is already open");
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var name = UniqueName();
        var acquired = SingleInstanceLock.TryAcquire(name);

        acquired.Should().NotBeNull();

        acquired!.Dispose();
        var again = () => acquired.Dispose();

        again.Should().NotThrow("shutdown paths run twice more often than anyone plans for");
    }

    private static string UniqueName() =>
        "Counterpoint-Test-" + Guid.NewGuid().ToString("N");
}
