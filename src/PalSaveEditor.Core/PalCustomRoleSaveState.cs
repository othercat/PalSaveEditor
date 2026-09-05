using System.Security.Cryptography;
using System.Text.Json;

namespace PalSaveEditor.Core;

public sealed class PalCustomRoleSaveState
{
    public const string Schema = "PAL98.CustomRoleSaveState.v1";
    public const int RoleFieldCount = 75;
    public const int ExperienceWordCount = 16;
    public const int ModifierCount = 98;

    public string SaveFile { get; set; } = string.Empty;
    public uint RpgSize { get; set; }
    public string RpgSha256 { get; set; } = string.Empty;
    public string LibraryId { get; set; } = string.Empty;
    public string LibraryVersion { get; set; } = string.Empty;
    public List<PalCustomRoleCurrentState> Roles { get; } = new();

    public PalCustomRoleSaveState Clone()
    {
        var clone = new PalCustomRoleSaveState
        {
            SaveFile = SaveFile,
            RpgSize = RpgSize,
            RpgSha256 = RpgSha256,
            LibraryId = LibraryId,
            LibraryVersion = LibraryVersion,
        };
        clone.Roles.AddRange(Roles.Select(role => role.Clone()));
        return clone;
    }

    public bool ContentEquals(PalCustomRoleSaveState other)
    {
        if (LibraryId != other.LibraryId || LibraryVersion != other.LibraryVersion ||
            Roles.Count != other.Roles.Count) return false;
        for (int index = 0; index < Roles.Count; index++)
        {
            PalCustomRoleCurrentState left = Roles[index];
            PalCustomRoleCurrentState right = other.Roles[index];
            if (left.RoleId != right.RoleId || !left.Fields.SequenceEqual(right.Fields) ||
                !left.ExperienceWords.SequenceEqual(right.ExperienceWords) ||
                !left.Modifiers.SequenceEqual(right.Modifiers)) return false;
        }
        return true;
    }

    public PalCustomRoleCurrentState GetRole(int roleId) =>
        Roles.FirstOrDefault(role => role.RoleId == roleId)
        ?? throw new ArgumentOutOfRangeException(nameof(roleId));

    public static PalCustomRoleSaveState CreateInitial(PalCustomRoleLibrary library)
    {
        library.Validate();
        var state = new PalCustomRoleSaveState
        {
            LibraryId = library.LibraryId,
            LibraryVersion = library.LibraryVersion,
        };
        foreach (PalCustomRoleDefinition definition in library.CustomRoles)
        {
            var role = new PalCustomRoleCurrentState { RoleId = definition.RoleId };
            role.Fields[0] = unchecked((short)definition.Avatar);
            role.Fields[1] = unchecked((short)definition.BattleSprite);
            role.Fields[2] = unchecked((short)definition.MapSprite);
            role.Fields[4] = unchecked((short)definition.AttackAll);
            role.Fields[6] = unchecked((short)definition.InitialState.Level);
            role.Fields[7] = definition.InitialState.MaxHp;
            role.Fields[8] = definition.InitialState.MaxMp;
            role.Fields[9] = definition.InitialState.MaxHp;
            role.Fields[10] = definition.InitialState.MaxMp;
            role.Fields[17] = definition.InitialState.Attack;
            role.Fields[18] = definition.InitialState.MagicPower;
            role.Fields[19] = definition.InitialState.Defense;
            role.Fields[20] = definition.InitialState.Dexterity;
            role.Fields[21] = definition.InitialState.FleeRate;
            role.Fields[22] = definition.InitialState.PoisonResistance;
            role.Fields[23] = definition.InitialState.WindResistance;
            role.Fields[24] = definition.InitialState.ThunderResistance;
            role.Fields[25] = definition.InitialState.WaterResistance;
            role.Fields[26] = definition.InitialState.FireResistance;
            role.Fields[27] = definition.InitialState.EarthResistance;
            role.Fields[64] = unchecked((short)definition.WalkFrames);
            role.Fields[65] = unchecked((short)definition.CooperativeMagic);
            int slot = 0;
            foreach (PalCustomRoleLearnedMagic magic in definition.LearnedMagics
                         .Where(magic => magic.Level <= definition.InitialState.Level)
                         .OrderBy(magic => magic.Level).ThenBy(magic => magic.ObjectId))
            {
                role.Fields[32 + slot++] = unchecked((short)magic.ObjectId);
            }
            state.Roles.Add(role);
        }
        return state;
    }
}

public sealed class PalCustomRoleCurrentState
{
    public int RoleId { get; set; }
    public short[] Fields { get; } = new short[PalCustomRoleSaveState.RoleFieldCount];
    public uint[] ExperienceWords { get; } = new uint[PalCustomRoleSaveState.ExperienceWordCount];
    public short[] Modifiers { get; } = new short[PalCustomRoleSaveState.ModifierCount];

    public PalCustomRoleCurrentState Clone()
    {
        var clone = new PalCustomRoleCurrentState { RoleId = RoleId };
        Fields.CopyTo(clone.Fields, 0);
        ExperienceWords.CopyTo(clone.ExperienceWords, 0);
        Modifiers.CopyTo(clone.Modifiers, 0);
        return clone;
    }
}

public static class PalCustomRoleSaveStateStore
{
    public static string GetPath(string rpgPath) => Path.GetFullPath(rpgPath) + ".pal98-custom-roles.json";

    public static bool TryLoad(
        string rpgPath,
        byte[] rpgBytes,
        PalCustomRoleLibrary library,
        out PalCustomRoleSaveState state,
        out string? warning)
    {
        return TryLoad(
            rpgPath,
            rpgBytes,
            library,
            out state,
            out warning,
            out _,
            out _);
    }

    public static bool TryLoad(
        string rpgPath,
        byte[] rpgBytes,
        PalCustomRoleLibrary library,
        out PalCustomRoleSaveState state,
        out string? warning,
        out bool requiresReconciliation,
        out int storedRoleCount)
    {
        string path = GetPath(rpgPath);
        state = PalCustomRoleSaveState.CreateInitial(library);
        warning = null;
        requiresReconciliation = false;
        storedRoleCount = 0;
        if (!File.Exists(path)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            RequireFields(root,
                ["schema", "schema_version", "save_file", "rpg_size", "rpg_sha256", "library_id", "library_version", "roles"]);
            if (RequireString(root, "schema") != PalCustomRoleSaveState.Schema ||
                RequireInt(root, "schema_version", 1, 1) != 1 ||
                RequireString(root, "save_file") != Path.GetFileName(rpgPath) ||
                RequireInt64(root, "rpg_size", 1, uint.MaxValue) != rpgBytes.Length ||
                !string.Equals(RequireString(root, "rpg_sha256"), Hash(rpgBytes), StringComparison.OrdinalIgnoreCase) ||
                RequireString(root, "library_id") != library.LibraryId ||
                RequireString(root, "library_version") != library.LibraryVersion)
                throw new InvalidDataException("sidecar 与当前 RPG 或固定角色库身份不匹配。");
            JsonElement roles = RequireArray(root, "roles");
            if (roles.GetArrayLength() > PalCustomRoleLibrary.MaximumCustomRoleCount)
                throw new InvalidDataException("sidecar 自定义角色数量越出运行时上限。");
            var storedRoles = new List<PalCustomRoleCurrentState>();
            int storedIndex = 0;
            foreach (JsonElement item in roles.EnumerateArray())
            {
                RequireFields(item, ["role_id", "fields", "experience_words", "modifiers"]);
                var role = new PalCustomRoleCurrentState
                {
                    RoleId = RequireInt(item, "role_id", 6, 15),
                };
                if (role.RoleId != PalCustomRoleLibrary.NativeRoleCount + storedIndex)
                    throw new InvalidDataException("sidecar 自定义角色编号不是从 6 开始的连续序列。");
                ReadInt16Array(item, "fields", role.Fields);
                ReadUInt32Array(item, "experience_words", role.ExperienceWords);
                ReadInt16Array(item, "modifiers", role.Modifiers);
                storedRoles.Add(role);
                storedIndex++;
            }
            storedRoleCount = storedRoles.Count;

            // Adding or removing only the trailing custom roles is the stable
            // role-library operation supported by the editor. Preserve the
            // common prefix from the save and initialize newly appended roles
            // from the fixed library. Identity, RPG hash and every stored role
            // payload remain strict; reordered or sparse ids still fail closed.
            var parsed = PalCustomRoleSaveState.CreateInitial(library);
            parsed.SaveFile = Path.GetFileName(rpgPath);
            parsed.RpgSize = checked((uint)rpgBytes.Length);
            parsed.RpgSha256 = Hash(rpgBytes);
            int commonCount = Math.Min(storedRoles.Count, parsed.Roles.Count);
            for (int index = 0; index < commonCount; index++)
            {
                if (storedRoles[index].RoleId != library.CustomRoles[index].RoleId)
                    throw new InvalidDataException("sidecar 自定义角色编号与固定角色库不匹配。");
                parsed.Roles[index] = storedRoles[index];
            }
            state = parsed;
            requiresReconciliation = storedRoles.Count != parsed.Roles.Count;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException)
        {
            warning = $"自定义主角存档状态未加载，已使用固定角色库初始值：{exception.Message}";
            state = PalCustomRoleSaveState.CreateInitial(library);
            requiresReconciliation = false;
            storedRoleCount = 0;
            return false;
        }
    }

    public static string WriteAtomically(
        string rpgPath,
        byte[] rpgBytes,
        PalCustomRoleLibrary library,
        PalCustomRoleSaveState state,
        bool createBackup)
    {
        library.Validate();
        if (state.LibraryId != library.LibraryId || state.LibraryVersion != library.LibraryVersion ||
            state.Roles.Count != library.CustomRoles.Count)
            throw new InvalidDataException("自定义主角状态与固定角色库不匹配。");
        string destination = GetPath(rpgPath);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        string? backup = File.Exists(destination) && createBackup
            ? destination + $".bak-{DateTime.Now:yyyyMMdd-HHmmss}"
            : null;
        string? rollback = File.Exists(destination)
            ? backup ?? destination + $".{Guid.NewGuid():N}.rollback"
            : null;
        state.SaveFile = Path.GetFileName(rpgPath);
        state.RpgSize = checked((uint)rpgBytes.Length);
        state.RpgSha256 = Hash(rpgBytes);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                Write(writer, state);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(destination)) File.Replace(temporary, destination, rollback!, true);
            else File.Move(temporary, destination);
            if (!TryLoad(rpgPath, rpgBytes, library, out PalCustomRoleSaveState verified, out string? warning) ||
                !state.ContentEquals(verified))
                throw new IOException(warning ?? "自定义主角 sidecar 写入后复核不一致。");
            if (!createBackup && rollback is not null) File.Delete(rollback);
            return backup ?? string.Empty;
        }
        catch
        {
            if (rollback is not null && File.Exists(rollback))
            {
                File.Copy(rollback, destination, true);
                if (!createBackup) File.Delete(rollback);
            }
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Write(Utf8JsonWriter writer, PalCustomRoleSaveState state)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", PalCustomRoleSaveState.Schema);
        writer.WriteNumber("schema_version", 1);
        writer.WriteString("save_file", state.SaveFile);
        writer.WriteNumber("rpg_size", state.RpgSize);
        writer.WriteString("rpg_sha256", state.RpgSha256);
        writer.WriteString("library_id", state.LibraryId);
        writer.WriteString("library_version", state.LibraryVersion);
        writer.WriteStartArray("roles");
        foreach (PalCustomRoleCurrentState role in state.Roles)
        {
            writer.WriteStartObject();
            writer.WriteNumber("role_id", role.RoleId);
            WriteArray(writer, "fields", role.Fields);
            WriteArray(writer, "experience_words", role.ExperienceWords);
            WriteArray(writer, "modifiers", role.Modifiers);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteArray(Utf8JsonWriter writer, string name, IEnumerable<short> values)
    {
        writer.WriteStartArray(name);
        foreach (short value in values) writer.WriteNumberValue(value);
        writer.WriteEndArray();
    }

    private static void WriteArray(Utf8JsonWriter writer, string name, IEnumerable<uint> values)
    {
        writer.WriteStartArray(name);
        foreach (uint value in values) writer.WriteNumberValue(value);
        writer.WriteEndArray();
    }

    private static void ReadInt16Array(JsonElement parent, string name, short[] destination)
    {
        JsonElement array = RequireArray(parent, name);
        if (array.GetArrayLength() != destination.Length)
            throw new InvalidDataException($"{name} 长度必须为 {destination.Length}。");
        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (!item.TryGetInt16(out short value)) throw new InvalidDataException($"{name}[{index}] 不是 I2。 ");
            destination[index++] = value;
        }
    }

    private static void ReadUInt32Array(JsonElement parent, string name, uint[] destination)
    {
        JsonElement array = RequireArray(parent, name);
        if (array.GetArrayLength() != destination.Length)
            throw new InvalidDataException($"{name} 长度必须为 {destination.Length}。");
        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (!item.TryGetUInt32(out uint value)) throw new InvalidDataException($"{name}[{index}] 不是 U4。 ");
            destination[index++] = value;
        }
    }

    private static string Hash(byte[] bytes)
    {
        using SHA256 algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    private static void RequireFields(JsonElement element, IReadOnlyCollection<string> required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("sidecar 节点必须是对象。");
        var missing = new HashSet<string>(required, StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!missing.Remove(property.Name) && !required.Contains(property.Name))
                throw new InvalidDataException($"sidecar 含未知字段 {property.Name}。");
        }
        if (missing.Count != 0) throw new InvalidDataException($"sidecar 缺少字段 {string.Join(", ", missing)}。");
    }

    private static string RequireString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"sidecar 缺少字符串 {name}。");
        return value.GetString()!;
    }

    private static int RequireInt(JsonElement element, string name, int minimum, int maximum)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int result) || result < minimum || result > maximum)
            throw new InvalidDataException($"sidecar 的 {name} 越界。");
        return result;
    }

    private static long RequireInt64(JsonElement element, string name, long minimum, long maximum)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || !value.TryGetInt64(out long result) || result < minimum || result > maximum)
            throw new InvalidDataException($"sidecar 的 {name} 越界。");
        return result;
    }

    private static JsonElement RequireArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"sidecar 缺少数组 {name}。");
        return value;
    }
}
