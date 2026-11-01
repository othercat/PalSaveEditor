using PalSaveEditor.Core;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;

internal static class GameplayRuleTests
{
    internal static int Run(string[] args)
    {
        if (!EditorEdition.IsGeshi || args.Length != 2 || Directory.Exists(args[1]))
            throw new ArgumentException("--gameplay-rules native-20-snapshot-root fresh-output");
        int checks = 0, contents = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
        void Reject(Action action, string label)
        { try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException) { Check(true, label); return; } throw new Exception(label); }
        var oldRoots = new List<string>();
        foreach (string source in Directory.GetDirectories(args[0]).OrderBy(p => p))
        {
            string snapshotPath = Path.Combine(source, "verification-gameplay-snapshot.json");
            if (!File.Exists(snapshotPath)) continue;
            string id = Path.GetFileName(source); contents++;
            string root = Path.Combine(args[1], id), pal = Path.Combine(root, "PAL"), saves = Path.Combine(root, "save", id);
            Directory.CreateDirectory(pal); Directory.CreateDirectory(saves);
            foreach (string name in new[] { "SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG" })
                File.Copy(Path.Combine(source, "pal", name), Path.Combine(pal, name));
            File.WriteAllText(Path.Combine(root, "sdlpal.cfg"), $"GamePath=./PAL/\nSavePath=./\nSaveDirectory=./save/{id}/\n");
            File.WriteAllText(Path.Combine(saves, ".content-package"), id + "\n");
            var catalog = PalResourceCatalog.Load(root); var profile = catalog.ContentProfile!;
            Check(profile.Id == id && catalog.GameplayBinding!.RuleId is null, id + " legacy four-resource identity retained");
            var bytes = new byte[profile.MinimumSaveLength + 7]; bytes[8] = 1;
            for (int i = 1408; i < 1728; ++i) bytes[i] = (byte)(i * 13 + 7);
            Encoding.ASCII.GetBytes("PWU1").CopyTo(bytes, 1432); bytes[1436] = 1; bytes[1437] = bytes[1438] = bytes[1439] = 0;
            for (int i = bytes.Length - 7; i < bytes.Length; i++) bytes[i] = (byte)(i * 19);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(PalSaveLayout.RoleFieldOffset(RoleField.MaxHp, 0), 2), 60000);
            string save = Path.Combine(saves, "1.rpg"); File.WriteAllBytes(save, bytes);
            var legacy = PalSaveDocument.Load(save); legacy.Save();
            Check(File.ReadAllBytes(save).SequenceEqual(bytes), id + " legacy no-op preserves high stats, reserved region and unknown tail");
            string rules = Path.Combine(pal, "GAMEPLAY.INI"), selection = Path.Combine(root, "geshi-game.v1.json");
            byte[] ruleBytes = File.ReadAllBytes(Path.Combine(source, "pal", "GAMEPLAY.INI")); File.WriteAllBytes(rules, ruleBytes);
            int beforeCount = Directory.GetFiles(saves).Length;
            Reject(() => legacy.Save(), id + " adding rules invalidates an already-open legacy document");
            Reject(() => legacy.SetCatalog(PalResourceCatalog.Load(root)), id + " SetCatalog cannot rebind an open document");
            Check(Directory.GetFiles(saves).Length == beforeCount && File.ReadAllBytes(save).SequenceEqual(bytes), id + " rejection precedes backup and RPG mutation");
            var standalone = PalSaveDocument.Load(save); standalone.Save();
            Check(standalone.Catalog!.GameplayBinding!.Sha256 is not null && File.ReadAllBytes(save).SequenceEqual(bytes), id + " standalone PAL formula file is explicitly bound");
            using var snapshot = JsonDocument.Parse(File.ReadAllBytes(snapshotPath));
            var native = snapshot.RootElement;
            var fields = new Dictionary<string, object?> {
                ["schema"]="SDLPAL.ContentSelection.v1",["package"]=id,
                ["gameplay_rule_id"]=native.GetProperty("gameplayRuleId").GetString(),
                ["gameplay_rule_revision"]=native.GetProperty("gameplayRuleRevision").GetInt32(),
                ["gameplay_rule_sha256"]=native.GetProperty("gameplayRuleSha256").GetString()
            };
            void Selection() => File.WriteAllText(selection, JsonSerializer.Serialize(fields));
            Selection(); Reject(() => standalone.Save(), id + " selection presence is part of the open-document binding");
            var bound = PalSaveDocument.Load(save); var binding = bound.Catalog!.GameplayBinding!;
            Check(binding.RuleId == (string)fields["gameplay_rule_id"]! && binding.Sha256 == (string)fields["gameplay_rule_sha256"]! && binding.Revision == 1,
                id + " editor identity matches the actual native engine");
            bound.Cash = uint.MaxValue; bound.Save();
            var edited = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(edited.AsSpan(40,4), uint.MaxValue);
            Check(File.ReadAllBytes(save).SequenceEqual(edited), id + " amount edit preserves every other RPG byte including PWU1 and unknown tail");
            int filesBeforeReject = Directory.GetFiles(saves).Length;
            foreach (string key in new[] { "gameplay_rule_id", "gameplay_rule_revision", "gameplay_rule_sha256" })
            {
                object? value = fields[key]; fields.Remove(key); Selection();
                Reject(() => PalSaveDocument.Load(save), id + " partial selection rejects " + key);
                fields[key] = value; Selection();
            }
            foreach (object? value in new object?[] { null, "1", 0, -1, 1.0m, true, 2147483648L })
            {
                fields["gameplay_rule_revision"] = value; Selection();
                Reject(() => bound.Save(), id + " noncanonical revision rejected " + value);
            }
            fields["gameplay_rule_revision"] = 1; Selection();
            string goodSelection = File.ReadAllText(selection);
            File.WriteAllText(selection, "{\"gameplay_rule_revision\":1," + goodSelection.Substring(1));
            Reject(() => bound.Save(), id + " duplicate selection property rejected"); Selection();
            File.Delete(rules); Reject(() => bound.Save(), id + " removed rule file cannot fall back to legacy");
            File.Delete(selection); Reject(() => bound.Save(), id + " removing both binding files does not rebind old document");
            File.WriteAllBytes(rules, ruleBytes); Selection();
            foreach (byte[] invalid in new[] { ruleBytes.Take(ruleBytes.Length - 1).ToArray(), ruleBytes.Concat(Encoding.ASCII.GetBytes("Unknown=1\n")).ToArray(), Encoding.ASCII.GetBytes("Schema=PAL.GameplayRules.v1\n") })
            { File.WriteAllBytes(rules, invalid); Reject(() => PalSaveDocument.Load(save), id + " complete approved bytes required"); }
            File.WriteAllBytes(rules, ruleBytes);
            Check(Directory.GetFiles(saves).Length == filesBeforeReject && File.ReadAllBytes(save).SequenceEqual(edited), id + " all rejected changes preserve saves and backups");
            var type = typeof(PalResourceCatalog).Assembly.GetType("PalSaveEditor.Core.MaintenanceGameplayRules", true)!;
            using ((IDisposable)type.GetMethod("AcquireForSave", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object?[] { bound.Catalog, save })!)
            {
                foreach (string file in new[] { rules, selection, Path.Combine(root, "sdlpal.cfg"), Path.Combine(pal, "DATA.MKF") })
                    Reject(() => { using var stream = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }, id + " write transaction denies resource mutation " + Path.GetFileName(file));
            }
            string foreignRoot = Path.Combine(args[1], id + "-legacy-target"), foreignPal = Path.Combine(foreignRoot, "PAL");
            Directory.CreateDirectory(foreignPal);
            foreach (string name in new[] { "SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG" }) File.Copy(Path.Combine(pal, name), Path.Combine(foreignPal, name));
            File.WriteAllText(Path.Combine(foreignRoot, "sdlpal.cfg"), "GamePath=./PAL/\nSavePath=./\n");
            string target = Path.Combine(foreignRoot, "save", "2.rpg");
            Reject(() => bound.Save(target), id + " save-as rejects same content with a different formula binding");
            File.Delete(Path.Combine(foreignRoot, "sdlpal.cfg"));
            Reject(() => bound.Save(target), id + " offline PAL copy without config cannot bypass save-as binding");
            File.WriteAllBytes(Path.Combine(foreignPal, "GAMEPLAY.INI"), ruleBytes);
            Reject(() => bound.Save(target), id + " offline PAL formula-only target retains its distinct selection state");
            Check(!File.Exists(target), id + " rejected save-as creates no RPG");
            File.Delete(selection); File.Delete(rules);
            var reopened = PalSaveDocument.Load(save); reopened.Save();
            Check(File.ReadAllBytes(save).SequenceEqual(edited), id + " explicit reopening permits the now-selected legacy contract");
            File.WriteAllText(selection, JsonSerializer.Serialize(new { schema="SDLPAL.ContentSelection.v1", package=id }));
            var legacySelected = PalSaveDocument.Load(save); legacySelected.Save();
            File.WriteAllText(selection, JsonSerializer.Serialize(new { schema="SDLPAL.ContentSelection.v1", package="other-content" }));
            Reject(() => legacySelected.Save(), id + " changed legacy selection content rejected");
            File.WriteAllText(selection, JsonSerializer.Serialize(new { schema="unknown", package=id }));
            Reject(() => PalSaveDocument.Load(save), id + " unknown legacy selection schema rejected");
            File.Delete(selection);
            string cfg = Path.Combine(root, "sdlpal.cfg"), originalCfg = File.ReadAllText(cfg);
            var discovered = GeshiSaveContract.Locate(root);
            string alternate = Path.Combine(root, "alternate"); Directory.CreateDirectory(alternate);
            foreach (string name in new[] { "SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG" }) File.Copy(Path.Combine(pal, name), Path.Combine(alternate, name));
            File.WriteAllText(cfg, originalCfg.Replace("./PAL/", "./alternate/"));
            bool denied = false;
            try { using var lease = (IDisposable)type.GetMethod("Acquire", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { discovered })!; }
            catch (TargetInvocationException ex) when (ex.InnerException is IOException) { denied = true; }
            Check(denied, id + " pointer switch between discovery and locking rejected");
            File.WriteAllText(cfg, originalCfg);
            Check(File.ReadAllBytes(Path.Combine(source, "pal", "GAMEPLAY.INI")).SequenceEqual(ruleBytes), id + " native input bytes preserved");
            oldRoots.Add(root);
        }
        Check(contents == 20, "all twenty content contracts exercised");
        Console.WriteLine("PASS gameplay rules contents=" + contents + " checks=" + checks); return 0;
    }
}
