using PalSaveEditor.Core;
using System.Buffers.Binary;

internal static class MaintenanceProfileTests
{
    internal static int Run(string[] args, bool expanded = false, bool geshiVariants = false)
    {
        if ((geshiVariants ? args.Length != 5 : expanded ? args.Length != 10 : args.Length is not (4 or 5)) || Directory.Exists(args[args.Length - 1])) throw new ArgumentException("Read-only native fixture roots and one fresh output directory required.");
        string[] expected = geshiVariants ? ["geshi-hard", "geshi-nightwolf", "geshi-fanmade", "geshi-fanmade-entertainment"] : expanded ? ["dream211", "dream211-pudu", "dream211-yueying", "dream211-yueying-rat", "mogai10", "tianqi20", "hunqian167-easy", "hunqian167-hard", "hunqian-branch"] : args.Length == 5 ? ["dream222-normal", "dream222-hard", "dreamspace39x-hard", "dream22-puppet100"] : ["dream222-normal", "dream222-hard", "dreamspace39x-hard"];
        int[] objectCounts = geshiVariants ? [588,588,588,588] : [589,589,595,595,565,586,575,575,565];
        int[] eventCounts = geshiVariants ? [5632,5632,5632,5632] : [5369,5369,5376,5376,5332,5403,5332,5332,5332];
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        void Reject(Action action, string message) { try { action(); } catch (InvalidDataException) { Check(true, message); return; } throw new Exception(message); }
        var roots = new List<string>();
        for (int i = 0; i < expected.Length; ++i)
        {
            var location = GeshiSaveContract.Locate(args[i]);
            string root = Path.Combine(args[args.Length - 1], expected[i]); roots.Add(root);
            Directory.CreateDirectory(Path.Combine(root, "PAL")); Directory.CreateDirectory(Path.Combine(root, "save", expected[i]));
            foreach (string file in new[] { "SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG" })
                File.Copy(Path.Combine(location.ResourceDirectory, file), Path.Combine(root, "PAL", file));
            File.WriteAllText(Path.Combine(root, "sdlpal.cfg"), "GamePath=./PAL/\nSavePath=./\nSaveDirectory=./save/"+expected[i]+"/\n");
            string marker = Path.Combine(root,"save",expected[i],".content-package"); File.WriteAllText(marker,expected[i]+"\n");
            byte[] source = File.ReadAllBytes(Path.Combine(location.SaveDirectory, "249.rpg"));
            byte[] bytes = source.Concat(new byte[] { 23, 87, 42 }).ToArray();
            string save = Path.Combine(root, "save", expected[i], "1.rpg"); File.WriteAllBytes(save, bytes);
            var document = PalSaveDocument.Load(save); var catalog = document.Catalog!;
            Check(catalog.ContentProfile!.Id == expected[i] && catalog.ObjectRecordCount == (expanded || geshiVariants ? objectCounts[i] : 589) && catalog.EventObjectBytes == (expanded || geshiVariants ? eventCounts[i] : 5369) * 32, "actual native save selects " + expected[i]);
            if (expanded || geshiVariants) {
                Check(catalog.IsItem(200) && !catalog.IsItem(296) && catalog.IsMagic(296), "equipment and magic union types remain distinct");
                if (expected[i] == "tianqi20") Check(catalog.IsItem(389) && !catalog.IsMagic(389), "Tianqi manual remains an item; its obsolete invalid magic record cannot be learned");
                if (expected[i].StartsWith("dream211-yueying")) Check(catalog.IsItem(576) && !catalog.IsItem(579) && catalog.IsItem(575) == (expected[i] == "dream211-yueying"), "Moon and Rat keep their separately audited item catalogs");
            } else {
                Check(catalog.IsItem(564) && catalog.IsItem(565) && catalog.IsItem(579) && !catalog.IsItem(569) && !catalog.IsItem(296), "resource-audited items exclude enemy magic aliases");
                Check(catalog.IsMagic(588) == (i == 2), "unused v1.8 588 cannot be added as a learned spell");
            }
            var result = document.Save();
            Check(File.ReadAllBytes(save).SequenceEqual(bytes) && File.ReadAllBytes(result.BackupPath!).SequenceEqual(bytes), "no-op and backup preserve every source/tail byte");
            document.Cash = 123456;
            document.Save(); byte[] changed = File.ReadAllBytes(save);
            Check(BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(40,4)) == 123456 && bytes.Where((b,n) => n < 40 || n >= 44).SequenceEqual(changed.Where((b,n) => n < 40 || n >= 44)), "cash edit preserves complete objects/events/tail");
            // The source can contain a magic ID from an enemy steal bug; no-op must preserve it.
            BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(1728,2), (ushort)(i == 2 ? 296 : 569));
            BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(1730,2), 1);
            File.WriteAllBytes(save, changed); document = PalSaveDocument.Load(save); document.Save();
            Check(File.ReadAllBytes(save).SequenceEqual(changed), "existing anomalous inventory remains opaque and lossless");
            var held = PalSaveDocument.Load(save);
            byte[] data = File.ReadAllBytes(Path.Combine(root, "PAL", "DATA.MKF"));
            data[100] ^= 1; File.WriteAllBytes(Path.Combine(root, "PAL", "DATA.MKF"), data);
            Reject(() => held.Save(), "mutated resources reject save before writes");
            Check(File.ReadAllBytes(save).SequenceEqual(changed), "failed resource validation does not alter RPG");
            File.Copy(Path.Combine(location.ResourceDirectory, "DATA.MKF"), Path.Combine(root, "PAL", "DATA.MKF"), true);
            Check(File.ReadAllBytes(Path.Combine(location.SaveDirectory, "249.rpg")).SequenceEqual(source), "native source stays unchanged");
            File.WriteAllText(marker,"geshi-easy\n"); Reject(()=>PalSaveDocument.Load(save),"wrong directory identity rejects before opening");
            File.Delete(marker); Reject(()=>PalSaveDocument.Load(save),"missing directory identity rejects before opening");
            File.WriteAllText(marker,expected[i]+"\n");
        }
        string first = Path.Combine(roots[0], "save", expected[0], "1.rpg"); var opened = PalSaveDocument.Load(first);
        File.Copy(Path.Combine(roots[1], "PAL", "DATA.MKF"), Path.Combine(roots[0], "PAL", "DATA.MKF"), true);
        Reject(() => opened.Save(), "valid normal-to-hard resource switch cannot reinterpret an opened save");
        string foreign = Path.Combine(roots[2], "save", "dream222-normal", "1.rpg"); Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
        File.Copy(Path.Combine(roots[2], "save", expected[2], "1.rpg"), foreign);
        Reject(() => PalSaveDocument.Load(foreign), "foreign profile save directory cannot borrow active resources");
        Console.WriteLine($"PASS maintenance profiles checks={checks}"); return 0;
    }
}
