using System.Text;

namespace Thor.Workflows.Ingestion.Extraction;

/// <summary>Strips a leading UTF-8 BOM, if present, shared by every connector's raw-export parsing.</summary>
public static class BomStripper
{
    public static byte[] Strip(byte[] bytes)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        return bytes.Length >= preamble.Length && bytes.AsSpan(0, preamble.Length).SequenceEqual(preamble)
            ? bytes[preamble.Length..]
            : bytes;
    }
}
