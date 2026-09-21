using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Thor.Workflows.Ingestion.Extraction;

/// <summary>
/// Best-effort recovery for JSON that doesn't quite parse: trailing commas/comments (fixed for
/// free via lenient reader options) and truncation (the byte stream ends mid-value, e.g. an
/// export that got cut off mid-write) — closes whatever was left open and drops only the
/// incomplete trailing fragment. Does not attempt to fix an invalid token inside an otherwise
/// well-bracketed document; guessing at that risks fabricating data, so those are reported as
/// unrecoverable instead.
/// </summary>
public static class JsonRepair
{
    private static readonly JsonDocumentOptions LenientOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Parses one top-level JSON value starting at the beginning of <paramref name="bytes"/>.
    /// <c>BytesConsumed</c> always points past the region that was consumed by this attempt —
    /// callers should resume splitting subsequent documents from there whether or not recovery
    /// succeeded.
    /// </summary>
    public static (JsonNode? Node, int BytesConsumed) TryParse(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = LenientOptions.AllowTrailingCommas,
            CommentHandling = LenientOptions.CommentHandling,
        });
        try
        {
            reader.Read();
            var node = JsonNode.Parse(ref reader);
            return (node, (int)reader.BytesConsumed);
        }
        catch (JsonException)
        {
            // fall through to recovery below
        }

        var end = FindTopLevelValueEnd(bytes, 0);
        if (end >= 0)
        {
            // A real closing bracket exists but the contents still didn't parse (e.g. an
            // invalid token) — not a shape we can safely guess-fix. Skip past it.
            return (null, end);
        }

        var repaired = TryRepairTruncated(bytes);
        return (repaired, bytes.Length);
    }

    /// <summary>String overload for AccountsPaths' double-encoded JSON.</summary>
    public static JsonNode? TryParse(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var (node, _) = TryParse(bytes);
        return node;
    }

    /// <summary>
    /// Parses <paramref name="bytes"/> as a top-level JSON object, falling back to
    /// <see cref="TryParse(ReadOnlySpan{byte})"/> repair on failure. <c>Repaired</c> indicates
    /// whether the fallback was needed to produce <c>Root</c>.
    /// </summary>
    public static (JsonObject? Root, bool Repaired) TryParseObject(ReadOnlySpan<byte> bytes)
    {
        try
        {
            if (JsonNode.Parse(bytes) is JsonObject direct)
            {
                return (direct, false);
            }
        }
        catch (JsonException)
        {
            // fall through to recovery below
        }

        var (node, _) = TryParse(bytes);
        return node is JsonObject repaired ? (repaired, true) : (null, false);
    }

    /// <summary>String overload of <see cref="TryParseObject(ReadOnlySpan{byte})"/>, e.g. for a double-encoded JSON string entry.</summary>
    public static (JsonObject? Node, bool Repaired) TryParseObject(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonObject direct)
            {
                return (direct, false);
            }
        }
        catch (JsonException)
        {
            // fall through to recovery below
        }

        return TryParse(json) is JsonObject repaired ? (repaired, true) : (null, false);
    }

    /// <summary>
    /// Scans forward from <paramref name="start"/>, tracking string/escape state and a
    /// bracket/brace stack, and returns the index just past the top-level value's closing
    /// bracket — or -1 if the bytes run out before it closes (truncated).
    /// </summary>
    private static int FindTopLevelValueEnd(ReadOnlySpan<byte> bytes, int start)
    {
        var i = start;
        while (i < bytes.Length && IsWhitespace(bytes[i]))
        {
            i++;
        }
        if (i >= bytes.Length || (bytes[i] != (byte)'{' && bytes[i] != (byte)'['))
        {
            return -1;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (; i < bytes.Length; i++)
        {
            var b = bytes[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (b == (byte)'\\')
                {
                    escaped = true;
                }
                else if (b == (byte)'"')
                {
                    inString = false;
                }
                continue;
            }

            if (b == (byte)'"')
            {
                inString = true;
            }
            else if (b == (byte)'{' || b == (byte)'[')
            {
                depth++;
            }
            else if (b == (byte)'}' || b == (byte)']')
            {
                depth--;
                if (depth == 0)
                {
                    return i + 1;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Closes a dangling open string, strips a trailing dangling comma or an incomplete
    /// trailing <c>"key":</c> fragment, then closes remaining open brackets in reverse order
    /// and reparses. Recovers everything up to the last complete field/element.
    /// </summary>
    private static JsonNode? TryRepairTruncated(ReadOnlySpan<byte> bytes)
    {
        var start = 0;
        while (start < bytes.Length && IsWhitespace(bytes[start]))
        {
            start++;
        }
        if (start >= bytes.Length || (bytes[start] != (byte)'{' && bytes[start] != (byte)'['))
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(bytes[start..]);
        var stack = new Stack<char>();
        var inString = false;
        var escaped = false;
        var lastSafeCut = -1; // index just after the last top-level-safe comma or opening bracket

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    stack.Push('}');
                    break;
                case '[':
                    stack.Push(']');
                    break;
                case '}' or ']':
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }
                    break;
                case ',':
                    lastSafeCut = i + 1;
                    break;
            }
        }

        // Still inside a string at end-of-input: whatever field/element was mid-write is
        // incomplete — the safest recovery point is the last complete comma before it (or the
        // start of the enclosing structure, if there was none).
        var repaired = inString && lastSafeCut >= 0
            ? text[..lastSafeCut]
            : text;

        // A trailing dangling "key": with no value, or a trailing dangling comma, is also
        // incomplete — cut back to the last safe comma.
        var trimmed = repaired.TrimEnd();
        if ((trimmed.EndsWith(':') || trimmed.EndsWith(',')) && lastSafeCut >= 0 && lastSafeCut < repaired.Length)
        {
            repaired = text[..lastSafeCut];
        }

        repaired = repaired.TrimEnd().TrimEnd(',');

        var closing = new StringBuilder();
        // Recompute what's left open against the (possibly shortened) repaired text.
        var openStack = ComputeOpenStack(repaired);
        while (openStack.Count > 0)
        {
            closing.Append(openStack.Pop());
        }

        var candidate = repaired + closing;
        try
        {
            return JsonNode.Parse(candidate, documentOptions: LenientOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Stack<char> ComputeOpenStack(string text)
    {
        var stack = new Stack<char>();
        var inString = false;
        var escaped = false;

        foreach (var c in text)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    stack.Push('}');
                    break;
                case '[':
                    stack.Push(']');
                    break;
                case '}' or ']':
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }
                    break;
            }
        }

        if (inString)
        {
            // Shouldn't happen given callers already cut back past any dangling string, but
            // close it defensively so reparse has a chance instead of throwing.
            stack.Push('"');
        }

        return stack;
    }

    private static bool IsWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
