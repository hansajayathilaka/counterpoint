using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>One column of a <see cref="ReportTableViewModel"/>: its heading, its width in pixels, and whether it holds a number.</summary>
/// <param name="Header">The heading text.</param>
/// <param name="Width">The column's width in device-independent pixels.</param>
/// <param name="IsNumeric">True to right-align the column (figures, quantities, dates that line up).</param>
public sealed record ReportColumn(string Header, int Width, bool IsNumeric = false);

/// <summary>One cell of a table, display-ready: text already formatted by <see cref="ReportText"/>.</summary>
public sealed class ReportCell
{
    public ReportCell(string text, int width, bool isNumeric, bool isBold, bool isWarning)
    {
        Text = text;
        Width = width;
        IsNumeric = isNumeric;
        IsBold = isBold;
        IsWarning = isWarning;
    }

    public string Text { get; }

    /// <summary>Pixels; a <see cref="double"/> because that is what Avalonia's <c>Width</c> takes.</summary>
    public double Width { get; }

    public bool IsNumeric { get; }

    public bool IsBold { get; }

    /// <summary>True for a cell that needs the owner's attention (a difference that should be zero, an open shift).</summary>
    public bool IsWarning { get; }
}

/// <summary>One row of a table.</summary>
public sealed class ReportTableRow
{
    public ReportTableRow(IReadOnlyList<ReportCell> cells)
    {
        Cells = cells;
    }

    public IReadOnlyList<ReportCell> Cells { get; }
}

/// <summary>
/// A display-only table the Stock, Tax and Cash report screens share: fixed columns, rows of formatted
/// text, an empty-state sentence and a row cap.
/// </summary>
/// <remarks>
/// <para>
/// This class only lays out text the Application layer's DTOs already carry; it adds, rounds and derives
/// nothing (SRS FR-9.6). A screen builds its tables from a DTO and each column's heading is the column's
/// meaning, which is also what the generic export (P3-T07) will read.
/// </para>
/// <para>
/// <b>Row cap.</b> The rows are plain text blocks, not a virtualised grid, so a 20 000-SKU stock list must
/// not be thrown at the screen whole. Past <see cref="RowLimit"/> rows the table stops adding and says how
/// many it left out; the owner narrows the filter.
/// </para>
/// </remarks>
public sealed partial class ReportTableViewModel : ViewModelBase
{
    /// <summary>How many rows a table shows before it says "narrow the filter".</summary>
    public const int RowLimit = 1000;

    private readonly ReportColumn[] _columns;

    [ObservableProperty]
    private string _emptyText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenRows))]
    private int _hiddenRowCount;

    public ReportTableViewModel(string title, string emptyText, params ReportColumn[] columns)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(emptyText);
        ArgumentNullException.ThrowIfNull(columns);

        Title = title;
        _emptyText = emptyText;
        _columns = columns;
        Headers = [.. columns.Select(column => new ReportCell(column.Header, column.Width, column.IsNumeric, isBold: true, isWarning: false))];
        Rows.CollectionChanged += OnRowsChanged;
    }

    /// <summary>The heading above the table; empty for none.</summary>
    public string Title { get; }

    public bool HasTitle => Title.Length > 0;

    public IReadOnlyList<ReportCell> Headers { get; }

    public ObservableCollection<ReportTableRow> Rows { get; } = [];

    /// <summary>True when there is at least one row.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>True when there are none - the screen then shows <see cref="EmptyText"/>.</summary>
    public bool IsEmpty => Rows.Count == 0;

    public bool HasHiddenRows => HiddenRowCount > 0;

    /// <summary>The sentence shown when rows were left out.</summary>
    public string HiddenRowsText => string.Create(
        CultureInfo.InvariantCulture,
        $"{HiddenRowCount} more rows are not shown. Narrow the filter or the dates to see them.");

    partial void OnHiddenRowCountChanged(int value) => OnPropertyChanged(nameof(HiddenRowsText));

    /// <summary>Removes every row.</summary>
    public void Clear()
    {
        Rows.Clear();
        HiddenRowCount = 0;
    }

    /// <summary>Adds an ordinary row. <paramref name="cells"/> must match the columns one for one.</summary>
    public void Add(params string[] cells) => AddRow(isBold: false, warningColumns: null, cells);

    /// <summary>Adds a bold row: a total, or a group heading.</summary>
    public void AddBold(params string[] cells) => AddRow(isBold: true, warningColumns: null, cells);

    /// <summary>Adds a row whose columns at <paramref name="warningColumns"/> are flagged for attention.</summary>
    public void AddWarning(IReadOnlyCollection<int> warningColumns, params string[] cells) =>
        AddRow(isBold: false, warningColumns, cells);

    private void AddRow(bool isBold, IReadOnlyCollection<int>? warningColumns, string[] cells)
    {
        if (cells.Length != _columns.Length)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"A row needs {_columns.Length} cells, got {cells.Length}."),
                nameof(cells));
        }

        if (Rows.Count >= RowLimit)
        {
            HiddenRowCount++;
            return;
        }

        Rows.Add(new ReportTableRow(
        [
            .. cells.Select((text, index) => new ReportCell(
                text,
                _columns[index].Width,
                _columns[index].IsNumeric,
                isBold,
                warningColumns?.Contains(index) == true)),
        ]));
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
