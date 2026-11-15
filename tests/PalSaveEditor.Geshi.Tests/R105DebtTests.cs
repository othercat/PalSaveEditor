using System.Buffers.Binary;
using System.Text;
using PalSaveEditor.Core;

internal static class R105DebtTests
{
    internal static int Run(string[] args)
    {
        if((args.Length!=2 && args.Length!=3) || Directory.Exists(args[1])) throw new ArgumentException("--r105-debt readonly-resource-root fresh-output [readonly-native-root]");
        int checks=0, contents=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
        foreach(string source in Directory.GetDirectories(args[0]))
        {
            string cid=Path.GetFileName(source), pal=Path.Combine(source,"PAL");
            if(!File.Exists(Path.Combine(pal,"SSS.MKF")))continue;
            string root=Path.Combine(args[1],cid), target=Path.Combine(root,"PAL");Directory.CreateDirectory(target);
            foreach(string name in new[]{"SSS.MKF","DATA.MKF","WORD.DAT","M.MSG","GAMEPLAY.INI"}) File.Copy(Path.Combine(pal,name),Path.Combine(target,name));
            File.WriteAllText(Path.Combine(root,"sdlpal.cfg"),"GamePath=./PAL/\nSavePath=./\n");
            var profile=PalResourceCatalog.Load(root).ContentProfile!;
            if(args.Length==3)
            {
                string native=Path.Combine(args[2],cid,"core-off","save","98.rpg"),copy=Path.Combine(root,"native.rpg");
                byte[] original=File.ReadAllBytes(native);File.WriteAllBytes(copy,original);
                var actual=PalSaveDocument.Load(copy,gameDirectory:root);
                Check(actual.CashBalance==-500,cid+" reads the actual engine's negative RPG balance");
                actual.Save();Check(File.ReadAllBytes(copy).SequenceEqual(original),cid+" native RPG no-op is byte-exact");
                actual.CashBalance=-750;actual.Save();
                byte[] edited=File.ReadAllBytes(copy);
                Check(PalSaveDocument.Load(copy,gameDirectory:root).CashBalance==-750,cid+" native RPG debt edit reopens");
                for(int i=0;i<original.Length;i++)if(i<1544 || i>=1548)Check(edited[i]==original[i],cid+" native edit preserves byte "+i);
                Check(BinaryPrimitives.ReadUInt32LittleEndian(edited.AsSpan(1544,4))==750,cid+" native engine debt wire agrees with editor");
            }
            byte[] bytes=new byte[profile.MinimumSaveLength+7];bytes[8]=1;
            for(int i=1408;i<1728;i++)bytes[i]=(byte)(i*13+7);
            Array.Clear(bytes,1536,48);Encoding.ASCII.GetBytes("PALM105\u0001").CopyTo(bytes,1536);
            bytes[1548]=0xcd;bytes[1549]=2;bytes[1552]=0x7e;bytes[1556]=3;bytes[1558]=6;bytes[1560]=31;
            string save=Path.Combine(root,"1.rpg");File.WriteAllBytes(save,bytes);
            foreach(long balance in new[]{-4294967295L,-500L,0L,2147483648L,4294967295L})
            {
                var doc=PalSaveDocument.Load(save,gameDirectory:root);doc.CashBalance=balance;doc.Save();
                var reopen=PalSaveDocument.Load(save,gameDirectory:root);Check(reopen.CashBalance==balance,cid+" signed balance roundtrip");
                Check(reopen.SwordFlightName=="御剑飞行",cid+" virtual flight name survives without occupying a native spell slot");
                byte[] actual=File.ReadAllBytes(save);
                Check(actual[1548]==0xcd && actual[1549]==2 && actual[1552]==0x7e && actual[1556]==3 && actual[1558]==6 && actual[1560]==31,cid+" independent story/flight/farewell bytes preserved");
                for(int i=0;i<bytes.Length;i++) if(!(i>=40&&i<44) && !(i>=1544&&i<1548)) Check(actual[i]==bytes[i],cid+" unrelated byte "+i);
                Check(BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(40,4))==(balance>=0?(uint)balance:0),cid+" legacy unsigned cash stays compatible");
            }
            var positive=PalSaveDocument.Load(save,gameDirectory:root);positive.CashBalance=-50;positive.Cash=100;positive.Save();
            Check(PalSaveDocument.Load(save,gameDirectory:root).CashBalance==100,cid+" legacy Cash setter explicitly clears old debt");
            contents++;
        }
        Check(contents==21,"all twenty-one resource profiles checked");
        File.WriteAllText(Path.Combine(args[1],"result.txt"),$"PASS contents={contents} checks={checks}\n");return 0;
    }
}
