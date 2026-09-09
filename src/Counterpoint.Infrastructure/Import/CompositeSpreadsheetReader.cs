using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Import;

namespace Counterpoint.Infrastructure.Import;

/// <summary>
/// <see cref="ISpreadsheetReader"/>, dispatched by file extension to ClosedXML or CsvHelper - the
/// one registration the composition root wires, so the Application layer never has to know which
/// package read the file (SRS FR-2.22).
/// </summary>
internal sealed class CompositeSpreadsheetReader : ISpreadsheetReader
{
    /// <inheritdoc />
    public Task<SpreadsheetTable> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("There is no file at this path.", filePath);
        }

        return Path.GetExtension(filePath).ToUpperInvariant() switch
        {
            ".XLSX" or ".XLS" => ClosedXmlSpreadsheetReader.ReadAsync(filePath, cancellationToken),
            ".CSV" => CsvHelperSpreadsheetReader.ReadAsync(filePath, cancellationToken),
            var extension => throw new NotSupportedException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{extension}' is not a supported spreadsheet format. Use .xlsx or .csv.")),
        };
    }
}
