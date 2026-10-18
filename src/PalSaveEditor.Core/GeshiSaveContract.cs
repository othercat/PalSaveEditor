using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PalSaveEditor.Core;

public sealed record GeshiGameLocation(string GameDirectory, string ResourceDirectory, string SaveDirectory);

/// <summary>
/// Contract for the authorized Geshi Easy resources, not a heuristic for arbitrary mods.
/// Resource object count (588) differs from the DOS save object's capacity (700).
/// No copyrighted resources are distributed by this project.
/// </summary>
public static class GeshiSaveContract
{
    public const int ResourceObjectCount = 588;
    public const int SaveObjectCount = 700;
    public const int EventCount = 5632;
    public const int EventOffset = 14064;
    public const int MinimumSaveLength = EventOffset + EventCount * 32;
    public const int MagicDataCount = 108;
    public const int MaximumInventoryAmount = 99;

    private static readonly (string Name, string Sha256)[] Resources =
    [
        ("SSS.MKF", "92efd5ec75dc945f3620f96f61e38879fdc0c605f5916d1143c36a4311ad57f4"),
        ("DATA.MKF", "c1342c3fe39e6c624c7388ad718438819d5981cf73e712d3c613548e7162dbc5"),
        ("WORD.DAT", "01c2ceb561730205ca358bb31d8cb0cedf79c0b63113abc1f32905849e231811"),
        ("M.MSG", "7e4597015be34d8f772bdde13453106b8c9cd824a4fc69073a33bf9aac39612f"),
    ];

    // The DOS object table is a union: flags alone cannot distinguish items from magic.
    // This set is established by target stores, equipment, scripts and enemy item refs.
    public static bool IsItem(int id) => id is >= 61 and <= 294 or 344 or 567 or 568 or 569 or 577 or 583 or 584 or 585;

    // 331 and 391 are empty placeholders. 344 is both a magic record and a script item.
    public static bool IsMagic(int id) => id is >= 295 and <= 397 && id is not 331 and not 391 || id is >= 571 and <= 576;

    public static string? FindGameRoot(string start)
    {
        string directory = File.Exists(start) ? Path.GetDirectoryName(Path.GetFullPath(start))! : Path.GetFullPath(start);
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "sdlpal.cfg")) ||
                File.Exists(Path.Combine(current.FullName, "pal.exe")))
                return current.FullName;
        }
        return null;
    }

    public static GeshiGameLocation Locate(string start)
    {
        string full = Path.GetFullPath(start);
        if (File.Exists(full)) full = Path.GetDirectoryName(full)!;
        string? root = FindGameRoot(full);
        if (root is null && File.Exists(Path.Combine(full, "WORD.DAT")))
        {
            // Explicitly selecting the PAL directory remains supported.
            root = string.Equals(Path.GetFileName(full), "PAL", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(full)! : full;
            return new(root, full, Path.Combine(root, "save"));
        }
        if (root is null && Directory.Exists(Path.Combine(full, "PAL"))) root = full;
        if (root is null) throw new DirectoryNotFoundException("未找到隔世游戏目录。请将工具放在游戏 Tools 内，或选择游戏目录下的 save 存档。");

        string gamePath = "PAL";
        string savePath = ".";
        string config = Path.Combine(root, "sdlpal.cfg");
        if (File.Exists(config))
        {
            foreach (string raw in File.ReadAllLines(config))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or ';') continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim().Trim('"');
                if (value.Length == 0) continue;
                if (key.Equals("GamePath", StringComparison.OrdinalIgnoreCase)) gamePath = value;
                if (key.Equals("SavePath", StringComparison.OrdinalIgnoreCase)) savePath = value;
            }
        }
        return new(root, ResolvePath(root, gamePath), Path.Combine(ResolvePath(root, savePath), "save"));
    }

    public static GeshiGameLocation LocateForSave(string savePath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(savePath))!;
        // A nearby game wins over the editor's package: never silently attach a foreign
        // game's save to this executable's own resources.
        string? root = FindGameRoot(directory);
        if (root is not null) return Locate(root);
        if (Directory.Exists(Path.Combine(directory, "PAL"))) return Locate(directory);
        string? parent = Directory.GetParent(directory)?.FullName;
        if (parent is not null && Directory.Exists(Path.Combine(parent, "PAL"))) return Locate(parent);
        return Locate(AppDomain.CurrentDomain.BaseDirectory);
    }

    public static void ValidateResources(string resourceDirectory)
    {
        foreach (var resource in Resources)
        {
            string path = Path.Combine(resourceDirectory, resource.Name);
            if (!File.Exists(path)) throw new FileNotFoundException($"隔世专用版缺少 {resource.Name}。", path);
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            if (!string.Equals(actual, resource.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"{resource.Name} 不是已验证的《隔世追凶·简单》资源，未打开或写入存档。");
        }
    }

    public static void ValidateSave(byte[] bytes)
    {
        if (bytes.Length < MinimumSaveLength)
            throw new InvalidDataException($"隔世 DOS 存档至少需要 {MinimumSaveLength:N0} 字节（700 对象、5632 事件）；当前 {bytes.Length:N0} 字节。原版 98/DOS 和梦幻存档不能使用此工具。");
        int partyCount = ReadWord(bytes, 6) + 1;
        int scene = ReadWord(bytes, 8);
        int followers = ReadWord(bytes, 32);
        if (partyCount > 5 || scene is < 1 or >= 300 || followers > 2 || partyCount + followers > 5)
            throw new InvalidDataException("隔世存档的场景或队伍头字段无效。未猜测或转换存档格式。");
        for (int index = 0; index < partyCount; index++)
            if (ReadWord(bytes, 44 + index * 10) >= 6)
                throw new InvalidDataException("隔世存档的正式队员必须属于 0～5 六个原生角色槽。");
        for (int index = partyCount; index < partyCount + followers; index++)
            if (ReadWord(bytes, 44 + index * 10) == 0)
                throw new InvalidDataException("隔世存档的随从 MGO 编号不能为 0。");
    }

    private static ushort ReadWord(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static string ResolvePath(string root, string value) => Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root, value));
}
