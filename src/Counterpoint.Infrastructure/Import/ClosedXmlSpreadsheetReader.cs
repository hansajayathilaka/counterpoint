using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Counterpoint.Application.Import;

namespace Counterpoint.Infrastructure.Import;

/// <summary>Reads the first sheet of an <c>.xlsx</c>/<c>.xls</c> workbook (SRS FR-2.22).</summary>
internal static class ClosedXmlSpreadsheetReader
{
    /// <summary>
    /// Synchronous under the hood - ClosedXML has no async file API - but kept <c>Task</c>-shaped
    /// so <see cref="CompositeSpreadsheetReader"/> can dispatch to it and to
    /// <see cref="CsvHelperSpreadsheetReader"/> through one awaited call.
    /// </summary>
    public static Task<SpreadsheetTable> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        if (lastRow == 0 || lastColumn == 0)
        {
            return Task.FromResult(new SpreadsheetTable([], []));
        }

        var headers = new List<string>(lastColumn);
        for (var column = 1; column <= lastColumn; column++)
        {
            headers.Add(worksheet.Cell(1, column).GetString());
        }

        var rows = new List<IReadOnlyList<string?>>(lastRow > 1 ? lastRow - 1 : 0);
        for (var row = 2; row <= lastRow; row++)
        {
            var cells = new List<string?>(lastColumn);
            for (var column = 1; column <= lastColumn; column++)
            {
                cells.Add(worksheet.Cell(row, column).GetString());
            }

            rows.Add(cells);
        }

        return Task.FromResult(new SpreadsheetTable(headers, rows));
    }
}
