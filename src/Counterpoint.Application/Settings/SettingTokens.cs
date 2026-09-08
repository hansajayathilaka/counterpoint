using System;
using Counterpoint.Domain.Services;

namespace Counterpoint.Application.Settings;

/// <summary>
/// The stored text of every enumerated setting.
/// </summary>
/// <remarks>
/// Written out rather than taken from <c>Enum.ToString</c> and <c>Enum.Parse</c>. The token is
/// data in the shop's database and the C# member name is not: renaming
/// <see cref="NegativeStockPolicy.Allow"/> must be a refactor, not a silent reversion of the
/// shop's negative-stock policy to its default the next time the till starts. An unrecognised
/// token falls back rather than throwing, for the same reason
/// <see cref="SettingsSerializer.FromRows"/> does: one bad row must not stop the till trading.
/// </remarks>
internal static class SettingTokens
{
    internal static string From(CurrencySymbolPosition value) => value switch
    {
        CurrencySymbolPosition.After => "AFTER",
        _ => "BEFORE",
    };

    internal static CurrencySymbolPosition ToSymbolPosition(string token, CurrencySymbolPosition fallback) =>
        token switch
        {
            "BEFORE" => CurrencySymbolPosition.Before,
            "AFTER" => CurrencySymbolPosition.After,
            _ => fallback,
        };

    internal static string From(RoundingRule value) => value switch
    {
        RoundingRule.HalfToEven => "HALF_TO_EVEN",
        _ => "HALF_AWAY_FROM_ZERO",
    };

    internal static RoundingRule ToRoundingRule(string token, RoundingRule fallback) => token switch
    {
        "HALF_AWAY_FROM_ZERO" => RoundingRule.HalfAwayFromZero,
        "HALF_TO_EVEN" => RoundingRule.HalfToEven,
        _ => fallback,
    };

    internal static string From(RefundMethod value) => value switch
    {
        RefundMethod.CreditNote => "CREDIT_NOTE",
        RefundMethod.Card => "CARD",
        _ => "CASH",
    };

    internal static RefundMethod ToRefundMethod(string token, RefundMethod fallback) => token switch
    {
        "CASH" => RefundMethod.Cash,
        "CREDIT_NOTE" => RefundMethod.CreditNote,
        "CARD" => RefundMethod.Card,
        _ => fallback,
    };

    internal static string From(NegativeStockPolicy value) => value switch
    {
        NegativeStockPolicy.Warn => "WARN",
        NegativeStockPolicy.Block => "BLOCK",
        _ => "ALLOW",
    };

    internal static NegativeStockPolicy ToNegativeStockPolicy(string token, NegativeStockPolicy fallback) =>
        token switch
        {
            "ALLOW" => NegativeStockPolicy.Allow,
            "WARN" => NegativeStockPolicy.Warn,
            "BLOCK" => NegativeStockPolicy.Block,
            _ => fallback,
        };

    internal static string From(ScannerSuffix value) => value switch
    {
        ScannerSuffix.Tab => "TAB",
        ScannerSuffix.None => "NONE",
        _ => "ENTER",
    };

    internal static ScannerSuffix ToScannerSuffix(string token, ScannerSuffix fallback) => token switch
    {
        "ENTER" => ScannerSuffix.Enter,
        "TAB" => ScannerSuffix.Tab,
        "NONE" => ScannerSuffix.None,
        _ => fallback,
    };

    internal static string From(CloudBackupTarget value) => value switch
    {
        CloudBackupTarget.GoogleDrive => "GOOGLE_DRIVE",
        _ => "NONE",
    };

    internal static CloudBackupTarget ToCloudTarget(string token, CloudBackupTarget fallback) => token switch
    {
        "NONE" => CloudBackupTarget.None,
        "GOOGLE_DRIVE" => CloudBackupTarget.GoogleDrive,
        _ => fallback,
    };

    /// <summary>Time of day as <c>HH:mm</c>, invariant, 24-hour.</summary>
    internal static string From(TimeOnly value) =>
        value.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture);

    internal static TimeOnly ToTimeOfDay(string token, TimeOnly fallback) =>
        TimeOnly.TryParseExact(
            token,
            "HH\\:mm",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsed)
            ? parsed
            : fallback;
}
