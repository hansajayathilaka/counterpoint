using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Import;

namespace Counterpoint.Infrastructure.Import;

/// <summary>
/// <see cref="ISpreadsheetWriter"/>, dispatched by file extension to ClosedXML or CsvHelper (SRS
/// FR-2.23), the write-side twin of <see cref="CompositeSpreadsheetReader"/>.
/// </summary>
internal sealed class CompositeSpreadsheetWriter : ISpreadsheetWriter
{
    /// <inheritdoc />
    public Task WriteAsync(string filePath, SpreadsheetTable table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(table);

        return Path.GetExtension(filePath).ToUpperInvariant() switch
        {
            ".XLSX" => ClosedXmlSpreadsheetWriter.WriteAsync(filePath, table, cancellationToken),
            ".CSV" => CsvHelperSpreadsheetWriter.WriteAsync(filePath, table, cancellationToken),
            var extension => throw new NotSupportedException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{extension}' is not a supported spreadsheet format. Use .xlsx or .csv.")),
        };
    }
}
