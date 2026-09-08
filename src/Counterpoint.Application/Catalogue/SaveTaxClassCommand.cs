using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Catalogue;

/// <summary>What <see cref="ITaxClassMaintenance"/> needs to create or edit a tax class (Q-02).</summary>
public sealed record SaveTaxClassCommand(string Name, TaxRate Rate);
