using System.Security.Cryptography;
using System.Text.Json;

namespace PalSaveEditor.Core;

// Per-open-document identity. Neither formulas nor character values are
// calculated here, and no binding bytes are inserted into an RPG.
public sealed record MaintenanceGameplayBinding(string Resources, string? RuleId,
    int Revision, string? Sha256, string SelectionState);

internal static partial class MaintenanceGameplayRules
{
    private sealed record Approved(string Content, string Id, int Revision,
        string Format, string Hash, string[] Resources);
    private static readonly string[] ResourceNames = ["SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG"];
    private static readonly string[] SelectionKeys = ["gameplay_rule_id", "gameplay_rule_revision", "gameplay_rule_sha256"];

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    private static byte[] ReadBounded(string path, int limit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > limit) throw new InvalidDataException("内容公式或选择文件过大。");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
    internal static MaintenanceGameplayBinding Read(GeshiGameLocation location, MaintenanceContentProfile profile)
    {
        var hashes = ResourceNames.Select(n => Hash(File.ReadAllBytes(Path.Combine(location.ResourceDirectory, n)))).ToArray();
        string resources = string.Join("|", hashes);
        string rules = Path.Combine(location.ResourceDirectory, "GAMEPLAY.INI");
        string selection = Path.Combine(location.GameDirectory, "geshi-game.v1.json");
        Approved? contract = null;
        if (File.Exists(rules))
        {
            // Full-file SHA binds the complete ordered schema, all 73 fields,
            // format and resource header checked by the engine's generator.
            string digest = Hash(ReadBounded(rules, 16384));
            contract = Contracts.SingleOrDefault(c => c.Content == profile.Id && c.Hash == digest &&
                c.Format == (profile.ObjectRecordSize == PalSaveLayout.WinObjectRecordSize ? "WIN95" : "DOS") && c.Resources.SequenceEqual(hashes));
            if (contract is null) throw new InvalidDataException("内容公式未经配套版本验证或与当前资源不一致，未打开或写入存档。");
        }
        string state = "absent";
        if (File.Exists(selection))
        {
            using var json = JsonDocument.Parse(ReadBounded(selection, 65536));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() != 1))
                throw new InvalidDataException("内容选择文件格式或重复字段无效。");
            if (root.EnumerateObject().Any(p => SelectionKeys.Any(k => p.Name.Equals(k, StringComparison.OrdinalIgnoreCase) && p.Name != k)))
                throw new InvalidDataException("内容公式选择字段大小写不规范。");
            if (!StringValue(root, "schema", "SDLPAL.ContentSelection.v1") || !StringValue(root, "package", profile.Id))
                throw new InvalidDataException("内容选择文件与当前资源不一致，请重新准备内容。");
            bool any = SelectionKeys.Any(k => root.TryGetProperty(k, out _));
            state = any ? "bound" : "legacy";
            if (any || contract is not null)
            {
                if (contract is null || !SelectionKeys.All(k => root.TryGetProperty(k, out _)) ||
                    !StringValue(root, "schema", "SDLPAL.ContentSelection.v1") || !StringValue(root, "package", profile.Id) ||
                    !StringValue(root, SelectionKeys[0], contract.Id) || !StringValue(root, SelectionKeys[2], contract.Hash) ||
                    root.GetProperty(SelectionKeys[1]).ValueKind != JsonValueKind.Number ||
                    root.GetProperty(SelectionKeys[1]).GetRawText() != contract.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    throw new InvalidDataException("内容选择与公式身份不一致，请重新准备内容后再打开存档。");
            }
        }
        // A standalone PAL copy may have rules without a launcher selection.
        // Its absence is frozen too; adding/removing a selection requires reopen.
        return new(resources, contract?.Id, contract?.Revision ?? 0, contract?.Hash, state);
    }
    private static bool StringValue(JsonElement root, string key, string value) =>
        root.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String && item.GetString() == value;

    internal static void ValidateCurrent(PalResourceCatalog catalog, GeshiGameLocation? location = null)
    {
        location ??= GeshiSaveContract.Locate(catalog.ResourceContext.GameDirectory);
        var profile = MaintenanceContentProfile.Resolve(location.ResourceDirectory);
        if (profile.Id != catalog.ContentProfile?.Id || Read(location, profile) != catalog.GameplayBinding)
            throw new InvalidDataException("内容资源或公式已切换，请重新打开对应存档；当前编辑未写入。");
    }

    internal sealed class Lease : IDisposable
    {
        private readonly List<FileStream> streams = new();
        private readonly HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> absent = new();
        private void Lock(string path)
        {
            string file = Path.GetFullPath(path);
            if (!paths.Add(file)) return;
            if (File.Exists(file)) streams.Add(new(file, FileMode.Open, FileAccess.Read, FileShare.Read));
            else absent.Add(file);
        }
        internal void Add(GeshiGameLocation location)
        {
            // Lock the pointer before its resources. If it changed between
            // discovery and this lock, refuse rather than locking directory A
            // while the reader subsequently follows the pointer to B.
            Lock(Path.Combine(location.GameDirectory, "sdlpal.cfg"));
            var pinned = GeshiSaveContract.Locate(location.GameDirectory);
            if (!Path.GetFullPath(pinned.ResourceDirectory).Equals(Path.GetFullPath(location.ResourceDirectory), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(pinned.SaveDirectory).Equals(Path.GetFullPath(location.SaveDirectory), StringComparison.OrdinalIgnoreCase))
                throw new IOException("内容目录在校验期间发生切换，请重新打开存档。");
            var files = ResourceNames.Select(n => Path.Combine(location.ResourceDirectory, n)).Concat(new[] {
                Path.Combine(location.ResourceDirectory, "GAMEPLAY.INI"), Path.Combine(location.GameDirectory, "geshi-game.v1.json"),
                Path.Combine(location.SaveDirectory, ".content-package") });
            foreach (string file in files) Lock(file);
        }
        internal void CheckMissing()
        {
            if (absent.Any(File.Exists)) throw new IOException("打开期间新增了内容配置或公式文件，请重新打开存档。");
        }
        public void Dispose() { foreach (var stream in streams) stream.Dispose(); }
    }
    internal static Lease Acquire(GeshiGameLocation location)
    {
        var lease = new Lease();
        try { lease.Add(location); return lease; } catch { lease.Dispose(); throw; }
    }
    internal static Lease AcquireForSave(PalResourceCatalog? catalog, string target)
    {
        if (catalog is null) throw new InvalidOperationException("未验证维护版资源，不能保存。");
        var lease = new Lease();
        try {
            lease.Add(GeshiSaveContract.Locate(catalog.ResourceContext.GameDirectory));
            var targetLocation = GeshiSaveContract.NearbyLocation(target);
            if (targetLocation is not null) lease.Add(targetLocation);
            return lease;
        } catch { lease.Dispose(); throw; }
    }
}
