using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Counterpoint.Application.Import;

namespace Counterpoint.Infrastructure.Import;

/// <summary>Writes a <see cref="SpreadsheetTable"/> as a single-sheet <c>.xlsx</c> workbook (SRS FR-2.23).</summary>
internal static class ClosedXmlSpreadsheetWriter
{
    private const string SheetName = "Catalogue";

    public static Task WriteAsync(string filePath, SpreadsheetTable table, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(SheetName);

        for (var column = 0; column < table.Headers.Count; column++)
        {
            worksheet.Cell(1, column + 1).Value = table.Headers[column];
        }

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var cells = table.Rows[row];
            for (var column = 0; column < cells.Count; column++)
            {
                worksheet.Cell(row + 2, column + 1).Value = cells[column] ?? string.Empty;
            }
        }

        workbook.SaveAs(filePath);
        return Task.CompletedTask;
    }
}
