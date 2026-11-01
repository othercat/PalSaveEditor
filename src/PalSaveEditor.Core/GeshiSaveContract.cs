using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;

namespace PalSaveEditor.Core;

public sealed record GeshiGameLocation(string GameDirectory, string ResourceDirectory, string SaveDirectory, bool ContentScopedSave = false);

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
        string? saveDirectory = null;
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
                if (key.Equals("SaveDirectory", StringComparison.OrdinalIgnoreCase)) saveDirectory = value;
            }
        }
        return new(root, ResolvePath(root, gamePath), saveDirectory is null
            ? Path.Combine(ResolvePath(root, savePath), "save") : ResolvePath(root, saveDirectory), saveDirectory is not null);
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

    // Target validation must also recognize standalone resource copies. Unlike
    // LocateForSave, this never falls back to the editor executable's own game.
    internal static GeshiGameLocation? NearbyLocation(string savePath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(savePath))!;
        string? root = FindGameRoot(directory);
        if (root is not null) return Locate(root);
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "WORD.DAT")) ||
                File.Exists(Path.Combine(current.FullName, "PAL", "WORD.DAT")))
                return Locate(current.FullName);
        return null;
    }

    public static void ValidateResources(string resourceDirectory)
        => GetResourceCodePage(resourceDirectory);

    public static int GetResourceCodePage(string resourceDirectory)
    {
        if (MaintenanceContentProfile.FindDream(resourceDirectory) is not null) return 936;
        bool original = true, simplified = true;
        foreach (var resource in Resources)
        {
            string path = Path.Combine(resourceDirectory, resource.Name);
            if (!File.Exists(path)) throw new FileNotFoundException($"隔世专用版缺少 {resource.Name}。", path);
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            string simplifiedHash = resource.Name switch
            {
                "WORD.DAT" => "6bb07cbcdc47073a60ab53386f7d57a7cc8768c7845fcf679baf08d72a08e4fd",
                "M.MSG" => "599525efa15e0e11e65942f4139ef097bdb8a784a9fd7cb1999226ef754435de",
                _ => resource.Sha256,
            };
            original &= string.Equals(actual, resource.Sha256, StringComparison.Ordinal);
            // Both Simplified dialogue editions still require the fixed Simplified WORD and SSS/DATA.
            simplified &= string.Equals(actual, simplifiedHash, StringComparison.Ordinal) ||
                (resource.Name == "M.MSG" && actual == "880aff05741da8d00a152b7a22661ad509817f36e7bacf52d3fae4cd8f1aceb4");
        }
        if (!original && !simplified)
            throw new InvalidDataException("配套文件不是完整的已验证隔世原版或简体资源组合，未打开或写入存档。");
        return simplified ? 936 : 950;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(string localeName, uint flags, string source, int sourceLength,
        StringBuilder? destination, int destinationLength, IntPtr version, IntPtr reserved, IntPtr sortHandle);

    public static string DisplayName(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        int size = LCMapStringEx("zh-CN", 0x02000000, value, -1, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (size == 0) throw new InvalidOperationException("简体名称转换失败。");
        var result = new StringBuilder(size);
        if (LCMapStringEx("zh-CN", 0x02000000, value, -1, result, size, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != size)
            throw new InvalidOperationException("简体名称转换失败。");
        return result.ToString();
    }

    public static void ValidateSave(byte[] bytes, MaintenanceContentProfile? profile = null)
    {
        profile ??= MaintenanceContentProfile.Geshi;
        if (bytes.Length < profile.MinimumSaveLength)
            throw new InvalidDataException($"{profile.Name} 存档至少需要 {profile.MinimumSaveLength:N0} 字节（700×{profile.ObjectRecordSize} 字节对象、{profile.EventCount} 事件）；当前 {bytes.Length:N0} 字节。");
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
