using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Import;

/// <summary>
/// Writes a <see cref="SpreadsheetTable"/> out as a <c>.xlsx</c> or <c>.csv</c> file (SRS FR-2.23).
/// </summary>
/// <remarks>A port, the write-side twin of <see cref="ISpreadsheetReader"/>.</remarks>
public interface ISpreadsheetWriter
{
    /// <summary>
    /// Writes <paramref name="table"/> to <paramref name="filePath"/>, in the format its
    /// extension names.
    /// </summary>
    /// <exception cref="System.NotSupportedException">The extension is neither <c>.xlsx</c> nor <c>.csv</c>.</exception>
    public Task WriteAsync(string filePath, SpreadsheetTable table, CancellationToken cancellationToken = default);
}
