using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PalSaveChecker.Core;

var failures = new List<string>();
Run("Tools parent directory", TestGameDirectoryLocator);
Run("clean save", TestCleanSave);
Run("polluted player records repair with backup", TestPollutedPlayerRecordsRepair);
Run("dynamic script state boundary", TestDynamicScriptBoundary);
Run("GBK config and Chinese patch name", TestGbkConfig);
Run("invalid patch fails closed", TestInvalidPatchFailsClosed);
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
    string value = line[(line.IndexOf('=') + 1)..];
    int comment = value.IndexOf(';');
    return (comment >= 0 ? value[..comment] : value).Trim();
}

static string HashFile(string path)
{
    using FileStream stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
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
    if (actual is null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
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
    private Fixture(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public static Fixture Create(
        int scriptCount = 16,
        string patchName = "fixture-patch",
        Encoding? configEncoding = null)
    {
        string root = Path.Combine(Path.GetTempPath(), $"PalSaveCheckerTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "patches"));
        File.WriteAllText(
            Path.Combine(root, "config.ini"),
            $"[Patch]\r\nDefaultPatch={patchName}\r\n",
            configEncoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        byte[] objects = new byte[600 * 14];
        for (int objectId = 0; objectId < 600; objectId++)
        {
            int offset = objectId * 14;
            for (int field = 0; field < 7; field++)
            {
                WriteUInt16Local(objects, offset + field * 2,
                    IsScriptField(objectId, field) ? (ushort)1 : (ushort)(objectId * 7 + field));
            }
        }
        byte[] scripts = new byte[scriptCount * 8];
        byte[] sss = BuildMkf([], [], objects, [], scripts);
        string zipPath = Path.Combine(root, "patches", $"{patchName}.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry("SSS.MKF", CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            stream.Write(sss);
        }

        byte[] save = new byte[0x1620 + objects.Length + 64];
        Buffer.BlockCopy(objects, 0, save, 0x1620, objects.Length);
        for (int index = 0x1620 + objects.Length; index < save.Length; index++)
        {
            save[index] = (byte)(index * 13);
        }
        File.WriteAllBytes(Path.Combine(root, "1.RPG"), save);
        return new Fixture(root);
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
