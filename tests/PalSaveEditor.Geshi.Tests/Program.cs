using System.Buffers.Binary;
using System.Text;
using System.Globalization;
using PalSaveEditor.Core;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length > 0 && args[0] == "--gameplay-rules")
{
    try { return GameplayRuleTests.Run(args.Skip(1).ToArray()); }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}
if (args.Length > 0 && args[0] == "--challenge-wuhou")
{
    try { return ChallengeWuhouTests.Run(args.Skip(1).ToArray()); }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}
if (args.Length > 0 && args[0] == "--classic-profiles")
{
    try { return ClassicProfileTests.Run(args.Skip(1).ToArray()); }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}
if (args.Length > 0 && args[0] == "--geshi-variants")
{
    try { return MaintenanceProfileTests.Run(args.Skip(1).ToArray(), geshiVariants: true); }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}
if (args.Length > 0 && args[0] == "--expanded-profiles")
{
    try { return MaintenanceProfileTests.Run(args.Skip(1).ToArray(), true); }
    catch (Exception error) {
        for (Exception? current = error; current is not null; current = current.InnerException)
            Console.Error.WriteLine(current.GetType().FullName + ": " + current.Message);
        return 1;
    }
}
if (args.Length > 0 && args[0] == "--maintenance-profiles")
{
    try { return MaintenanceProfileTests.Run(args.Skip(1).ToArray()); }
    catch (Exception error) {
        for (Exception? current = error; current is not null; current = current.InnerException)
            Console.Error.WriteLine(current.GetType().FullName + ": " + current.Message);
        return 1;
    }
}
if (args.Length > 0 && args[0] == "--resource-matrix")
    return ResourceIdentityTests.Run(args.Skip(1).ToArray());
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: PalSaveEditor.Geshi.Tests <read-only game root> <new fixture directory> [read-only foreign RPG...]");
    return 2;
}
string source = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
if (!EditorEdition.IsGeshi || Directory.Exists(output) || File.Exists(output))
    throw new InvalidOperationException("Dedicated build and a new output directory are required.");
var sourceLocation = GeshiSaveContract.Locate(source);
GeshiSaveContract.ValidateResources(sourceLocation.ResourceDirectory);
string[] resourceNames = ["SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG"];
var originalSaves = Enumerable.Range(1, 5).ToDictionary(slot => slot,
    slot => File.ReadAllBytes(Path.Combine(sourceLocation.SaveDirectory, $"{slot}.rpg")));
var foreignSaves = args.Skip(2).ToDictionary(Path.GetFullPath, File.ReadAllBytes);
Directory.CreateDirectory(output);
int checks = 0;
int passed = 0;
var failures = new List<string>();

var tests = new (string Name, Action Run)[]
{
    ("700-object DOS contract and simplified typed catalog", Catalog),
    ("simplified display and search on traditional cultures", TraditionalCulture),
    ("five original saves: no-op byte identity and retained backups", OriginalRoundTrips),
    ("exact cash/role edits preserve objects events reserved bytes and extra tail", ExactFieldEdits),
    ("six native roles, five party records, bounded followers", PartyAndRoles),
    ("32 physical magic slots, type checks and add/remove persistence", NativeMagics),
    ("typed inventory and equipment updates preserve opaque sections", InventoryAndEquipment),
    ("sdlpal.cfg GamePath and SavePath relative to root, including Tools startup", ConfiguredPaths),
    ("PALDLL profile custom roles and stale sidecars are never authority", NoPalDllState),
    ("four resource identities fail closed at load and save", ResourceMismatch),
    ("wrong format/truncated/header-invalid saves and overwrite targets rejected", InvalidSaves),
    ("external game save change is not overwritten", ConcurrentSave),
    ("failed backup/write leaves original file and changes recoverable", FailedWrite),
};
foreach (var test in tests)
{
    try { test.Run(); passed++; Console.WriteLine("PASS " + test.Name); }
    catch (Exception exception) { failures.Add(test.Name); Console.Error.WriteLine("FAIL " + test.Name + ": " + exception); }
}
try
{
    foreach (var item in originalSaves)
        Same(item.Value, File.ReadAllBytes(Path.Combine(sourceLocation.SaveDirectory, $"{item.Key}.rpg")), "source original stays read-only " + item.Key);
    foreach (var item in foreignSaves)
        Same(item.Value, File.ReadAllBytes(item.Key), "foreign source stays read-only");
    GeshiSaveContract.ValidateResources(sourceLocation.ResourceDirectory);
}
catch (Exception exception) { failures.Add("read-only sources"); Console.Error.WriteLine(exception); }
Console.WriteLine($"{passed}/{tests.Length} groups; {checks} assertions; failures={failures.Count}; fixtures={output}");
return failures.Count == 0 ? 0 : 1;

string Case(string name)
{
    string root = Path.Combine(output, name);
    Directory.CreateDirectory(Path.Combine(root, "PAL"));
    Directory.CreateDirectory(Path.Combine(root, "save"));
    File.WriteAllText(Path.Combine(root, "sdlpal.cfg"), "GamePath=./PAL/\r\nSavePath=./\r\n");
    // Marker only, never executed: central backup discovery follows the real package.
    File.WriteAllBytes(Path.Combine(root, "pal.exe"), []);
    foreach (string namePart in resourceNames)
        File.Copy(Path.Combine(sourceLocation.ResourceDirectory, namePart), Path.Combine(root, "PAL", namePart));
    foreach (var save in originalSaves) File.WriteAllBytes(SavePath(root, save.Key), save.Value);
    return root;
}
string SavePath(string root, int slot = 3) => Path.Combine(root, "save", $"{slot}.rpg");
PalSaveDocument Load(string root, int slot = 3) => PalSaveDocument.Load(SavePath(root, slot));
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
void Equal<T>(T expected, T actual, string message) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
void Same(byte[] expected, byte[] actual, string message) => Check(expected.AsSpan().SequenceEqual(actual), message);
void Reject<T>(Action action, string message) where T : Exception
{
    checks++;
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException("Did not reject: " + message);
}
void OnlyChanges(byte[] before, byte[] after, params (int Offset, int Length)[] ranges)
{
    Equal(before.Length, after.Length, "file length preserved");
    Check(Enumerable.Range(0, before.Length).All(i => before[i] == after[i] || ranges.Any(r => i >= r.Offset && i < r.Offset + r.Length)), "no bytes changed outside declared fields");
}
void Catalog()
{
    var catalog = PalResourceCatalog.Load(source);
    Equal(700, PalSaveLayout.ObjectCount, "save object capacity");
    Equal(14064, PalSaveLayout.DosEventObjectOffset, "DOS event offset");
    Equal(588, catalog.ObjectRecordCount, "resource object count");
    Equal(700, catalog.RuntimeObjectRecordCount, "runtime capacity");
    Equal(12, catalog.ObjectRecordSize, "DOS width");
    Equal(180224, catalog.EventObjectBytes, "real events");
    Equal("金刚武神咒", catalog.GetObjectName(584), "learning item simplified");
    Equal("隔世追凶", catalog.GetObjectName(571), "special magic simplified");
    Equal("魂息", catalog.GetObjectName(575), "special magic Big5");
    Equal(242, Enumerable.Range(0, 588).Count(catalog.IsItem), "audited item ID count");
    Equal(107, Enumerable.Range(0, 588).Count(catalog.IsMagic), "usable magic ID count");
    Check(catalog.IsItem(584) && !catalog.IsMagic(584), "learning scroll is item");
    Check(!catalog.IsItem(571) && !catalog.IsItem(575), "special skills never inventory");
    Check(catalog.IsMagic(344) && catalog.IsItem(344), "dual-purpose script object");
    Check(!catalog.IsMagic(331) && !catalog.IsMagic(391) && !catalog.IsMagic(398), "placeholders/enemies not magic");
    Check(catalog.SearchObjects(null, 600, catalog.IsItem).All(x => catalog.IsItem(x.Id)), "item picker source");
    Check(catalog.SearchObjects(null, 600, catalog.IsMagic).Any(x => x.Id == 571), "magic picker source");
}
void TraditionalCulture()
{
    var before = CultureInfo.CurrentCulture;
    var beforeUi = CultureInfo.CurrentUICulture;
    try
    {
        foreach (string name in new[] { "zh-CN", "zh-TW", "zh-HK" })
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            var catalog = PalResourceCatalog.Load(source);
            Equal("隔世追凶", catalog.GetObjectName(571), name + " always simplified magic");
            Equal("铜钱镖", catalog.GetObjectName(344), name + " always simplified item");
            Check(catalog.SearchObjects("金刚", 600, catalog.IsItem).Any(x => x.Id == 584), name + " simplified search");
        }
        Equal("铜钱镖", GeshiSaveContract.DisplayName("銅錢鏢"), "legacy resource display conversion");
    }
    finally { CultureInfo.CurrentCulture = before; CultureInfo.CurrentUICulture = beforeUi; }
}
void OriginalRoundTrips()
{
    string root = Case("roundtrip");
    foreach (var entry in originalSaves)
    {
        var document = Load(root, entry.Key);
        Equal(SaveFormat.GeshiEasyDos, document.Format, "dedicated format");
        Equal(194288, document.Length, "original byte length");
        Check(!document.IsDirty, "load clean");
        Same(entry.Value, document.ToArray(), "load bytes identical");
        var saved = document.Save();
        Check(saved.BackupPath is not null && saved.BackupPath.Contains(Path.Combine("backup", "SaveTools")), "central backup path");
        Same(entry.Value, File.ReadAllBytes(saved.BackupPath!), "exact original backup");
        Same(entry.Value, File.ReadAllBytes(saved.TargetPath), "no-op save bytes identical");
        Same(entry.Value, Load(root, entry.Key).ToArray(), "reload bytes identical");
        Equal(32, document.MagicCapacity, "native magic capacity");
        Equal(6, document.RuntimeRoleCount, "native role count");
    }
}
void ExactFieldEdits()
{
    string root = Case("fields-tail");
    byte[] before = originalSaves[3].Concat(new byte[] { 0x47, 0x45, 0x53, 0x48, 0x49, 0, 0xFF }).ToArray();
    File.WriteAllBytes(SavePath(root), before);
    var document = Load(root);
    document.Cash += 17;
    ushort hp = checked((ushort)(document.GetRoleField(5, RoleField.Hp) + 1));
    document.SetRoleField(5, RoleField.Hp, hp);
    Check(document.IsDirty, "field edit dirty");
    byte[] edited = document.ToArray();
    OnlyChanges(before, edited, (40, 4), (626, 2));
    Equal(hp, BinaryPrimitives.ReadUInt16LittleEndian(edited.AsSpan(626, 2)), "role5 HP physical offset");
    var result = document.Save(createBackup: false);
    Check(result.BackupPath is null, "no retained backup option");
    Same(edited, Load(root).ToArray(), "edited save reload including non-aligned tail");
    Same(before.Skip(5664).ToArray(), edited.Skip(5664).ToArray(), "all objects events extra bytes preserved");
    Check(!document.IsDirty, "persisted baseline clean");
    Check(!Directory.EnumerateFiles(Path.Combine(root, "save")).Any(p => p.EndsWith(".rollback") || p.EndsWith(".tmp")), "temporary files removed");
}
void PartyAndRoles()
{
    string root = Case("party");
    var document = Load(root);
    for (int role = 0; role < 6; role++) Check(document.GetRole(role).DisplayName.Length > 0, "six named role slots");
    document.SetFollowers([]);
    document.SetParty([5, 4, 3, 2, 1]);
    Equal(5, document.PartyCount, "five engine party slots");
    Reject<ArgumentOutOfRangeException>(() => document.SetParty([0, 1, 2, 3, 4, 5]), "six party records");
    Reject<ArgumentOutOfRangeException>(() => document.SetParty([6]), "PALDLL custom role");
    Reject<InvalidDataException>(() => document.SetParty([5, 5]), "duplicate role");
    Reject<InvalidDataException>(() => document.SetFollowers([554]), "party/follower queue overflow");
    document.SetParty([5, 4, 3]);
    document.SetFollowers([554, 12]);
    Equal(2, document.GetFollowers().Count, "two followers");
    Reject<ArgumentOutOfRangeException>(() => document.SetFollowers([1, 2, 3]), "trail supports at most two followers");
    document.Save();
    Equal(554, (int)Load(root).GetFollowers()[0].SpriteId, "follower MGO persistence");
}
void NativeMagics()
{
    string root = Case("magic");
    var document = Load(root);
    document.ReplaceMagics(5, [571, 575, 351]);
    Equal(3, document.GetMagics(5).Count, "physical native skills");
    Equal("金刚武神咒", document.GetMagics(5)[2].DisplayName, "magic name");
    document.AddMagic(5, 571);
    Equal(3, document.GetMagics(5).Count, "duplicate is not added");
    Reject<InvalidDataException>(() => document.AddMagic(5, 584), "item cannot be magic");
    Reject<ArgumentOutOfRangeException>(() => document.RemoveMagic(5, 32), "no 33rd physical slot");
    Reject<InvalidDataException>(() => document.ReplaceMagics(5, [571, 584]), "mixed replacement rejected");
    ushort[] full = Enumerable.Range(295, 103).Where(GeshiSaveContract.IsMagic).Take(32).Select(i => (ushort)i).ToArray();
    document.ReplaceMagics(5, full);
    Reject<InvalidOperationException>(() => document.AddMagic(5, 575), "33rd physical magic rejected");
    document.RemoveMagic(5, 0);
    document.AddMagic(5, 575);
    Equal(32, document.GetMagics(5).Count, "remove creates native capacity");
    OnlyChanges(originalSaves[3], document.ToArray(), (892, 384));
    document.Save();
    var reload = Load(root);
    Equal(32, reload.GetMagics(5).Count, "32 persisted skills");
    Check(reload.GetMagics(5).Any(m => m.MagicId == 575), "new special magic persists");
    Check(!Directory.EnumerateFiles(Path.Combine(root, "save"), "*.json").Any(), "no magic sidecar created");
}
void InventoryAndEquipment()
{
    string root = Case("inventory");
    var document = Load(root);
    int amount = document.GetInventory().Single(x => x.ItemId == 283).Amount;
    document.AddInventoryItem(283, 1);
    Equal(amount + 1, (int)document.GetInventory().Single(x => x.ItemId == 283).Amount, "item amount increments");
    Reject<InvalidDataException>(() => document.AddInventoryItem(571, 1), "571 not an item");
    Reject<InvalidDataException>(() => document.AddInventoryItem(575, 1), "575 not an item");
    Reject<InvalidDataException>(() => document.SetEquipment(5, 3, 584), "non-equipment scroll");
    document.SetEquipment(5, 3, 283);
    Equal((ushort)283, document.GetEquipment(5)[3].ItemId, "equipment item selected");
    OnlyChanges(originalSaves[3], document.ToArray(), (1728, 1536), (686, 2));
    document.Save();
    Equal(amount + 1, (int)Load(root).GetInventory().Single(x => x.ItemId == 283).Amount, "inventory persists");
    document.SetEquipment(5, 3, 0);
    Equal((ushort)0, document.GetEquipment(5)[3].ItemId, "equipment clear supported");

    string limitRoot = Case("inventory-limit");
    var limits = Load(limitRoot);
    var existing = limits.GetInventory().Single(x => x.ItemId == 283);
    byte[] unedited = limits.ToArray();
    Reject<ArgumentOutOfRangeException>(() => limits.SetInventorySlot(existing.Slot, 283, 100), "new edit 100 rejected");
    Reject<ArgumentOutOfRangeException>(() => limits.SetInventorySlot(existing.Slot, 283, ushort.MaxValue), "new edit ushort max rejected");
    Reject<ArgumentOutOfRangeException>(() => limits.AddInventoryItem(283, 100), "add 100 rejected");
    Reject<ArgumentOutOfRangeException>(() => limits.AddInventoryItem(283, ushort.MaxValue), "add ushort max rejected");
    Same(unedited, limits.ToArray(), "rejected amount changes no bytes");
    limits.SetInventorySlot(existing.Slot, 283, 98);
    limits.AddInventoryItem(283, 1);
    Equal((ushort)99, limits.GetInventory().Single(x => x.ItemId == 283).Amount, "sum exactly 99 accepted");
    byte[] atLimit = limits.ToArray();
    Reject<ArgumentOutOfRangeException>(() => limits.AddInventoryItem(283, 1), "sum 100 rejected");
    Same(atLimit, limits.ToArray(), "overflow does not clamp or mutate inventory");
    limits.Save();
    Equal((ushort)99, Load(limitRoot).GetInventory().Single(x => x.ItemId == 283).Amount, "quantity 99 persists");

    string historicalRoot = Case("inventory-historical-values");
    byte[] historical = (byte[])originalSaves[3].Clone();
    int knownSlot = Load(historicalRoot).GetInventory().Single(x => x.ItemId == 283).Slot;
    BinaryPrimitives.WriteUInt16LittleEndian(historical.AsSpan(PalSaveLayout.InventorySlotOffset(knownSlot) + 2, 2), 150);
    int unknownOffset = PalSaveLayout.InventorySlotOffset(255);
    BinaryPrimitives.WriteUInt16LittleEndian(historical.AsSpan(unknownOffset, 2), 600);
    BinaryPrimitives.WriteUInt16LittleEndian(historical.AsSpan(unknownOffset + 2, 2), ushort.MaxValue);
    File.WriteAllBytes(SavePath(historicalRoot), historical);
    var preserved = Load(historicalRoot);
    Equal((ushort)150, preserved.GetInventory().Single(x => x.ItemId == 283).Amount, "old oversized known value loaded");
    Equal(ushort.MaxValue, preserved.GetInventory().Single(x => x.ItemId == 600).Amount, "unknown oversized value loaded");
    Check(!preserved.IsDirty, "historical values load without cleanup");
    preserved.Save();
    Same(historical, File.ReadAllBytes(SavePath(historicalRoot)), "historical values no-op round trip unchanged");
    Reject<ArgumentOutOfRangeException>(() => preserved.AddInventoryItem(283, 1), "old oversized value cannot grow");
    Reject<InvalidDataException>(() => preserved.SetInventorySlot(255, 600, 99), "unknown item edit rejected for UI error handling");
    Same(historical, preserved.ToArray(), "historical rejection leaves all bytes untouched");
    preserved.Cash++;
    preserved.Save();
    OnlyChanges(historical, File.ReadAllBytes(SavePath(historicalRoot)), (40, 4));
    preserved.SetInventorySlot(knownSlot, 283, 99);
    Equal((ushort)99, preserved.GetInventory().Single(x => x.ItemId == 283).Amount, "explicit reduction to 99 accepted");
}
void ConfiguredPaths()
{
    string root = Case("paths");
    Directory.Move(Path.Combine(root, "PAL"), Path.Combine(root, "资源"));
    Directory.CreateDirectory(Path.Combine(root, "进度"));
    Directory.Move(Path.Combine(root, "save"), Path.Combine(root, "进度", "save"));
    File.WriteAllText(Path.Combine(root, "sdlpal.cfg"), "# UTF8 configuration\r\nGamePath=./资源/\r\nSavePath=./进度/\r\n");
    string tools = Path.Combine(root, "Tools", "SaveEditor");
    Directory.CreateDirectory(tools);
    var location = GeshiSaveContract.Locate(tools);
    Equal(Path.Combine(root, "资源"), location.ResourceDirectory.TrimEnd(Path.DirectorySeparatorChar), "relative GamePath");
    Equal(Path.Combine(root, "进度", "save"), location.SaveDirectory, "relative SavePath plus engine save suffix");
    Equal(SaveFormat.GeshiEasyDos, PalSaveDocument.Load(Path.Combine(location.SaveDirectory, "3.rpg")).Format, "automatic configured discovery");
    File.WriteAllText(Path.Combine(root, "sdlpal.cfg"), $"GamePath={location.ResourceDirectory}\r\nSavePath={Path.Combine(root, "进度")}\r\n");
    Equal(location.ResourceDirectory, GeshiSaveContract.Locate(tools).ResourceDirectory, "absolute GamePath");
    string plainRoot = Case("plain-root");
    File.Move(Path.Combine(plainRoot, "sdlpal.cfg"), Path.Combine(plainRoot, "sdlpal.cfg.unused"));
    File.Move(Path.Combine(plainRoot, "pal.exe"), Path.Combine(plainRoot, "pal.exe.unused"));
    Equal(plainRoot, GeshiSaveContract.LocateForSave(SavePath(plainRoot)).GameDirectory, "resource-only package root");
    File.Copy(SavePath(plainRoot), Path.Combine(plainRoot, "3.rpg"));
    Equal(plainRoot, GeshiSaveContract.LocateForSave(Path.Combine(plainRoot, "3.rpg")).GameDirectory, "direct RPG beside PAL");
}
void NoPalDllState()
{
    string root = Case("no-paldll");
    string save = SavePath(root);
    string[] foreign = [save + ".pal98-ext-magics.json", save + ".pal98-custom-roles.json",
        Path.Combine(root, "palmod", "CustomRoles", "roles.json"), Path.Combine(root, "palmod", "Profiles", "current.json"),
        Path.Combine(root, "palmod", "PAL98_SKILL_REGISTRY.v1.json")];
    foreach (string path in foreign) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "invalid PALDLL state must be ignored"); }
    File.WriteAllText(Path.Combine(root, "PAL.dll"), "not a loadable DLL");
    var document = Load(root);
    Equal(6, document.RuntimeRoleCount, "custom role library not read");
    Equal(32, document.MagicCapacity, "DLL version not inspected");
    Check(!document.HasCustomRoleLibrary && !document.HasCustomRoleSaveState && !document.HasExtendedMagicSidecar, "no sidecar authority");
    Check(document.CustomRoleLibraryWarning is null && document.ExtendedMagicSidecarWarning is null, "unrelated files do not block load");
    Reject<NotSupportedException>(() => document.SetRoleDisplayName(5, "新姓名"), "no global name overrides");
    Reject<InvalidDataException>(() => document.SetFormat(SaveFormat.PalWin95), "no format switching");
    document.Cash++;
    document.Save();
    foreach (string path in foreign) Equal("invalid PALDLL state must be ignored", File.ReadAllText(path), "foreign companion untouched");
    document.CustomRoleLibrary.NativeRoleNames.Add(0, "no write");
    Reject<NotSupportedException>(() => document.Save(), "public legacy model cannot write library");
}
void ResourceMismatch()
{
    string root = Case("resource-guard");
    var document = Load(root);
    document.Cash++;
    foreach (string name in resourceNames)
    {
        string path = Path.Combine(root, "PAL", name);
        byte[] original = File.ReadAllBytes(path);
        byte[] altered = (byte[])original.Clone(); altered[altered.Length - 1] ^= 1;
        File.WriteAllBytes(path, altered);
        Reject<InvalidDataException>(() => Load(root), name + " load hash guard");
        Reject<InvalidDataException>(() => document.Save(), name + " save hash guard");
        Same(originalSaves[3], File.ReadAllBytes(SavePath(root)), "resource mismatch did not write save");
        File.WriteAllBytes(path, original);
    }
}
void InvalidSaves()
{
    string root = Case("invalid-save");
    foreach (int length in new[] { 176528, 183488, 184672, 185872, 194287 })
    {
        File.WriteAllBytes(SavePath(root), originalSaves[3].Take(length).ToArray());
        Reject<InvalidDataException>(() => Load(root), "wrong format or short file " + length);
    }
    foreach (var badHeader in new[] { (6, 5), (8, 0), (8, 300), (44, 6), (32, 3) })
    {
        byte[] raw = (byte[])originalSaves[3].Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(badHeader.Item1, 2), (ushort)badHeader.Item2);
        File.WriteAllBytes(SavePath(root), raw);
        Reject<InvalidDataException>(() => Load(root), "invalid header " + badHeader);
    }
    File.WriteAllBytes(SavePath(root), originalSaves[3]);
    var document = Load(root);
    string target = Path.Combine(root, "save", "foreign.rpg");
    File.WriteAllBytes(target, originalSaves[3].Take(176528).ToArray());
    byte[] before = File.ReadAllBytes(target);
    Reject<InvalidDataException>(() => document.Save(target), "save as cannot replace foreign format");
    Same(before, File.ReadAllBytes(target), "foreign target remains intact");
    foreach (var sample in foreignSaves)
    {
        File.WriteAllBytes(target, sample.Value);
        Reject<InvalidDataException>(() => PalSaveDocument.Load(target), "actual foreign RPG " + sample.Key);
    }
}
void ConcurrentSave()
{
    string root = Case("concurrent");
    var document = Load(root);
    document.Cash++;
    byte[] next = (byte[])originalSaves[3].Clone(); next[0] ^= 1;
    File.WriteAllBytes(SavePath(root), next);
    Reject<IOException>(() => document.Save(), "external progress changed");
    Same(next, File.ReadAllBytes(SavePath(root)), "external save retained");
    Check(document.IsDirty, "unsaved edits retained in memory");
}
void FailedWrite()
{
    string root = Case("write-failure");
    var document = Load(root);
    document.Cash++;
    File.WriteAllText(Path.Combine(root, "backup"), "occupied path");
    Reject<IOException>(() => document.Save(), "backup creation failure");
    Same(originalSaves[3], File.ReadAllBytes(SavePath(root)), "original retained on backup failure");
    Check(document.IsDirty, "edit retained after failure");
    using (var locked = new FileStream(SavePath(root), FileMode.Open, FileAccess.Read, FileShare.None))
        Reject<IOException>(() => document.Save(createBackup: false), "exclusive file lock");
    Same(originalSaves[3], File.ReadAllBytes(SavePath(root)), "original retained on lock failure");
    document.Save(createBackup: false);
    Equal(document.Cash, Load(root).Cash, "retry persists after failure");
}
