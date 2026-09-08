using System.Collections.Generic;

namespace Counterpoint.Domain.Catalogue;

/// <summary>
/// One dimension of a variant matrix, for example <c>length</c> with values
/// <c>{"25mm","32mm",...}</c> (SRS FR-2.6). <see cref="VariantMatrixGenerator"/> takes the
/// cartesian product of every axis given to it.
/// </summary>
/// <param name="Name">The attribute name, for example <c>"length"</c> or <c>"finish"</c>.</param>
/// <param name="Values">Every value this axis takes, for example <c>["25mm","32mm","40mm"]</c>.</param>
public sealed record VariantAxis(string Name, IReadOnlyList<string> Values);
