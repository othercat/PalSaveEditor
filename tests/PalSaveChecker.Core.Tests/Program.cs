using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PalSaveChecker.Core;
using PalSaveEditor.Core;

var failures = new List<string>();
Run("Tools deployment layouts", TestGameDirectoryLocator);
Run("clean save", TestCleanSave);
Run("redundant stale native sidecar is ignored", TestRedundantStaleNativeSidecar);
Run("polluted player records repair with backup", TestPollutedPlayerRecordsRepair);
Run("polluted save repair without retained backup", TestPollutedSaveRepairWithoutBackup);
Run("dynamic script state boundary", TestDynamicScriptBoundary);
Run("empty contact trigger repair", TestEmptyContactTriggerRepair);
Run("extended magic sidecar repair preserves recoverable slots", TestExtendedMagicSidecarRepair);
Run("malformed extended magic sidecar falls back safely", TestMalformedExtendedMagicSidecarRepair);
Run("active profile stale learned magic ids are migrated", TestActiveProfileStaleRandomMagicRepair);
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
        string checker = Directory.CreateDirectory(Path.Combine(tools, "PalSaveChecker")).FullName;
        Equal(Path.GetFullPath(root), GameDirectoryLocator.Resolve(tools), "Tools should resolve to parent");
        Equal(Path.GetFullPath(root), GameDirectoryLocator.Resolve(checker), "Tools child should resolve to game root");
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

static void TestRedundantStaleNativeSidecar()
{
    using Fixture fixture = Fixture.Create();
    string savePath = Path.Combine(fixture.Root, "1.RPG");
    byte[] bytes = File.ReadAllBytes(savePath);
    WriteUInt16(bytes, PalSaveLayout.MagicOffset(0, 0), 321);
    File.WriteAllBytes(savePath, bytes);

    var state = ExtendedRoleMagicState.FromPhysicalPage0(bytes);
    Equal(false, state.HasExtendedPayload,
        "native 32-slot state has no extended payload");
    ExtendedRoleMagicSidecar.WriteAtomically(savePath, bytes, state);
    string sidecarPath = ExtendedRoleMagicSidecar.GetPath(savePath);
    string originalSidecarHash = HashFile(sidecarPath);

    bytes[PalSaveLayout.CashOffset] ^= 1;
    File.WriteAllBytes(savePath, bytes);
    var service = new SaveCompatibilityService();
    SaveCheckReport report = service.Check(fixture.Root);
    Equal(SaveCheckStatus.Clean, report.Saves[0].Status,
        "stale redundant sidecar does not pollute a native save");
    Equal(false, report.Saves[0].ExtendedMagicSidecarIssue,
        "stale redundant sidecar is not classified as a repair issue");
    Equal(false, report.HasProblems,
        "redundant stale sidecar does not require repair");

    SaveRepairReport noOp = service.Repair(fixture.Root, keepBackup: false);
    Equal(0, noOp.Results.Count,
        "repair leaves an already-compatible native save alone");
    Equal(originalSidecarHash, HashFile(sidecarPath),
        "checker does not rewrite the harmless stale sidecar");
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

static void TestEmptyContactTriggerRepair()
{
    using Fixture fixture = Fixture.Create(scriptCount: 16, eventObjectBytes: 96);
    string savePath = Path.Combine(fixture.Root, "1.RPG");
    byte[] bytes = File.ReadAllBytes(savePath);
    int firstEvent = PalSaveLayout.WinEventObjectOffset;
    int secondEvent = firstEvent + PalSaveLayout.EventObjectRecordSize;
    int thirdEvent = secondEvent + PalSaveLayout.EventObjectRecordSize;

    WriteUInt16(bytes, firstEvent + 8, 0);
    WriteUInt16(bytes, firstEvent + 12, 2);
    WriteUInt16(bytes, firstEvent + 14, 6);
    WriteUInt16(bytes, secondEvent + 8, 1);
    WriteUInt16(bytes, secondEvent + 12, 2);
    WriteUInt16(bytes, secondEvent + 14, 6);
    WriteUInt16(bytes, thirdEvent + 8, 3);
    WriteUInt16(bytes, thirdEvent + 12, 2);
    WriteUInt16(bytes, thirdEvent + 14, 4);
    File.WriteAllBytes(savePath, bytes);

    var service = new SaveCompatibilityService();
    SaveCheckReport before = service.Check(fixture.Root);
    Equal(SaveCheckStatus.Polluted, before.Saves[0].Status, "empty contact trigger detected");
    Equal(2, before.Saves[0].EmptyContactTriggerCount, "empty contact trigger count");

    SaveRepairReport repair = service.Repair(fixture.Root);
    Equal(false, repair.HasFailures, "empty contact trigger repair result");
    byte[] after = File.ReadAllBytes(savePath);
    Equal((ushort)0, ReadUInt16(after, firstEvent + 14), "empty trigger mode disabled");
    Equal((ushort)0, ReadUInt16(after, firstEvent + 8), "empty trigger script preserved");
    Equal((ushort)6, ReadUInt16(after, secondEvent + 14), "non-empty trigger mode preserved");
    Equal((ushort)0, ReadUInt16(after, thirdEvent + 14), "all-zero indexed trigger mode disabled");
    Equal((ushort)3, ReadUInt16(after, thirdEvent + 8), "all-zero indexed trigger script preserved");

    byte[] expected = (byte[])bytes.Clone();
    WriteUInt16(expected, firstEvent + 14, 0);
    WriteUInt16(expected, thirdEvent + 14, 0);
    SequenceEqual(expected, after, "only empty event trigger mode changed");
}

static void TestExtendedMagicSidecarRepair()
{
    using Fixture fixture = Fixture.Create();
    string savePath = Path.Combine(fixture.Root, "1.RPG");
    byte[] bytes = File.ReadAllBytes(savePath);
    var state = ExtendedRoleMagicState.FromPhysicalPage0(bytes);
    for (ushort magic = 100; magic < 140; magic++)
    {
        state.Roles[0][magic - 100] = magic;
    }
    state.Roles[5][0] = 500;
    state.HasRandomLevelProgress = true;
    state.RandomLevelAppliedThroughLevel[0] = 99;
    state.RandomLevelAppliedThroughLevel[5] = 60;
    state.ProjectActivePage(bytes);
    File.WriteAllBytes(savePath, bytes);
    ExtendedRoleMagicSidecar.WriteAtomically(savePath, bytes, state);

    bytes[0x1620] ^= 0x5A;
    File.WriteAllBytes(savePath, bytes);

    var service = new SaveCompatibilityService();
    SaveCheckReport before = service.Check(fixture.Root);
    Equal(SaveCheckStatus.Polluted, before.Saves[0].Status, "stale binding is detected");
    Equal(true, before.Saves[0].ExtendedMagicSidecarIssue, "sidecar issue is classified");

    SaveRepairReport repair = service.Repair(fixture.Root, keepBackup: false);
    Equal(false, repair.HasFailures, "combined RPG and sidecar repair succeeds");
    byte[] repaired = File.ReadAllBytes(savePath);
    Equal(true, ExtendedRoleMagicSidecar.TryLoad(
        savePath, repaired, out var restored, out _), "sidecar is strictly rebound");
    Equal((ushort)139, restored.Roles[0][39], "recoverable slot beyond 32 is preserved");
    Equal((ushort)500, restored.Roles[5][0], "sixth-role slot is preserved");
    Equal(true, restored.HasRandomLevelProgress,
        "random level progress survives sidecar rebinding repair");
    Equal((ushort)99, restored.RandomLevelAppliedThroughLevel[0],
        "role zero random level progress survives repair");
    Equal((ushort)60, restored.RandomLevelAppliedThroughLevel[5],
        "sixth-role random level progress survives repair");
}

static void TestMalformedExtendedMagicSidecarRepair()
{
    using Fixture fixture = Fixture.Create();
    string savePath = Path.Combine(fixture.Root, "1.RPG");
    byte[] bytes = File.ReadAllBytes(savePath);
    WriteUInt16(bytes, PalSaveLayout.MagicOffset(0, 0), 321);
    File.WriteAllBytes(savePath, bytes);
    File.WriteAllText(ExtendedRoleMagicSidecar.GetPath(savePath), "{}");

    var service = new SaveCompatibilityService();
    SaveCheckReport before = service.Check(fixture.Root);
    Equal(SaveCheckStatus.Polluted, before.Saves[0].Status, "malformed sidecar is detected");
    Equal(true, before.Saves[0].ExtendedMagicSidecarIssue, "malformed sidecar is repairable");

    SaveRepairReport repair = service.Repair(fixture.Root, keepBackup: false);
    Equal(false, repair.HasFailures, "malformed sidecar repair succeeds");
    byte[] repaired = File.ReadAllBytes(savePath);
    Equal(true, ExtendedRoleMagicSidecar.TryLoad(
        savePath, repaired, out var restored, out _), "replacement sidecar is valid");
    Equal((ushort)321, restored.Roles[0][0], "physical page zero is retained");
    Equal((ushort)0, restored.Roles[0][32], "unrecoverable extra slots are cleared");
}

static void TestActiveProfileStaleRandomMagicRepair()
{
    using Fixture fixture = Fixture.Create(resourceObjectCount: 600);
    const string profileId = "pal98.test.skill-composition";
    const string profileVersion = "1.0.2";
    string contentCatalog =
        "{" +
        "\"schema\":\"PAL98.ContentCatalog.v1\"," +
        "\"catalog_id\":\"pal98.test.catalog\"," +
        "\"catalog_version\":\"1.0.0\"," +
        $"\"profile_id\":\"{profileId}\"," +
        $"\"profile_version\":\"{profileVersion}\"," +
        $"\"save_namespace\":\"{profileId}\"," +
        "\"magics\":[{" +
        "\"logical_id\":\"skill.pal98.classic.test\"," +
        "\"object_id\":600," +
        "\"display_name\":\"测试术\"," +
        "\"status\":\"pal98-static-verified\"," +
        "\"learnable\":true," +
        "\"randomizable\":true," +
        "\"grantable\":true," +
        "\"exclusions\":[]," +
        "\"source_mappings\":[{" +
        "\"source_set_id\":\"skill-source.pal98-classic\"," +
        "\"object_id\":600}]}]}";
    fixture.EnableActiveProfile(
        profileId,
        profileVersion,
        "测试技能组合包",
        wordDatByteLength: 6_000,
        saveNamespace: profileId,
        skillObjects: Fixture.BuildSkillObjectsPack(600, 2),
        contentCatalogJson: contentCatalog);
    string historicalDirectory = Directory.CreateDirectory(Path.Combine(
        fixture.Root,
        "palmod",
        "Profiles",
        profileId,
        "1.0.1",
        "palmod",
        "profile")).FullName;
    string historicalCatalog = contentCatalog
        .Replace($"\"profile_version\":\"{profileVersion}\"", "\"profile_version\":\"1.0.1\"")
        .Replace("\"object_id\":600,", "\"object_id\":688,")
        .Replace("\"object_id\":600}]", "\"object_id\":688}]");
    File.WriteAllText(
        Path.Combine(historicalDirectory, "content-catalog.json"),
        historicalCatalog,
        new UTF8Encoding(false));

    string savePath = Path.Combine(fixture.Root, "1.RPG");
    byte[] bytes = File.ReadAllBytes(savePath);
    var state = ExtendedRoleMagicState.FromPhysicalPage0(bytes);
    state.Roles[0][0] = 688;
    state.Roles[0][1] = 600;
    state.HasRandomLevelProgress = true;
    state.RandomLevelAppliedThroughLevel[0] = 99;
    state.ProjectActivePage(bytes);
    File.WriteAllBytes(savePath, bytes);
    ExtendedRoleMagicSidecar.WriteAtomically(savePath, bytes, state);

    var service = new SaveCompatibilityService();
    SaveCheckReport before = service.Check(fixture.Root);
    Equal(SaveCheckStatus.Polluted, before.Saves[0].Status,
        "stale active-profile object id is detected");
    Equal(true, before.Saves[0].LearnedMagicProfileIssue,
        "stale active-profile learned magic is classified");
    Contains(before.Saves[0].LearnedMagicProfileError, "1 个可迁移",
        "migratable object count is reported");

    SaveRepairReport repair = service.Repair(fixture.Root, keepBackup: false);
    Equal(false, repair.HasFailures, "stale random sidecar repair succeeds");
    byte[] repaired = File.ReadAllBytes(savePath);
    Equal(true, ExtendedRoleMagicSidecar.TryLoad(
        savePath, repaired, out var restored, out _),
        "reset random sidecar is rebound to repaired RPG");
    Equal(false, restored.HasRandomLevelProgress,
        "stale random level progress is reset for current-profile refill");
    Equal((ushort)600, restored.Roles[0][0],
        "stale random skill is mapped by stable logical id");
    Equal((ushort)600, ReadUInt16(repaired, PalSaveLayout.MagicOffset(0, 0)),
        "RPG physical magic page receives the mapped object id");
    Equal((ushort)0, restored.Roles[0][1],
        "migration removes the duplicate current object id");
    Equal(SaveCheckStatus.Clean, repair.After.Saves[0].Status,
        "repaired sidecar passes current active-profile catalog check");
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
            $"scripts={item.InvalidScriptCount} emptyContact={item.EmptyContactTriggerCount} error={item.Error}");
    }

    Equal(true, report.Saves.Any(item =>
        item.Status is SaveCheckStatus.Clean or SaveCheckStatus.Polluted),
        "at least one save matches the active Hunqian layout");
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
        if (scriptCount > 1)
        {
            WriteUInt16Local(scripts, 8, 1);
        }
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
        Buffer.BlockCopy(events, 0, save, PalSaveLayout.WinEventObjectOffset, events.Length);
        File.WriteAllBytes(Path.Combine(root, "1.RPG"), save);
        return new Fixture(root, sss);
    }

    public void EnableActiveProfile(
        string profileId,
        string profileVersion,
        string displayName,
        int wordDatByteLength = 5_750,
        string? saveNamespace = null,
        byte[]? skillObjects = null,
        string? contentCatalogJson = null)
    {
        string staged = Path.Combine(Root, "palmod", "Profiles", profileId, profileVersion);
        string resources = Directory.CreateDirectory(Path.Combine(staged, "resources")).FullName;
        string manifest = Directory.CreateDirectory(Path.Combine(staged, "manifest")).FullName;
        string sssPath = Path.Combine(resources, "SSS.MKF");
        string wordPath = Path.Combine(resources, "WORD.DAT");
        File.WriteAllBytes(sssPath, _sssBytes);
        File.WriteAllBytes(wordPath, new byte[wordDatByteLength]);

        var resourceEntries = new List<string>
        {
            ResourceJson("SSS.MKF", "resources/SSS.MKF", sssPath),
            ResourceJson("WORD.DAT", "resources/WORD.DAT", wordPath),
        };
        if (skillObjects is not null)
        {
            string skillObjectsPath = Path.Combine(resources, "SKILL.OBJECTS");
            File.WriteAllBytes(skillObjectsPath, skillObjects);
            resourceEntries.Add(ResourceJson(
                "SKILL.OBJECTS", "resources/SKILL.OBJECTS", skillObjectsPath));
        }
        if (contentCatalogJson is not null)
        {
            string contentCatalogPath = Path.Combine(resources, "CONTENT.CATALOG");
            File.WriteAllText(
                contentCatalogPath, contentCatalogJson, new UTF8Encoding(false));
            resourceEntries.Add(ResourceJson(
                "CONTENT.CATALOG", "resources/CONTENT.CATALOG", contentCatalogPath));
        }

        string descriptor =
            "{" +
            "\"schema\":\"PAL98.GameProfile.v1\"," +
            $"\"profile_id\":\"{profileId}\"," +
            $"\"profile_version\":\"{profileVersion}\"," +
            $"\"display_name\":\"{displayName}\"," +
            (saveNamespace is null ? string.Empty : $"\"save_namespace\":\"{saveNamespace}\",") +
            "\"resource_set\":[" + string.Join(",", resourceEntries) + "]}";
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

    public static byte[] BuildSkillObjectsPack(int firstObjectId, int recordCount)
    {
        const int headerBytes = 20;
        const int objectBytes = 14;
        const int wordBytes = 10;
        byte[] result = new byte[headerBytes + recordCount * (objectBytes + wordBytes)];
        result[0] = (byte)'P';
        result[1] = (byte)'S';
        result[2] = (byte)'O';
        result[3] = (byte)'1';
        WriteUInt16Local(result, 4, 1);
        WriteUInt16Local(result, 6, objectBytes);
        WriteUInt16Local(result, 8, wordBytes);
        WriteUInt32(result, 12, checked((uint)firstObjectId));
        WriteUInt32(result, 16, checked((uint)recordCount));
        for (int index = 0; index < recordCount; index++)
        {
            int offset = headerBytes + index * (objectBytes + wordBytes);
            result[offset] = 1;
            result[offset + objectBytes] = (byte)'X';
        }
        return result;
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
