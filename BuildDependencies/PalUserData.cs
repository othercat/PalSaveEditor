using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Pal98.Storage
{
    public sealed class UserDataException : IOException
    {
        public UserDataException(string message, Exception inner) : base(message, inner) { }
    }

    // Linked source shared by the managed tools. The native implementation uses
    // the same installation identity, relative layout and source receipt format.
    public sealed class UserDataStore
    {
        private const long MaximumConfigurationBytes = 64 * 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private static string Io(string path)
        {
            if (path == null || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.Length < 180) return path;
            string full = Path.GetFullPath(path);
            return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full.Substring(2) : @"\\?\" + full;
        }
        private readonly Action<string> report;
        private readonly HashSet<string> warnings = new HashSet<string>(StringComparer.Ordinal);
        public string InstallationRoot { get; private set; }
        public string InstallationId { get; private set; }
        public string DirectoryPath { get; private set; }

        public UserDataStore(string installationRoot, string localAppData = null, Action<string> reportError = null)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            InstallationRoot = Canonical(installationRoot);
            InstallationId = Identity(InstallationRoot);
            string local = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new UserDataException("无法确定可读写的用户配置目录。", null);
            DirectoryPath = Path.Combine(Path.GetFullPath(local), "PAL98", "UserData", "v1", InstallationId);
            report = reportError;
        }

        public static string Canonical(string path)
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path.Substring(4);
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            return full.Length == root.Length ? root : full.TrimEnd('\\', '/');
        }

        public static string Identity(string installationRoot)
        {
            return Hash(Utf8.GetBytes(Canonical(installationRoot).ToUpperInvariant())).Substring(0, 24);
        }

        public static string FindInstallation(string executableDirectory)
        {
            string original = Canonical(executableDirectory), candidate = original;
            for (int level = 0; level < 6 && candidate != null; ++level) {
                if (File.Exists(Path.Combine(candidate, "PAL.exe"))) return candidate;
                candidate = Path.GetDirectoryName(candidate);
            }
            return original;
        }

        public void Initialize()
        {
            Access(DirectoryPath, () => {
                SafeDirectory(DirectoryPath);
                string probe = Path.Combine(DirectoryPath, ".write-probe-" + Guid.NewGuid().ToString("N"));
                using (var stream = new FileStream(Io(probe), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)) {
                    stream.WriteByte(0x50); stream.Position = 0;
                    if (stream.ReadByte() != 0x50) throw new IOException("用户目录读写校验失败。");
                }
            });
        }

        // Preserve the portable fast path. The directory is probed once before
        // a caller starts a configuration transaction; no settings are copied.
        public bool PreferUserDirectory(string component, string sourceRoot, IEnumerable<string> names, bool allowProtectedReadOnly = false)
        {
            ValidateRelative(component);
            try {
                try { File.GetAttributes(Io(Path.Combine(DirectoryPath, component))); return true; }
                catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
                var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Canonical(sourceRoot) };
                foreach (string name in names) {
                    ValidateRelative(name);
                    string source = Path.Combine(sourceRoot, name.Replace('/', '\\'));
                    if (Exists(source)) {
                        bool protectedReadOnly = allowProtectedReadOnly && (File.GetAttributes(Io(source)) & FileAttributes.ReadOnly) != 0;
                        using (var stream = new FileStream(Io(source), FileMode.Open, protectedReadOnly ? FileAccess.Read : FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)) { }
                    }
                    string parent = Path.GetDirectoryName(source);
                    while (parent != null && !Directory.Exists(Io(parent))) parent = Path.GetDirectoryName(parent);
                    if (parent != null) directories.Add(parent);
                }
                foreach (string directory in directories) {
                    string probe = Path.Combine(directory, ".pal98-write-probe-" + Guid.NewGuid().ToString("N"));
                    using (var stream = new FileStream(Io(probe), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)) stream.WriteByte(0x50);
                }
                return false;
            } catch (Exception error) when (IsPermissionFailure(error)) { return true; }
        }

        public static bool IsPermissionFailure(Exception error)
        {
            int code = error.HResult & 0xffff;
            return error is UnauthorizedAccessException || error is System.Security.SecurityException || error is IOException && (code == 5 || code == 19);
        }

        // Read-only observers (timer/online) must see the same effective settings
        // as PAL, but must never migrate files or substitute a user's resources.
        public static bool IsGameConfiguration(string relative)
        {
            string p = relative.Replace('\\', '/').ToLowerInvariant();
            if (p.IndexOf('/') < 0 && (p.EndsWith(".ini") || p.Contains(".ini.") || p == "hardcore-keyboard.v1.json" ||
                p == "hardcore-transaction.pending" || p == "hardcore-session.v1.json" || p.StartsWith("hardcore-session.v1.json.", StringComparison.Ordinal) || p == "music-directories.v1.json")) return true;
            foreach (string directory in new[] { "palmod/tournamentlock", "palmod/configurations", "copymen_scripts", "graphics/presets", "backup/configtool", "palmod/backups" })
                if (p == directory || p.StartsWith(directory + "/", StringComparison.Ordinal)) return true;
            foreach (string file in new[] { "palmod/common-tools.v1.json", "palmod/title-presentation.v1.json", "palmod/random-skill-selection.v1.json",
                "graphics/renderer-state.v1.json", "musics/songlist.txt", "voices/voice_rules.txt", "voices/playlist-state.v1.json", "palmod/profiles/current.json", "palmod/profiles/previous.json" })
                if (p == file || p.StartsWith(file + ".", StringComparison.Ordinal)) return true;
            return false;
        }

        public static string ReadGamePath(string installation, string relative, string localAppData = null)
        {
            ValidateRelative(relative);
            var store = new UserDataStore(installation, localAppData);
            string source = Path.Combine(store.InstallationRoot, relative.Replace('/', '\\'));
            string game = Path.Combine(store.DirectoryPath, "game"), target = Path.Combine(game, relative.Replace('/', '\\'));
            if (!DirectoryExists(game)) return Io(source);
            store.RejectReparse(target);
            bool cache = relative.Replace('\\', '/').StartsWith("palmod/Profiles/", StringComparison.OrdinalIgnoreCase);
            return Io(IsGameConfiguration(relative) || cache && (FileExists(target) || DirectoryExists(target)) ? target : source);
        }

        public string ComponentDirectory(string component)
        {
            ValidateRelative(component);
            if (component.IndexOf('/') >= 0 || component.IndexOf('\\') >= 0) throw new ArgumentException("A single component is required.");
            string path = Path.Combine(DirectoryPath, component);
            Access(path, () => SafeDirectory(path));
            return path;
        }

        public string PathFor(string component, string relative)
        {
            ValidateRelative(relative);
            return Path.Combine(ComponentDirectory(component), relative.Replace('/', Path.DirectorySeparatorChar));
        }

        // Source paths are explicit. The store never scans outside its caller's
        // declared configuration set or copies authentication/resource directories.
        public string Resolve(string component, string relative, string source)
        {
            string target = PathFor(component, relative);
            string warning = null;
            Access(target, () => WithLock(() => {
                RejectReparse(target);
                Recover(component, relative, target);
                warning = Synchronize(component, relative, source, target);
            }));
            // Do not hold a cross-process mutex while a caller displays a dialog.
            if (warning != null && report != null) {
                bool first; lock (warnings) first = warnings.Add(warning);
                if (first) report(warning);
            }
            if (warning == null) lock (warnings) warnings.RemoveWhere(value => value.Contains("\n" + target + "\n"));
            return target;
        }

        public void Write(string component, string relative, byte[] bytes, string source = null)
        {
            string target = PathFor(component, relative);
            Access(target, () => WithLock(() => {
                Recover(component, relative, target);
                string stamp = StampForWrite(component, relative, source);
                Commit(component, relative, target, bytes, stamp, DateTime.UtcNow.ToFileTimeUtc());
            }));
        }

        // Legacy settings are imported only before the new store has observed
        // this logical file. They never compete with package files on later runs.
        public void ImportLegacyOnce(string component, string relative, string legacySource, string packageSource)
        {
            string target = PathFor(component, relative);
            Access(target, () => WithLock(() => {
                Recover(component, relative, target);
                if (Exists(target) || Exists(Receipt(component, relative)) || Exists(Receipt(component, relative) + ".removed")) return;
                if (Exists(legacySource)) Commit(component, relative, target, ReadBounded(legacySource), SourceStamp(packageSource), DateTime.UtcNow.ToFileTimeUtc());
            }));
        }

        public void WriteText(string component, string relative, string value, string source = null)
        {
            Write(component, relative, Utf8.GetBytes(value), source);
        }

        public void Update(string component, string relative, string source, Func<byte[], byte[]> transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            string target = PathFor(component, relative);
            Access(target, () => WithLock(() => {
                RejectReparse(target); Recover(component, relative, target);
                Synchronize(component, relative, source, target);
                byte[] result = transform(Exists(target) ? ReadBounded(target) : null);
                Commit(component, relative, target, result, StampForWrite(component, relative, source), DateTime.UtcNow.ToFileTimeUtc());
            }));
        }

        public void Remove(string component, string relative, string source = null)
        {
            string target = PathFor(component, relative);
            Access(target, () => WithLock(() => {
                Recover(component, relative, target);
                Commit(component, relative, target, null, StampForWrite(component, relative, source), 0);
            }));
        }

        // Record this before moving/deleting a user directory. If the directory
        // operation fails or is interrupted, its remaining files stay readable;
        // a successful move cannot be undone by importing the unchanged package.
        public void SuppressPackage(string component, string relative, string source)
        {
            string target = PathFor(component, relative);
            Access(target, () => WithLock(() => {
                RejectReparse(target); Recover(component, relative, target);
                AtomicWrite(Receipt(component, relative) + ".removed",
                    Utf8.GetBytes(StampForWrite(component, relative, source) ?? "absent\n"));
            }));
        }

        public static bool FileExists(string path) => Exists(path);

        public static bool DirectoryExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return (File.GetAttributes(Io(path)) & FileAttributes.Directory) != 0; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        private string Synchronize(string component, string relative, string source, string target)
        {
            bool userExists = Exists(target);
            string stamp;
            try { stamp = SourceStamp(source); }
            catch (Exception error) {
                if (!IsAccessFailure(error) || !userExists) throw;
                VerifyReadable(target);
                return "无法读取安装目录中的配置，继续使用已保存的用户配置：\n" + target + "\n" + error.Message;
            }
            string receipt = Receipt(component, relative);
            string observed = Exists(receipt) ? File.ReadAllText(Io(receipt), Utf8) : null;
            string removed = receipt + ".removed";
            bool deleted = Exists(removed) && File.ReadAllText(Io(removed), Utf8) == (stamp ?? "absent\n");
            if (stamp != null && !deleted && (!userExists ||
                stamp != observed && StampTime(stamp) > File.GetLastWriteTimeUtc(Io(target)).ToFileTimeUtc())) {
                byte[] bytes;
                try { bytes = ReadBounded(source); }
                catch (Exception error) {
                    if (!IsAccessFailure(error) || !userExists) throw;
                    VerifyReadable(target);
                    return "安装目录的新配置未能同步，继续使用已保存的用户配置：\n" + target + "\n" + error.Message;
                }
                // A source modified while it is read is not a publishable snapshot.
                if (SourceStamp(source) != stamp) throw new IOException("同步期间本地配置发生变化，请重试：" + source);
                Commit(component, relative, target, bytes, stamp, StampTime(stamp));
                userExists = true;
            }
            if (userExists) VerifyReadable(target);
            return null;
        }

        private static long StampTime(string stamp) => long.Parse(stamp.Split(' ')[0], CultureInfo.InvariantCulture);

        private string StampForWrite(string component, string relative, string source)
        {
            try { return SourceStamp(source); }
            catch (Exception error) {
                if (!IsAccessFailure(error)) throw;
                string receipt = Receipt(component, relative);
                if (!Exists(receipt)) throw;
                string stamp = File.ReadAllText(Io(receipt), Utf8);
                return stamp == "absent\n" ? null : stamp;
            }
        }

        private string Pending(string component, string relative)
        {
            string directory = Path.Combine(ComponentDirectory("state"), "pending");
            SafeDirectory(directory);
            return Path.Combine(directory, component.ToLowerInvariant() + "-" + Path.GetFileName(Receipt(component, relative)) + ".pending");
        }

        // A durable intent precedes both data and receipt changes. A interrupted
        // commit is replayed before another read/write; it cannot resurrect the
        // distribution copy over a successfully written user setting.
        private void Commit(string component, string relative, string target, byte[] bytes, string stamp, long time)
        {
            if (bytes != null && bytes.LongLength > MaximumConfigurationBytes) throw new InvalidDataException("配置文件超过允许大小。");
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream, Utf8, true)) {
                    writer.Write(1); writer.Write(stamp ?? "absent\n"); writer.Write(time);
                    writer.Write(bytes == null ? -1 : bytes.Length);
                    if (bytes != null) writer.Write(bytes);
                }
                AtomicWrite(Pending(component, relative), stream.ToArray());
            }
            Recover(component, relative, target);
        }

        private void Recover(string component, string relative, string target)
        {
            string pending = Pending(component, relative);
            if (!Exists(pending)) return;
            string stamp; long time; byte[] bytes = null;
            using (var stream = new MemoryStream(ReadBounded(pending, MaximumConfigurationBytes + 1024)))
            using (var reader = new BinaryReader(stream, Utf8)) {
                if (reader.ReadInt32() != 1) throw new InvalidDataException("用户配置恢复记录版本无效：" + pending);
                stamp = reader.ReadString(); time = reader.ReadInt64(); int length = reader.ReadInt32();
                if (length < -1 || length > MaximumConfigurationBytes || (length >= 0 && stream.Length - stream.Position != length))
                    throw new InvalidDataException("用户配置恢复记录损坏：" + pending);
                if (length >= 0) bytes = reader.ReadBytes(length);
            }
            string receipt = Receipt(component, relative);
            if (bytes == null) {
                AtomicWrite(receipt + ".removed", Utf8.GetBytes(stamp));
                if (Exists(target)) { File.Copy(Io(target), Io(BackupName(target)), false); File.Delete(Io(target)); }
            } else {
                AtomicWrite(target, bytes);
                File.SetLastWriteTimeUtc(Io(target), DateTime.FromFileTimeUtc(time));
                AtomicWrite(receipt, Utf8.GetBytes(stamp));
                if (Exists(receipt + ".removed")) File.Delete(Io(receipt + ".removed"));
            }
            File.Delete(Io(pending));
        }

        private string Receipt(string component, string relative)
        {
            string directory = Path.Combine(ComponentDirectory("state"), "sources");
            SafeDirectory(directory);
            return Path.Combine(directory, Hash(Utf8.GetBytes((component + "/" + relative.Replace('\\', '/')).ToLowerInvariant())) + ".stamp");
        }

        private void AtomicWrite(string target, byte[] bytes)
        {
            if (bytes == null || bytes.LongLength > MaximumConfigurationBytes + 1024) throw new InvalidDataException("配置文件超过允许大小。");
            SafeDirectory(Path.GetDirectoryName(target));
            RejectReparse(target);
            string temporary = target + ".new-" + Guid.NewGuid().ToString("N");
            try {
                using (var stream = new FileStream(Io(temporary), FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (Exists(target)) File.Replace(Io(temporary), Io(target), Io(BackupName(target)));
                else File.Move(Io(temporary), Io(target));
            } finally { if (File.Exists(Io(temporary))) File.Delete(Io(temporary)); }
        }

        private string BackupName(string target)
        {
            string directory = Path.Combine(ComponentDirectory("state"), "previous");
            SafeDirectory(directory);
            return Path.Combine(directory, Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N"));
        }

        private void SafeDirectory(string path)
        {
            RejectReparse(path);
            Directory.CreateDirectory(Io(path));
            RejectReparse(path);
        }

        private void RejectReparse(string path)
        {
            for (string current = path; current != null && current.Length >= DirectoryPath.Length; current = Path.GetDirectoryName(current)) {
                try { if ((File.GetAttributes(Io(current)) & FileAttributes.ReparsePoint) != 0) throw new IOException("用户配置目录不能是重解析点：" + current); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        private static bool Exists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return (File.GetAttributes(Io(path)) & FileAttributes.Directory) == 0; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        private static string SourceStamp(string path)
        {
            if (!Exists(path)) return null;
            var info = new FileInfo(Io(path));
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("配置来源不能是文件链接：" + path);
            return info.LastWriteTimeUtc.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture) + " " + info.Length.ToString(CultureInfo.InvariantCulture) + "\n";
        }

        private static void VerifyReadable(string path)
        {
            using (var stream = new FileStream(Io(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                if (stream.Length > MaximumConfigurationBytes) throw new InvalidDataException("用户配置文件超过允许大小：" + path);
            }
        }

        private static byte[] ReadBounded(string path, long maximum = MaximumConfigurationBytes)
        {
            using (var stream = new FileStream(Io(path), FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (stream.Length > maximum) throw new InvalidDataException("本地配置文件超过允许大小：" + path);
                using (var output = new MemoryStream()) {
                    var buffer = new byte[8192]; int count;
                    while ((count = stream.Read(buffer, 0, buffer.Length)) != 0) {
                        if (output.Length + count > maximum) throw new InvalidDataException("配置文件超过允许大小：" + path);
                        output.Write(buffer, 0, count);
                    }
                    return output.ToArray();
                }
            }
        }

        private void WithLock(Action action)
        {
            using (var mutex = new Mutex(false, @"Local\PAL98.UserData.v1." + InstallationId)) {
                bool owned = false;
                try {
                    try { owned = mutex.WaitOne(TimeSpan.FromSeconds(10)); } catch (AbandonedMutexException) { owned = true; }
                    if (!owned) throw new IOException("另一个 PAL98 程序正在同步用户配置，请稍后重试。");
                    action();
                } finally { if (owned) mutex.ReleaseMutex(); }
            }
        }

        private void Access(string path, Action action)
        {
            try { action(); }
            catch (UserDataException) { throw; }
            catch (Exception error) {
                if (!IsAccessFailure(error)) throw;
                string message = "无法读取或保存用户配置。\n" + path + "\n" + error.Message + "\n请检查当前 Windows 用户对该目录的读写权限和磁盘空间。原安装目录中的配置未被覆盖。";
                if (report != null) report(message);
                throw new UserDataException(message, error);
            }
        }

        private static bool IsAccessFailure(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException;
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static void ValidateRelative(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0) throw new ArgumentException("Unsafe configuration path.");
            foreach (string part in relative.Replace('\\', '/').Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.TrimEnd(' ', '.') != part || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    System.Text.RegularExpressions.Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw new ArgumentException("Unsafe configuration path.");
        }
    }
}
