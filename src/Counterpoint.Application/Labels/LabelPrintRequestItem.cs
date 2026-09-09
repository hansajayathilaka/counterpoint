namespace Counterpoint.Application.Labels;

/// <summary>
/// One product to print labels for, and how many labels it needs (SRS FR-2.10, FR-2.12).
/// </summary>
/// <param name="ProductVariantId">The variant the label is for.</param>
/// <param name="QuantityPerLabel">
/// How many copies to print. At least 1 - <see cref="ILabelPrintService.PrintAsync"/> refuses
/// anything less.
/// </param>
public sealed record LabelPrintRequestItem(long ProductVariantId, int QuantityPerLabel);
