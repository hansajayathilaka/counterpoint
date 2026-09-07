using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Counterpoint.Application.Security;

/// <summary>
/// Argon2id password hashing (SRS FR-1.3, NFR-S1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The stored form.</b> One self-describing PHC string per account:
/// <c>$argon2id$v=19$m=65536,t=3,p=1$&lt;salt&gt;$&lt;hash&gt;</c>, salt and hash in unpadded
/// base64. Self-describing matters: when <c>HW-T07</c> retunes the work factors against the shop
/// terminal, every account hashed under the old ones still verifies, because the cost is read
/// back out of the row rather than assumed from this file.
/// </para>
/// <para>
/// <b>What is never stored.</b> The password. There is no encryption here, no key, and no way
/// back: <see cref="Verify"/> re-derives and compares in constant time. A forgotten password is
/// reset by the owner through <see cref="IUserAdministration"/>, which writes a new hash.
/// </para>
/// <para>
/// <b>Why Application and not Infrastructure.</b> This is arithmetic over byte arrays. It opens
/// no connection, touches no file and asks the operating system for nothing but random bytes, so
/// it is a use case, not an adapter - and keeping it here is what lets the authorisation rules
/// that depend on it stay in the Application layer too (CLAUDE.md invariant 8).
/// </para>
/// </remarks>
public sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>The algorithm tag. Only Argon2id is written, and only Argon2id is accepted.</summary>
    private const string Algorithm = "argon2id";

    /// <summary>Argon2 version 1.3, as decimal 19 - the value RFC 9106 standardised.</summary>
    private const int Version = 19;

    /// <summary>
    /// Ceilings applied to the work factors <em>read back from a stored hash</em>. Every string
    /// this class writes is far below them; they exist so that a corrupted or tampered row cannot
    /// turn a login attempt into an out-of-memory kill on the till.
    /// </summary>
    private const int MaximumStoredMemoryKib = 1_048_576;

    private const int MaximumStoredIterations = 32;
    private const int MaximumStoredParallelism = 32;
    private const int MaximumStoredHashBytes = 128;

    /// <summary>
    /// The shortest password or PIN the shop may set (SRS FR-1.1 allows either).
    /// </summary>
    /// <remarks>
    /// Four, deliberately, and recorded in <c>app_setting</c> so the owner can see it. A hardware
    /// counter is a place where a long password gets written on a sticky note, and the defence
    /// that actually holds here is not entropy in the secret: it is Argon2id making an offline
    /// guess expensive and <see cref="AccountLockout"/> making an online one hopeless - five
    /// tries, then exponential backoff. A four-digit PIN under that policy allows roughly four
    /// guesses an hour.
    /// </remarks>
    public const int MinimumPasswordLength = 4;

    private readonly Argon2Parameters _parameters;

    public PasswordHasher(Argon2Parameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfLessThan(parameters.MemoryKib, 8);
        ArgumentOutOfRangeException.ThrowIfLessThan(parameters.Iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(parameters.Parallelism, 1);

        _parameters = parameters;
    }

    /// <summary>The work factors this instance hashes new passwords with.</summary>
    public Argon2Parameters Parameters => _parameters;

    /// <inheritdoc />
    public string Hash(string password)
    {
        RequirePasswordIsAcceptable(password);

        var salt = RandomNumberGenerator.GetBytes(Argon2Parameters.SaltBytes);
        var hash = Derive(password, salt, _parameters.MemoryKib, _parameters.Iterations, _parameters.Parallelism, Argon2Parameters.HashBytes);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"${Algorithm}$v={Version}$m={_parameters.MemoryKib},t={_parameters.Iterations},p={_parameters.Parallelism}${Base64(salt)}${Base64(hash)}");
    }

    /// <inheritdoc />
    public bool Verify(string password, string? encodedHash)
    {
        ArgumentNullException.ThrowIfNull(password);

        // An empty password cannot be the one anything here was hashed from - Hash refuses
        // anything shorter than MinimumPasswordLength - so this is a definite "no" rather than a
        // short cut. It also keeps an empty box on the login screen from reaching Argon2, which
        // rejects a zero-length password with an exception a sign-in path must not see.
        if (password.Length == 0)
        {
            return false;
        }

        if (!TryParse(encodedHash, out var memoryKib, out var iterations, out var parallelism, out var salt, out var expected))
        {
            return false;
        }

        var actual = Derive(password, salt, memoryKib, iterations, parallelism, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <inheritdoc />
    public bool IsUsable(string? encodedHash) => TryParse(encodedHash, out _, out _, out _, out _, out _);

    /// <summary>
    /// Refuses a password the shop's policy does not allow, in the words the person typing it
    /// needs (SRS UI-06).
    /// </summary>
    public static void RequirePasswordIsAcceptable(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (password.Length < MinimumPasswordLength)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"A password or PIN must be at least {MinimumPasswordLength} characters long."));
        }
    }

    /// <summary>
    /// Runs Argon2id. The password is encoded UTF-8 and the derived bytes are returned; the
    /// caller decides whether they are being stored or compared.
    /// </summary>
    private static byte[] Derive(
        string password,
        byte[] salt,
        int memoryKib,
        int iterations,
        int parallelism,
        int length)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(length);
    }

    /// <summary>
    /// Reads a stored PHC string back into its parts.
    /// </summary>
    /// <remarks>
    /// It returns false rather than throwing for every kind of bad input - the wrong algorithm,
    /// the wrong version, a truncated string, a work factor outside the sane ceilings, or the
    /// <c>!</c> placeholder the first-run seed writes. A login screen must treat "there is no
    /// usable credential here" exactly like "that was the wrong password", and an exception
    /// escaping into the sign-in path would do neither.
    /// </remarks>
    private static bool TryParse(
        string? encodedHash,
        out int memoryKib,
        out int iterations,
        out int parallelism,
        out byte[] salt,
        out byte[] hash)
    {
        memoryKib = 0;
        iterations = 0;
        parallelism = 0;
        salt = [];
        hash = [];

        if (string.IsNullOrEmpty(encodedHash))
        {
            return false;
        }

        // $argon2id$v=19$m=..,t=..,p=..$salt$hash - a leading '$' gives an empty first field.
        var parts = encodedHash.Split('$');
        if (parts.Length != 6 || parts[0].Length != 0)
        {
            return false;
        }

        if (!string.Equals(parts[1], Algorithm, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadTagged(parts[2], "v=", out var version) || version != Version)
        {
            return false;
        }

        var costs = parts[3].Split(',');
        if (costs.Length != 3 ||
            !TryReadTagged(costs[0], "m=", out memoryKib) ||
            !TryReadTagged(costs[1], "t=", out iterations) ||
            !TryReadTagged(costs[2], "p=", out parallelism))
        {
            return false;
        }

        if (memoryKib is < 8 or > MaximumStoredMemoryKib ||
            iterations is < 1 or > MaximumStoredIterations ||
            parallelism is < 1 or > MaximumStoredParallelism)
        {
            return false;
        }

        if (!TryFromBase64(parts[4], out salt) || salt.Length == 0 ||
            !TryFromBase64(parts[5], out hash) || hash.Length is 0 or > MaximumStoredHashBytes)
        {
            return false;
        }

        return true;
    }

    private static bool TryReadTagged(string field, string tag, out int value)
    {
        value = 0;

        return field.StartsWith(tag, StringComparison.Ordinal)
            && int.TryParse(field.AsSpan(tag.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Unpadded base64, as PHC strings are written.</summary>
    private static string Base64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=');

    private static bool TryFromBase64(string value, out byte[] bytes)
    {
        bytes = [];

        if (value.Length == 0)
        {
            return false;
        }

        var padded = (value.Length % 4) switch
        {
            2 => value + "==",
            3 => value + "=",
            0 => value,
            _ => null,
        };

        if (padded is null)
        {
            return false;
        }

        var buffer = new byte[(padded.Length / 4) * 3];
        if (!Convert.TryFromBase64String(padded, buffer, out var written))
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }
}
