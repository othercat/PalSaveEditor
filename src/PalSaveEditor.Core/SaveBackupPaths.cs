using System.Globalization;

namespace PalSaveEditor.Core;

/// <summary>Retained backups only; temporary write/rollback files stay beside their target.</summary>
public static class SaveBackupPaths
{
    public static string Create(string sourcePath)
    {
        string source = Path.GetFullPath(sourcePath);
        RequireNoReparsePoints(source);
        string directory = Path.GetDirectoryName(source)
            ?? throw new IOException("无法确定备份源目录。");
        string root = directory;
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            string executable = Path.Combine(current.FullName, "PAL.EXE");
            if (!File.Exists(executable)) continue;
            RequireNoReparsePoints(executable);
            root = current.FullName;
            break;
        }

        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("备份源不在所属目录内。");
        string relative = source.Substring(prefix.Length);
        string backupRoot = Path.Combine(root, "backup", "SaveTools");
        string session;
        do
        {
            session = Path.Combine(backupRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        } while (Directory.Exists(session) || File.Exists(session));
        string backup = Path.Combine(session, relative);
        CreateSafeDirectory(Path.GetDirectoryName(backup)!);
        RequireUnusedPath(backup);
        return backup;
    }

    public static string Companion(string rpgBackupPath, string suffix)
    {
        if (suffix.Length == 0 || suffix[0] != '.' || suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("存档备份附属文件名无效。");
        string path = Path.GetFullPath(rpgBackupPath) + suffix;
        RequireUnusedPath(path);
        return path;
    }

    private static void CreateSafeDirectory(string path)
    {
        RequireNoReparsePoints(path);
        string? parent = Path.GetDirectoryName(path);
        if (!Directory.Exists(path))
        {
            if (parent is not null && !Directory.Exists(parent)) CreateSafeDirectory(parent);
            Directory.CreateDirectory(path);
        }
        RequireNoReparsePoints(path);
    }

    private static void RequireUnusedPath(string path)
    {
        RequireNoReparsePoints(path);
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("备份路径已被占用，未覆盖已有文件：" + path);
    }

    private static void RequireNoReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("备份路径不能经过符号链接或目录联接：" + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
