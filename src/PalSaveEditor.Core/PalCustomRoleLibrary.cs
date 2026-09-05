using System.Text;
using System.Text.Json;

namespace PalSaveEditor.Core;

public sealed class PalCustomRoleLibrary
{
    public const string Schema = "PAL98.CustomRoleLibrary.v1";
    public const string RuntimeMinVersion = "1.6.2.0";
    public const string ContentScope = "all-effective-content-profiles";
    public const string RelativePath = "palmod\\CustomRoles\\roles.json";
    public const int NativeRoleCount = 6;
    public const int MaximumRoleCount = 16;
    public const int MaximumCustomRoleCount = MaximumRoleCount - NativeRoleCount;

    public string LibraryId { get; set; } = "local.custom-roles";
    public string LibraryVersion { get; set; } = "1.0.0";
    public Dictionary<int, string> NativeRoleNames { get; } = new();
    public List<PalCustomRoleDefinition> CustomRoles { get; } = new();

    public int RuntimeRoleCount => NativeRoleCount + CustomRoles.Count;

    public PalCustomRoleLibrary Clone()
    {
        var clone = new PalCustomRoleLibrary
        {
            LibraryId = LibraryId,
            LibraryVersion = LibraryVersion,
        };
        foreach (KeyValuePair<int, string> pair in NativeRoleNames)
        {
            clone.NativeRoleNames.Add(pair.Key, pair.Value);
        }
        clone.CustomRoles.AddRange(CustomRoles.Select(role => role.Clone()));
        return clone;
    }

    public bool ContentEquals(PalCustomRoleLibrary other)
    {
        if (other is null || LibraryId != other.LibraryId ||
            LibraryVersion != other.LibraryVersion ||
            NativeRoleNames.Count != other.NativeRoleNames.Count ||
            NativeRoleNames.Any(pair =>
                !other.NativeRoleNames.TryGetValue(pair.Key, out string? name) ||
                name != pair.Value) || CustomRoles.Count != other.CustomRoles.Count)
            return false;
        for (int index = 0; index < CustomRoles.Count; index++)
        {
            if (JsonSerializer.Serialize(CustomRoles[index]) !=
                JsonSerializer.Serialize(other.CustomRoles[index])) return false;
        }
        return true;
    }

    public void Validate(int? runtimeObjectCount = null)
    {
        PalCustomRoleLibraryStore.ValidateIdentifier(LibraryId, nameof(LibraryId), 96);
        PalCustomRoleLibraryStore.ValidateVersion(LibraryVersion, nameof(LibraryVersion));
        if (NativeRoleNames.Count > NativeRoleCount)
        {
            throw new InvalidDataException("原生人物姓名覆盖超过 PAL98 的六个固定角色槽。");
        }
        foreach (KeyValuePair<int, string> pair in NativeRoleNames)
        {
            if ((uint)pair.Key >= NativeRoleCount)
            {
                throw new InvalidDataException($"原生人物编号 {pair.Key} 越出 0 到 5。");
            }
            PalCustomRoleLibraryStore.ValidateRoleName(pair.Value, $"native_role_names[{pair.Key}]");
        }
        if (CustomRoles.Count > MaximumCustomRoleCount)
        {
            throw new InvalidDataException(
                $"自定义人物最多 {MaximumCustomRoleCount} 个；当前运行时角色命名空间为 0 到 {MaximumRoleCount - 1}。");
        }
        for (int index = 0; index < CustomRoles.Count; index++)
        {
            PalCustomRoleDefinition role = CustomRoles[index];
            int expectedRoleId = NativeRoleCount + index;
            if (role.RoleId != expectedRoleId)
            {
                throw new InvalidDataException(
                    $"自定义人物必须从角色 {NativeRoleCount} 连续编号；第 {index + 1} 项应为 {expectedRoleId}，实际为 {role.RoleId}。");
            }
            role.Validate(runtimeObjectCount);
        }
    }
}

public sealed class PalCustomRoleDefinition
{
    public int RoleId { get; set; } = PalCustomRoleLibrary.NativeRoleCount;
    public string DisplayName { get; set; } = "新主角";
    public ushort MapSprite { get; set; } = 12;
    public ushort BattleSprite { get; set; }
    public ushort Avatar { get; set; }
    public ushort WalkFrames { get; set; } = 3;
    public ushort CooperativeMagic { get; set; }
    public ushort AttackAll { get; set; }
    public PalCustomRoleStats InitialState { get; set; } = new();
    public PalCustomRoleGrowth GrowthPerLevel { get; set; } = new();
    public List<PalCustomRoleLearnedMagic> LearnedMagics { get; } = new();

    public PalCustomRoleDefinition Clone()
    {
        var clone = new PalCustomRoleDefinition
        {
            RoleId = RoleId,
            DisplayName = DisplayName,
            MapSprite = MapSprite,
            BattleSprite = BattleSprite,
            Avatar = Avatar,
            WalkFrames = WalkFrames,
            CooperativeMagic = CooperativeMagic,
            AttackAll = AttackAll,
            InitialState = InitialState.Clone(),
            GrowthPerLevel = GrowthPerLevel.Clone(),
        };
        clone.LearnedMagics.AddRange(LearnedMagics.Select(entry => entry with { }));
        return clone;
    }

    public void Validate(int? runtimeObjectCount = null)
    {
        if (RoleId < PalCustomRoleLibrary.NativeRoleCount ||
            RoleId >= PalCustomRoleLibrary.MaximumRoleCount)
        {
            throw new InvalidDataException(
                $"自定义人物编号 {RoleId} 越出 {PalCustomRoleLibrary.NativeRoleCount} 到 {PalCustomRoleLibrary.MaximumRoleCount - 1}。");
        }
        PalCustomRoleLibraryStore.ValidateRoleName(DisplayName, $"custom_roles[{RoleId}].display_name");
        InitialState.Validate();
        GrowthPerLevel.Validate();
        if (LearnedMagics.Count > 32)
        {
            throw new InvalidDataException("自定义人物领悟表最多 32 条，对应 PAL98 原生人物法术槽上限。");
        }
        var objectIds = new HashSet<ushort>();
        ushort previousLevel = 0;
        foreach (PalCustomRoleLearnedMagic entry in LearnedMagics
                     .OrderBy(entry => entry.Level)
                     .ThenBy(entry => entry.ObjectId))
        {
            if (entry.Level is < 1 or > 99 || entry.ObjectId == 0)
            {
                throw new InvalidDataException("领悟表必须使用 1 到 99 级和非零对象编号。");
            }
            if (entry.Level < previousLevel)
            {
                throw new InvalidDataException("领悟表等级顺序无效。");
            }
            previousLevel = entry.Level;
            if (runtimeObjectCount is int count && entry.ObjectId >= count)
            {
                throw new InvalidDataException(
                    $"领悟法术对象 {entry.ObjectId} 越出当前运行时对象表 0 到 {count - 1}。");
            }
            if (!objectIds.Add(entry.ObjectId))
            {
                throw new InvalidDataException($"领悟法术对象 {entry.ObjectId} 重复。");
            }
        }
    }
}

public sealed class PalCustomRoleStats
{
    public ushort Level { get; set; } = 1;
    public short MaxHp { get; set; }
    public short MaxMp { get; set; }
    public short Attack { get; set; }
    public short MagicPower { get; set; }
    public short Defense { get; set; }
    public short Dexterity { get; set; }
    public short FleeRate { get; set; }
    public short PoisonResistance { get; set; }
    public short WindResistance { get; set; }
    public short ThunderResistance { get; set; }
    public short WaterResistance { get; set; }
    public short FireResistance { get; set; }
    public short EarthResistance { get; set; }

    public PalCustomRoleStats Clone() => (PalCustomRoleStats)MemberwiseClone();

    public void Validate()
    {
        if (Level is < 1 or > 99)
        {
            throw new InvalidDataException("自定义人物初始等级必须为 1 到 99。");
        }
        foreach ((string name, short value) in MainValues())
        {
            if (value < 0)
            {
                throw new InvalidDataException($"自定义人物初始{name}不能为负数。");
            }
        }
    }

    private IEnumerable<(string Name, short Value)> MainValues()
    {
        yield return ("最大体力", MaxHp);
        yield return ("最大真气", MaxMp);
        yield return ("武术", Attack);
        yield return ("灵力", MagicPower);
        yield return ("防御", Defense);
        yield return ("身法", Dexterity);
        yield return ("吉运", FleeRate);
    }
}

public sealed class PalCustomRoleGrowth
{
    public short MaxHp { get; set; }
    public short MaxMp { get; set; }
    public short Attack { get; set; }
    public short MagicPower { get; set; }
    public short Defense { get; set; }
    public short Dexterity { get; set; }
    public short FleeRate { get; set; }
    public short PoisonResistance { get; set; }
    public short WindResistance { get; set; }
    public short ThunderResistance { get; set; }
    public short WaterResistance { get; set; }
    public short FireResistance { get; set; }
    public short EarthResistance { get; set; }

    public PalCustomRoleGrowth Clone() => (PalCustomRoleGrowth)MemberwiseClone();
    public void Validate() { }
}

public sealed record PalCustomRoleLearnedMagic(ushort Level, ushort ObjectId);
public sealed record PalCustomRoleLibraryWriteResult(string Path, string? BackupPath);

public static class PalCustomRoleLibraryStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string GetPath(string gameDirectory) =>
        Path.Combine(Path.GetFullPath(gameDirectory), PalCustomRoleLibrary.RelativePath);

    public static bool TryLoad(
        string gameDirectory,
        out PalCustomRoleLibrary library,
        out string? error,
        int? runtimeObjectCount = null)
    {
        string path = GetPath(gameDirectory);
        library = new PalCustomRoleLibrary();
        error = null;
        if (!File.Exists(path)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            RequireObject(root, path);
            RequireFields(root,
                ["schema", "library_id", "library_version", "runtime_min_version", "content_scope", "native_role_names", "custom_roles"],
                [], path);
            RequireString(root, "schema", path, PalCustomRoleLibrary.Schema);
            RequireString(root, "runtime_min_version", path, PalCustomRoleLibrary.RuntimeMinVersion);
            RequireString(root, "content_scope", path, PalCustomRoleLibrary.ContentScope);
            library.LibraryId = RequireString(root, "library_id", path);
            library.LibraryVersion = RequireString(root, "library_version", path);

            foreach (JsonElement item in RequireArray(root, "native_role_names", path).EnumerateArray())
            {
                RequireObject(item, "native_role_names[]");
                RequireFields(item, ["role_id", "display_name"], [], "native_role_names[]");
                int roleId = RequireInt(item, "role_id", 0, PalCustomRoleLibrary.NativeRoleCount - 1, "native_role_names[]");
                if (library.NativeRoleNames.ContainsKey(roleId))
                    throw new InvalidDataException($"native_role_names 中角色 {roleId} 重复。");
                library.NativeRoleNames.Add(roleId, RequireString(item, "display_name", "native_role_names[]"));
            }

            foreach (JsonElement item in RequireArray(root, "custom_roles", path).EnumerateArray())
            {
                library.CustomRoles.Add(ParseRole(item, path));
            }
            library.Validate(runtimeObjectCount);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            library = new PalCustomRoleLibrary();
            error = $"自定义人物库无效：{ex.Message}";
            return false;
        }
    }

    public static PalCustomRoleLibraryWriteResult WriteAtomically(
        string gameDirectory,
        PalCustomRoleLibrary library,
        bool createBackup = true,
        int? runtimeObjectCount = null)
    {
        library.Validate(runtimeObjectCount);
        string destination = GetPath(gameDirectory);
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("无法确定自定义人物库目录。");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        string? backup = File.Exists(destination) && createBackup ? BuildBackupPath(destination) : null;
        string? rollback = File.Exists(destination) && !createBackup
            ? Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.rollback")
            : backup;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                Write(writer, library);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(destination)) File.Replace(temporary, destination, rollback!, ignoreMetadataErrors: true);
            else File.Move(temporary, destination);
            if (!TryLoad(gameDirectory, out PalCustomRoleLibrary verified, out string? error, runtimeObjectCount) ||
                !library.ContentEquals(verified))
                throw new IOException(error ?? "自定义人物库写入后复核不一致。");
            if (!createBackup && rollback is not null) File.Delete(rollback);
            return new(destination, backup);
        }
        catch
        {
            if (rollback is not null && File.Exists(rollback))
            {
                File.Copy(rollback, destination, overwrite: true);
                if (!createBackup) File.Delete(rollback);
            }
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static void ValidateRoleName(string value, string source)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            throw new InvalidDataException($"{source} 的人物名不能为空或包含控制字符。");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding gbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        int byteCount;
        try { byteCount = gbk.GetByteCount(value); }
        catch (EncoderFallbackException ex)
        {
            throw new InvalidDataException($"{source} 的人物名不能编码为 PAL98 CP936。", ex);
        }
        if (byteCount is < 1 or > 10)
            throw new InvalidDataException($"{source} 的人物名编码后为 {byteCount} 字节，必须为 1 到 10 字节。");
    }

    internal static void ValidateIdentifier(string value, string source, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum ||
            !char.IsLetterOrDigit(value[0]) || char.IsUpper(value[0]) ||
            value.Any(character => !char.IsLower(character) && !char.IsDigit(character) && character != '.' && character != '_' && character != '-'))
            throw new InvalidDataException($"{source} 不是有效的小写库标识。");
    }

    internal static void ValidateVersion(string value, string source)
    {
        string[] parts = value?.Split('.') ?? [];
        if (parts.Length != 3 || parts.Any(part => !int.TryParse(part, out int number) || number < 0))
            throw new InvalidDataException($"{source} 必须是三段非负版本号。");
    }

    private static PalCustomRoleDefinition ParseRole(JsonElement element, string source)
    {
        RequireObject(element, "custom_roles[]");
        RequireFields(element,
            ["role_id", "display_name", "map_sprite", "battle_sprite", "avatar", "walk_frames", "cooperative_magic", "attack_all", "initial_state", "growth_per_level", "learned_magics"],
            [], "custom_roles[]");
        var role = new PalCustomRoleDefinition
        {
            RoleId = RequireInt(element, "role_id", PalCustomRoleLibrary.NativeRoleCount, PalCustomRoleLibrary.MaximumRoleCount - 1, source),
            DisplayName = RequireString(element, "display_name", source),
            MapSprite = RequireU16(element, "map_sprite", source),
            BattleSprite = RequireU16(element, "battle_sprite", source),
            Avatar = RequireU16(element, "avatar", source),
            WalkFrames = RequireU16(element, "walk_frames", source),
            CooperativeMagic = RequireU16(element, "cooperative_magic", source),
            AttackAll = RequireU16(element, "attack_all", source),
            InitialState = ParseStats(element.GetProperty("initial_state"), source),
            GrowthPerLevel = ParseGrowth(element.GetProperty("growth_per_level"), source),
        };
        foreach (JsonElement item in RequireArray(element, "learned_magics", source).EnumerateArray())
        {
            RequireObject(item, "learned_magics[]");
            RequireFields(item, ["level", "object_id"], [], "learned_magics[]");
            role.LearnedMagics.Add(new(
                checked((ushort)RequireInt(item, "level", 1, 99, source)),
                checked((ushort)RequireInt(item, "object_id", 1, ushort.MaxValue, source))));
        }
        role.LearnedMagics.Sort((left, right) =>
        {
            int level = left.Level.CompareTo(right.Level);
            return level != 0 ? level : left.ObjectId.CompareTo(right.ObjectId);
        });
        return role;
    }

    private static PalCustomRoleStats ParseStats(JsonElement element, string source)
    {
        string[] fields = ["level", "max_hp", "max_mp", "attack", "magic_power", "defense", "dexterity", "flee_rate", "poison_resistance", "wind_resistance", "thunder_resistance", "water_resistance", "fire_resistance", "earth_resistance"];
        RequireObject(element, "initial_state");
        RequireFields(element, fields, [], "initial_state");
        return new PalCustomRoleStats
        {
            Level = checked((ushort)RequireInt(element, "level", 1, 99, source)),
            MaxHp = RequireShort(element, "max_hp", 0, short.MaxValue, source),
            MaxMp = RequireShort(element, "max_mp", 0, short.MaxValue, source),
            Attack = RequireShort(element, "attack", 0, short.MaxValue, source),
            MagicPower = RequireShort(element, "magic_power", 0, short.MaxValue, source),
            Defense = RequireShort(element, "defense", 0, short.MaxValue, source),
            Dexterity = RequireShort(element, "dexterity", 0, short.MaxValue, source),
            FleeRate = RequireShort(element, "flee_rate", 0, short.MaxValue, source),
            PoisonResistance = RequireShort(element, "poison_resistance", short.MinValue, short.MaxValue, source),
            WindResistance = RequireShort(element, "wind_resistance", short.MinValue, short.MaxValue, source),
            ThunderResistance = RequireShort(element, "thunder_resistance", short.MinValue, short.MaxValue, source),
            WaterResistance = RequireShort(element, "water_resistance", short.MinValue, short.MaxValue, source),
            FireResistance = RequireShort(element, "fire_resistance", short.MinValue, short.MaxValue, source),
            EarthResistance = RequireShort(element, "earth_resistance", short.MinValue, short.MaxValue, source),
        };
    }

    private static PalCustomRoleGrowth ParseGrowth(JsonElement element, string source)
    {
        string[] fields = ["max_hp", "max_mp", "attack", "magic_power", "defense", "dexterity", "flee_rate", "poison_resistance", "wind_resistance", "thunder_resistance", "water_resistance", "fire_resistance", "earth_resistance"];
        RequireObject(element, "growth_per_level");
        RequireFields(element, fields, [], "growth_per_level");
        return new PalCustomRoleGrowth
        {
            MaxHp = RequireShort(element, "max_hp", short.MinValue, short.MaxValue, source),
            MaxMp = RequireShort(element, "max_mp", short.MinValue, short.MaxValue, source),
            Attack = RequireShort(element, "attack", short.MinValue, short.MaxValue, source),
            MagicPower = RequireShort(element, "magic_power", short.MinValue, short.MaxValue, source),
            Defense = RequireShort(element, "defense", short.MinValue, short.MaxValue, source),
            Dexterity = RequireShort(element, "dexterity", short.MinValue, short.MaxValue, source),
            FleeRate = RequireShort(element, "flee_rate", short.MinValue, short.MaxValue, source),
            PoisonResistance = RequireShort(element, "poison_resistance", short.MinValue, short.MaxValue, source),
            WindResistance = RequireShort(element, "wind_resistance", short.MinValue, short.MaxValue, source),
            ThunderResistance = RequireShort(element, "thunder_resistance", short.MinValue, short.MaxValue, source),
            WaterResistance = RequireShort(element, "water_resistance", short.MinValue, short.MaxValue, source),
            FireResistance = RequireShort(element, "fire_resistance", short.MinValue, short.MaxValue, source),
            EarthResistance = RequireShort(element, "earth_resistance", short.MinValue, short.MaxValue, source),
        };
    }

    private static void Write(Utf8JsonWriter writer, PalCustomRoleLibrary library)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", PalCustomRoleLibrary.Schema);
        writer.WriteString("library_id", library.LibraryId);
        writer.WriteString("library_version", library.LibraryVersion);
        writer.WriteString("runtime_min_version", PalCustomRoleLibrary.RuntimeMinVersion);
        writer.WriteString("content_scope", PalCustomRoleLibrary.ContentScope);
        writer.WriteStartArray("native_role_names");
        foreach (KeyValuePair<int, string> pair in library.NativeRoleNames.OrderBy(pair => pair.Key))
        {
            writer.WriteStartObject();
            writer.WriteNumber("role_id", pair.Key);
            writer.WriteString("display_name", pair.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("custom_roles");
        foreach (PalCustomRoleDefinition role in library.CustomRoles.OrderBy(role => role.RoleId))
        {
            writer.WriteStartObject();
            writer.WriteNumber("role_id", role.RoleId);
            writer.WriteString("display_name", role.DisplayName);
            writer.WriteNumber("map_sprite", role.MapSprite);
            writer.WriteNumber("battle_sprite", role.BattleSprite);
            writer.WriteNumber("avatar", role.Avatar);
            writer.WriteNumber("walk_frames", role.WalkFrames);
            writer.WriteNumber("cooperative_magic", role.CooperativeMagic);
            writer.WriteNumber("attack_all", role.AttackAll);
            WriteStats(writer, role.InitialState);
            WriteGrowth(writer, role.GrowthPerLevel);
            writer.WriteStartArray("learned_magics");
            foreach (PalCustomRoleLearnedMagic entry in role.LearnedMagics.OrderBy(entry => entry.Level).ThenBy(entry => entry.ObjectId))
            {
                writer.WriteStartObject();
                writer.WriteNumber("level", entry.Level);
                writer.WriteNumber("object_id", entry.ObjectId);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStats(Utf8JsonWriter writer, PalCustomRoleStats value)
    {
        writer.WriteStartObject("initial_state");
        writer.WriteNumber("level", value.Level);
        WriteCommonStats(writer, value.MaxHp, value.MaxMp, value.Attack, value.MagicPower, value.Defense, value.Dexterity, value.FleeRate, value.PoisonResistance, value.WindResistance, value.ThunderResistance, value.WaterResistance, value.FireResistance, value.EarthResistance);
        writer.WriteEndObject();
    }

    private static void WriteGrowth(Utf8JsonWriter writer, PalCustomRoleGrowth value)
    {
        writer.WriteStartObject("growth_per_level");
        WriteCommonStats(writer, value.MaxHp, value.MaxMp, value.Attack, value.MagicPower, value.Defense, value.Dexterity, value.FleeRate, value.PoisonResistance, value.WindResistance, value.ThunderResistance, value.WaterResistance, value.FireResistance, value.EarthResistance);
        writer.WriteEndObject();
    }

    private static void WriteCommonStats(Utf8JsonWriter writer, short maxHp, short maxMp, short attack, short magicPower, short defense, short dexterity, short fleeRate, short poison, short wind, short thunder, short water, short fire, short earth)
    {
        writer.WriteNumber("max_hp", maxHp);
        writer.WriteNumber("max_mp", maxMp);
        writer.WriteNumber("attack", attack);
        writer.WriteNumber("magic_power", magicPower);
        writer.WriteNumber("defense", defense);
        writer.WriteNumber("dexterity", dexterity);
        writer.WriteNumber("flee_rate", fleeRate);
        writer.WriteNumber("poison_resistance", poison);
        writer.WriteNumber("wind_resistance", wind);
        writer.WriteNumber("thunder_resistance", thunder);
        writer.WriteNumber("water_resistance", water);
        writer.WriteNumber("fire_resistance", fire);
        writer.WriteNumber("earth_resistance", earth);
    }

    private static void RequireObject(JsonElement element, string source)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{source} 必须是 JSON 对象。");
    }

    private static JsonElement RequireArray(JsonElement element, string property, string source)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{source} 缺少数组 {property}。");
        return value;
    }

    private static string RequireString(JsonElement element, string property, string source, string? expected = null)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"{source} 缺少非空字符串 {property}。");
        string result = value.GetString()!;
        if (expected is not null && result != expected) throw new InvalidDataException($"{source} 的 {property}={result}，预期 {expected}。");
        return result;
    }

    private static int RequireInt(JsonElement element, string property, int minimum, int maximum, string source)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || !value.TryGetInt32(out int result) || result < minimum || result > maximum)
            throw new InvalidDataException($"{source} 的 {property} 必须为 {minimum} 到 {maximum} 的整数。");
        return result;
    }

    private static ushort RequireU16(JsonElement element, string property, string source) => checked((ushort)RequireInt(element, property, 0, ushort.MaxValue, source));
    private static short RequireShort(JsonElement element, string property, int minimum, int maximum, string source) => checked((short)RequireInt(element, property, minimum, maximum, source));

    private static void RequireFields(JsonElement element, IReadOnlyCollection<string> requiredNames, IReadOnlyCollection<string> optionalNames, string source)
    {
        var required = new HashSet<string>(requiredNames, StringComparer.Ordinal);
        var allowed = new HashSet<string>(requiredNames, StringComparer.Ordinal);
        allowed.UnionWith(optionalNames);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)) throw new InvalidDataException($"{source} 含未知字段 {property.Name}。");
            required.Remove(property.Name);
        }
        if (required.Count != 0) throw new InvalidDataException($"{source} 缺少必要字段 {string.Join(", ", required)}。");
    }

    private static string BuildBackupPath(string destination)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string candidate = $"{destination}.bak-{stamp}";
        int suffix = 1;
        while (File.Exists(candidate)) candidate = $"{destination}.bak-{stamp}-{suffix++}";
        return candidate;
    }
}
