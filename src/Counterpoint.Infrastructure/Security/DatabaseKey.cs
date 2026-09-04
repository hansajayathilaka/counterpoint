namespace Counterpoint.Infrastructure.Security;

/// <summary>Shape of the SQLCipher database key. One place, so the stores cannot disagree.</summary>
public static class DatabaseKey
{
    /// <summary>256 bits, the raw key size SQLCipher expects when given <c>x'...'</c> key material.</summary>
    public const int SizeInBytes = 32;
}
