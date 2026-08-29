using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PalSaveEditor.Core;

public sealed class ExtendedRoleMagicState
{
    public const string Schema = "PAL98.ExtendedRoleMagicSlots.v1";
    public const int RoleCount = 6;
    public const int CapacityPerRole = 999;
    public const int PageSize = 32;
    public const int PageCount = 32;
    public const string Suffix = ".pal98-ext-magics.json";

    public ushort ActivePage { get; set; }
    public ushort[][] Roles { get; } = Enumerable.Range(0, RoleCount)
        .Select(_ => new ushort[CapacityPerRole])
        .ToArray();

    public ExtendedRoleMagicState Clone()
    {
        var clone = new ExtendedRoleMagicState { ActivePage = ActivePage };
        for (var role = 0; role < RoleCount; role++)
        {
            Array.Copy(Roles[role], clone.Roles[role], CapacityPerRole);
        }
        return clone;
    }

    public bool ContentEquals(ExtendedRoleMagicState other) =>
        ActivePage == other.ActivePage &&
        Enumerable.Range(0, RoleCount)
            .All(role => Roles[role].AsSpan().SequenceEqual(other.Roles[role]));

    public void ProjectActivePage(byte[] rpgBytes)
    {
        var first = ActivePage * PageSize;
        for (var role = 0; role < RoleCount; role++)
        {
            for (var slot = 0; slot < PageSize; slot++)
            {
                var index = first + slot;
                var value = index < CapacityPerRole ? Roles[role][index] : (ushort)0;
                var offset = PalSaveLayout.MagicOffset(slot, role);
                rpgBytes[offset] = (byte)value;
                rpgBytes[offset + 1] = (byte)(value >> 8);
            }
        }
    }

    public static ExtendedRoleMagicState FromPhysicalPage0(byte[] rpgBytes)
    {
        var state = new ExtendedRoleMagicState();
        for (var role = 0; role < RoleCount; role++)
        {
            for (var slot = 0; slot < PageSize; slot++)
            {
                var offset = PalSaveLayout.MagicOffset(slot, role);
                state.Roles[role][slot] = (ushort)(
                    rpgBytes[offset] | (rpgBytes[offset + 1] << 8));
            }
        }
        return state;
    }
}

public static class ExtendedRoleMagicSidecar
{
    public static bool SupportsPath(string rpgPath)
    {
        var fileName = Path.GetFileName(rpgPath);
        return fileName.Length == 5 &&
            fileName[0] is >= '1' and <= '5' &&
            fileName.EndsWith(".RPG", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetPath(string rpgPath) =>
        Path.GetFullPath(rpgPath) + ExtendedRoleMagicState.Suffix;

    public static bool TryLoad(
        string rpgPath,
        byte[] rpgBytes,
        out ExtendedRoleMagicState state,
        out string? warning) =>
        TryLoadCore(rpgPath, rpgBytes, preserveStateOnBindingMismatch: false, out state, out warning);

    public static bool TryLoadRecoverable(
        string rpgPath,
        byte[] rpgBytes,
        out ExtendedRoleMagicState state,
        out string? warning) =>
        TryLoadCore(rpgPath, rpgBytes, preserveStateOnBindingMismatch: true, out state, out warning);

    private static bool TryLoadCore(
        string rpgPath,
        byte[] rpgBytes,
        bool preserveStateOnBindingMismatch,
        out ExtendedRoleMagicState state,
        out string? warning)
    {
        state = ExtendedRoleMagicState.FromPhysicalPage0(rpgBytes);
        warning = null;
        var sidecarPath = GetPath(rpgPath);
        if (!File.Exists(sidecarPath))
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(sidecarPath),
                new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            RequireOnlyProperties(root,
                "schema", "schema_version", "save_file", "rpg_size",
                "rpg_sha256", "role_count", "capacity_per_role",
                "page_size", "active_page", "roles");
            if (root.GetProperty("schema").GetString() != ExtendedRoleMagicState.Schema ||
                root.GetProperty("schema_version").GetInt32() != 1 ||
                !string.Equals(
                    root.GetProperty("save_file").GetString(),
                    Path.GetFileName(rpgPath),
                    StringComparison.OrdinalIgnoreCase) ||
                root.GetProperty("role_count").GetInt32() != ExtendedRoleMagicState.RoleCount ||
                root.GetProperty("capacity_per_role").GetInt32() != ExtendedRoleMagicState.CapacityPerRole ||
                root.GetProperty("page_size").GetInt32() != ExtendedRoleMagicState.PageSize)
            {
                throw new InvalidDataException("扩展法术槽 sidecar 与当前 RPG 身份或维度不匹配。");
            }
            var activePage = root.GetProperty("active_page").GetInt32();
            if (activePage is < 0 or >= ExtendedRoleMagicState.PageCount)
            {
                throw new InvalidDataException("扩展法术槽页码无效。");
            }
            var roles = root.GetProperty("roles");
            if (roles.ValueKind != JsonValueKind.Array ||
                roles.GetArrayLength() != ExtendedRoleMagicState.RoleCount)
            {
                throw new InvalidDataException("扩展法术槽角色数无效。");
            }
            var loaded = new ExtendedRoleMagicState { ActivePage = (ushort)activePage };
            var roleIndex = 0;
            foreach (var row in roles.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array ||
                    row.GetArrayLength() != ExtendedRoleMagicState.CapacityPerRole)
                {
                    throw new InvalidDataException("扩展法术槽容量无效。");
                }
                var slot = 0;
                foreach (var value in row.EnumerateArray())
                {
                    var magic = value.GetInt32();
                    if (magic is < 0 or > short.MaxValue)
                    {
                        throw new InvalidDataException("扩展法术槽包含无效对象编号。");
                    }
                    loaded.Roles[roleIndex][slot++] = (ushort)magic;
                }
                roleIndex++;
            }
            state = loaded;
            if (root.GetProperty("rpg_size").GetInt64() != rpgBytes.Length ||
                root.GetProperty("rpg_sha256").GetString() != Hash(rpgBytes))
            {
                warning = "扩展法术槽 sidecar 的 RPG 大小或 SHA-256 绑定已失效。";
                if (!preserveStateOnBindingMismatch)
                {
                    state = ExtendedRoleMagicState.FromPhysicalPage0(rpgBytes);
                }
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            warning = $"扩展法术槽 sidecar 无效，当前仅加载 RPG 的原生 32 槽：{ex.Message}";
            return false;
        }
    }

    public static void WriteAtomically(
        string rpgPath,
        byte[] rpgBytes,
        ExtendedRoleMagicState state)
    {
        var fileName = Path.GetFileName(rpgPath);
        if (!SupportsPath(rpgPath))
        {
            throw new InvalidDataException("扩展法术槽只能随 1.RPG 到 5.RPG 保存。");
        }
        var sidecarPath = GetPath(rpgPath);
        var temporary = sidecarPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("schema", ExtendedRoleMagicState.Schema);
                writer.WriteNumber("schema_version", 1);
                writer.WriteString("save_file", fileName.ToUpperInvariant());
                writer.WriteNumber("rpg_size", rpgBytes.Length);
                writer.WriteString("rpg_sha256", Hash(rpgBytes));
                writer.WriteNumber("role_count", ExtendedRoleMagicState.RoleCount);
                writer.WriteNumber("capacity_per_role", ExtendedRoleMagicState.CapacityPerRole);
                writer.WriteNumber("page_size", ExtendedRoleMagicState.PageSize);
                writer.WriteNumber("active_page", state.ActivePage);
                writer.WritePropertyName("roles");
                writer.WriteStartArray();
                foreach (var role in state.Roles)
                {
                    writer.WriteStartArray();
                    foreach (var magic in role) writer.WriteNumberValue(magic);
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(sidecarPath))
            {
                File.Replace(temporary, sidecarPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, sidecarPath);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static void RequireOnlyProperties(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("扩展法术槽 sidecar 根节点不是对象。");
        }
        var allowed = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowed.Remove(property.Name))
            {
                throw new InvalidDataException($"扩展法术槽 sidecar 含未知字段：{property.Name}。");
            }
        }
        if (allowed.Count != 0)
        {
            throw new InvalidDataException("扩展法术槽 sidecar 缺少必要字段。");
        }
    }
}
