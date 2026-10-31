using PalSaveEditor.Core;

internal static class R97ImportedTests
{
    private static int checks;
    private static void Check(bool ok,string label) {if(!ok)throw new Exception(label);++checks;}
    public static int Run(string[] args)
    {
        if(args.Length!=2 || Directory.Exists(args[1]))throw new ArgumentException("native matrix and new evidence directory required");
        Directory.CreateDirectory(args[1]);
        foreach(string contentRoot in Directory.GetDirectories(Path.Combine(args[0],"cases")))
        {
            string content=Path.GetFileName(contentRoot);
            string native=Directory.GetFiles(Path.Combine(contentRoot,"all"),"99.rpg",SearchOption.AllDirectories).Single();
            string root=Path.Combine(args[1],content),pal=Path.Combine(root,"PAL"),save=Path.Combine(root,"save",content);
            Directory.CreateDirectory(pal);Directory.CreateDirectory(save);
            foreach(string name in new[]{"SSS.MKF","DATA.MKF","WORD.DAT","M.MSG","GAMEPLAY.INI"})
                File.Copy(Path.Combine(args[0],"resources",content,"PAL",name),Path.Combine(pal,name));
            File.WriteAllText(Path.Combine(root,"sdlpal.cfg"),$"GamePath=./PAL/\nSaveDirectory=./save/{content}/\n");
            File.WriteAllText(Path.Combine(save,".content-package"),content+"\n");
            byte[] original=File.ReadAllBytes(native);string path=Path.Combine(save,"1.rpg");File.WriteAllBytes(path,original);
            var document=PalSaveDocument.Load(path);var catalog=document.Catalog!;
            Check(catalog.ContentProfile!.Id==content,content+" identity");
            Check(catalog.GetObjectName(600)=="梦蛇" && catalog.GetObjectName(628)=="乾坤一掷",content+" original skill names");
            for(int i=600;i<629;++i)Check(catalog.IsMagic(i) && !catalog.IsItem(i) && catalog.SearchObjects(i.ToString(),filter:catalog.IsMagic).Any(v=>v.Id==i),content+" selectable imported spell "+i);
            for(int i=629;i<=634;++i)Check(!catalog.IsMagic(i) && !catalog.IsItem(i),content+" dependency stays hidden "+i);
            Check(catalog.IsItem(640) && !catalog.IsMagic(640) && !catalog.IsEquipment(640) && catalog.GetObjectName(640)=="万能麻烦",content+" universal item type and name");
            Check(document.GetInventory().Any(v=>v.ItemId==640 && v.DisplayName=="万能麻烦"),content+" native universal inventory is readable");
            document.Save();Check(File.ReadAllBytes(path).SequenceEqual(original),content+" no-op save byte identity");
            document.Cash+=17;document.Save();byte[] edited=File.ReadAllBytes(path);int objectBytes=catalog.ObjectRecordSize*700;
            Check(original.AsSpan(PalSaveLayout.PoisonOffset,320).SequenceEqual(edited.AsSpan(PalSaveLayout.PoisonOffset,320)),content+" all320 reserved bytes preserved");
            Check(original.AsSpan(PalSaveLayout.ObjectOffset,objectBytes).SequenceEqual(edited.AsSpan(PalSaveLayout.ObjectOffset,objectBytes)),content+" all700 object records preserved");
            Check(original.Where((b,i)=>i<40 || i>=44).SequenceEqual(edited.Where((b,i)=>i<40 || i>=44)),content+" one cash edit changes only its field");
            document.AddMagic(0,600);
            var item=document.GetInventory().Single(v=>v.ItemId==640);document.SetInventorySlot(item.Slot,640,2);document.Save();
            var reload=PalSaveDocument.Load(path);
            Check(reload.GetMagics(0).Any(v=>v.MagicId==600 && v.DisplayName=="梦蛇") && reload.GetInventory().Any(v=>v.ItemId==640 && v.Amount==2),content+" spell addition and quantity edit persist");
            byte[] after=File.ReadAllBytes(path);
            Check(original.AsSpan(PalSaveLayout.PoisonOffset,320).SequenceEqual(after.AsSpan(PalSaveLayout.PoisonOffset,320)) && original.AsSpan(PalSaveLayout.ObjectOffset,objectBytes).SequenceEqual(after.AsSpan(PalSaveLayout.ObjectOffset,objectBytes)),content+" explicit import edits preserve ownership and dynamic object bindings");
            Array.Clear(after,PalSaveLayout.PoisonOffset+112,16);string ordinary=Path.Combine(save,"2.rpg");File.WriteAllBytes(ordinary,after);
            var plain=PalSaveDocument.Load(ordinary);
            Check(!plain.Catalog!.IsMagic(600) && !plain.Catalog.IsItem(640),content+" IDs alone do not claim ownership");
            plain.Save();Check(File.ReadAllBytes(ordinary).SequenceEqual(after),content+" unowned unknown IDs remain opaque and preserved");
            after[PalSaveLayout.PoisonOffset+112]=(byte)'P';after[PalSaveLayout.PoisonOffset+113]=(byte)'C';after[PalSaveLayout.PoisonOffset+114]=(byte)'L';after[PalSaveLayout.PoisonOffset+115]=(byte)'1';after[PalSaveLayout.PoisonOffset+116]=2;
            File.WriteAllBytes(ordinary,after);Check(!PalSaveDocument.Load(ordinary).Catalog!.IsMagic(600),content+" unknown marker version remains opaque");
        }
        File.WriteAllText(Path.Combine(args[1],"summary.json"),$"{{\"passed\":true,\"checks\":{checks},\"reservedAndObjectsPreserved\":true}}\n");
        Console.WriteLine("PASS r97 imported catalog/save checks="+checks);return 0;
    }
}
