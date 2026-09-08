namespace Counterpoint.Application.Catalogue;

/// <summary>
/// What <see cref="IUomMaintenance"/> needs to create or edit a unit of measure.
/// </summary>
/// <param name="Name">The unit's name: 'Metre', 'Piece', 'Coil'.</param>
/// <param name="Symbol">The short form printed on a receipt: 'm', 'pc', 'coil'.</param>
/// <param name="DecimalPlaces">How finely the unit can be sold, 0 to 4 (<c>ck_uom_decimal_places</c>).</param>
public sealed record SaveUomCommand(string Name, string Symbol, int DecimalPlaces);
