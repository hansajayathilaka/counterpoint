using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Settings;

/// <summary>
/// FR-10.3 - how the shop charges tax. Fully configurable, and deliberately empty of any
/// particular regime (Q-02): the rates live in <c>tax_class</c> rows and the regime is a data
/// decision taken at first run, never a code decision. There is no VAT anywhere in this codebase.
/// </summary>
/// <param name="PricesIncludeTax">
/// True when a catalogue price already contains its tax and the bill carves it out
/// (<c>price - price/(1+rate)</c>); false when tax is added on top of the net price.
/// </param>
/// <param name="DefaultTaxClassName">
/// The tax class a new product is given when nobody chooses one, and the class first run creates.
/// </param>
/// <param name="DefaultTaxRate">
/// The rate that class carries. Zero by default: a rate this build invented would be a wrong
/// number printed on a bill, which is worse than none.
/// </param>
/// <param name="TaxLabel">
/// What tax is called on the bill and on screen - the shop's word, not the developer's (NFR-U3).
/// </param>
public sealed record TaxSettings(
    bool PricesIncludeTax,
    string DefaultTaxClassName,
    TaxRate DefaultTaxRate,
    string TaxLabel);
