using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Settings.FirstRun;

/// <summary>
/// One tax class the shop trades under, as the first-run wizard collects it (SRS FR-10.3).
/// </summary>
/// <param name="Name">What the shop calls it, for example <c>Exempt</c>.</param>
/// <param name="Rate">Its rate. Zero is a perfectly good answer, and is the default (Q-02).</param>
public sealed record TaxClassDefinition(string Name, TaxRate Rate);
