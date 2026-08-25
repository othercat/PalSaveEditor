using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PalSaveChecker.Core;
using PalSaveEditor.Core;

var failures = new List<string>();
Run("Tools parent directory", TestGameDirectoryLocator);
Run("clean save", TestCleanSave);
Run("polluted player records repair with backup", TestPollutedPlayerRecordsRepair);
Run("polluted save repair without retained backup", TestPollutedSaveRepairWithoutBackup);
Run("dynamic script state boundary", TestDynamicScriptBoundary);
Run("GBK config and Chinese patch name", TestGbkConfig);
Run("invalid patch fails closed", TestInvalidPatchFailsClosed);
Run("Dream 2.2 visible active profile contract", TestDream220VisibleActiveProfile);
Run("active profile layout mismatch fails closed", TestActiveProfileLayoutMismatch);
Run("invalid active profile does not fall back", TestInvalidActiveProfileFailsClosed);
Run("running game repair policy", TestRunningGameRepairPolicy);
Run("optional Hunqian 1.67 runtime read-only check", TestOptionalHunqianRuntime);
Run("optional real runtime isolated repair", TestOptionalRealRuntime);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED ({failures.Count})");
    foreach (string failure in failures)
    {
        Console.Error.WriteLine(failure);
    }
    return 1;
}

Console.WriteLine("All PalSaveChecker tests passed.");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {name}: {ex.Message}");
    }
}

static void TestGameDirectoryLocator()
{
    string root = CreateTempDirectory();
    try
    {
        string tools = Directory.CreateDirectory(Path.Combine(root, "Tools")).FullName;
        Equal(Path.GetFullPath(root), GameDirectoryLocator.Resolve(tools), "Tools should resolve to parent");
        Equal(Path.GetFullPath(root), GameDirectoryLocator.Resolve(root), "non-Tools should remain unchanged");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void TestCleanSave()
{
    using Fixture fixture = Fixture.Create();
    SaveCheckReport report = new SaveCompatibilityService().Check(fixture.Root);
    Equal(SaveCheckStatus.Clean, report.Saves[0].Status, "1.RPG status");
    Equal(false, report.HasProblems, "clean report");
    Contains(report.ReferenceDescription, "fixture-patch.zip", "ZIP reference");
}

static void TestPollutedPlayerRecordsRepair()
{
    using Fixture fixture = Fixture.Create();
    string savePath = Path.Combine(fixture.Root, "1.RPG");
    byte[] original = File.ReadAllBytes(savePath);
    for (int index = 0; index < 748; index++)
    {
        original[0x1620 + index] = (byte)(index * 37 + 11);
    }
    byte[] originalTail = original.AsSpan(0x1620 + 748).ToArray();
    File.WriteAllBytes(savePath, original);

    var service = new SaveCompatibilityService();
    SaveCheckReport before = service.Check(fixture.Root);
    Equal(SaveCheckStatus.Polluted, before.Saves[0].Status, "pollution detected");
    Equal(true, before.Saves[0].Repairable, "pollution repairable");

    SaveRepairReport repair = service.Repair(fixture.Root);
    Equal(false, repair.HasFailures, "repair result");
    Equal(SaveCheckStatus.Clean, repair.After.Saves[0].Status, "post repair status");
    SaveRepairItem item = repair.Results.Single(result => result.FileName == "1.RPG");
    Equal(true, item.Success, "repair success");
    Equal(true, File.Exists(item.BackupPath), "backup exists");
    SequenceEqual(original, File.ReadAllBytes(item.BackupPath!), "backup bytes");
    SequenceEqual(originalTail, File.ReadAllBytes(savePath).AsSpan(0x1620 + 748).ToArray(), "tail preserved");
}

static void TestDynamicScriptBoundary()
{
    using Fixture fixture = Fixture.Create(scriptCount: 16);
    string savePath = Path.Combine(fixture.Root, "1.RPG");
    byte[] bytes = File.ReadAllBytes(savePath);
    int itemScript = 0x1620 + 0x0040 * 14 + 2 * 2;
    WriteUInt16(bytes, itemScript, 9);
    File.WriteAllBytes(savePath, bytes);

    var service = new SaveCompatibilityService();
    Equal(SaveCheckStatus.Clean, service.Check(fixture.Root).Saves[0].Status, "in-range runtime script state");
    WriteUInt16(bytes, itemScript, 16);
    File.WriteAllBytes(savePath, bytes);
    SaveCheckReport polluted = service.Check(fixture.Root);
    Equal(SaveCheckStatus.Polluted, polluted.Saves[0].Status, "out-of-range script");
    Equal(1, polluted.Saves[0].InvalidScriptCount, "out-of-range count");

    SaveRepairReport repaired = service.Repair(fixture.Root);
    Equal(false, repaired.HasFailures, "script repair result");
    byte[] after = File.ReadAllBytes(savePath);
    Equal((ushort)1, ReadUInt16(after, itemScript), "restored reference script");
}

static void TestPollutedSaveRepairWithoutBackup()
{
    using Fixture fixture = Fixture.Create();
    string savePath = Path.Combine(fixture.Root, "1.RPG");
    byte[] bytes = File.ReadAllBytes(savePath);
    bytes[0x1620] ^= 0x5A;
    File.WriteAllBytes(savePath, bytes);

    SaveRepairReport repair = new SaveCompatibilityService().Repair(fixture.Root, keepBackup: false);
    Equal(false, repair.HasFailures, "repair without retained backup");
    SaveRepairItem item = repair.Results.Single(result => result.FileName == "1.RPG");
    Equal(true, item.Success, "repair without backup success");
    Equal<string?>(null, item.BackupPath, "backup path omitted");
    Equal(0, Directory.EnumerateFiles(fixture.Root, "*.bak-*", SearchOption.TopDirectoryOnly).Count(),
        "no retained backup files");
    Equal(0, Directory.EnumerateFiles(fixture.Root, "*.rollback", SearchOption.TopDirectoryOnly).Count(),
        "no temporary rollback files");
    Equal(SaveCheckStatus.Clean, repair.After.Saves[0].Status, "post repair without backup status");
}

static void TestInvalidPatchFailsClosed()
{
    using Fixture fixture = Fixture.Create();
    string savePath = Path.Combine(fixture.Root, "1.RPG");
    byte[] before = File.ReadAllBytes(savePath);
    string zipPath = Path.Combine(fixture.Root, "patches", "fixture-patch.zip");
    File.WriteAllBytes(zipPath, [1, 2, 3, 4]);

    var service = new SaveCompatibilityService();
    SaveCheckReport check = service.Check(fixture.Root);
    Equal(true, check.ReferenceError is not null, "invalid reference reported");
    SaveRepairReport repair = service.Repair(fixture.Root);
    Equal(true, repair.HasFailures, "repair rejected");
    SequenceEqual(before, File.ReadAllBytes(savePath), "save untouched");
}

static void TestGbkConfig()
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    using Fixture fixture = Fixture.Create(
        patchName: "剧情补丁",
        configEncoding: Encoding.GetEncoding(936));
    SaveCheckReport report = new SaveCompatibilityService().Check(fixture.Root);
    Equal(SaveCheckStatus.Clean, report.Saves[0].Status, "GBK config status");
    Contains(report.ReferenceDescription, "剧情补丁.zip", "Chinese patch path");
}

static void TestActiveProfileLayoutMismatch()
{
    using Fixture fixture = Fixture.Create(eventObjectBytes: 170_624);
    fixture.EnableActiveProfile("pal98.hunqian167.easy", "1.0.0", "魂牵梦萦 1.67 简单 兼容配置档");
    string incompatiblePath = Path.Combine(fixture.Root, "2.RPG");
    byte[] compatible = File.ReadAllBytes(Path.Combine(fixture.Root, "1.RPG"));
    File.WriteAllBytes(incompatiblePath, compatible.AsSpan(0, 176_528).ToArray());
    byte[] before = File.ReadAllBytes(incompatiblePath);

    var service = new SaveCompatibilityService();
    SaveCheckReport report = service.Check(fixture.Root);
    Contains(report.ReferenceDescription, "pal98.hunqian167.easy@1.0.0", "active profile reference");
    Equal(SaveCheckStatus.Clean, report.Saves[0].Status, "profile-compatible save");
    Equal(SaveCheckStatus.Incompatible, report.Saves[1].Status, "classic-length save rejected");
    Equal(false, report.Saves[1].Repairable, "layout mismatch is not repairable");
    Contains(report.Saves[1].Error, "184,688", "expected profile save length");
    Contains(report.Saves[1].Error, "255", "missing event records");
    Equal(false, report.CanRepair, "layout mismatch does not enable repair");

    SaveRepairReport repair = service.Repair(fixture.Root);
    Equal(true, repair.HasFailures, "incompatible save remains a reported failure");
    SequenceEqual(before, File.ReadAllBytes(incompatiblePath), "incompatible save untouched");
}

static void TestDream220VisibleActiveProfile()
{
    using Fixture fixture = Fixture.Create(eventObjectBytes: 171_808, resourceObjectCount: 589);
    const string derivedProfileId = "pal98.dream220.compat.drawcard.16e143813df5";
    PalPublicToolProfile contract = PalPublicToolProfiles.Find(derivedProfileId, "1.0.18")
        ?? throw new InvalidOperationException("Dream DrawCard public profile family was not resolved.");
    fixture.EnableActiveProfile(
        contract.ProfileId,
        contract.ProfileVersion,
        contract.DisplayName,
        contract.WordDatByteLength,
        contract.ProfileId);
    string incompatiblePath = Path.Combine(fixture.Root, "2.RPG");
    byte[] compatible = File.ReadAllBytes(Path.Combine(fixture.Root, "1.RPG"));
    File.WriteAllBytes(incompatiblePath, compatible.AsSpan(0, SaveFormatDetector.KnownPal98Length).ToArray());

    SaveCheckReport report = new SaveCompatibilityService().Check(fixture.Root);
    Contains(report.ReferenceDescription, "梦幻2.2显血版 + 抽卡", "Dream visible display name");
    Contains(report.ReferenceDescription, derivedProfileId + "@1.0.18", "Dream visible profile identity");
    Equal(SaveCheckStatus.Clean, report.Saves[0].Status, "Dream visible compatible save");
    Equal(SaveCheckStatus.Incompatible, report.Saves[1].Status, "Dream visible rejects Classic save");
    Contains(report.Saves[1].Error, "185,872", "Dream visible expected length");
    Equal(false, report.Saves[1].Repairable, "Dream visible layout mismatch not repairable");
}

static void TestInvalidActiveProfileFailsClosed()
{
    using Fixture fixture = Fixture.Create(eventObjectBytes: 170_624);
    fixture.EnableActiveProfile("pal98.hunqian167.easy", "1.0.0", "魂牵梦萦 1.67 简单 兼容配置档");
    string descriptor = Path.Combine(
        fixture.Root,
        "palmod",
        "Profiles",
        "pal98.hunqian167.easy",
        "1.0.0",
        "manifest",
        "game-profile.json");
    File.AppendAllText(descriptor, " ");

    SaveCheckReport report = new SaveCompatibilityService().Check(fixture.Root);
    Equal(true, report.ReferenceError is not null, "tampered active profile rejected");
    Contains(report.ReferenceError, "SHA-256", "descriptor identity failure reported");
    Equal(SaveCheckStatus.Unreadable, report.Saves[0].Status, "no fallback to patch ZIP");
}

static void TestRunningGameRepairPolicy()
{
    RepairRunDecision stopped = RepairRunPolicy.Evaluate(isPalRunning: false);
    Equal(true, stopped.CanRepair, "stopped game repair allowed");
    Equal<string?>(null, stopped.Warning, "stopped game warning");

    RepairRunDecision running = RepairRunPolicy.Evaluate(isPalRunning: true);
    Equal(true, running.CanRepair, "running game repair allowed");
    Contains(running.Warning, "可以修复磁盘存档", "running game warning allows repair");
    Contains(running.Warning, "再次保存同一槽位", "running game overwrite warning");
}

static void TestOptionalHunqianRuntime()
{
    string? root = Environment.GetEnvironmentVariable("PAL98_HUNQIAN167_RUNTIME_GAME");
    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
    {
        Console.WriteLine("SKIP optional Hunqian runtime (PAL98_HUNQIAN167_RUNTIME_GAME not set)");
        return;
    }

    SaveCheckReport report = new SaveCompatibilityService().Check(root);
    Contains(report.ReferenceDescription, "pal98.hunqian167", "Hunqian active profile reference");
    foreach (SaveCheckItem item in report.Saves)
    {
        Console.WriteLine(
            $"HUNQIAN {item.FileName} status={item.Status} definitions={item.DefinitionMismatchCount} " +
            $"scripts={item.InvalidScriptCount} error={item.Error}");
    }

    Equal(false, report.Saves[0].Status == SaveCheckStatus.Incompatible, "Hunqian slot 1 layout");
    Equal(false, report.Saves[1].Status == SaveCheckStatus.Incompatible, "Hunqian slot 2 layout");
}

static void TestOptionalRealRuntime()
{
    string? source = Environment.GetEnvironmentVariable("PAL98_DREAM220_RUNTIME_GAME");
    if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
    {
        Console.WriteLine("SKIP optional real runtime (PAL98_DREAM220_RUNTIME_GAME not set)");
        return;
    }

    string originalSave = Path.Combine(source, "1.rpg");
    string originalConfig = Path.Combine(source, "config.ini");
    if (!File.Exists(originalSave) || !File.Exists(originalConfig))
    {
        Console.WriteLine("SKIP optional real runtime (config.ini or 1.rpg missing)");
        return;
    }

    string originalHash = HashFile(originalSave);
    string root = CreateTempDirectory();
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "patches"));
        File.Copy(originalConfig, Path.Combine(root, "config.ini"));
        string defaultPatch = ReadDefaultPatchName(originalConfig);
        File.Copy(Path.Combine(source, "patches", $"{defaultPatch}.zip"),
            Path.Combine(root, "patches", $"{defaultPatch}.zip"));
        File.Copy(originalSave, Path.Combine(root, "1.RPG"));

        var service = new SaveCompatibilityService();
        SaveCheckReport before = service.Check(root);
        Console.WriteLine(
            $"REAL 1.RPG status={before.Saves[0].Status} definitions={before.Saves[0].DefinitionMismatchCount} scripts={before.Saves[0].InvalidScriptCount}");
        if (before.Saves[0].Status == SaveCheckStatus.Polluted)
        {
            SaveRepairReport repaired = service.Repair(root);
            Equal(false, repaired.HasFailures, "real isolated repair");
            Equal(SaveCheckStatus.Clean, repaired.After.Saves[0].Status, "real isolated post-check");
            string repairedPath = Path.Combine(root, "1.RPG");
            Console.WriteLine($"REAL repaired candidate sha256={HashFile(repairedPath)}");
            Console.WriteLine(
                $"REAL changed ranges={DescribeChangedRanges(File.ReadAllBytes(originalSave), File.ReadAllBytes(repairedPath))}");
        }
        else
        {
            Equal(SaveCheckStatus.Clean, before.Saves[0].Status, "real isolated check");
        }
        Equal(originalHash, HashFile(originalSave), "runtime original untouched");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static string ReadDefaultPatchName(string configPath)
{
    string line = File.ReadLines(configPath)
        .First(value => value.TrimStart().StartsWith("DefaultPatch=", StringComparison.OrdinalIgnoreCase));
    string value = line.Substring(line.IndexOf('=') + 1);
    int comment = value.IndexOf(';');
    return (comment >= 0 ? value.Substring(0, comment) : value).Trim();
}

static string HashFile(string path)
{
    using FileStream stream = File.OpenRead(path);
    using SHA256 sha256 = SHA256.Create();
    return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
}

static string CreateTempDirectory()
{
    string path = Path.Combine(Path.GetTempPath(), $"PalSaveCheckerTests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
}

static void Contains(string? actual, string expected, string message)
{
    if (actual is null || actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
    {
        throw new InvalidOperationException($"{message}: '{expected}' not found in '{actual}'");
    }
}

static void SequenceEqual(byte[] expected, byte[] actual, string message)
{
    if (!expected.AsSpan().SequenceEqual(actual))
    {
        throw new InvalidOperationException($"{message}: bytes differ");
    }
}

static string DescribeChangedRanges(byte[] before, byte[] after)
{
    var ranges = new List<string>();
    int length = Math.Min(before.Length, after.Length);
    int index = 0;
    while (index < length)
    {
        if (before[index] == after[index])
        {
            index++;
            continue;
        }
        int start = index;
        while (index < length && before[index] != after[index])
        {
            index++;
        }
        ranges.Add($"{start}-{index - 1}");
    }
    if (before.Length != after.Length)
    {
        ranges.Add($"length:{before.Length}->{after.Length}");
    }
    return string.Join(",", ranges);
}

static ushort ReadUInt16(byte[] bytes, int offset) => (ushort)(bytes[offset] | bytes[offset + 1] << 8);

static void WriteUInt16(byte[] bytes, int offset, ushort value)
{
    bytes[offset] = (byte)value;
    bytes[offset + 1] = (byte)(value >> 8);
}

file sealed class Fixture : IDisposable
{
    private readonly byte[] _sssBytes;

    private Fixture(string root, byte[] sssBytes)
    {
        Root = root;
        _sssBytes = sssBytes;
    }

    public string Root { get; }

    public static Fixture Create(
        int scriptCount = 16,
        string patchName = "fixture-patch",
        Encoding? configEncoding = null,
        int eventObjectBytes = 64,
        int resourceObjectCount = 600)
    {
        string root = Path.Combine(Path.GetTempPath(), $"PalSaveCheckerTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "patches"));
        File.WriteAllText(
            Path.Combine(root, "config.ini"),
            $"[Patch]\r\nDefaultPatch={patchName}\r\n",
            configEncoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        byte[] objects = new byte[resourceObjectCount * 14];
        for (int objectId = 0; objectId < resourceObjectCount; objectId++)
        {
            int offset = objectId * 14;
            for (int field = 0; field < 7; field++)
            {
                WriteUInt16Local(objects, offset + field * 2,
                    IsScriptField(objectId, field) ? (ushort)1 : (ushort)(objectId * 7 + field));
            }
        }
        if (eventObjectBytes <= 0 || eventObjectBytes % 32 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eventObjectBytes));
        }
        byte[] events = new byte[eventObjectBytes];
        byte[] scripts = new byte[scriptCount * 8];
        byte[] sss = BuildMkf(events, [], objects, [], scripts);
        string zipPath = Path.Combine(root, "patches", $"{patchName}.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry("SSS.MKF", CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            stream.Write(sss, 0, sss.Length);
        }

        byte[] save = new byte[14_064 + eventObjectBytes];
        Buffer.BlockCopy(objects, 0, save, 0x1620, objects.Length);
        for (int index = 0x1620 + objects.Length; index < save.Length; index++)
        {
            save[index] = (byte)(index * 13);
        }
        File.WriteAllBytes(Path.Combine(root, "1.RPG"), save);
        return new Fixture(root, sss);
    }

    public void EnableActiveProfile(
        string profileId,
        string profileVersion,
        string displayName,
        int wordDatByteLength = 5_750,
        string? saveNamespace = null)
    {
        string staged = Path.Combine(Root, "palmod", "Profiles", profileId, profileVersion);
        string resources = Directory.CreateDirectory(Path.Combine(staged, "resources")).FullName;
        string manifest = Directory.CreateDirectory(Path.Combine(staged, "manifest")).FullName;
        string sssPath = Path.Combine(resources, "SSS.MKF");
        string wordPath = Path.Combine(resources, "WORD.DAT");
        File.WriteAllBytes(sssPath, _sssBytes);
        File.WriteAllBytes(wordPath, new byte[wordDatByteLength]);

        string descriptor =
            "{" +
            "\"schema\":\"PAL98.GameProfile.v1\"," +
            $"\"profile_id\":\"{profileId}\"," +
            $"\"profile_version\":\"{profileVersion}\"," +
            $"\"display_name\":\"{displayName}\"," +
            (saveNamespace is null ? string.Empty : $"\"save_namespace\":\"{saveNamespace}\",") +
            "\"resource_set\":[" +
            ResourceJson("SSS.MKF", "resources/SSS.MKF", sssPath) + "," +
            ResourceJson("WORD.DAT", "resources/WORD.DAT", wordPath) +
            "]}";
        string descriptorPath = Path.Combine(manifest, "game-profile.json");
        File.WriteAllText(descriptorPath, descriptor, new UTF8Encoding(false));
        string descriptorHash = HashFileLocal(descriptorPath).ToLowerInvariant();

        string profiles = Directory.CreateDirectory(Path.Combine(Root, "palmod", "Profiles")).FullName;
        string pointer =
            "{" +
            "\"schema\":\"PAL98.EffectiveGameProfilePointer.v1\"," +
            $"\"profile_id\":\"{profileId}\"," +
            $"\"profile_version\":\"{profileVersion}\"," +
            $"\"descriptor_sha256\":\"{descriptorHash}\"," +
            $"\"staging_relative_path\":\"{profileId}/{profileVersion}\"" +
            "}";
        File.WriteAllText(Path.Combine(profiles, "current.json"), pointer, new UTF8Encoding(false));
    }

    private static string ResourceJson(string kind, string relativePath, string path) =>
        "{" +
        $"\"kind\":\"{kind}\"," +
        $"\"relative_path\":\"{relativePath}\"," +
        $"\"sha256\":\"{HashFileLocal(path).ToLowerInvariant()}\"," +
        $"\"size_bytes\":{new FileInfo(path).Length}" +
        "}";

    private static string HashFileLocal(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }

    private static byte[] BuildMkf(params byte[][] chunks)
    {
        int headerLength = (chunks.Length + 1) * sizeof(uint);
        int totalLength = headerLength + chunks.Sum(chunk => chunk.Length);
        byte[] result = new byte[totalLength];
        int offset = headerLength;
        for (int index = 0; index < chunks.Length; index++)
        {
            WriteUInt32(result, index * 4, (uint)offset);
            Buffer.BlockCopy(chunks[index], 0, result, offset, chunks[index].Length);
            offset += chunks[index].Length;
        }
        WriteUInt32(result, chunks.Length * 4, (uint)offset);
        return result;
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt16Local(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static bool IsScriptField(int objectId, int field) => objectId switch
    {
        < 0x003D => field is 2 or 3,
        < 0x0127 => field is 2 or 3 or 4 or 5,
        < 0x018E => field is 2 or 3 or 4,
        < 0x0227 => field is 2 or 3 or 4,
        _ => field is 2 or 4,
    };
}
