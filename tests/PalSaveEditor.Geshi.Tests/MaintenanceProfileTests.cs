using PalSaveEditor.Core;
using System.Buffers.Binary;

internal static class MaintenanceProfileTests
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4 || Directory.Exists(args[3])) throw new ArgumentException("Three read-only native fixture roots and one fresh output directory required.");
        string[] expected = ["dream222-normal", "dream222-hard", "dreamspace39x-hard"];
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        void Reject(Action action, string message) { try { action(); } catch (InvalidDataException) { Check(true, message); return; } throw new Exception(message); }
        var roots = new List<string>();
        for (int i = 0; i < 3; ++i)
        {
            var location = GeshiSaveContract.Locate(args[i]);
            string root = Path.Combine(args[3], expected[i]); roots.Add(root);
            Directory.CreateDirectory(Path.Combine(root, "PAL")); Directory.CreateDirectory(Path.Combine(root, "save", expected[i]));
            foreach (string file in new[] { "SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG" })
                File.Copy(Path.Combine(location.ResourceDirectory, file), Path.Combine(root, "PAL", file));
            File.WriteAllText(Path.Combine(root, "sdlpal.cfg"), "GamePath=./PAL/\nSavePath=./\nSaveDirectory=./save/"+expected[i]+"/\n");
            string marker = Path.Combine(root,"save",expected[i],".content-package"); File.WriteAllText(marker,expected[i]+"\n");
            byte[] source = File.ReadAllBytes(Path.Combine(location.SaveDirectory, "249.rpg"));
            byte[] bytes = source.Concat(new byte[] { 23, 87, 42 }).ToArray();
            string save = Path.Combine(root, "save", expected[i], "1.rpg"); File.WriteAllBytes(save, bytes);
            var document = PalSaveDocument.Load(save); var catalog = document.Catalog!;
            Check(catalog.ContentProfile!.Id == expected[i] && catalog.ObjectRecordCount == 589 && catalog.EventObjectBytes == 5369 * 32, "actual native save selects " + expected[i]);
            Check(catalog.IsItem(564) && catalog.IsItem(565) && catalog.IsItem(579) && !catalog.IsItem(569) && !catalog.IsItem(296), "resource-audited items exclude enemy magic aliases");
            Check(catalog.IsMagic(588) == (i == 2), "unused v1.8 588 cannot be added as a learned spell");
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
