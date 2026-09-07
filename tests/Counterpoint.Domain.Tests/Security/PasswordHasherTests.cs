using System;
using Counterpoint.Application.Security;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Security;

/// <summary>
/// Argon2id password hashing (SRS FR-1.3, NFR-S1).
/// </summary>
/// <remarks>
/// Cheap work factors throughout: these tests are about the format, the comparison and the
/// refusals, none of which change with the cost. The cost itself is measured against
/// <see cref="Argon2Parameters.Default"/> in the integration suite's login-budget test, and
/// finally on the shop terminal in HW-T07.
/// </remarks>
public sealed class PasswordHasherTests
{
    private static readonly Argon2Parameters Cheap = new(MemoryKib: 256, Iterations: 1, Parallelism: 1);

    private static PasswordHasher Hasher(Argon2Parameters? parameters = null) => new(parameters ?? Cheap);

    [Fact]
    public void FR_1_3_TheStoredFormIsAnArgon2idPhcStringAndCarriesNoPassword()
    {
        var encoded = Hasher().Hash("hunter2");

        encoded.Should().StartWith("$argon2id$v=19$m=256,t=1,p=1$");
        encoded.Split('$').Should().HaveCount(6, "the PHC form is $alg$v$costs$salt$hash");
        encoded.Should().NotContain("hunter2", "there is nothing reversible stored, ever");
    }

    [Fact]
    public void FR_1_3_TheSamePasswordHashesDifferentlyEveryTime()
    {
        var hasher = Hasher();

        var first = hasher.Hash("hunter2");
        var second = hasher.Hash("hunter2");

        first.Should().NotBe(second, "each hash carries its own random salt");
        hasher.Verify("hunter2", first).Should().BeTrue();
        hasher.Verify("hunter2", second).Should().BeTrue();
    }

    [Fact]
    public void FR_1_1_TheRightPasswordVerifiesAndTheWrongOneDoesNot()
    {
        var hasher = Hasher();
        var encoded = hasher.Hash("hunter2");

        hasher.Verify("hunter2", encoded).Should().BeTrue();
        hasher.Verify("Hunter2", encoded).Should().BeFalse("passwords are case sensitive");
        hasher.Verify("hunter", encoded).Should().BeFalse();
        hasher.Verify(string.Empty, encoded).Should().BeFalse();
    }

    [Fact]
    public void AHashMadeUnderOtherWorkFactorsStillVerifies()
    {
        // The cost is read back out of the stored string, not assumed. This is what lets HW-T07
        // retune the parameters against the shop terminal without invalidating every account.
        var encoded = Hasher(new Argon2Parameters(MemoryKib: 512, Iterations: 2, Parallelism: 1))
            .Hash("hunter2");

        Hasher().Verify("hunter2", encoded).Should().BeTrue();
        Hasher().Verify("wrong", encoded).Should().BeFalse();
    }

    [Theory]
    [InlineData("!")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("hunter2")]
    [InlineData("$argon2i$v=19$m=256,t=1,p=1$c2FsdHNhbHQ$aGFzaGhhc2g")]
    [InlineData("$argon2id$v=16$m=256,t=1,p=1$c2FsdHNhbHQ$aGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=256,t=1$c2FsdHNhbHQ$aGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=99999999,t=1,p=1$c2FsdHNhbHQ$aGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=256,t=1,p=1$!!!!$aGFzaGhhc2g")]
    public void AnUnusableStoredHashIsRefusedQuietlyRatherThanThrowing(string? stored)
    {
        var hasher = Hasher();

        // "!" is the placeholder FirstRunSeeder writes: an account nothing can authenticate as
        // (docs/01_DATA_MODEL.md §11). A sign-in screen must treat every one of these exactly
        // like a wrong password, so none of them may throw.
        hasher.Verify("anything", stored).Should().BeFalse();
        hasher.IsUsable(stored).Should().BeFalse();
    }

    [Fact]
    public void AFreshlyWrittenHashIsUsable()
    {
        Hasher().IsUsable(Hasher().Hash("hunter2")).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("123")]
    public void FR_1_3_APasswordShorterThanThePolicyIsRefusedInPlainLanguage(string password)
    {
        var hash = () => Hasher().Hash(password);

        hash.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 4 characters*");
    }

    [Fact]
    public void FR_1_3_AFourCharacterPinIsAccepted()
    {
        // SRS FR-1.1 allows a PIN as well as a password. See PasswordHasher.MinimumPasswordLength
        // for why four is enough here and what carries the weight instead.
        PasswordHasher.MinimumPasswordLength.Should().Be(4);

        var encoded = Hasher().Hash("4821");

        Hasher().Verify("4821", encoded).Should().BeTrue();
    }

    [Fact]
    public void NFR_S1_TheShippedWorkFactorsAreRfc9106SecondRecommendedOption()
    {
        // 64 MiB, t=3, p=4. The lane count is a latency dial and not a security one - see
        // Argon2Parameters for the measurement that chose it and why an attacker pays the same
        // either way. HW-T07 retunes all three against the shop terminal.
        Argon2Parameters.Default.MemoryKib.Should().Be(65_536, "64 MB");
        Argon2Parameters.Default.Iterations.Should().Be(3);
        Argon2Parameters.Default.Parallelism.Should().Be(4);
        Argon2Parameters.SaltBytes.Should().Be(16);
        Argon2Parameters.HashBytes.Should().Be(32);
    }
}
