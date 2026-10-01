using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Thor.Workflows.Hosting;

/// <summary>
/// Derives a deterministic RunId from tenant/manifest/namespace inputs, or a random one when
/// neither an explicit RunId nor a manifest is given.
/// </summary>
public static class DeterministicRunId
{
    public static Guid Derive(Guid tenantId, Guid? scanManifestId, Guid? explicitRunId, Guid namespaceId, ILogger logger)
    {
        if (explicitRunId is { } given)
        {
            return given;
        }

        if (scanManifestId is { } manifestId)
        {
            return NameBasedGuidV5(namespaceId, $"{tenantId:D}:{manifestId:D}");
        }

        logger.LogWarning(
            "Full-table-scan fallback with no explicit RunId for tenant {TenantId} — using a " +
            "random RunId; retries of this run will NOT collapse via this workflow's idempotency keys.",
            tenantId);
        return Guid.NewGuid();
    }

    /// <summary>RFC 4122 §4.3 name-based UUID, version 5 (SHA-1).</summary>
    private static Guid NameBasedGuidV5(Guid namespaceId, string name)
    {
        Span<byte> namespaceBytes = stackalloc byte[16];
        namespaceId.TryWriteBytes(namespaceBytes);
        SwapGuidByteOrder(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var toHash = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(toHash);
        nameBytes.CopyTo(toHash.AsSpan(namespaceBytes.Length));

        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(toHash, hash);

        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // variant RFC 4122

        SwapGuidByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    /// <summary>Swaps a GUID's byte order between .NET's internal layout and RFC 4122 network order.</summary>
    private static void SwapGuidByteOrder(Span<byte> guidBytes)
    {
        (guidBytes[0], guidBytes[3]) = (guidBytes[3], guidBytes[0]);
        (guidBytes[1], guidBytes[2]) = (guidBytes[2], guidBytes[1]);
        (guidBytes[4], guidBytes[5]) = (guidBytes[5], guidBytes[4]);
        (guidBytes[6], guidBytes[7]) = (guidBytes[7], guidBytes[6]);
    }
}
