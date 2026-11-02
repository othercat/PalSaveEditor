using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using PalSaveEditor.Core;

// Refs othercat/sdlpal-lostedit#224. Only isolated small resource fixtures and
// synthesized RPGs are written; no executable or original save is launched.
internal static class Dreamspace35ProfileTests
{
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
        throw new InvalidOperationException("Expected rejection: " + label);
    }
    private static string Hash(byte[] value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant();
    }
    private static string Fixture(string source, string root, string id)
    {
        Directory.CreateDirectory(Path.Combine(root, "PAL"));
        Directory.CreateDirectory(Path.Combine(root, "save", id));
        foreach (string name in Names) File.Copy(Path.Combine(source, name), Path.Combine(root, "PAL", name));
        File.WriteAllText(Path.Combine(root, "sdlpal.cfg"), $"GamePath=./PAL/\nSavePath=./\nSaveDirectory=./save/{id}/\n");
        File.WriteAllText(Path.Combine(root, "save", id, ".content-package"), id + "\n");
        File.WriteAllText(Path.Combine(root, "geshi-game.v1.json"), JsonSerializer.Serialize(new
        {
            schema = "SDLPAL.ContentSelection.v1", package = id, gameplay_rule_id = id + "-rules-v1",
            gameplay_rule_revision = 1, gameplay_rule_sha256 = Hash(File.ReadAllBytes(Path.Combine(source, "GAMEPLAY.INI"))),
        }));
        return root;
    }
    internal static int Run(string[] args)
    {
        if (!EditorEdition.IsGeshi || args.Length != 3 || Directory.Exists(args[2]))
            throw new ArgumentException("--dreamspace35 readonly35-PAL readonly39-PAL fresh-output");
        string output = args[2];
        string root35 = Fixture(args[0], Path.Combine(output, "dreamspace35"), "dreamspace35");
        string root39 = Fixture(args[1], Path.Combine(output, "dreamspace39x-hard"), "dreamspace39x-hard");
        var c35 = PalResourceCatalog.Load(root35); var c39 = PalResourceCatalog.Load(root39);
        Check(c35.ContentProfile!.Id == "dreamspace35" && c39.ContentProfile!.Id == "dreamspace39x-hard", "35 and39 remain independent resource identities");
        var p35 = c35.ContentProfile!; var p39 = c39.ContentProfile!;
        Check(p35.ObjectCount == 589 && p35.EventCount == 5369 && p35.MagicCount == 115 &&
            p35.ObjectRecordSize == 12 && p35.MinimumSaveLength == p39.MinimumSaveLength, "35 retains the native700-object DOS RPG layout");
        Check(c35.GameplayBinding!.RuleId == "dreamspace35-rules-v1" && c35.GameplayBinding.Revision == 1 &&
            c35.GameplayBinding != c39.GameplayBinding, "35 complete formula binding cannot alias39");
        Check(p35.MagicIds!.Length == 85 && c35.IsMagic(315) && c35.IsMagic(588) &&
            !c35.IsMagic(395) && !c35.IsMagic(396) && c39.IsMagic(395), "35 legal player skills come from its own initial/learning/script tables");
        Check(c35.IsItem(564) && c35.IsItem(565) && c35.IsItem(579) && !c35.IsItem(572) && !c35.IsMagic(572), "35 equipment and enemy object unions remain distinct");
        Check(c35.GetObjectName(588) == "月影心剑" && c35.GetObjectName(572) == "林天南幻影", "35 names do not borrow39 object labels");
        byte[] bytes = new byte[p35.MinimumSaveLength + 17]; bytes[8] = 1;
        for (int i = PalSaveLayout.PoisonOffset; i < PalSaveLayout.InventoryOffset; i++) bytes[i] = (byte)(i * 17 + 3);
        for (int i = bytes.Length - 17; i < bytes.Length; i++) bytes[i] = (byte)(i * 11);
        string save = Path.Combine(root35, "save", "dreamspace35", "1.rpg"); File.WriteAllBytes(save, bytes);
        var document = PalSaveDocument.Load(save); var result = document.Save();
        Check(File.ReadAllBytes(save).SequenceEqual(bytes) && File.ReadAllBytes(result.BackupPath!).SequenceEqual(bytes), "35 no-op save and backup preserve every RPG byte");
        document.Cash = uint.MaxValue; document.Save();
        byte[] expected = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(40, 4), uint.MaxValue);
        Check(File.ReadAllBytes(save).SequenceEqual(expected), "35 cash edit preserves objects events reserved region and unknown tail");
        document.AddMagic(0, 588); document.Save();
        Check(PalSaveDocument.Load(save).GetMagics(0).Any(m => m.MagicId == 588), "35 own late spell can be added and reloaded");
        byte[] current = File.ReadAllBytes(save); int count = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length;
        string rule = Path.Combine(root35, "PAL", "GAMEPLAY.INI"); byte[] originalRules = File.ReadAllBytes(rule);
        File.Copy(Path.Combine(root39, "PAL", "GAMEPLAY.INI"), rule, true);
        Reject(() => document.Save(), "39 rule file cannot authorize35 save");
        Check(File.ReadAllBytes(save).SequenceEqual(current) && Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length == count, "rule mismatch rejects before backup or RPG writes");
        File.WriteAllBytes(rule, originalRules);
        foreach (string name in Names) File.Copy(Path.Combine(args[1], name), Path.Combine(root35, "PAL", name), true);
        Reject(() => document.Save(), "an open35 document cannot be rebound to39 resources");
        Check(File.ReadAllBytes(save).SequenceEqual(current), "cross-version resource change never writes the old RPG");
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        { passed = true, checks, independent35Profile = true, ownPlayerMagicCount = 85, rpgLayoutUnchanged = true, gamesLaunched = 0 }));
        Console.WriteLine("PASS dreamspace35 editor checks=" + checks);
        return 0;
    }
}
