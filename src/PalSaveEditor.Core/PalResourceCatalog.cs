using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace PalSaveEditor.Core;

public sealed class PalResourceCatalog
{
    private readonly string[] _words;
    private readonly ushort[]? _objectFlags;

    private PalResourceCatalog(
        string sourceDirectory,
        string[] words,
        int wordDatByteLength,
        ushort[]? objectFlags,
        int objectRecordSize,
        int eventObjectBytes)
    {
        SourceDirectory = sourceDirectory;
        _words = words;
        WordDatByteLength = wordDatByteLength;
        _objectFlags = objectFlags;
        ObjectRecordSize = objectRecordSize;
        EventObjectBytes = eventObjectBytes;
    }

    public string SourceDirectory { get; }
    public int WordCount => _words.Length;
    public int WordDatByteLength { get; }
    public int ObjectRecordSize { get; }
    public int EventObjectBytes { get; }
    public bool HasObjectMetadata => _objectFlags is not null;

    public static PalResourceCatalog? TryDiscover(string savePath)
    {
        var saveDirectory = Path.GetDirectoryName(Path.GetFullPath(savePath));
        if (saveDirectory is null)
        {
            return null;
        }

        foreach (var candidate in EnumerateCandidateDirectories(saveDirectory))
        {
            if (File.Exists(Path.Combine(candidate, "WORD.DAT")))
            {
                return Load(candidate);
            }
        }

        return null;
    }

    public static PalResourceCatalog Load(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var requestedDirectory = Path.GetFullPath(gameDirectory);
        var fullDirectory = TryResolveActiveProfileResources(requestedDirectory) ?? requestedDirectory;
        var wordPath = Path.Combine(fullDirectory, "WORD.DAT");
        if (!File.Exists(wordPath))
        {
            throw new FileNotFoundException("所选目录中没有 WORD.DAT。", wordPath);
        }

        var wordBytes = File.ReadAllBytes(wordPath);
        var wordPayloadLength = wordBytes.Length;
        if (wordPayloadLength % 10 == 2 && wordBytes[^2] == 0x0D && wordBytes[^1] == 0x0A)
        {
            wordPayloadLength -= 2;
        }

        if (wordPayloadLength == 0 || wordPayloadLength % 10 != 0)
        {
            throw new InvalidDataException($"WORD.DAT 长度 {wordBytes.Length:N0} 不是 10 字节记录，或梦幻版末尾 CRLF 的有效组合。");
        }

        ushort[]? flags = null;
        var recordSize = 0;
        var eventObjectBytes = 0;
        var sssPath = Path.Combine(fullDirectory, "SSS.MKF");
        if (File.Exists(sssPath))
        {
            eventObjectBytes = ReadMkfChunk(sssPath, 0).Length;
            var objectChunk = ReadMkfChunk(sssPath, 2);
            recordSize = DetectObjectRecordSize(objectChunk.Length);
            if (recordSize != 0)
            {
                var count = Math.Min(PalSaveLayout.ObjectCount, objectChunk.Length / recordSize);
                flags = new ushort[count];
                var flagsOffset = recordSize == PalSaveLayout.WinObjectRecordSize ? 12 : 10;
                for (var i = 0; i < count; i++)
                {
                    flags[i] = BinaryPrimitives.ReadUInt16LittleEndian(
                        objectChunk.AsSpan(i * recordSize + flagsOffset, sizeof(ushort)));
                }
            }
        }

        // Original DOS resources (including Dream 2.20) use Big5, while the
        // mainland Win95 release uses GBK. The object-record width is the same
        // edition signal used by the game data itself and avoids mojibake.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var codePage = recordSize == PalSaveLayout.DosObjectRecordSize ? 950 : 936;
        var wordEncoding = Encoding.GetEncoding(
            codePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ReplacementFallback);
        var words = new string[wordPayloadLength / 10];
        for (var i = 0; i < words.Length; i++)
        {
            var record = wordBytes.AsSpan(i * 10, 10);
            var zero = record.IndexOf((byte)0);
            var length = zero >= 0 ? zero : record.Length;
            words[i] = wordEncoding.GetString(record[..length]).Trim();
        }

        return new(fullDirectory, words, wordBytes.Length, flags, recordSize, eventObjectBytes);
    }

    public string GetWord(int id, string fallbackPrefix = "对象")
    {
        if ((uint)id < (uint)_words.Length && !string.IsNullOrWhiteSpace(_words[id]))
        {
            return _words[id];
        }

        return $"{fallbackPrefix} #{id}";
    }

    public string GetRoleName(int roleId, ushort nameWordId) =>
        GetWord(nameWordId, $"角色 {roleId}");

    public string GetObjectName(int objectId) => GetWord(objectId);

    public ushort GetObjectFlags(int objectId) =>
        _objectFlags is not null && (uint)objectId < (uint)_objectFlags.Length ? _objectFlags[objectId] : (ushort)0;

    public IReadOnlyList<(ushort Id, string Name)> SearchObjects(string? query, int maximum = 200)
    {
        var normalized = query?.Trim() ?? string.Empty;
        var result = new List<(ushort, string)>();
        var limit = Math.Min(_words.Length, ushort.MaxValue + 1);
        for (var id = 1; id < limit && result.Count < maximum; id++)
        {
            var name = _words[id];
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (normalized.Length == 0 || name.Contains(normalized, StringComparison.CurrentCultureIgnoreCase) ||
                id.ToString().Contains(normalized, StringComparison.Ordinal))
            {
                result.Add(((ushort)id, name));
            }
        }

        return result;
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string saveDirectory)
    {
        yield return saveDirectory;
        var parent = Directory.GetParent(saveDirectory);
        if (parent is not null)
        {
            yield return parent.FullName;
        }
    }

    private static string? TryResolveActiveProfileResources(string gameDirectory)
    {
        var profilesDirectory = Path.Combine(gameDirectory, "palmod", "Profiles");
        var pointerPath = Path.Combine(profilesDirectory, "current.json");
        if (!File.Exists(pointerPath))
        {
            return null;
        }

        try
        {
            using var pointer = JsonDocument.Parse(File.ReadAllBytes(pointerPath));
            if (!pointer.RootElement.TryGetProperty("staging_relative_path", out var relativeElement))
            {
                return null;
            }

            var relativePath = relativeElement.GetString();
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                return null;
            }

            var profilesRoot = Path.GetFullPath(profilesDirectory);
            var stagedDirectory = Path.GetFullPath(Path.Combine(profilesRoot, relativePath));
            var profilesPrefix = profilesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                 Path.DirectorySeparatorChar;
            if (!stagedDirectory.StartsWith(profilesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var resourcesDirectory = Path.Combine(stagedDirectory, "resources");
            return File.Exists(Path.Combine(resourcesDirectory, "WORD.DAT"))
                ? resourcesDirectory
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int DetectObjectRecordSize(int length)
    {
        const int minimumPlausibleObjectCount = 500;
        var winCount = length % PalSaveLayout.WinObjectRecordSize == 0
            ? length / PalSaveLayout.WinObjectRecordSize
            : 0;
        var dosCount = length % PalSaveLayout.DosObjectRecordSize == 0
            ? length / PalSaveLayout.DosObjectRecordSize
            : 0;

        if (winCount is >= minimumPlausibleObjectCount and <= PalSaveLayout.ObjectCount &&
            dosCount is not (>= minimumPlausibleObjectCount and <= PalSaveLayout.ObjectCount))
        {
            return PalSaveLayout.WinObjectRecordSize;
        }

        if (dosCount is >= minimumPlausibleObjectCount and <= PalSaveLayout.ObjectCount &&
            winCount is not (>= minimumPlausibleObjectCount and <= PalSaveLayout.ObjectCount))
        {
            return PalSaveLayout.DosObjectRecordSize;
        }

        return 0;
    }

    private static byte[] ReadMkfChunk(string path, int chunkIndex)
    {
        using var stream = File.OpenRead(path);
        Span<byte> four = stackalloc byte[4];
        ReadExactly(stream, four);
        var firstOffset = BinaryPrimitives.ReadUInt32LittleEndian(four);
        if (firstOffset < 8 || firstOffset % 4 != 0 || firstOffset > stream.Length)
        {
            throw new InvalidDataException("SSS.MKF 的索引表无效。");
        }

        var chunkCount = checked((int)(firstOffset / 4) - 1);
        if ((uint)chunkIndex >= (uint)chunkCount)
        {
            throw new InvalidDataException($"SSS.MKF 不含块 {chunkIndex}。");
        }

        stream.Position = chunkIndex * 4L;
        ReadExactly(stream, four);
        var start = BinaryPrimitives.ReadUInt32LittleEndian(four);
        ReadExactly(stream, four);
        var end = BinaryPrimitives.ReadUInt32LittleEndian(four);
        if (end < start || end > stream.Length)
        {
            throw new InvalidDataException($"SSS.MKF 块 {chunkIndex} 的边界无效。");
        }

        var data = new byte[checked((int)(end - start))];
        stream.Position = start;
        ReadExactly(stream, data);
        return data;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var readTotal = 0;
        while (readTotal < buffer.Length)
        {
            var read = stream.Read(buffer[readTotal..]);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            readTotal += read;
        }
    }
}
