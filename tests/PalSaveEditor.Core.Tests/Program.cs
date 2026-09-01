using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PalSaveEditor.Core;

var tests = new (string Name, Action Run)[]
{
    ("layout constants", TestLayoutConstants),
    ("known format detection", TestKnownFormatDetection),
    ("synthetic field round trip", TestSyntheticFieldRoundTrip),
    ("party and follower shared queue round trip", TestPartyAndFollowers),
    ("inventory compression and duplicate guard", TestInventory),
    ("999-slot magic sidecar round trip and binding guard", TestExtendedMagicSidecar),
    ("native 32-slot stale sidecar is silent and not rewritten", TestNativeStaleSidecar),
    ("independent skill registry exposes all active-resource skills", TestSkillRegistry),
    ("active content catalog exposes composed extended skills", TestComposedContentCatalog),
    ("optional real composed skill profile catalog", TestOptionalComposedSkillProfile),
    ("Dream 2.2 visible active profile contract", TestDream220VisibleActiveProfile),
    ("Hunqian 1.67 active profile layout guard", TestHunqianActiveProfileLayout),
    ("optional Hunqian 1.67 runtime read-only load", TestOptionalHunqianRuntime),
    ("real sample detection and resource catalogs", TestRealSamples),
    ("safe write creates exact backup", TestSafeWrite),
    ("safe write without retained backup", TestSafeWriteWithoutBackup),
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failures.Add(name);
        Console.Error.WriteLine($"FAIL  {name}: {exception}");
    }
}

if (failures.Count != 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) failed: {string.Join(", ", failures)}");
    return 1;
}

Console.WriteLine($"All {tests.Length} tests passed.");
return 0;

static void TestLayoutConstants()
{
    Equal(12_864, PalSaveLayout.DosEventObjectOffset, "DOS fixed prefix");
    Equal(14_064, PalSaveLayout.WinEventObjectOffset, "Win fixed prefix");
    Equal(1_728, PalSaveLayout.InventoryOffset, "inventory offset");
    Equal(3_264, PalSaveLayout.SceneOffset, "scene offset");
    Equal(580, PalSaveLayout.RoleFieldOffset(RoleField.Level, 0), "role level offset");
    Equal(784, PalSaveLayout.RoleFieldOffset(RoleField.WindResistance, 0), "role wind resistance offset");
    Equal(796, PalSaveLayout.RoleFieldOffset(RoleField.ThunderResistance, 0), "role thunder resistance offset");
    Equal(808, PalSaveLayout.RoleFieldOffset(RoleField.WaterResistance, 0), "role water resistance offset");
    Equal(820, PalSaveLayout.RoleFieldOffset(RoleField.FireResistance, 0), "role fire resistance offset");
    Equal(832, PalSaveLayout.RoleFieldOffset(RoleField.EarthResistance, 0), "role earth resistance offset");
    Equal(1_276, PalSaveLayout.RoleFieldOffset(RoleField.WalkFrames, 0), "role walk-frame upper bound offset");
    Equal(1_278, PalSaveLayout.RoleFieldOffset(RoleField.WalkFrames, 1), "second role walk-frame upper bound offset");
    Equal(892, PalSaveLayout.MagicOffset(0, 0), "magic offset");
}

static void TestPartyAndFollowers()
{
    var directory = CreateTestDirectory();
    try
    {
        var path = Path.Combine(directory, "party-followers.rpg");
        var bytes = new byte[PalSaveLayout.WinEventObjectOffset + 32];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(PalSaveLayout.PartyMaxIndexOffset), 1); // two party members
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(PalSaveLayout.FollowerOffset), 2);
        WriteQueueRecord(bytes, 0, 0, 0x10);
        WriteQueueRecord(bytes, 1, 2, 0x20);
        WriteQueueRecord(bytes, 2, 12, 0xA0);
        WriteQueueRecord(bytes, 3, 81, 0xB0);
        File.WriteAllBytes(path, bytes);

        var document = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        Equal(2, document.PartyCount, "initial party count");
        Equal(2, document.FollowerCount, "initial follower count");
        Equal((ushort)12, document.GetFollowers()[0].SpriteId, "Tian Gui Huang MGO id");
        Equal((ushort)81, document.GetFollowers()[1].SpriteId, "Yun Yi MGO id");

        document.SetParty([5, 1, 0]);
        Equal(3, document.PartyCount, "expanded party count");
        Equal((ushort)12, document.GetFollowers()[0].SpriteId, "first follower relocated after party resize");
        Equal((ushort)81, document.GetFollowers()[1].SpriteId, "second follower relocated after party resize");
        AssertQueueMarker(document.ToArray(), 3, 0xA0, "first follower record preserved");
        AssertQueueMarker(document.ToArray(), 4, 0xB0, "second follower record preserved");

        var beforeRejectedChange = document.ToArray();
        Throws<InvalidDataException>(() => document.SetParty([0, 1, 2, 3]), "reject party plus follower overflow");
        SequenceEqual(beforeRejectedChange, document.ToArray(), "overflow rejection is atomic");

        document.SetFollowers([81, 12]);
        Equal((ushort)81, document.GetFollowers()[0].SpriteId, "moved follower one");
        Equal((ushort)12, document.GetFollowers()[1].SpriteId, "moved follower two");
        AssertQueueMarker(document.ToArray(), 3, 0xB0, "second follower full record moved with sprite");
        AssertQueueMarker(document.ToArray(), 4, 0xA0, "first follower full record moved with sprite");
        Throws<ArgumentOutOfRangeException>(() => document.SetFollowers([0]), "reject zero follower sprite");
        Throws<ArgumentOutOfRangeException>(() => document.SetFollowers([12, 81, 9]), "reject more than two followers");

        document.SetRoleField(1, RoleField.BattleSprite, 5);
        document.SetRoleField(1, RoleField.MapSprite, 512);
        document.SetRoleField(1, RoleField.WalkFrames, 3);
        document.Save(createBackup: false);
        var reloaded = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        Equal((ushort)5, reloaded.GetRoleField(1, RoleField.BattleSprite), "snake Linger battle sprite round trip");
        Equal((ushort)512, reloaded.GetRoleField(1, RoleField.MapSprite), "snake Linger map sprite round trip");
        Equal((ushort)3, reloaded.GetRoleField(1, RoleField.WalkFrames), "snake Linger walk frames round trip");
        Equal((ushort)81, reloaded.GetFollowers()[0].SpriteId, "follower round trip");
        Equal((ushort)12, reloaded.GetFollowers()[1].SpriteId, "second follower round trip");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestKnownFormatDetection()
{
    Equal(SaveFormat.PalDos, SaveFormatDetector.Detect(183_488).Format, "DOS detection");
    Equal(SaveFormat.PalWin95, SaveFormatDetector.Detect(176_528).Format, "Win detection");
    Equal(SaveFormat.Dream220Dos, SaveFormatDetector.Detect(184_672).Format, "Dream detection");
    Equal(SaveFormat.Dream220Win95, SaveFormatDetector.Detect(185_872).Format, "PALDLL Dream detection");

    Throws<InvalidDataException>(() => SaveFormatDetector.Detect(14_065), "invalid event boundary");

    var directory = CreateTestDirectory();
    try
    {
        var path = Path.Combine(directory, "dos.rpg");
        File.WriteAllBytes(path, new byte[SaveFormatDetector.KnownPalDosLength]);
        var document = PalSaveDocument.Load(path, SaveFormat.PalDos);
        Throws<InvalidDataException>(() => document.SetFormat(SaveFormat.PalWin95), "reject wrong manual format");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestExtendedMagicSidecar()
{
    var directory = CreateTestDirectory();
    try
    {
        var path = Path.Combine(directory, "1.RPG");
        File.WriteAllBytes(path, new byte[SaveFormatDetector.KnownPal98Length]);

        var document = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        Equal(999, document.MagicCapacity, "extended per-role capacity");
        for (ushort magic = 100; magic < 140; magic++)
        {
            document.AddMagic(0, magic);
        }
        document.AddMagic(5, 500);
        var progressState = ExtendedRoleMagicState.FromPhysicalPage0(
            File.ReadAllBytes(path));
        progressState.HasRandomLevelProgress = true;
        progressState.RandomLevelAppliedThroughLevel[0] = 99;
        progressState.RandomLevelAppliedThroughLevel[5] = 60;
        ExtendedRoleMagicSidecar.WriteAtomically(
            path, File.ReadAllBytes(path), progressState);
        document = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        for (ushort magic = 100; magic < 140; magic++)
        {
            document.AddMagic(0, magic);
        }
        document.AddMagic(5, 500);
        Equal(40, document.GetMagics(0).Count, "role zero keeps more than 32 magics");
        Equal((ushort)500, document.GetMagics(5).Single().MagicId, "sixth role participates");
        document.Save(createBackup: false);

        var sidecarPath = ExtendedRoleMagicSidecar.GetPath(path);
        True(File.Exists(sidecarPath), "numbered save writes extended magic sidecar");
        var reloaded = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        True(reloaded.HasExtendedMagicSidecar, "valid sidecar is loaded");
        Equal(40, reloaded.GetMagics(0).Count, "all extended magics survive reload");
        Equal((ushort)139, reloaded.GetMagics(0).Last().MagicId, "slot 40 survives reload");
        Equal((ushort)500, reloaded.GetMagics(5).Single().MagicId, "sixth-role magic survives reload");
        True(ExtendedRoleMagicSidecar.TryLoad(
            path, File.ReadAllBytes(path), out var progressReloaded, out _),
            "progress-bearing sidecar reloads");
        True(progressReloaded.HasRandomLevelProgress,
            "random level progress remains present after editor save");
        Equal((ushort)99, progressReloaded.RandomLevelAppliedThroughLevel[0],
            "role zero random level progress survives editor save");
        Equal((ushort)60, progressReloaded.RandomLevelAppliedThroughLevel[5],
            "sixth-role random level progress survives editor save");

        var sparseSidecar = JsonNode.Parse(File.ReadAllText(sidecarPath))!.AsObject();
        sparseSidecar["roles"]!.AsArray()[0]!.AsArray()[0] = 0;
        File.WriteAllText(sidecarPath, sparseSidecar.ToJsonString());
        var sparse = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        True(sparse.HasExtendedMagicSidecar, "sparse runtime sidecar remains valid");
        Equal(39, sparse.GetMagics(0).Count, "sparse sidecar exposes one hole");
        sparse.AddMagic(0, 105);
        Equal(39, sparse.GetMagics(0).Count,
            "adding an existing magic after an earlier hole is a no-op");
        Equal(1, sparse.GetMagics(0).Count(magic => magic.MagicId == 105),
            "sparse sidecar cannot create a same-source duplicate");

        var tampered = File.ReadAllBytes(path);
        tampered[PalSaveLayout.CashOffset] ^= 1;
        File.WriteAllBytes(path, tampered);
        var guarded = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        True(!guarded.HasExtendedMagicSidecar, "RPG hash mismatch rejects stale sidecar");
        True(!string.IsNullOrWhiteSpace(guarded.ExtendedMagicSidecarWarning), "rejection is visible");
        Equal(32, guarded.GetMagics(0).Count, "invalid sidecar falls back to physical page zero");

        byte[] guardedRpg = File.ReadAllBytes(path);
        string guardedSidecarHash = HashFile(sidecarPath);
        guarded.AddMagic(0, 777);
        Throws<InvalidOperationException>(
            () => guarded.Save(createBackup: false),
            "editor refuses to overwrite a recoverable extended payload");
        SequenceEqual(guardedRpg, File.ReadAllBytes(path),
            "rejected save leaves RPG untouched");
        Equal(guardedSidecarHash, HashFile(sidecarPath),
            "rejected save leaves recoverable sidecar untouched");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestNativeStaleSidecar()
{
    var directory = CreateTestDirectory();
    try
    {
        var path = Path.Combine(directory, "1.RPG");
        var bytes = new byte[SaveFormatDetector.KnownPal98Length];
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(PalSaveLayout.MagicOffset(0, 0)), 296);
        File.WriteAllBytes(path, bytes);

        var state = ExtendedRoleMagicState.FromPhysicalPage0(bytes);
        True(!state.HasExtendedPayload,
            "native page-only state is not an extended payload");
        ExtendedRoleMagicSidecar.WriteAtomically(path, bytes, state);
        string sidecarPath = ExtendedRoleMagicSidecar.GetPath(path);
        string originalSidecarHash = HashFile(sidecarPath);

        bytes[PalSaveLayout.CashOffset] ^= 1;
        File.WriteAllBytes(path, bytes);
        var document = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        True(!document.HasExtendedMagicSidecar,
            "stale native-only sidecar is not treated as active extension data");
        True(document.ExtendedMagicSidecarWarning is null,
            "stale native-only sidecar is ignored silently");
        Equal((ushort)296, document.GetMagics(0).Single().MagicId,
            "physical native slots remain authoritative");

        document.Save(createBackup: false);
        Equal(originalSidecarHash, HashFile(sidecarPath),
            "ordinary native save does not rewrite the stale sidecar");
        var reloaded = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        True(reloaded.ExtendedMagicSidecarWarning is null,
            "unchanged stale native-only sidecar remains harmless after save");

        var secondPath = Path.Combine(directory, "2.RPG");
        File.WriteAllBytes(secondPath, new byte[SaveFormatDetector.KnownPal98Length]);
        PalSaveDocument.Load(secondPath, SaveFormat.PalWin95)
            .Save(createBackup: false);
        True(!File.Exists(ExtendedRoleMagicSidecar.GetPath(secondPath)),
            "ordinary numbered native save does not create a redundant sidecar");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestSyntheticFieldRoundTrip()
{
    var directory = CreateTestDirectory();
    try
    {
        var path = Path.Combine(directory, "synthetic.rpg");
        var bytes = new byte[PalSaveLayout.WinEventObjectOffset + 32];
        FillTail(bytes, PalSaveLayout.WinEventObjectOffset);
        File.WriteAllBytes(path, bytes);

        var document = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        document.Cash = 123_456;
        document.SetRoleField(2, RoleField.Level, 77);
        document.SetRoleField(2, RoleField.Hp, 888);
        document.SetRoleSignedField(2, RoleField.WindResistance, -15);
        document.SetRoleSignedField(2, RoleField.EarthResistance, 35);
        document.SetExperience(2, 4_321);
        document.SetParty([0, 2, 4]);
        document.AddMagic(2, 99);

        Equal((uint)123_456, document.Cash, "cash");
        Equal((ushort)77, document.GetRole(2).Level, "level");
        Equal((ushort)888, document.GetRole(2).Hp, "HP");
        Equal((short)-15, document.GetRoleSignedField(2, RoleField.WindResistance), "signed wind resistance");
        Equal((short)35, document.GetRoleSignedField(2, RoleField.EarthResistance), "earth resistance");
        Equal((ushort)4_321, document.GetExperience(2), "experience");
        Equal(3, document.PartyCount, "party count");
        Equal((ushort)4, document.GetParty()[2].RoleId, "third party role");
        Equal((ushort)99, document.GetMagics(2).Single().MagicId, "magic");
        SequenceEqual(bytes.AsSpan(PalSaveLayout.WinEventObjectOffset), document.ToArray().AsSpan(PalSaveLayout.WinEventObjectOffset), "opaque event tail");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestInventory()
{
    var directory = CreateTestDirectory();
    try
    {
        var path = Path.Combine(directory, "inventory.rpg");
        File.WriteAllBytes(path, new byte[PalSaveLayout.DosEventObjectOffset + 32]);
        var document = PalSaveDocument.Load(path, SaveFormat.PalDos);

        document.AddInventoryItem(61, 3);
        document.AddInventoryItem(62, 4);
        document.AddInventoryItem(61, 2);
        Equal(2, document.GetInventory().Count, "inventory count");
        Equal((ushort)5, document.GetInventory()[0].Amount, "combined amount");

        document.ClearInventorySlot(0);
        var remaining = document.GetInventory().Single();
        Equal(0, remaining.Slot, "compressed slot");
        Equal((ushort)62, remaining.ItemId, "remaining item");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestSkillRegistry()
{
    string directory = CreateTestDirectory();
    try
    {
        byte[] objects = new byte[565 * PalSaveLayout.WinObjectRecordSize];
        byte[] sss = BuildMkf([], [], objects);
        string sssPath = Path.Combine(directory, "SSS.MKF");
        string dataPath = Path.Combine(directory, "DATA.MKF");
        string wordPath = Path.Combine(directory, "WORD.DAT");
        File.WriteAllBytes(sssPath, sss);
        File.WriteAllBytes(dataPath, [1, 2, 3, 4]);
        File.WriteAllBytes(wordPath, new byte[5_650]);

        string registryDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "palmod")).FullName;
        string registryPath = Path.Combine(
            registryDirectory,
            "PAL98_SKILL_REGISTRY.v1.json");
        string registry =
            "{" +
            "\"schema\":\"PAL98.SkillRegistry.v1\"," +
            "\"registry_id\":\"pal98.skill-registry\"," +
            "\"registry_version\":\"0.4.0\"," +
            "\"source_sets\":[{" +
            "\"source_set_id\":\"skill-source.pal98-classic\"," +
            "\"profile_ids\":[]," +
            "\"inventory_state\":\"exact-pal98-snapshot\"," +
            "\"resources\":[" +
            ResourceIdentityJson("SSS.MKF", sssPath) + "," +
            ResourceIdentityJson("DATA.MKF", dataPath) + "," +
            ResourceIdentityJson("WORD.DAT", wordPath) + "]," +
            "\"table_shape\":{\"object_record_size\":14,\"object_records\":565}" +
            "}]," +
            "\"skill_sets\":[{" +
            "\"skill_set_id\":\"skill-set.pal98-classic.random-candidates\"," +
            "\"display_name\":\"仙剑98原版技能（随机候选）\"," +
            "\"source_set_ids\":[\"skill-source.pal98-classic\"]," +
            "\"member_logical_ids\":[" +
            "\"skill.pal98.classic.000a\"," +
            "\"skill.pal98.classic.000b\"]," +
            "\"selection_state\":\"composer-required\"}]," +
            "\"skills\":[" +
            SkillRegistryEntryJson("skill.pal98.classic.000a", "候选技能", 10, true, false) + "," +
            SkillRegistryEntryJson("skill.pal98.classic.000b", "非候选扩展技能", 11, false, true) +
            "]}";
        File.WriteAllText(registryPath, registry, new UTF8Encoding(false));

        // This file deliberately excludes the second skill. PalSaveEditor must
        // not read gameplay selection when enumerating editable skills.
        File.WriteAllText(
            Path.Combine(registryDirectory, "random-skill-selection.v1.json"),
            "{\"excluded_logical_ids\":[\"skill.pal98.classic.000b\"]}",
            new UTF8Encoding(false));

        PalResourceCatalog resources = PalResourceCatalog.Load(directory);
        PalSkillRegistryResolution resolved = PalSkillRegistryCatalog
            .Load(registryPath)
            .Resolve(resources);

        Equal("exact-resource-sha256", resolved.Evidence, "skill source evidence");
        Equal(2, resolved.Skills.Count, "all registered skills exposed");
        Equal((ushort)10, resolved.Skills[0].ObjectId, "first skill object id");
        Equal((ushort)11, resolved.Skills[1].ObjectId, "excluded gameplay skill still editable");
        True(!resolved.Skills[1].DefaultRandomCandidate, "non-default skill remains visible");
        True(resolved.Skills[1].Deprecated, "deprecated metadata retained");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static string ResourceIdentityJson(string kind, string path) =>
    "{" +
    $"\"kind\":\"{kind}\"," +
    $"\"sha256\":\"{HashFile(path)}\"" +
    "}";

static string SkillRegistryEntryJson(
    string logicalId,
    string displayName,
    ushort objectId,
    bool defaultCandidate,
    bool deprecated) =>
    "{" +
    $"\"logical_id\":\"{logicalId}\"," +
    $"\"display_name\":\"{displayName}\"," +
    "\"owner_source_set_id\":\"skill-source.pal98-classic\"," +
    $"\"default_random_candidate\":{defaultCandidate.ToString().ToLowerInvariant()}," +
    $"\"deprecated\":{deprecated.ToString().ToLowerInvariant()}," +
    "\"source_bindings\":[{" +
    "\"source_set_id\":\"skill-source.pal98-classic\"," +
    $"\"object_id\":{objectId}," +
    "\"object_record_size\":14," +
    $"\"pal98_target_object_id\":{objectId}" +
    "}]}";

static void TestComposedContentCatalog()
{
    string directory = CreateTestDirectory();
    try
    {
        const string profileId = "pal98.classic.skill-library.hunqian167";
        const string profileVersion = "1.0.0";
        string staged = Path.Combine(
            directory,
            "palmod",
            "Profiles",
            profileId,
            profileVersion);
        string resourcesDirectory = Directory.CreateDirectory(
            Path.Combine(staged, "resources")).FullName;
        string profileDirectory = Directory.CreateDirectory(
            Path.Combine(staged, "palmod", "profile")).FullName;
        string manifestDirectory = Directory.CreateDirectory(
            Path.Combine(staged, "manifest")).FullName;

        byte[] objects = new byte[565 * PalSaveLayout.WinObjectRecordSize];
        byte[] sss = BuildMkf([], [], objects);
        string sssPath = Path.Combine(resourcesDirectory, "SSS.MKF");
        string wordPath = Path.Combine(resourcesDirectory, "WORD.DAT");
        string skillObjectsPath = Path.Combine(profileDirectory, "skill-objects.v1.bin");
        File.WriteAllBytes(sssPath, sss);
        File.WriteAllBytes(wordPath, new byte[5_650]);
        File.WriteAllBytes(skillObjectsPath, BuildSkillObjectsPack(565, 157));

        string contentCatalogPath = Path.Combine(profileDirectory, "content-catalog.json");
        string contentCatalog =
            "{" +
            "\"schema\":\"PAL98.ContentCatalog.v1\"," +
            "\"catalog_id\":\"pal98.skill-composition.classic-hunqian167\"," +
            "\"catalog_version\":\"1.0.0\"," +
            $"\"profile_id\":\"{profileId}\"," +
            $"\"profile_version\":\"{profileVersion}\"," +
            "\"magics\":[" +
            ContentMagicJson(
                "skill.pal98.classic.0127",
                "斩龙诀",
                295,
                "skill-source.pal98-classic",
                295,
                true,
                false) + "," +
            ContentMagicJson(
                "skill.hunqian167.easy.018f",
                "斩龙诀1",
                708,
                "skill-source.hunqian167.easy",
                399,
                false,
                true) +
            "]}";
        File.WriteAllText(contentCatalogPath, contentCatalog, new UTF8Encoding(false));

        string descriptor =
            "{" +
            "\"schema\":\"PAL98.GameProfile.v1\"," +
            $"\"profile_id\":\"{profileId}\"," +
            $"\"profile_version\":\"{profileVersion}\"," +
            "\"display_name\":\"仙剑98 + 魂牵梦萦1.67技能库\"," +
            "\"resource_set\":[" +
            ProfileResourceJson("SSS.MKF", "resources/SSS.MKF", sssPath) + "," +
            ProfileResourceJson("WORD.DAT", "resources/WORD.DAT", wordPath) + "," +
            ProfileResourceJson(
                "SKILL.OBJECTS",
                "palmod/profile/skill-objects.v1.bin",
                skillObjectsPath) + "," +
            ProfileResourceJson(
                "CONTENT.CATALOG",
                "palmod/profile/content-catalog.json",
                contentCatalogPath) +
            "]}";
        string descriptorPath = Path.Combine(manifestDirectory, "game-profile.json");
        File.WriteAllText(descriptorPath, descriptor, new UTF8Encoding(false));

        string profilesDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "palmod", "Profiles")).FullName;
        string pointer =
            "{" +
            "\"schema\":\"PAL98.EffectiveGameProfilePointer.v1\"," +
            $"\"profile_id\":\"{profileId}\"," +
            $"\"profile_version\":\"{profileVersion}\"," +
            $"\"descriptor_sha256\":\"{HashFile(descriptorPath)}\"," +
            $"\"staging_relative_path\":\"{profileId}/{profileVersion}\"" +
            "}";
        File.WriteAllText(
            Path.Combine(profilesDirectory, "current.json"),
            pointer,
            new UTF8Encoding(false));

        // Deliberately exclude the extended skill from random gameplay. The
        // save editor's content-catalog path must not read this file.
        File.WriteAllText(
            Path.Combine(directory, "palmod", "random-skill-selection.v1.json"),
            "{\"excluded_logical_ids\":[\"skill.hunqian167.easy.018f\"]}",
            new UTF8Encoding(false));

        PalResourceCatalog resources = PalResourceCatalog.Load(directory);
        Equal(PalSaveLayout.WinObjectRecordSize, resources.ObjectRecordSize,
            "base object record width");
        Equal(565, resources.ObjectRecordCount, "base object record count");
        Equal(722, resources.RuntimeObjectRecordCount,
            "PSO1 extended runtime object count");
        Equal(565, resources.WordCount, "classic WORD remains unchanged");
        Equal(contentCatalogPath, resources.ResourceContext.ContentCatalogPath!,
            "descriptor-verified content catalog path");

        PalSkillRegistryResolution resolved =
            PalSkillRegistryCatalog.ResolveContentCatalog(resources);
        Equal("active-profile-content-catalog-sha256", resolved.Evidence,
            "content catalog evidence");
        Equal(2, resolved.Skills.Count, "all grantable catalog skills exposed");
        Equal("斩龙诀", resolved.Skills[0].DisplayName,
            "classic same-name variant keeps its original catalog name");
        Equal("斩龙诀1", resolved.Skills[1].DisplayName,
            "extended same-name variant keeps its numbered catalog name");
        Equal((ushort)708, resolved.Skills[1].ObjectId,
            "extended object id is not limited by classic WORD count");
        Equal(PalSkillRegistryCatalog.ComposedHunqian167SkillSetId,
            resolved.Skills[1].SkillSetId,
            "Hunqian skills use the composed product pool");
        True(!resolved.Skills[1].DefaultRandomCandidate,
            "non-random skill remains editable");
        True(resolved.Skills[1].Deprecated, "catalog exclusions metadata retained");

        string savePath = Path.Combine(directory, "1.RPG");
        File.WriteAllBytes(savePath, new byte[SaveFormatDetector.KnownPal98Length]);
        PalSaveDocument document = PalSaveDocument.Load(savePath, SaveFormat.PalWin95);
        document.AddMagic(0, resolved.Skills[1].ObjectId);
        Equal((ushort)708, document.GetMagics(0).Single().MagicId,
            "extended skill id round trips through the save layout");

        File.WriteAllText(
            contentCatalogPath,
            contentCatalog.Replace("\"斩龙诀1\"", "\"风雪冰天1-1\""),
            new UTF8Encoding(false));
        Throws<InvalidDataException>(
            () => PalSkillRegistryCatalog.ResolveContentCatalog(resources),
            "reject content-catalog names wider than one PAL98 WORD record");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static string ContentMagicJson(
    string logicalId,
    string displayName,
    ushort targetObjectId,
    string sourceSetId,
    ushort sourceObjectId,
    bool randomizable,
    bool deprecated) =>
    "{" +
    $"\"logical_id\":\"{logicalId}\"," +
    $"\"object_id\":{targetObjectId}," +
    $"\"display_name\":\"{displayName}\"," +
    "\"status\":\"pal98-static-verified\"," +
    "\"learnable\":true," +
    $"\"randomizable\":{randomizable.ToString().ToLowerInvariant()}," +
    "\"grantable\":true," +
    $"\"exclusions\":[{(deprecated ? "\"deprecated\"" : string.Empty)}]," +
    "\"source_mappings\":[{" +
    $"\"source_set_id\":\"{sourceSetId}\"," +
    $"\"object_id\":{sourceObjectId}" +
    "}]}";

static void TestOptionalComposedSkillProfile()
{
    string? root = Environment.GetEnvironmentVariable(
        "PAL98_SKILL_COMPOSITION_RUNTIME_GAME");
    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
    {
        Console.WriteLine(
            "SKIP  optional composed skill profile " +
            "(PAL98_SKILL_COMPOSITION_RUNTIME_GAME not set)");
        return;
    }

    PalResourceCatalog resources = PalResourceCatalog.Load(root);
    string expectedProfileId = Environment.GetEnvironmentVariable(
        "PAL98_SKILL_COMPOSITION_RUNTIME_PROFILE_ID") ??
        "pal98.classic.skill-library.hunqian167";
    string expectedProfileVersion = Environment.GetEnvironmentVariable(
        "PAL98_SKILL_COMPOSITION_RUNTIME_PROFILE_VERSION") ?? "1.0.10";
    int expectedBaseObjectCount = ReadEnvironmentInt(
        "PAL98_SKILL_COMPOSITION_BASE_OBJECT_COUNT", 565);
    int expectedRuntimeObjectCount = ReadEnvironmentInt(
        "PAL98_SKILL_COMPOSITION_RUNTIME_OBJECT_COUNT", 676);
    int expectedWordCount = ReadEnvironmentInt(
        "PAL98_SKILL_COMPOSITION_WORD_COUNT", 565);
    ushort expectedMaximumVisibleObjectId = checked((ushort)ReadEnvironmentInt(
        "PAL98_SKILL_COMPOSITION_MAX_VISIBLE_OBJECT_ID", 666));
    int expectedSkillCount = ReadEnvironmentInt(
        "PAL98_SKILL_COMPOSITION_VISIBLE_SKILL_COUNT", 206);
    int expectedClassicSkillCount = ReadEnvironmentInt(
        "PAL98_SKILL_COMPOSITION_CLASSIC_SKILL_COUNT", 104);
    int expectedHunqianSkillCount = ReadEnvironmentInt(
        "PAL98_SKILL_COMPOSITION_HUNQIAN_SKILL_COUNT", 102);
    Equal(expectedProfileId, resources.ActiveProfileId!,
        "composed profile id");
    Equal(expectedProfileVersion, resources.ActiveProfileVersion!,
        "composed profile version");
    Equal(PalSaveLayout.WinObjectRecordSize, resources.ObjectRecordSize,
        "composed profile object width");
    Equal(expectedBaseObjectCount, resources.ObjectRecordCount,
        "composed profile base object count");
    Equal(expectedRuntimeObjectCount, resources.RuntimeObjectRecordCount,
        "composed profile runtime object count");
    Equal(expectedWordCount, resources.WordCount,
        "composed profile base WORD count");

    PalSkillRegistryResolution resolved =
        PalSkillRegistryCatalog.ResolveContentCatalog(resources);
    Equal(expectedSkillCount, resolved.Skills.Count,
        "composed profile visible skill count");
    Equal(expectedClassicSkillCount, resolved.Skills.Count(skill =>
        skill.SkillSetId == PalSkillRegistryCatalog.ClassicSkillSetId),
        "composed profile classic skill count");
    Equal(expectedHunqianSkillCount, resolved.Skills.Count(skill =>
        skill.SkillSetId == PalSkillRegistryCatalog.ComposedHunqian167SkillSetId),
        "composed profile Hunqian skill count");
    Equal(expectedMaximumVisibleObjectId,
        resolved.Skills.Max(skill => skill.ObjectId),
        "hidden closure objects are not shown as editable skills");
    True(resolved.Skills.All(skill =>
            skill.ObjectId < resources.RuntimeObjectRecordCount),
        "all composed skill object ids are backed by the extended table");
}

static int ReadEnvironmentInt(string name, int fallback)
{
    string? raw = Environment.GetEnvironmentVariable(name);
    return int.TryParse(raw, out int value) ? value : fallback;
}

static byte[] BuildSkillObjectsPack(uint firstObjectId, uint recordCount)
{
    const int headerBytes = 20;
    const int objectBytes = PalSaveLayout.WinObjectRecordSize;
    const int wordBytes = 10;
    byte[] result = new byte[checked(headerBytes + (int)recordCount * (objectBytes + wordBytes))];
    Encoding.ASCII.GetBytes("PSO1").CopyTo(result, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), objectBytes);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), wordBytes);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), firstObjectId);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), recordCount);
    int extensionCount = checked((int)recordCount);
    for (int index = 0; index < extensionCount; index++)
    {
        int offset = headerBytes + index * (objectBytes + wordBytes);
        result[offset] = 1;
        result[offset + objectBytes] = (byte)'X';
    }
    return result;
}

static void TestHunqianActiveProfileLayout()
{
    string directory = CreateTestDirectory();
    try
    {
        const string profileId = "pal98.hunqian167.easy";
        const string profileVersion = "1.0.0";
        const int eventBytes = 170_624;
        string staged = Path.Combine(directory, "palmod", "Profiles", profileId, profileVersion);
        string resources = Directory.CreateDirectory(Path.Combine(staged, "resources")).FullName;
        string manifest = Directory.CreateDirectory(Path.Combine(staged, "manifest")).FullName;
        byte[] events = new byte[eventBytes];
        byte[] objects = new byte[575 * PalSaveLayout.WinObjectRecordSize];
        byte[] sss = BuildMkf(events, [], objects);
        string sssPath = Path.Combine(resources, "SSS.MKF");
        string wordPath = Path.Combine(resources, "WORD.DAT");
        File.WriteAllBytes(sssPath, sss);
        File.WriteAllBytes(wordPath, new byte[5_750]);

        string descriptor =
            "{" +
            "\"schema\":\"PAL98.GameProfile.v1\"," +
            $"\"profile_id\":\"{profileId}\"," +
            $"\"profile_version\":\"{profileVersion}\"," +
            "\"display_name\":\"魂牵梦萦 1.67 简单 兼容配置档\"," +
            "\"resource_set\":[" +
            ProfileResourceJson("SSS.MKF", "resources/SSS.MKF", sssPath) + "," +
            ProfileResourceJson("WORD.DAT", "resources/WORD.DAT", wordPath) +
            "]}";
        string descriptorPath = Path.Combine(manifest, "game-profile.json");
        File.WriteAllText(descriptorPath, descriptor, new UTF8Encoding(false));
        string profiles = Directory.CreateDirectory(Path.Combine(directory, "palmod", "Profiles")).FullName;
        string pointer =
            "{" +
            "\"schema\":\"PAL98.EffectiveGameProfilePointer.v1\"," +
            $"\"profile_id\":\"{profileId}\"," +
            $"\"profile_version\":\"{profileVersion}\"," +
            $"\"descriptor_sha256\":\"{HashFile(descriptorPath)}\"," +
            $"\"staging_relative_path\":\"{profileId}/{profileVersion}\"" +
            "}";
        File.WriteAllText(Path.Combine(profiles, "current.json"), pointer, new UTF8Encoding(false));

        string compatiblePath = Path.Combine(directory, "1.RPG");
        var compatible = new byte[PalSaveLayout.WinEventObjectOffset + eventBytes];
        FillTail(compatible, PalSaveLayout.WinEventObjectOffset);
        File.WriteAllBytes(compatiblePath, compatible);
        string incompatiblePath = Path.Combine(directory, "3.RPG");
        File.WriteAllBytes(incompatiblePath, compatible.AsSpan(0, SaveFormatDetector.KnownPal98Length).ToArray());

        PalSaveDocument document = PalSaveDocument.Load(compatiblePath, gameDirectory: directory);
        Equal(SaveFormat.PalWin95, document.Format, "Hunqian PALDLL Win95 format");
        True(!document.Detection.IsHeuristic, "active profile resource proof");
        NotNull(document.Catalog, "Hunqian catalog");
        Equal(profileId, document.Catalog!.ActiveProfileId!, "active profile id");
        Equal(profileVersion, document.Catalog.ActiveProfileVersion!, "active profile version");
        Equal(eventBytes, document.Catalog.EventObjectBytes, "Hunqian event bytes");
        Equal(PalSaveLayout.WinObjectRecordSize, document.Catalog.ObjectRecordSize, "Hunqian object width");
        True(document.Detection.Reason.IndexOf("5,332", StringComparison.Ordinal) >= 0,
            "Hunqian event count evidence");

        byte[] eventTail = document.ToArray().AsSpan(PalSaveLayout.WinEventObjectOffset).ToArray();
        document.Cash = 167;
        document.Save(createBackup: false);
        PalSaveDocument roundTrip = PalSaveDocument.Load(compatiblePath, gameDirectory: directory);
        Equal((uint)167, roundTrip.Cash, "Hunqian field round trip");
        SequenceEqual(eventTail, roundTrip.ToArray().AsSpan(PalSaveLayout.WinEventObjectOffset),
            "Hunqian opaque event state preserved");
        Equal(PalSaveLayout.WinEventObjectOffset + eventBytes, roundTrip.Length, "Hunqian length preserved");

        Throws<InvalidDataException>(
            () => PalSaveDocument.Load(incompatiblePath, gameDirectory: directory),
            "classic save rejected under Hunqian profile");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestDream220VisibleActiveProfile()
{
    string directory = CreateTestDirectory();
    try
    {
        PalPublicToolProfile baseContract = PalPublicToolProfiles.Dream220Visible;
        Equal("PAL98.PublicToolProfile.v1", baseContract.Schema, "public profile schema");
        Equal("pal98.dream220.compat", baseContract.ProfileId, "Dream profile id");
        Equal("1.0.18", baseContract.ProfileVersion, "Dream profile version");
        Equal("梦幻2.2显血版", baseContract.DisplayName, "Dream display name");
        Equal("主播粉丝|孙小柔|othercat", string.Join("|", baseContract.Credits.Select(credit => credit.Name)),
            "Dream ordered credits");
        Equal(5_369, baseContract.EventObjectRecordCount, "Dream event count");
        Equal(185_872, baseContract.ExpectedSaveLength, "Dream save length");
        Throws<InvalidDataException>(
            () => baseContract.ValidateDescriptor("仙剑梦幻 2.20 兼容配置档", baseContract.ProfileId),
            "1.0.18 legacy display name rejected");

        const string derivedProfileId = "pal98.dream220.compat.drawcard.16e143813df5";
        PalPublicToolProfile contract = PalPublicToolProfiles.Find(derivedProfileId, "1.0.18")
            ?? throw new InvalidOperationException("Dream DrawCard public profile family was not resolved.");
        Equal(derivedProfileId, contract.ProfileId, "Dream derived profile id");
        Equal("梦幻2.2显血版 + 抽卡", contract.DisplayName, "Dream derived display name");
        contract.ValidateDescriptor(contract.DisplayName, derivedProfileId);
        True(PalPublicToolProfiles.Find(
            "pal98.dream220.compat.drawcard.16E143813DF5", "1.0.18") is null,
            "uppercase derived identity rejected");
        True(PalPublicToolProfiles.Find(
            "pal98.dream220.compat.drawcard.16e143813df", "1.0.18") is null,
            "short derived identity rejected");
        True(PalPublicToolProfiles.Find(derivedProfileId, "1.0.17") is null,
            "wrong derived version rejected");

        string staged = Path.Combine(directory, "palmod", "Profiles", contract.ProfileId, contract.ProfileVersion);
        string resources = Directory.CreateDirectory(Path.Combine(staged, "resources")).FullName;
        string manifest = Directory.CreateDirectory(Path.Combine(staged, "manifest")).FullName;
        byte[] events = new byte[contract.EventObjectRecordCount * PalSaveLayout.EventObjectRecordSize];
        byte[] objects = new byte[contract.ResourceObjectRecordCount * contract.ObjectRecordSize];
        byte[] sss = BuildMkf(events, [], objects);
        string sssPath = Path.Combine(resources, "SSS.MKF");
        string wordPath = Path.Combine(resources, "WORD.DAT");
        File.WriteAllBytes(sssPath, sss);
        File.WriteAllBytes(wordPath, new byte[contract.WordDatByteLength]);

        string descriptor =
            "{" +
            "\"schema\":\"PAL98.GameProfile.v1\"," +
            $"\"profile_id\":\"{contract.ProfileId}\"," +
            $"\"profile_version\":\"{contract.ProfileVersion}\"," +
            $"\"display_name\":\"{contract.DisplayName}\"," +
            $"\"save_namespace\":\"{contract.ProfileId}\"," +
            "\"resource_set\":[" +
            ProfileResourceJson("SSS.MKF", "resources/SSS.MKF", sssPath) + "," +
            ProfileResourceJson("WORD.DAT", "resources/WORD.DAT", wordPath) +
            "]}";
        string descriptorPath = Path.Combine(manifest, "game-profile.json");
        File.WriteAllText(descriptorPath, descriptor, new UTF8Encoding(false));
        string profiles = Directory.CreateDirectory(Path.Combine(directory, "palmod", "Profiles")).FullName;
        string pointer =
            "{" +
            "\"schema\":\"PAL98.EffectiveGameProfilePointer.v1\"," +
            $"\"profile_id\":\"{contract.ProfileId}\"," +
            $"\"profile_version\":\"{contract.ProfileVersion}\"," +
            $"\"descriptor_sha256\":\"{HashFile(descriptorPath)}\"," +
            $"\"staging_relative_path\":\"{contract.ProfileId}/{contract.ProfileVersion}\"" +
            "}";
        File.WriteAllText(Path.Combine(profiles, "current.json"), pointer, new UTF8Encoding(false));

        string compatiblePath = Path.Combine(directory, "1.RPG");
        var compatible = new byte[contract.ExpectedSaveLength];
        FillTail(compatible, PalSaveLayout.WinEventObjectOffset);
        File.WriteAllBytes(compatiblePath, compatible);
        string incompatiblePath = Path.Combine(directory, "2.RPG");
        File.WriteAllBytes(incompatiblePath, compatible.AsSpan(0, SaveFormatDetector.KnownPal98Length).ToArray());

        PalSaveDocument document = PalSaveDocument.Load(compatiblePath, gameDirectory: directory);
        Equal(SaveFormat.Dream220Win95, document.Format, "Dream visible Win95 format");
        True(!document.Detection.IsHeuristic, "Dream visible public profile/resource proof");
        NotNull(document.Catalog, "Dream visible catalog");
        Equal(contract.DisplayName, document.Catalog!.ActiveProfileDisplayName!, "Dream visible active display name");
        Equal(derivedProfileId, document.Catalog.ActiveProfileId!, "Dream visible derived active profile id");
        Equal(589, document.Catalog.WordCount, "Dream visible word count");
        Equal(contract.EventObjectRecordCount * PalSaveLayout.EventObjectRecordSize,
            document.Catalog.EventObjectBytes, "Dream visible event bytes");

        byte[] eventTail = document.ToArray().AsSpan(PalSaveLayout.WinEventObjectOffset).ToArray();
        document.Cash = 220;
        document.Save(createBackup: false);
        PalSaveDocument roundTrip = PalSaveDocument.Load(compatiblePath, gameDirectory: directory);
        Equal((uint)220, roundTrip.Cash, "Dream visible field round trip");
        SequenceEqual(eventTail, roundTrip.ToArray().AsSpan(PalSaveLayout.WinEventObjectOffset),
            "Dream visible opaque event state preserved");
        Throws<InvalidDataException>(
            () => PalSaveDocument.Load(incompatiblePath, gameDirectory: directory),
            "Classic save rejected under Dream visible profile");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestOptionalHunqianRuntime()
{
    string? root = Environment.GetEnvironmentVariable("PAL98_HUNQIAN167_RUNTIME_GAME");
    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
    {
        Console.WriteLine("SKIP  optional Hunqian runtime (PAL98_HUNQIAN167_RUNTIME_GAME not set)");
        return;
    }

    PalSaveDocument first = PalSaveDocument.Load(Path.Combine(root, "1.RPG"), gameDirectory: root);
    PalSaveDocument second = PalSaveDocument.Load(Path.Combine(root, "2.RPG"), gameDirectory: root);
    Equal(184_688, first.Length, "Hunqian runtime slot 1 length");
    Equal(184_688, second.Length, "Hunqian runtime slot 2 length");
    Equal("pal98.hunqian167.easy", first.Catalog!.ActiveProfileId!, "Hunqian runtime profile");
    True(!first.Detection.IsHeuristic && !second.Detection.IsHeuristic, "Hunqian runtime resource proof");

    string thirdPath = Path.Combine(root, "3.RPG");
    if (File.Exists(thirdPath) && new FileInfo(thirdPath).Length == SaveFormatDetector.KnownPal98Length)
    {
        Throws<InvalidDataException>(
            () => PalSaveDocument.Load(thirdPath, gameDirectory: root),
            "Hunqian runtime rejects Classic slot");
    }
}

static void TestRealSamples()
{
    var winSave = @"D:\SteamLibrary\steamapps\common\PAL\PAL98\1.RPG";
    var dosSave = @"D:\SteamLibrary\steamapps\common\PAL\PAL_DOS\0.RPG";
    var dreamDirectory = @"D:\Workspace\KnowledgeRoots\PAL\外塞之雾\仙剑梦幻2.20\pal";
    var dreamSave = Path.Combine(dreamDirectory, "1.rpg");
    var palDllDreamDirectory = @"D:\Workspace\KnowledgeRoots\PAL\othercat\PALDLL_DX9-dream220-runtime\game";
    var palDllDreamSave = Path.Combine(palDllDreamDirectory, "2.rpg");

    if (!File.Exists(winSave) || !File.Exists(dosSave) || !File.Exists(dreamSave))
    {
        Console.WriteLine("SKIP  real samples are not installed on this host");
        return;
    }

    Equal(SaveFormat.PalWin95, PalSaveDocument.Load(winSave).Format, "real Win save");
    Equal(SaveFormat.PalDos, PalSaveDocument.Load(dosSave).Format, "real DOS save");

    var dream = PalSaveDocument.Load(dreamSave, gameDirectory: dreamDirectory);
    Equal(SaveFormat.Dream220Dos, dream.Format, "real Dream save");
    NotNull(dream.Catalog, "Dream catalog");
    Equal(589, dream.Catalog!.WordCount, "Dream words");
    Equal(12, dream.Catalog.ObjectRecordSize, "Dream DOS objects");
    Equal(171_808, dream.Catalog.EventObjectBytes, "Dream events");
    Equal("經驗值", dream.Catalog.GetWord(2), "Dream Big5 word decoding");

    if (File.Exists(palDllDreamSave) && new FileInfo(palDllDreamSave).Length == SaveFormatDetector.KnownDream220Win95Length)
    {
        var palDllDream = PalSaveDocument.Load(palDllDreamSave, gameDirectory: palDllDreamDirectory);
        Equal(SaveFormat.Dream220Win95, palDllDream.Format, "PALDLL Dream save");
        NotNull(palDllDream.Catalog, "PALDLL Dream catalog");
        Equal(565, palDllDream.Catalog!.WordCount, "PALDLL Dream words");
        Equal(14, palDllDream.Catalog.ObjectRecordSize, "PALDLL Dream Win95 objects");
        Equal(171_808, palDllDream.Catalog.EventObjectBytes, "PALDLL Dream events");
        True(!palDllDream.Detection.IsHeuristic, "PALDLL Dream profile resource proof");
        True(palDllDream.Catalog.SourceDirectory.IndexOf("pal98.dream220.compat", StringComparison.OrdinalIgnoreCase) >= 0,
            "PALDLL Dream active profile resources");
        Throws<InvalidDataException>(() => palDllDream.SetFormat(SaveFormat.Dream220Dos),
            "reject DOS Dream layout for PALDLL Dream save");
        palDllDream.SetFormat(SaveFormat.PalWin95);
        Equal(SaveFormat.PalWin95, palDllDream.Format, "allow Win95 layout alias for PALDLL Dream save");
    }
    else if (File.Exists(palDllDreamSave))
    {
        Console.WriteLine("SKIP  PALDLL Dream path currently contains a different-format save fixture");
    }

    var winCatalog = PalResourceCatalog.Load(Path.GetDirectoryName(winSave)!);
    Equal("状态", winCatalog.GetWord(3), "Win95 GBK word decoding");
}

static void TestSafeWrite()
{
    var source = @"D:\SteamLibrary\steamapps\common\PAL\PAL98\1.RPG";
    if (!File.Exists(source))
    {
        Console.WriteLine("SKIP  Win sample is not installed on this host");
        return;
    }

    var directory = CreateTestDirectory();
    try
    {
        var path = Path.Combine(directory, "1.RPG");
        File.Copy(source, path);
        var before = File.ReadAllBytes(path);
        var document = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        document.Cash = document.Cash == uint.MaxValue ? 0 : document.Cash + 1;
        var result = document.Save();

        NotNull(result.BackupPath, "backup path");
        True(File.Exists(result.BackupPath!), "backup exists");
        SequenceEqual(before, File.ReadAllBytes(result.BackupPath!), "backup bytes");
        Equal(before.Length, File.ReadAllBytes(path).Length, "output length");
        True(!document.IsDirty, "document clean after save");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestSafeWriteWithoutBackup()
{
    var directory = CreateTestDirectory();
    try
    {
        var path = Path.Combine(directory, "1.RPG");
        File.WriteAllBytes(path, new byte[PalSaveLayout.WinEventObjectOffset + 32]);
        var document = PalSaveDocument.Load(path, SaveFormat.PalWin95);
        document.Cash = 123_456;
        byte[] expected = document.ToArray();

        SaveWriteResult result = document.Save(createBackup: false);

        True(result.BackupPath is null, "backup path omitted");
        SequenceEqual(expected, File.ReadAllBytes(path), "saved bytes");
        True(!document.IsDirty, "document clean after save without backup");
        Equal(0, Directory.EnumerateFiles(directory, "*.bak-*", SearchOption.TopDirectoryOnly).Count(),
            "no retained backup files");
        Equal(0, Directory.EnumerateFiles(directory, "*.rollback", SearchOption.TopDirectoryOnly).Count(),
            "no temporary rollback files");
        Equal(0, Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly).Count(),
            "no temporary write files");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static string CreateTestDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), $"PalSaveEditor.Tests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
}

static void FillTail(byte[] bytes, int offset)
{
    for (var i = offset; i < bytes.Length; i++)
    {
        bytes[i] = (byte)(i * 31);
    }
}

static byte[] BuildMkf(params byte[][] chunks)
{
    int headerLength = (chunks.Length + 1) * sizeof(uint);
    int totalLength = headerLength + chunks.Sum(chunk => chunk.Length);
    byte[] result = new byte[totalLength];
    int offset = headerLength;
    for (int index = 0; index < chunks.Length; index++)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * 4), (uint)offset);
        chunks[index].CopyTo(result, offset);
        offset += chunks[index].Length;
    }
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(chunks.Length * 4), (uint)offset);
    return result;
}

static string ProfileResourceJson(string kind, string relativePath, string path) =>
    "{" +
    $"\"kind\":\"{kind}\"," +
    $"\"relative_path\":\"{relativePath}\"," +
    $"\"sha256\":\"{HashFile(path)}\"," +
    $"\"size_bytes\":{new FileInfo(path).Length}" +
    "}";

static string HashFile(string path)
{
    using FileStream stream = File.OpenRead(path);
    using SHA256 sha256 = SHA256.Create();
    return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
}

static void WriteQueueRecord(byte[] bytes, int queueIndex, ushort id, byte marker)
{
    var offset = PalSaveLayout.PartyRecordOffset(queueIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), id);
    bytes.AsSpan(offset + sizeof(ushort), PalSaveLayout.PartyEntrySize - sizeof(ushort)).Fill(marker);
}

static void AssertQueueMarker(byte[] bytes, int queueIndex, byte marker, string message)
{
    var offset = PalSaveLayout.PartyRecordOffset(queueIndex) + sizeof(ushort);
    var actual = bytes.AsSpan(offset, PalSaveLayout.PartyEntrySize - sizeof(ushort));
    Span<byte> expected = stackalloc byte[PalSaveLayout.PartyEntrySize - sizeof(ushort)];
    expected.Fill(marker);
    SequenceEqual(expected, actual, message);
}

static void Equal<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
}

static void SequenceEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, string message)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException($"{message}: byte sequences differ");
    }
}

static void True(bool value, string message)
{
    if (!value)
    {
        throw new InvalidOperationException(message);
    }
}

static void NotNull(object? value, string message) => True(value is not null, message);

static void Throws<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}");
}
