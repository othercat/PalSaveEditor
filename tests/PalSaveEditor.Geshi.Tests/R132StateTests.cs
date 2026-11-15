using System.Text;
using PalSaveEditor.Core;
internal static class R132StateTests
{
    internal static int Run(string[] args)
    {
        string source=args[0],output=args[1];Directory.CreateDirectory(output);int contents=0,checks=0;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
        foreach(string run in Directory.GetDirectories(source)) {
            string native=Path.Combine(run,"save","245.rpg");if(!File.Exists(native))continue;
            string cid=Path.GetFileName(run),save=Path.Combine(output,cid+".rpg");byte[] original=File.ReadAllBytes(native);
            Check(Encoding.ASCII.GetString(original,1584,8)=="PALMA351",cid+" native extension offset");
            // Preserve a completed village flag while unrelated cash is edited.
            Encoding.ASCII.GetBytes("PALM105\u0001").CopyTo(original,1536);original[1549]|=4;File.WriteAllBytes(save,original);
            var doc=PalSaveDocument.Load(save,gameDirectory:run);
            Check(doc.Ma35StateDescription?.Contains("34/35")==true,cid+" native feed count");
            Check(doc.Ma35StateDescription?.Contains("已首次出航")==true,cid+" native first-voyage state");
            Check(doc.PeachVillagePermanentlyOpen,cid+" new maintenance flag");
            doc.Save();Check(File.ReadAllBytes(save).SequenceEqual(original),cid+" byte-exact no-op");
            doc.CashBalance=75;doc.Save();byte[] edited=File.ReadAllBytes(save);
            for(int i=0;i<original.Length;i++)if(!(i>=40&&i<44)&&!(i>=1544&&i<1548))Check(edited[i]==original[i],cid+" unrelated byte "+i);
            edited[1594]^=1;File.WriteAllBytes(save,edited);doc=PalSaveDocument.Load(save,gameDirectory:run);
            Check(doc.Ma35StateDescription?.Contains("校验无效")==true,cid+" corrupt extension visible");
            doc.Save();Check(File.ReadAllBytes(save).SequenceEqual(edited),cid+" corrupt bytes not silently rewritten");contents++;
        }
        Check(contents==22,"all22 actual native RPGs");File.WriteAllText(Path.Combine(output,"result.txt"),$"PASS {contents} contents; {checks} checks; no game or network\n");return 0;
    }
}
