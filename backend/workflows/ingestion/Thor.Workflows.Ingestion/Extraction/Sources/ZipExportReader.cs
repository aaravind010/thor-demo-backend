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
        using var entryStream = OpenFirstEntry(zipArchiveBytes);
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Opens the same entry as <see cref="ReadFirstEntry"/> as a forward-only stream, without buffering
    /// its contents. Disposing the stream releases the archive.
    /// </summary>
    public static Stream OpenFirstEntry(byte[] zipArchiveBytes)
    {
        var zip = new ZipArchive(new MemoryStream(zipArchiveBytes), ZipArchiveMode.Read);
        try
        {
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Length > 0)
                ?? throw new InvalidDataException("Zip archive contains no file entries.");
            return new ArchiveEntryStream(entry.Open(), zip);
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    private sealed class ArchiveEntryStream(Stream entryStream, ZipArchive owner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => entryStream.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => entryStream.Read(buffer);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                entryStream.Dispose();
                owner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
