using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Import;
using CsvHelper;

namespace Counterpoint.Infrastructure.Import;

/// <summary>Writes a <see cref="SpreadsheetTable"/> as a <c>.csv</c> file (SRS FR-2.23).</summary>
internal static class CsvHelperSpreadsheetWriter
{
    public static async Task WriteAsync(string filePath, SpreadsheetTable table, CancellationToken cancellationToken)
    {
        using var textWriter = new StreamWriter(filePath);
        using var csv = new CsvWriter(textWriter, CultureInfo.InvariantCulture, leaveOpen: false);

        foreach (var header in table.Headers)
        {
            csv.WriteField(header);
        }

        await csv.NextRecordAsync().ConfigureAwait(false);

        foreach (var row in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var cell in row)
            {
                csv.WriteField(cell ?? string.Empty);
            }

            await csv.NextRecordAsync().ConfigureAwait(false);
        }
    }
}
