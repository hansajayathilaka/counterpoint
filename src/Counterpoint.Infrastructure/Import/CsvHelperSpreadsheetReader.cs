using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Import;
using CsvHelper;

namespace Counterpoint.Infrastructure.Import;

/// <summary>Reads a <c>.csv</c> file (SRS FR-2.22).</summary>
internal static class CsvHelperSpreadsheetReader
{
    public static async Task<SpreadsheetTable> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        using var textReader = new StreamReader(filePath);
        using var csv = new CsvReader(textReader, CultureInfo.InvariantCulture, leaveOpen: false);

        if (!await csv.ReadAsync().ConfigureAwait(false) || !csv.ReadHeader())
        {
            return new SpreadsheetTable([], []);
        }

        var headers = csv.HeaderRecord ?? [];
        var rows = new List<IReadOnlyList<string?>>();

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cells = new List<string?>(headers.Length);
            for (var i = 0; i < headers.Length; i++)
            {
                cells.Add(csv.GetField(i));
            }

            rows.Add(cells);
        }

        return new SpreadsheetTable(headers, rows);
    }
}
