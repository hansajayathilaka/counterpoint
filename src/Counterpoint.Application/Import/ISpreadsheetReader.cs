using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Import;

/// <summary>
/// Reads a <c>.xlsx</c> or <c>.csv</c> file into a <see cref="SpreadsheetTable"/> (SRS FR-2.22).
/// </summary>
/// <remarks>
/// A port. The Application layer states what it needs; <c>Counterpoint.Infrastructure</c> supplies
/// the ClosedXML/CsvHelper implementation - the same split <see cref="Persistence.ISaleWriter"/>
/// (Application) and <c>SqliteSaleWriter</c> (Infrastructure) already draw, applied to a file
/// instead of a database connection. Reading a spreadsheet is I/O, and I/O is Infrastructure's job
/// even though what it produces is consumed entirely by an Application-layer service.
/// </remarks>
public interface ISpreadsheetReader
{
    /// <summary>
    /// Reads the first sheet of an Excel workbook, or every row of a CSV file, keyed by the
    /// extension of <paramref name="filePath"/>.
    /// </summary>
    /// <exception cref="System.IO.FileNotFoundException">There is no file at <paramref name="filePath"/>.</exception>
    /// <exception cref="System.NotSupportedException">The extension is neither <c>.xlsx</c>/<c>.xls</c> nor <c>.csv</c>.</exception>
    public Task<SpreadsheetTable> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}
