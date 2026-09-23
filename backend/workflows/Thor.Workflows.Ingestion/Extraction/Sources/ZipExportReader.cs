using System.IO.Compression;

namespace Thor.Workflows.Ingestion.Extraction.Sources;

/// <summary>
/// Unzips a connector export archive's bytes — deliberately takes bytes, not a file path, so
/// it works identically regardless of which <see cref="IExportSource"/> produced them.
/// </summary>
public static class ZipExportReader
{
    /// <summary>
    /// Returns the bytes of the archive's one connector export member (§4.3: one zip → one
    /// output unit — the extractor never splits or fans out further). Skips directory
    /// placeholder entries (their <see cref="ZipArchiveEntry.Name"/> is empty) so an export
    /// wrapped in one or more folders is still read correctly.
    /// </summary>
    public static byte[] ReadFirstEntry(byte[] zipArchiveBytes)
    {
        using var stream = new MemoryStream(zipArchiveBytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.Length > 0)
            ?? throw new InvalidDataException("Zip archive contains no file entries.");
        using var entryStream = entry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
