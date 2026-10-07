using System.Text;
using System.Text.Json.Nodes;

namespace Thor.Workflows.Ingestion.Extraction;

/// <summary>Running counts of the records a <see cref="StreamingRecordReader"/> pass repaired or gave up on.</summary>
public sealed class RecordTally
{
    private int _drainedRepaired;
    private int _drainedSkipped;

    public int Repaired { get; set; }
    public int Skipped { get; set; }

    /// <summary>Passes whose array was not found in the document.</summary>
    public int ArraysMissing { get; set; }

    /// <summary>Returns the counts accumulated since the previous call.</summary>
    public (int Repaired, int Skipped) Drain()
    {
        var delta = (Repaired - _drainedRepaired, Skipped - _drainedSkipped);
        _drainedRepaired = Repaired;
        _drainedSkipped = Skipped;
        return delta;
    }
}

/// <summary>
/// Streams the elements of one named array inside a JSON export as individual objects, holding only
/// one record in memory at a time. Record boundaries come from tracking strings and bracket kinds,
/// not from strict parsing, so a bad token inside one record cannot abort the scan; each record is
/// then parsed through <see cref="JsonRepair"/>.
/// </summary>
public static class StreamingRecordReader
{
    private const int BufferSize = 81920;
    private const int MaxKeyLength = 256;

    /// <summary>
    /// Yields each object of the array at <paramref name="path"/> (e.g. <c>Accounts</c>, <c>AccountDetails</c>).
    /// A record that needs repair is counted in <see cref="RecordTally.Repaired"/>; one that cannot be
    /// parsed is counted in <see cref="RecordTally.Skipped"/>. Input ending between records counts as one
    /// repair; ending inside one is counted through that record's own repair. Throws when the document's
    /// root is not an object.
    /// </summary>
    public static IEnumerable<JsonObject> Read(Stream stream, string[] path, RecordTally tally)
    {
        var scanner = new Scanner(stream);
        foreach (var record in scanner.ReadRecordBytes(path, tally))
        {
            var (root, repaired) = JsonRepair.TryParseObject(record);
            if (root is null)
            {
                tally.Skipped++;
                continue;
            }

            if (repaired)
            {
                tally.Repaired++;
            }
            yield return root;
        }
    }

    private sealed class Scanner(Stream stream)
    {
        private readonly byte[] _buffer = new byte[BufferSize];
        private int _position;
        private int _length;
        private int _pushedBack = -1;
        private byte[] _record = new byte[4096];
        private int _recordLength;
        private readonly List<byte> _open = [];

        public IEnumerable<byte[]> ReadRecordBytes(string[] path, RecordTally tally)
        {
            if (!TryEnterRoot())
            {
                throw new InvalidOperationException("Export did not parse to a JSON object.");
            }

            if (!TrySeekArray(path))
            {
                tally.ArraysMissing++;
                yield break;
            }

            while (true)
            {
                var b = NextSignificant();
                if (b is ']' or '}')
                {
                    yield break;
                }
                if (b < 0)
                {
                    tally.Repaired++;
                    yield break;
                }

                _recordLength = 0;
                var complete = CaptureElement((byte)b);
                if (_recordLength > 0)
                {
                    yield return _record.AsSpan(0, _recordLength).ToArray();
                }
                if (!complete)
                {
                    yield break;
                }
            }
        }

        /// <summary>Skips a leading BOM, whitespace and comments, and confirms the document opens with an object.</summary>
        private bool TryEnterRoot()
        {
            var b = Next();
            if (b == 0xEF && Next() == 0xBB && Next() == 0xBF)
            {
                b = Next();
            }
            while (IsWhitespace(b) || (b == '/' && TrySkipComment()))
            {
                b = Next();
            }
            return b == '{';
        }

        /// <summary>
        /// Advances to just past the <c>[</c> that opens the array at <paramref name="path"/>. Returns
        /// false if the input ends first or the root closes without it.
        /// </summary>
        private bool TrySeekArray(string[] path)
        {
            var pathKeys = path.Select(Encoding.UTF8.GetBytes).ToArray();
            // onPath[i]: the container at depth i+1 is the object the path expects there.
            var onPath = new List<bool> { true };
            var keyMatches = false;
            var afterColon = false;
            Span<byte> stringBuffer = stackalloc byte[MaxKeyLength];

            while (true)
            {
                var b = Next();
                if (b < 0)
                {
                    return false;
                }

                switch (b)
                {
                    case '"':
                        var text = ReadString(stringBuffer, out var truncated);
                        if (!afterColon)
                        {
                            var depth = onPath.Count;
                            keyMatches = !truncated && onPath[depth - 1] && depth <= pathKeys.Length && text.SequenceEqual(pathKeys[depth - 1]);
                        }
                        break;
                    case '/':
                        TrySkipComment();
                        break;
                    case ':':
                        afterColon = true;
                        break;
                    case '{':
                        onPath.Add(afterColon && keyMatches);
                        keyMatches = afterColon = false;
                        break;
                    case '[':
                        if (afterColon && keyMatches && onPath.Count == pathKeys.Length)
                        {
                            return true;
                        }
                        onPath.Add(false);
                        keyMatches = afterColon = false;
                        break;
                    case '}' or ']':
                        onPath.RemoveAt(onPath.Count - 1);
                        keyMatches = afterColon = false;
                        if (onPath.Count == 0)
                        {
                            return false;
                        }
                        break;
                    case ',':
                        keyMatches = afterColon = false;
                        break;
                }
            }
        }

        /// <summary>
        /// Reads one array element (starting at <paramref name="first"/>) into the record buffer. Returns
        /// false if the input ended before the element closed. A closer matches the nearest open bracket of
        /// its kind; one with no match is stray and stays in the record's bytes so the record fails to parse
        /// rather than being silently fixed.
        /// </summary>
        private bool CaptureElement(byte first)
        {
            _open.Clear();
            AddToRecord(first);
            if (first is (byte)'{' or (byte)'[')
            {
                _open.Add(first);
            }
            var inString = first == (byte)'"';
            var escaped = false;

            while (true)
            {
                var b = Next();
                if (b < 0)
                {
                    return false;
                }

                if (inString)
                {
                    AddToRecord((byte)b);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (b == '\\')
                    {
                        escaped = true;
                    }
                    else if (b == '"')
                    {
                        inString = false;
                        if (_open.Count == 0)
                        {
                            return true;
                        }
                    }
                    continue;
                }

                if (_open.Count == 0 && b is ',' or ']' or '}')
                {
                    // A bare value (not an object/array) ends at its delimiter; the delimiter is not part of it.
                    Push((byte)b);
                    return true;
                }

                if (b == '/' && TrySkipComment())
                {
                    AddToRecord((byte)' ');
                    continue;
                }

                AddToRecord((byte)b);
                if (b == '"')
                {
                    inString = true;
                }
                else if (b is '{' or '[')
                {
                    _open.Add((byte)b);
                }
                else if (b is '}' or ']')
                {
                    var opener = (byte)(b == '}' ? '{' : '[');
                    var match = _open.LastIndexOf(opener);
                    if (match >= 0)
                    {
                        _open.RemoveRange(match, _open.Count - match);
                        if (_open.Count == 0)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        /// <summary>Reads a string's raw (still-escaped) bytes up to its closing quote, keeping at most <paramref name="buffer"/>'s length.</summary>
        private ReadOnlySpan<byte> ReadString(Span<byte> buffer, out bool truncated)
        {
            var length = 0;
            truncated = false;
            var escaped = false;
            while (true)
            {
                var b = Next();
                if (b < 0)
                {
                    return buffer[..length];
                }
                if (escaped)
                {
                    escaped = false;
                }
                else if (b == '\\')
                {
                    escaped = true;
                }
                else if (b == '"')
                {
                    return buffer[..length];
                }

                if (length < buffer.Length)
                {
                    buffer[length++] = (byte)b;
                }
                else
                {
                    truncated = true;
                }
            }
        }

        /// <summary>Next byte that is not whitespace, a separating comma, or part of a comment; -1 at end of input.</summary>
        private int NextSignificant()
        {
            while (true)
            {
                var b = Next();
                if (b == ',' || IsWhitespace(b) || (b == '/' && TrySkipComment()))
                {
                    continue;
                }
                return b;
            }
        }

        /// <summary>Called after reading a <c>/</c>: consumes the rest of a <c>//</c> or <c>/* */</c> comment, or returns false (leaving the next byte unread) if it isn't one.</summary>
        private bool TrySkipComment()
        {
            var next = Next();
            if (next == '/')
            {
                int b;
                do
                {
                    b = Next();
                } while (b >= 0 && b != '\n');
                return true;
            }
            if (next == '*')
            {
                var previous = 0;
                int b;
                while ((b = Next()) >= 0)
                {
                    if (previous == '*' && b == '/')
                    {
                        break;
                    }
                    previous = b;
                }
                return true;
            }
            if (next >= 0)
            {
                Push((byte)next);
            }
            return false;
        }

        private void AddToRecord(byte b)
        {
            if (_recordLength == _record.Length)
            {
                Array.Resize(ref _record, _record.Length * 2);
            }
            _record[_recordLength++] = b;
        }

        private int Next()
        {
            if (_pushedBack >= 0)
            {
                var pushed = _pushedBack;
                _pushedBack = -1;
                return pushed;
            }
            if (_position == _length)
            {
                _length = stream.Read(_buffer, 0, _buffer.Length);
                _position = 0;
                if (_length == 0)
                {
                    return -1;
                }
            }
            return _buffer[_position++];
        }

        private void Push(byte b) => _pushedBack = b;

        private static bool IsWhitespace(int b) => b is ' ' or '\t' or '\r' or '\n';
    }
}
