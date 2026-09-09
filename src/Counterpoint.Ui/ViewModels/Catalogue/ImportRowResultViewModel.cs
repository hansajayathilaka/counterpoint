using System;
using Counterpoint.Application.Import;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>One sampled row from an <see cref="ImportPreviewReport"/> bucket, shown as text (SRS FR-2.22).</summary>
public sealed class ImportRowResultViewModel
{
    public ImportRowResultViewModel(ImportRowResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        RowNumber = result.RowNumber;
        Code = result.Code ?? "(blank)";
        Outcome = result.Outcome.ToString();
        ErrorsText = result.Errors.Count == 0 ? string.Empty : string.Join("; ", result.Errors);
    }

    public int RowNumber { get; }

    public string Code { get; }

    public string Outcome { get; }

    /// <summary>Every validation problem this row failed, joined into one line - empty for anything but an error row.</summary>
    public string ErrorsText { get; }
}
