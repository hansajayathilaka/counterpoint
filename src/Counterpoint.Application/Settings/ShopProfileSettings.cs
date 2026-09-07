namespace Counterpoint.Application.Settings;

/// <summary>
/// FR-10.1 - who the shop is, as it appears at the top of every bill and report.
/// </summary>
/// <param name="Name">Trading name.</param>
/// <param name="AddressLine1">First address line.</param>
/// <param name="AddressLine2">Second address line, blank when the address fits on one.</param>
/// <param name="Phone">Telephone, as it is printed.</param>
/// <param name="Email">Email, as it is printed.</param>
/// <param name="TaxRegistrationNumber">
/// The shop's tax registration number. Empty until the shop has one: the tax regime is a data
/// decision taken at first run, never a code decision (Q-02).
/// </param>
/// <param name="LogoPath">
/// Absolute path of the logo bitmap, or empty for no logo. A path and not the image itself -
/// <c>app_setting.value</c> is TEXT, and a receipt logo belongs on disk beside the database.
/// </param>
public sealed record ShopProfileSettings(
    string Name,
    string AddressLine1,
    string AddressLine2,
    string Phone,
    string Email,
    string TaxRegistrationNumber,
    string LogoPath);
