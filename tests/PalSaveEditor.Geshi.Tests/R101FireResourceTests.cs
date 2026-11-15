using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PalSaveEditor.Core;

// Refs othercat/sdlpal-lostedit#222. Inputs contain only five resource files
// extracted from the real content ZIPs; RPGs are synthesized in fresh outputs.
internal static class R101FireResourceTests
{
    private const string Content = "dreamspace39x-hard";
    private const string OldRuleHash = "e49ac1f745b19e0c2a482089b6c3f2d5497a73d2d4a80b13649f088cab48b6c7";
    private const string NewRuleHash = "458c13dc68cd173ada1b690fd9a193163efe0427975a0470666fd5202441fa40";
    private static readonly string[] Names = ["SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG", "GAMEPLAY.INI"];
    private static int checks;

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        checks++;
        Console.WriteLine("PASS " + label);
    }

    private static void Reject(Action action, string label)
    {
        try { action(); }
        catch (InvalidDataException) { Check(true, label); return; }
        throw new InvalidOperationException("Expected InvalidDataException: " + label);
    }

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static void Selection(string root, string id, string hash)
    {
        File.WriteAllText(Path.Combine(root, "geshi-game.v1.json"), JsonSerializer.Serialize(new
        {
            schema = "SDLPAL.ContentSelection.v1", package = id,
            gameplay_rule_id = id + "-rules-v1", gameplay_rule_revision = 1,
            gameplay_rule_sha256 = hash,
        }));
    }

    private static string Fixture(string source, string output, string id)
    {
        Directory.CreateDirectory(Path.Combine(output, "PAL"));
        string saves = Path.Combine(output, "save", id);
        Directory.CreateDirectory(saves);
        foreach (string name in Names)
            File.Copy(Path.Combine(source, "PAL", name), Path.Combine(output, "PAL", name));
        File.WriteAllText(Path.Combine(output, "sdlpal.cfg"), $"GamePath=./PAL/\nSavePath=./\nSaveDirectory=./save/{id}/\n");
        File.WriteAllText(Path.Combine(saves, ".content-package"), id + "\n");
        Selection(output, id, Hash(File.ReadAllBytes(Path.Combine(output, "PAL", "GAMEPLAY.INI"))));
        return output;
    }

    private static string SaveFixture(string root)
    {
        var profile = PalResourceCatalog.Load(root).ContentProfile!;
        byte[] bytes = new byte[profile.MinimumSaveLength + 11];
        bytes[8] = 1;
        for (int i = PalSaveLayout.PoisonOffset; i < PalSaveLayout.InventoryOffset; i++)
            bytes[i] = (byte)(i * 13 + 7);
        for (int i = bytes.Length - 11; i < bytes.Length; i++) bytes[i] = (byte)(i * 19);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(PalSaveLayout.RoleFieldOffset(RoleField.MaxHp, 0), 2), 60000);
        string path = Path.Combine(root, "save", profile.Id, "1.rpg");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    internal static int Run(string[] args)
    {
        if (!EditorEdition.IsGeshi || args.Length != 2 || Directory.Exists(args[1]) || File.Exists(args[1]))
            throw new ArgumentException("--r101-fire-resource read-only-five-file-fixtures fresh-output");
        string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        var protectedSources = Directory.GetFiles(input, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        Directory.CreateDirectory(output);
        string[] oldSources = Directory.GetDirectories(Path.Combine(input, "r100")).OrderBy(path => path).ToArray();
        Check(oldSources.Length == 20, "all twenty r100 content contracts supplied");
        string? oldDreamRoot = null;
        foreach (string source in oldSources)
        {
            string id = Path.GetFileName(source);
            string root = Fixture(source, Path.Combine(output, "r100", id), id);
            var catalog = PalResourceCatalog.Load(root);
            Check(catalog.ContentProfile!.Id == id && catalog.GameplayBinding!.RuleId == id + "-rules-v1" &&
                catalog.GameplayBinding.Revision == 1, id + " complete r100 identity/rules retained");
            string path = SaveFixture(root);
            byte[] original = File.ReadAllBytes(path);
            var document = PalSaveDocument.Load(path);
            var result = document.Save();
            Check(File.ReadAllBytes(path).SequenceEqual(original) &&
                File.ReadAllBytes(result.BackupPath!).SequenceEqual(original), id + " no-op preserves RPG, opaque region and tail");
            if (id == Content) oldDreamRoot = root;
        }
        Check(oldDreamRoot is not null, "r100 dreamspace fixture present");
        string newSource = Path.Combine(input, "r101", Content);
        string newRoot = Fixture(newSource, Path.Combine(output, "r101", Content), Content);
        var oldCatalog = PalResourceCatalog.Load(oldDreamRoot!);
        var newCatalog = PalResourceCatalog.Load(newRoot);
        var oldProfile = oldCatalog.ContentProfile!;
        var newProfile = newCatalog.ContentProfile!;
        Check(oldProfile.Id == newProfile.Id && oldProfile.ObjectCount == newProfile.ObjectCount &&
            oldProfile.EventCount == newProfile.EventCount && oldProfile.MagicCount == newProfile.MagicCount &&
            oldProfile.ObjectRecordSize == newProfile.ObjectRecordSize && oldProfile.EventOffset == newProfile.EventOffset &&
            oldProfile.MinimumSaveLength == newProfile.MinimumSaveLength && oldProfile.Format == newProfile.Format,
            "r101 resource alias retains the entire existing RPG layout");
        Check(oldProfile.ItemIds!.SequenceEqual(newProfile.ItemIds!) && oldProfile.MagicIds!.SequenceEqual(newProfile.MagicIds!),
            "r101 resource alias retains the exact item/magic catalogs");
        Check(oldCatalog.GameplayBinding!.Sha256 == OldRuleHash && newCatalog.GameplayBinding!.Sha256 == NewRuleHash &&
            oldCatalog.GameplayBinding.RuleId == newCatalog.GameplayBinding.RuleId &&
            oldCatalog.GameplayBinding.Revision == newCatalog.GameplayBinding.Revision,
            "new complete binding and original complete binding are both accepted at revision1");
        foreach (string name in Names.Skip(1).Take(3))
            Check(File.ReadAllBytes(Path.Combine(oldDreamRoot!, "PAL", name)).SequenceEqual(File.ReadAllBytes(Path.Combine(newRoot, "PAL", name))),
                name + " unchanged in the actual new patch");
        string newSave = SaveFixture(newRoot);
        byte[] newBytes = File.ReadAllBytes(newSave);
        var newDocument = PalSaveDocument.Load(newSave);
        newDocument.Save();
        Check(File.ReadAllBytes(newSave).SequenceEqual(newBytes), "new resource no-op is byte-exact");
        newDocument.Cash = uint.MaxValue;
        newDocument.Save();
        byte[] expected = (byte[])newBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(40, 4), uint.MaxValue);
        Check(File.ReadAllBytes(newSave).SequenceEqual(expected), "new resource amount edit preserves all other RPG bytes");

        string newRules = Path.Combine(newRoot, "PAL", "GAMEPLAY.INI");
        byte[] correctRules = File.ReadAllBytes(newRules);
        File.Copy(Path.Combine(oldDreamRoot!, "PAL", "GAMEPLAY.INI"), newRules, true);
        Reject(() => PalSaveDocument.Load(newSave), "new scripts cannot borrow the old approved rule file");
        File.WriteAllBytes(newRules, correctRules);
        Selection(newRoot, Content, OldRuleHash);
        Reject(() => newDocument.Save(), "old selection hash cannot authorize the new complete rule file");
        Selection(newRoot, Content, NewRuleHash);
        string oldRules = Path.Combine(oldDreamRoot!, "PAL", "GAMEPLAY.INI");
        byte[] originalOldRules = File.ReadAllBytes(oldRules);
        File.WriteAllBytes(oldRules, correctRules);
        Reject(() => PalResourceCatalog.Load(oldDreamRoot!), "old scripts cannot borrow the new approved rule file");
        File.WriteAllBytes(oldRules, originalOldRules);

        string oldSave = Path.Combine(oldDreamRoot!, "save", Content, "1.rpg");
        var heldOld = PalSaveDocument.Load(oldSave);
        byte[] oldBytes = File.ReadAllBytes(oldSave);
        int fileCount = Directory.GetFiles(oldDreamRoot!, "*", SearchOption.AllDirectories).Length;
        foreach (string name in Names)
            File.Copy(Path.Combine(newSource, "PAL", name), Path.Combine(oldDreamRoot!, "PAL", name), true);
        Selection(oldDreamRoot!, Content, NewRuleHash);
        Reject(() => heldOld.Save(), "valid old-to-new resource switch requires reopening an existing document");
        Check(File.ReadAllBytes(oldSave).SequenceEqual(oldBytes) &&
            Directory.GetFiles(oldDreamRoot!, "*", SearchOption.AllDirectories).Length == fileCount,
            "resource-switch rejection occurs before backup or RPG writes");
        var reopenedNew = PalSaveDocument.Load(oldSave);
        reopenedNew.Save();
        Check(File.ReadAllBytes(oldSave).SequenceEqual(oldBytes), "the old RPG opens and saves unchanged with the new resource contract");
        foreach (string name in Names)
            File.Copy(Path.Combine(input, "r100", Content, "PAL", name), Path.Combine(oldDreamRoot!, "PAL", name), true);
        Selection(oldDreamRoot!, Content, OldRuleHash);
        Reject(() => reopenedNew.Save(), "valid new-to-old resource switch also requires reopening");
        var reopenedOld = PalSaveDocument.Load(oldSave);
        reopenedOld.Save();
        Check(File.ReadAllBytes(oldSave).SequenceEqual(oldBytes), "original r100 contract still reopens and preserves the same RPG");
        foreach (var pair in protectedSources)
            Check(File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value), "source fixture remains read-only: " + pair.Key.Substring(input.Length));
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        {
            passed = true, checks, r100Contents = 20, r101NewResource = true,
            originalContractsPreserved = true, sameRpgLayout = true, gamesLaunched = 0,
        }));
        Console.WriteLine("PASS r101 fire-resource identity and binding checks=" + checks);
        return 0;
    }
}
