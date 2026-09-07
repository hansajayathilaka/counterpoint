using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// The Argon2id work factors and the lockout policy, written where the owner can read them
/// (P1-T02 step 1, docs/01_DATA_MODEL.md §8).
/// </summary>
public sealed class SecurityPolicyRecorderTests
{
    [Fact]
    public async Task NFR_S1_TheWorkFactorsInForceAreRecordedInAppSetting()
    {
        // SaleFixture composes the same container Counterpoint.App does, and calls the same
        // EnsureRecordedAsync from the same place in start-up.
        await using var fixture = await SaleFixture.CreateAsync(hashing: Argon2Parameters.Default);

        await AssertSettingAsync(fixture, SecurityPolicyRecorder.Argon2MemoryKibKey, "65536");
        await AssertSettingAsync(fixture, SecurityPolicyRecorder.Argon2IterationsKey, "3");
        await AssertSettingAsync(fixture, SecurityPolicyRecorder.Argon2ParallelismKey, "4");
    }

    [Fact]
    public async Task NFR_S9_TheLockoutPolicyIsRecordedToo()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        await AssertSettingAsync(fixture, SecurityPolicyRecorder.MinimumPasswordLengthKey, "4");
        await AssertSettingAsync(fixture, SecurityPolicyRecorder.MaxFailedAttemptsKey, "5");
        await AssertSettingAsync(fixture, SecurityPolicyRecorder.LockoutBaseSecondsKey, "30");
        await AssertSettingAsync(fixture, SecurityPolicyRecorder.LockoutMaximumSecondsKey, "900");
    }

    [Fact]
    public async Task TheRecordedValuesAreTheOnesTheHasherActuallyUses()
    {
        // A row left behind by an older build would be a confident, checkable lie, so the recorder
        // reads the same Argon2Parameters instance the hasher was given rather than a copy of the
        // numbers.
        var cheap = SaleFixture.TestArgon2Parameters;

        await using var fixture = await SaleFixture.CreateAsync();

        await AssertSettingAsync(
            fixture,
            SecurityPolicyRecorder.Argon2MemoryKibKey,
            cheap.MemoryKib.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task RunningItTwiceIsSafeAndLeavesOneRowPerSetting()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        await fixture.Resolve<SecurityPolicyRecorder>().EnsureRecordedAsync();
        await fixture.Resolve<SecurityPolicyRecorder>().EnsureRecordedAsync();

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM app_setting WHERE key LIKE 'security.%';"))
            .Should().Be(7);
    }

    private static async Task AssertSettingAsync(SaleFixture fixture, string key, string expected)
    {
        (await fixture.ScalarAsync($"SELECT value FROM app_setting WHERE key = '{key}';"))
            .Should().Be(expected, "app_setting must say what this build hashes with");

        (await fixture.ScalarAsync($"SELECT value_type FROM app_setting WHERE key = '{key}';"))
            .Should().Be("INT");
    }
}
