using System.IO.Compression;
using System.Text;
using Thor.Workflows.Ingestion.Extraction.Sources;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Extraction;

public class ZipExportReaderTests
{
    private static byte[] BuildZip(params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }
        return stream.ToArray();
    }

    [Fact]
    public void ReadFirstEntry_SingleEntry_ReturnsItsExactBytes()
    {
        var expected = Encoding.UTF8.GetBytes("hello export");
        var zipBytes = BuildZip(("export.json", expected));

        var result = ZipExportReader.ReadFirstEntry(zipBytes);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReadFirstEntry_MultipleEntries_ReturnsOnlyTheFirstEntrysBytes()
    {
        var first = Encoding.UTF8.GetBytes("first-entry");
        var second = Encoding.UTF8.GetBytes("second-entry");
        var zipBytes = BuildZip(("a-export.json", first), ("b-export.json", second));

        var result = ZipExportReader.ReadFirstEntry(zipBytes);

        Assert.Equal(first, result);
    }

    [Fact]
    public void ReadFirstEntry_EmptyZip_ThrowsInvalidDataException()
    {
        var zipBytes = BuildZip();

        Assert.Throws<InvalidDataException>(() => ZipExportReader.ReadFirstEntry(zipBytes));
    }

    [Fact]
    public void ReadFirstEntry_NotAZipArchive_ThrowsInvalidDataException()
    {
        var garbage = Encoding.UTF8.GetBytes("not a zip file at all");

        Assert.Throws<InvalidDataException>(() => ZipExportReader.ReadFirstEntry(garbage));
    }

    [Fact]
    public void ReadFirstEntry_EntryNestedInsideFolder_ReturnsItsBytes()
    {
        var expected = Encoding.UTF8.GetBytes("nested export");
        var zipBytes = BuildZip(("export/", []), ("export/data.json", expected));

        var result = ZipExportReader.ReadFirstEntry(zipBytes);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReadFirstEntry_OnlyDirectoryEntries_ThrowsInvalidDataException()
    {
        var zipBytes = BuildZip(("export/", []));

        Assert.Throws<InvalidDataException>(() => ZipExportReader.ReadFirstEntry(zipBytes));
    }
}
