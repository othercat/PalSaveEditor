using PalSaveEditor.Core;

internal static class ChallengeWuhouTests
{
    private static int checks;
    private static void Check(bool ok, string why)
    { if (!ok) throw new Exception(why); ++checks; Console.WriteLine("PASS " + why); }
    private static void Reject(Action action, string why)
    { try { action(); } catch (InvalidDataException) { Check(true, why); return; } throw new Exception(why); }
    public static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("native hero matrix root and fresh output required");
        string[] contents = ["geshi-easy", "geshi-hard", "geshi-nightwolf", "geshi-fanmade", "geshi-fanmade-entertainment", "tianqi20"];
        string[] names = ["梦蛇", "夺魂", "元灵归心术", "风神", "雷神", "雪妖", "火神", "山神", "赎魂", "灵血咒"];
        Directory.CreateDirectory(args[1]);
        foreach (string content in contents)
        {
            string nativeRoot = Path.Combine(args[0], content + "-wuhou-32");
            var location = GeshiSaveContract.Locate(nativeRoot);
            string root = Path.Combine(args[1], content), resources = Path.Combine(root, "PAL");
            string saves = Path.Combine(root, "save", content);
            Directory.CreateDirectory(resources); Directory.CreateDirectory(saves);
            foreach (string name in new[] {"SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG"})
                File.Copy(Path.Combine(location.ResourceDirectory, name), Path.Combine(resources, name));
            File.WriteAllText(Path.Combine(root, "sdlpal.cfg"), $"GamePath=./PAL/\nSaveDirectory=./save/{content}/\n");
            File.WriteAllText(Path.Combine(saves, ".content-package"), content + "\n");
            byte[] native = File.ReadAllBytes(Path.Combine(location.SaveDirectory, "99.rpg"));
            for (int mode = 0; mode < 2; ++mode)
            {
                byte[] source = mode == 0 ? native : File.ReadAllBytes(Path.Combine(args[0], content + "-imported-save-rules-off", "save", "100.rpg"));
                byte[] bytes = source.Concat(new byte[]{73, 25, 193}).ToArray();
                if (mode == 1) Check(bytes.Skip(1408).Take(24).All(b => b == 0), content + " actual rules-off save retains independent PWU1 only");
                string path = Path.Combine(saves, (mode + 1) + ".rpg"); File.WriteAllBytes(path, bytes);
                var document = PalSaveDocument.Load(path);
                Check(document.GetMagics(0).Take(10).Select(m => m.DisplayName).SequenceEqual(names), content + " ten private skill names mode=" + mode);
                for (int i = 0; i < 10; i++) Check(document.Catalog!.IsMagic(690 + i) &&
                    document.Catalog.SearchObjects(names[i], filter: document.Catalog.IsMagic).Any(e => e.Id == 690 + i), content + " searchable private skill " + i);
                Check(Enumerable.Range(680, 5).All(id => !document.Catalog!.IsMagic(id) && !document.Catalog.IsItem(id)) &&
                    Enumerable.Range(690, 10).All(id => !document.Catalog!.IsItem(id)), content + " hidden animation carriers and skills are not items");
                var result = document.Save();
                Check(File.ReadAllBytes(path).SequenceEqual(bytes) && File.ReadAllBytes(result.BackupPath!).SequenceEqual(bytes), content + " no-op and backup preserve every byte");
                ushort[] original = document.GetMagics(0).Select(m => m.MagicId).ToArray();
                document.ReplaceMagics(0, original.Where(id => id != 699)); document.AddMagic(0, 699);
                Check(document.GetMagics(0).Select(m => m.MagicId).SequenceEqual(original), content + " delete and re-add imported skill");
                Reject(() => document.AddMagic(0, 680), content + " no auxiliary magic selection");
                document.Cash += 17; document.Save();
                byte[] after = File.ReadAllBytes(path);
                Check(bytes.Where((v, i) => i < 40 || i >= 44).SequenceEqual(after.Where((v, i) => i < 40 || i >= 44)), content + " explicit edit touches only cash");
                Check(PalSaveDocument.Load(path).GetMagics(0).Select(m => m.DisplayName).SequenceEqual(names), content + " edited save reload names");
                var rawCatalog = PalResourceCatalog.Load(root);
                document.SetCatalog(rawCatalog);
                Check(document.Catalog!.IsMagic(699) && !rawCatalog.IsMagic(699), content + " SetCatalog preserves ownership without mutating shared catalog");
            }
            byte[] plain = (byte[])native.Clone(); Array.Clear(plain, 1432, 8);
            string plainPath = Path.Combine(saves, "3.rpg"); File.WriteAllBytes(plainPath, plain);
            var ordinary = PalSaveDocument.Load(plainPath);
            Check(!ordinary.Catalog!.IsMagic(690) && ordinary.Catalog.GetObjectName(690) != names[0], content + " ordinary save does not inherit challenge catalog");
            Reject(() => ordinary.AddMagic(0, 690), content + " no private additions without ownership");
            ordinary.Save(); Check(File.ReadAllBytes(plainPath).SequenceEqual(plain), content + " unknown existing skill IDs preserved");
            plain[1432] = (byte)'P'; plain[1433] = (byte)'W'; plain[1434] = (byte)'U'; plain[1435] = (byte)'1'; plain[1436] = 2;
            File.WriteAllBytes(plainPath, plain);
            Check(!PalSaveDocument.Load(plainPath).Catalog!.IsMagic(690), content + " future ownership version remains opaque");
        }
        Console.WriteLine("PASS challenge Wuhou checks=" + checks); return 0;
    }
}
