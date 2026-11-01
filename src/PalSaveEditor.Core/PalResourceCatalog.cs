using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace PalSaveEditor.Core;

public sealed class PalResourceCatalog
{
    private readonly string[] _words;
    private readonly ushort[]? _objectFlags;
    private IReadOnlyDictionary<int, string> _globalSkillNames = new Dictionary<int, string>();
    private bool _challengeWuhouOwned;
    private bool _challengeClassicOwned, _universalTroubleOwned;
    private static readonly string[] ChallengeClassicNames =
        ["梦蛇", "气疗术", "观音咒", "凝神归元", "元灵归心术", "赎魂", "回梦", "夺魂", "鬼降", "净衣咒", "冰心诀", "灵血咒", "金刚咒", "风咒", "风神", "雷咒", "天雷破", "雷神", "冰咒", "雪妖", "炎咒", "炎杀咒", "土咒", "山神", "气剑指", "三尸咒", "御蜂术", "火神", "乾坤一掷"];
    private static readonly string[] ChallengeWuhouNames =
        ["梦蛇", "夺魂", "元灵归心术", "风神", "雷神", "雪妖", "火神", "山神", "赎魂", "灵血咒"];

    // Refs #8 / sdlpal-lostedit#159. PWU1 persists independently when the
    // player disables challenge rules; never infer ownership from skill IDs.
    internal PalResourceCatalog ForSave(byte[] bytes)
    {
        if (!EditorEdition.IsGeshi) return this;
        bool supported = ContentProfile?.Id is "geshi-easy" or "geshi-hard" or "geshi-nightwolf" or
            "geshi-fanmade" or "geshi-fanmade-entertainment" or "tianqi20";
        byte[] marker = [(byte)'P', (byte)'W', (byte)'U', (byte)'1', 1, 0, 0, 0];
        return new PalResourceCatalog(ResourceContext, _words, WordDatByteLength, _objectFlags,
            ObjectRecordSize, ObjectRecordCount, RuntimeObjectRecordCount, EventObjectBytes)
        {
            ContentProfile = ContentProfile, GameplayBinding = GameplayBinding, _globalSkillNames = _globalSkillNames,
            _challengeWuhouOwned = supported && bytes.Length >= PalSaveLayout.PoisonOffset + 32 &&
                bytes.AsSpan(PalSaveLayout.PoisonOffset + 24, 8).SequenceEqual(marker),
            _challengeClassicOwned = ContentProfile is not null && bytes.Length >= PalSaveLayout.PoisonOffset + 120 &&
                bytes.AsSpan(PalSaveLayout.PoisonOffset + 112, 8).SequenceEqual(new byte[]{(byte)'P',(byte)'C',(byte)'L',(byte)'1',1,0,0,0}),
            _universalTroubleOwned = ContentProfile is not null && bytes.Length >= PalSaveLayout.PoisonOffset + 128 &&
                bytes.AsSpan(PalSaveLayout.PoisonOffset + 120, 8).SequenceEqual(new byte[]{(byte)'P',(byte)'U',(byte)'M',(byte)'1',1,0,0,0})
        };
    }

    private bool IsChallengeWuhouMagic(int id) => _challengeWuhouOwned && id is >= 690 and < 700;
    private bool IsChallengeClassicMagic(int id) => _challengeClassicOwned && id is >= 600 and < 629;
    private bool IsUniversalTrouble(int id) => _universalTroubleOwned && id == 640;

    private PalResourceCatalog(
        PalGameResourceContext resourceContext,
        string[] words,
        int wordDatByteLength,
        ushort[]? objectFlags,
        int objectRecordSize,
        int objectRecordCount,
        int runtimeObjectRecordCount,
        int eventObjectBytes)
    {
        ResourceContext = resourceContext;
        SourceDirectory = resourceContext.ResourceDirectory;
        _words = words;
        WordDatByteLength = wordDatByteLength;
        _objectFlags = objectFlags;
        ObjectRecordSize = objectRecordSize;
        ObjectRecordCount = objectRecordCount;
        RuntimeObjectRecordCount = runtimeObjectRecordCount;
        EventObjectBytes = eventObjectBytes;
    }

    public MaintenanceContentProfile? ContentProfile { get; private set; }
    public MaintenanceGameplayBinding? GameplayBinding { get; private set; }
    public string SourceDirectory { get; }
    public PalGameResourceContext ResourceContext { get; }
    public string? ActiveProfileId => ResourceContext.ProfileId;
    public string? ActiveProfileVersion => ResourceContext.ProfileVersion;
    public string? ActiveProfileDisplayName => ResourceContext.ProfileDisplayName;
    public bool IsActiveProfile => ResourceContext.IsActiveProfile;
    public int WordCount => _words.Length;
    public int WordDatByteLength { get; }
    public int ObjectRecordSize { get; }
    public int ObjectRecordCount { get; }
    public int RuntimeObjectRecordCount { get; }
    public int EventObjectBytes { get; }
    public bool HasObjectMetadata => _objectFlags is not null;

    public static PalResourceCatalog? TryDiscover(string savePath)
    {
        if (EditorEdition.IsGeshi)
            return Load(GeshiSaveContract.LocateForSave(savePath).GameDirectory);
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
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new ArgumentException("游戏资料目录不能为空。", nameof(gameDirectory));
        }
        PalGameResourceContext resourceContext;
        var maintenanceLocation = EditorEdition.IsGeshi ? GeshiSaveContract.Locate(gameDirectory) : null;
        using var maintenanceLease = maintenanceLocation is null ? null : MaintenanceGameplayRules.Acquire(maintenanceLocation);
        MaintenanceContentProfile? maintenanceProfile = null;
        if (EditorEdition.IsGeshi)
        {
            var location = maintenanceLocation!;
            maintenanceProfile = MaintenanceContentProfile.Resolve(location.ResourceDirectory);
            resourceContext = new(location.GameDirectory, location.ResourceDirectory,
                null, null, null, null, null, null, null);
        }
        else resourceContext = PalGameResourceContextResolver.Resolve(gameDirectory);
        var fullDirectory = resourceContext.ResourceDirectory;
        var wordPath = Path.Combine(fullDirectory, "WORD.DAT");
        if (!File.Exists(wordPath))
        {
            throw new FileNotFoundException("所选目录中没有 WORD.DAT。", wordPath);
        }

        var wordBytes = File.ReadAllBytes(wordPath);
        var wordPayloadLength = wordBytes.Length;
        if (wordPayloadLength % 10 == 2 &&
            wordBytes[wordBytes.Length - 2] == 0x0D &&
            wordBytes[wordBytes.Length - 1] == 0x0A)
        {
            wordPayloadLength -= 2;
        }

        if (wordPayloadLength == 0 || wordPayloadLength % 10 != 0)
        {
            throw new InvalidDataException($"WORD.DAT 长度 {wordBytes.Length:N0} 不是 10 字节记录，或梦幻版末尾 CRLF 的有效组合。");
        }

        ushort[]? flags = null;
        var recordSize = 0;
        var objectRecordCount = 0;
        var eventObjectBytes = 0;
        var sssPath = Path.Combine(fullDirectory, "SSS.MKF");
        if (File.Exists(sssPath))
        {
            eventObjectBytes = ReadMkfChunk(sssPath, 0).Length;
            var objectChunk = ReadMkfChunk(sssPath, 2);
            recordSize = EditorEdition.IsGeshi ? maintenanceProfile!.ObjectRecordSize : DetectObjectRecordSize(objectChunk.Length);
            if (recordSize != 0)
            {
                objectRecordCount = objectChunk.Length / recordSize;
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

        resourceContext.PublicToolProfile?.ValidateResources(
            wordBytes.Length,
            recordSize,
            objectRecordCount,
            eventObjectBytes);

        int runtimeObjectRecordCount = EditorEdition.IsGeshi ? GeshiSaveContract.SaveObjectCount : ResolveRuntimeObjectRecordCount(
            resourceContext.SkillObjectsPath,
            recordSize,
            objectRecordCount,
            wordPayloadLength,
            resourceContext.GlobalSkillLibraryPath is not null);

        // Original DOS resources (including Dream 2.20) use Big5, while the
        // mainland Win95 release uses GBK. The object-record width is the same
        // edition signal used by the game data itself and avoids mojibake.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var codePage = EditorEdition.IsGeshi ? GeshiSaveContract.GetResourceCodePage(fullDirectory) :
            recordSize == PalSaveLayout.DosObjectRecordSize ? 950 : 936;
        var wordEncoding = Encoding.GetEncoding(
            codePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ReplacementFallback);
        var words = new string[wordPayloadLength / 10];
        for (var i = 0; i < words.Length; i++)
        {
            int recordOffset = i * 10;
            int length = Array.IndexOf(wordBytes, (byte)0, recordOffset, 10);
            length = length >= 0 ? length - recordOffset : 10;
            words[i] = wordEncoding.GetString(wordBytes, recordOffset, length).Trim();
            if (EditorEdition.IsGeshi) words[i] = GeshiSaveContract.DisplayName(words[i]);
        }

        var result = new PalResourceCatalog(
            resourceContext,
            words,
            wordBytes.Length,
            flags,
            recordSize,
            objectRecordCount,
            runtimeObjectRecordCount,
            eventObjectBytes);
        if (EditorEdition.IsGeshi)
        {
            var profile = maintenanceProfile!;
            result.ContentProfile = profile;
            result.GameplayBinding = MaintenanceGameplayRules.Read(
                new(resourceContext.GameDirectory, fullDirectory, ""), profile);
            byte[] magicData = ReadMkfChunk(Path.Combine(fullDirectory, "DATA.MKF"), 4);
            byte[] objects = ReadMkfChunk(sssPath, 2);
            if (objectRecordCount != profile.ObjectCount ||
                eventObjectBytes != profile.EventCount * 32 ||
                magicData.Length != profile.MagicCount * 32)
                throw new InvalidDataException("维护版资源表结构与当前内容合同不一致。");
            foreach (int id in Enumerable.Range(1, objectRecordCount - 1).Where(profile.IsMagic))
                if (BinaryPrimitives.ReadUInt16LittleEndian(objects.AsSpan(id * recordSize, 2)) >= profile.MagicCount)
                    throw new InvalidDataException($"维护版法术 #{id} 超出 DATA.MKF 法术表。");
        }
        if (resourceContext.GlobalSkillLibraryPath is not null)
        {
            using JsonDocument library = JsonDocument.Parse(File.ReadAllBytes(resourceContext.GlobalSkillLibraryPath));
            var names = new Dictionary<int, string>();
            foreach (JsonElement entry in library.RootElement.GetProperty("entries").EnumerateArray())
            {
                int id = entry.GetProperty("object_id").GetInt32();
                string? name = entry.GetProperty("display_name").GetString();
                if (id < 2048 || id >= runtimeObjectRecordCount || string.IsNullOrWhiteSpace(name) || names.ContainsKey(id))
                    throw new InvalidDataException("固定技能名称或编号无效。");
                names.Add(id, name!);
            }
            result._globalSkillNames = names;
        }
        maintenanceLease?.CheckMissing();
        return result;
    }

    public string GetWord(int id, string fallbackPrefix = "对象")
    {
        if (IsChallengeClassicMagic(id)) return ChallengeClassicNames[id - 600];
        if (IsUniversalTrouble(id)) return "万能麻烦";
        if (IsChallengeWuhouMagic(id)) return ChallengeWuhouNames[id - 690];
        if (_globalSkillNames.TryGetValue(id, out string? name)) return name;
        if ((uint)id < (uint)_words.Length && !string.IsNullOrWhiteSpace(_words[id]))
        {
            return _words[id];
        }

        return $"{fallbackPrefix} #{id}";
    }

    public string GetRoleName(int roleId, ushort nameWordId) =>
        GetWord(nameWordId, $"角色 {roleId}");

    public string GetObjectName(int objectId) => GetWord(objectId);

    public ushort GetObjectFlags(int objectId) => IsUniversalTrouble(objectId) ? (ushort)17 :
        _objectFlags is not null && (uint)objectId < (uint)_objectFlags.Length ? _objectFlags[objectId] : (ushort)0;

    public bool IsItem(int id) => !EditorEdition.IsGeshi || IsUniversalTrouble(id) || ContentProfile!.IsItem(id);
    public bool IsMagic(int id) => !EditorEdition.IsGeshi || IsChallengeClassicMagic(id) || IsChallengeWuhouMagic(id) || ContentProfile!.IsMagic(id);
    public bool IsEquipment(int id) => IsItem(id) && (!EditorEdition.IsGeshi || (GetObjectFlags(id) & 2) != 0);

    public IReadOnlyList<(ushort Id, string Name)> SearchObjects(string? query, int maximum = 200, Func<int, bool>? filter = null)
    {
        var normalized = query?.Trim() ?? string.Empty;
        var result = new List<(ushort, string)>();
        var privateLimit = _challengeWuhouOwned ? 700 : _universalTroubleOwned ? 641 : _challengeClassicOwned ? 629 : 0;
        var limit = Math.Min(Math.Max(_words.Length, privateLimit), ushort.MaxValue + 1);
        for (var id = 1; id < limit && result.Count < maximum; id++)
        {
            if (filter is not null && !filter(id)) continue;
            var name = IsChallengeClassicMagic(id) ? ChallengeClassicNames[id - 600] : IsUniversalTrouble(id) ? "万能麻烦" :
                IsChallengeWuhouMagic(id) ? ChallengeWuhouNames[id - 690] : id < _words.Length ? _words[id] : "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (normalized.Length == 0 || name.IndexOf(normalized, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                id.ToString().IndexOf(normalized, StringComparison.Ordinal) >= 0)
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

    private static int DetectObjectRecordSize(int length)
    {
        const int minimumPlausibleObjectCount = 500;
        const int maximumExtendedObjectCount = 4096;
        var winCount = length % PalSaveLayout.WinObjectRecordSize == 0
            ? length / PalSaveLayout.WinObjectRecordSize
            : 0;
        var dosCount = length % PalSaveLayout.DosObjectRecordSize == 0
            ? length / PalSaveLayout.DosObjectRecordSize
            : 0;

        bool winPlausible = winCount is >= minimumPlausibleObjectCount and <= maximumExtendedObjectCount;
        bool dosPlausible = dosCount is >= minimumPlausibleObjectCount and <= maximumExtendedObjectCount;
        if (winPlausible && !dosPlausible)
        {
            return PalSaveLayout.WinObjectRecordSize;
        }

        if (dosPlausible && !winPlausible)
        {
            return PalSaveLayout.DosObjectRecordSize;
        }

        // Preserve the original <=600 interpretation when the alternate
        // division only becomes plausible because extended tables are allowed.
        if (winPlausible && dosPlausible)
        {
            if (winCount <= PalSaveLayout.ObjectCount && dosCount > PalSaveLayout.ObjectCount)
            {
                return PalSaveLayout.WinObjectRecordSize;
            }
            if (dosCount <= PalSaveLayout.ObjectCount && winCount > PalSaveLayout.ObjectCount)
            {
                return PalSaveLayout.DosObjectRecordSize;
            }
        }

        return 0;
    }

    private static int ResolveRuntimeObjectRecordCount(
        string? skillObjectsPath,
        int objectRecordSize,
        int baseObjectCount,
        int baseWordBytes,
        bool globalSkills)
    {
        if (string.IsNullOrWhiteSpace(skillObjectsPath))
        {
            return baseObjectCount;
        }
        if (objectRecordSize != PalSaveLayout.WinObjectRecordSize ||
            baseObjectCount is <= 0 or > PalSaveLayout.ObjectCount ||
            baseWordBytes != baseObjectCount * 10)
        {
            throw new InvalidDataException(
                "SKILL.OBJECTS 需要 1..600 条 14 字节基础对象，且 WORD.DAT 与对象数一致。 ");
        }

        byte[] pack = File.ReadAllBytes(skillObjectsPath);
        const int headerBytes = 20;
        const int wordRecordBytes = 10;
        const int maximumPackBytes = 4 * 1024 * 1024;
        const uint maximumRuntimeObjectCount = 4096;
        int combinedRecordBytes = PalSaveLayout.WinObjectRecordSize + wordRecordBytes;
        if (pack.Length < headerBytes || pack.Length > maximumPackBytes ||
            pack[0] != (byte)'P' || pack[1] != (byte)'S' ||
            pack[2] != (byte)'O' || pack[3] != (byte)'1' ||
            BinaryPrimitives.ReadUInt16LittleEndian(pack.AsSpan(4)) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(pack.AsSpan(6)) !=
                PalSaveLayout.WinObjectRecordSize ||
            BinaryPrimitives.ReadUInt16LittleEndian(pack.AsSpan(8)) != wordRecordBytes ||
            BinaryPrimitives.ReadUInt16LittleEndian(pack.AsSpan(10)) != 0)
        {
            throw new InvalidDataException(
                "SKILL.OBJECTS 不是 PSO1 v1（14 字节对象、10 字节字库）格式。 ");
        }

        uint firstObjectId = BinaryPrimitives.ReadUInt32LittleEndian(pack.AsSpan(12));
        uint recordCount = BinaryPrimitives.ReadUInt32LittleEndian(pack.AsSpan(16));
        if (firstObjectId != (uint)baseObjectCount || recordCount == 0 ||
            firstObjectId > maximumRuntimeObjectCount ||
            recordCount > maximumRuntimeObjectCount - firstObjectId ||
            firstObjectId + recordCount <= (uint)PalSaveLayout.ObjectCount ||
            pack.LongLength != headerBytes + (long)recordCount * combinedRecordBytes)
        {
            throw new InvalidDataException(
                "SKILL.OBJECTS 起始对象、记录数或文件长度与基础资源不一致。 ");
        }

        int extensionCount = checked((int)recordCount);
        for (int index = 0; index < extensionCount; index++)
        {
            int offset = headerBytes + index * combinedRecordBytes;
            if (globalSkills && firstObjectId + index < 2048)
            {
                if (firstObjectId + recordCount <= 2048 || !IsAllZero(pack, offset, combinedRecordBytes))
                    throw new InvalidDataException("全局技能保留编号区被占用或缺少固定技能。");
                continue;
            }
            if (IsAllZero(pack, offset, PalSaveLayout.WinObjectRecordSize) ||
                IsAllZero(
                    pack,
                    offset + PalSaveLayout.WinObjectRecordSize,
                    wordRecordBytes))
            {
                throw new InvalidDataException(
                    $"SKILL.OBJECTS 第 {index} 条对象或字库记录为空。 ");
            }
        }

        return checked((int)(firstObjectId + recordCount));
    }

    private static bool IsAllZero(byte[] bytes, int offset, int count)
    {
        for (int index = 0; index < count; index++)
        {
            if (bytes[offset + index] != 0)
            {
                return false;
            }
        }
        return true;
    }

    private static byte[] ReadMkfChunk(string path, int chunkIndex)
    {
        using var stream = File.OpenRead(path);
        var four = new byte[4];
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

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        int readTotal = 0;
        while (readTotal < buffer.Length)
        {
            int read = stream.Read(buffer, readTotal, buffer.Length - readTotal);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            readTotal += read;
        }
    }
}
