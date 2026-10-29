using System.Buffers.Binary;
using System.Text.Json;
using PalSaveEditor.Core;

internal static class ClassicProfileTests
{
    internal static int Run(string[] args)
    {
        if (!EditorEdition.IsGeshi || args.Length != 3 || Directory.Exists(args[2]))
            throw new ArgumentException("--classic-profiles DOS-native-root classic-v6-native-root fresh-output");
        int checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
            checks++; Console.WriteLine("PASS " + message);
        }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidDataException) { Check(true, message); return; }
            throw new Exception(message);
        }
        string[] ids = ["pal-dos", "pal98-classic-v6"];
        var roots = new List<string>();
        var saves = new List<string>();
        for (int edition = 0; edition < 2; edition++)
        {
            string id = ids[edition];
            int width = edition == 0 ? 12 : 14;
            int events = edition == 0 ? 5332 : 5077;
            int minimum = edition == 0 ? 184688 : 177928;
            var format = edition == 0 ? SaveFormat.GeshiEasyDos : SaveFormat.MaintenanceWin95;
            var other = edition == 0 ? SaveFormat.MaintenanceWin95 : SaveFormat.GeshiEasyDos;
            var location = GeshiSaveContract.Locate(args[edition]);
            string root = Path.Combine(args[2], id); roots.Add(root);
            string resource = Path.Combine(root, "PAL");
            string saveDirectory = Path.Combine(root, "save", id);
            Directory.CreateDirectory(resource); Directory.CreateDirectory(saveDirectory);
            foreach (string name in new[] { "SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG" })
                File.Copy(Path.Combine(location.ResourceDirectory, name), Path.Combine(resource, name));
            File.WriteAllText(Path.Combine(root, "sdlpal.cfg"),
                $"GamePath=./PAL/\nSavePath=./\nSaveDirectory=./save/{id}/\n");
            string marker = Path.Combine(saveDirectory, ".content-package");
            File.WriteAllText(marker, id + "\n");
            byte[] native = File.ReadAllBytes(Path.Combine(location.SaveDirectory, "249.rpg"));
            Check(native.Length == minimum, id + " actual engine save length");
            byte[] initial = native.Concat(new byte[] { 23, 87, 42 }).ToArray();
            // The editor must preserve the full reserved challenge/route region,
            // including fields it does not understand, and arbitrary tail bytes.
            for (int i = 0; i < 320; i++) initial[1408 + i] = (byte)(i * 17 + 3);
            string save = Path.Combine(saveDirectory, "1.rpg"); saves.Add(save);
            File.WriteAllBytes(save, initial);
            var document = PalSaveDocument.Load(save);
            var catalog = document.Catalog!;
            var profile = catalog.ContentProfile!;
            Check(document.Format == format && !document.Detection.IsHeuristic &&
                catalog.ObjectRecordSize == width && catalog.ObjectRecordCount == 565 &&
                catalog.RuntimeObjectRecordCount == 700 && catalog.EventObjectBytes == events * 32 &&
                profile.EventOffset == 5664 + 700 * width && profile.MinimumSaveLength == minimum,
                id + " verified content determines exact 700-object layout");
            Check(profile.Id == id && GeshiSaveContract.GetResourceCodePage(resource) == 936,
                id + " GBK content identity");
            using var snapshot = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(args[edition], "verification-content.json")));
            JsonElement catalogJson = snapshot.RootElement.GetProperty("catalog");
            int[] items = snapshot.RootElement.GetProperty("itemIds").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            Check(items.SequenceEqual(Enumerable.Range(1, 700).Where(catalog.IsItem)), id + " item catalog matches native bridge");
            foreach (int item in items)
            {
                JsonElement entry = catalogJson[item];
                Check(catalog.GetObjectName(item) == entry.GetProperty("name").GetString() &&
                    catalog.GetObjectFlags(item) == entry.GetProperty("data")[6].GetInt32(),
                    id + " name/flags " + item);
            }
            Check(items.Count(catalog.IsEquipment) == 106 && !catalog.IsItem(344) &&
                !catalog.IsMagic(24) && !catalog.IsMagic(331) && !catalog.IsMagic(391) &&
                !catalog.IsMagic(431) && !catalog.IsMagic(700) && catalog.IsMagic(394),
                id + " typed catalogs exclude internal/empty/extended objects");
            foreach (var role in snapshot.RootElement.GetProperty("roles").EnumerateArray())
            {
                int roleId = role.GetProperty("id").GetInt32();
                Check(document.GetEquipment(roleId).Select(e => (int)e.ItemId).SequenceEqual(
                    role.GetProperty("equipment").EnumerateArray().Select(v => v.GetInt32())) &&
                    document.GetMagics(roleId).Select(m => (int)m.MagicId).SequenceEqual(
                    role.GetProperty("skills").EnumerateArray().Select(v => v.GetInt32())),
                    id + " native equipment/skills role " + roleId);
            }
            var saved = document.Save();
            Check(File.ReadAllBytes(save).SequenceEqual(initial) &&
                File.ReadAllBytes(saved.BackupPath!).SequenceEqual(initial), id + " lossless no-op and backup");
            Reject(() => document.SetFormat(other), id + " no cross-layout conversion");
            Reject(() => PalSaveDocument.Load(save, other), id + " no forced foreign layout at open");
            Reject(() => document.SetFormat(SaveFormat.PalWin95), id + " no generic 600-object layout");
            Reject(() => SaveFormatDetector.Detect(initial.Length, catalog.WordDatByteLength,
                width == 12 ? 14 : 12, events * 32, profile), id + " wrong resource width rejected");
            document.SetFormat(SaveFormat.Auto);
            var expected = (byte[])initial.Clone();
            document.Cash = 123456;
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(40, 4), 123456);
            document.SetRoleField(0, RoleField.Level, 7);
            BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(PalSaveLayout.RoleFieldOffset(RoleField.Level, 0), 2), 7);
            document.SetEquipment(0, 0, 199);
            BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(PalSaveLayout.EquipmentOffset(0, 0), 2), 199);
            document.SetInventorySlot(0, 61, 99);
            BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(1728, 2), 61);
            BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(1730, 2), 99);
            BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(1732, 2), 0);
            int emptyMagic = Enumerable.Range(0, 32).First(slot =>
                BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(PalSaveLayout.MagicOffset(slot, 0), 2)) == 0);
            document.AddMagic(0, 394);
            BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(PalSaveLayout.MagicOffset(emptyMagic, 0), 2), 394);
            document.Save();
            Check(File.ReadAllBytes(save).SequenceEqual(expected) && PalSaveDocument.Load(save).ToArray().SequenceEqual(expected),
                id + " edits change only requested cash/level/equipment/item/magic bytes");
            Reject(() => document.AddMagic(0, 24), id + " throw helper cannot be learned");
            Reject(() => document.SetInventorySlot(1, 394, 1), id + " magic cannot enter inventory");
            // Existing unknown inventory stays opaque until that item is edited.
            BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(1728, 2), 650);
            File.WriteAllBytes(save, expected);
            document = PalSaveDocument.Load(save); document.Save();
            Check(File.ReadAllBytes(save).SequenceEqual(expected), id + " unknown inventory remains lossless");
            string shortSave = Path.Combine(saveDirectory, "600-object.rpg");
            File.WriteAllBytes(shortSave, native.Take(native.Length - 100 * width).ToArray());
            Reject(() => PalSaveDocument.Load(shortSave), id + " original 600-object save rejected");
            File.WriteAllText(marker, ids[1 - edition] + "\n");
            Reject(() => PalSaveDocument.Load(save), id + " foreign content marker rejected");
            File.Delete(marker);
            Reject(() => PalSaveDocument.Load(save), id + " missing content marker rejected");
            File.WriteAllText(marker, id + "\n");
            byte[] data = File.ReadAllBytes(Path.Combine(resource, "DATA.MKF"));
            data[100] ^= 1; File.WriteAllBytes(Path.Combine(resource, "DATA.MKF"), data);
            Reject(() => document.Save(), id + " resource mutation prevents write");
            Check(File.ReadAllBytes(save).SequenceEqual(expected), id + " rejected write leaves save intact");
            File.Copy(Path.Combine(location.ResourceDirectory, "DATA.MKF"), Path.Combine(resource, "DATA.MKF"), true);
            Check(File.ReadAllBytes(Path.Combine(location.SaveDirectory, "249.rpg")).SequenceEqual(native), id + " source remains untouched");
        }
        var held = PalSaveDocument.Load(saves[0]);
        Reject(() => held.SetCatalog(PalResourceCatalog.Load(roots[1])), "cannot change an open document to v6 resources");
        foreach (string name in new[] { "SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG" })
            File.Copy(Path.Combine(roots[1], "PAL", name), Path.Combine(roots[0], "PAL", name), true);
        Reject(() => held.Save(), "complete legal resource switch still prevents a stale document write");
        Console.WriteLine($"PASS classic profiles checks={checks}");
        return 0;
    }
}
