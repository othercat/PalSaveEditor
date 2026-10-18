using System.Security.Cryptography;
using PalSaveEditor.Core;

internal static class ResourceIdentityTests
{
    private static readonly string[] Names = ["SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG"];
    private static int checks;

    internal static int Run(string[] args)
    {
        try
        {
            if (!EditorEdition.IsGeshi || args.Length != 4)
                throw new ArgumentException("Dedicated build: --resource-matrix original-root simplified-root pal98-root new-output");
            string output = Path.GetFullPath(args[3]);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new.");
            Directory.CreateDirectory(output);
            var locations = args.Take(3).Select(GeshiSaveContract.Locate).ToArray();
            var sources = locations.Select(location => Names.Select(name => File.ReadAllBytes(Path.Combine(location.ResourceDirectory, name))).ToArray()).ToArray();
            byte[] sourceSave = File.ReadAllBytes(Path.Combine(locations[2].SaveDirectory, "3.rpg"));
            Check(Hash(sources[2][3]) == "880aff05741da8d00a152b7a22661ad509817f36e7bacf52d3fae4cd8f1aceb4", "actual halfwidth dialogue fixture");

            for (int word = 0; word < 2; word++)
            for (int message = 0; message < 3; message++)
            {
                string root = Fixture(output, $"pair-{word}-{message}", sources[2], sourceSave);
                string save = Path.Combine(root, "save", "3.rpg");
                var openDocument = PalSaveDocument.Load(save);
                openDocument.Cash++;
                File.WriteAllBytes(Path.Combine(root, "PAL", "WORD.DAT"), sources[word][2]);
                File.WriteAllBytes(Path.Combine(root, "PAL", "M.MSG"), sources[message][3]);
                bool valid = word == 0 ? message == 0 : message != 0;
                if (valid)
                {
                    Check(GeshiSaveContract.GetResourceCodePage(Path.Combine(root, "PAL")) == (word == 0 ? 950 : 936), "correct complete-edition codepage");
                    var catalog = PalResourceCatalog.Load(root);
                    Check(catalog.GetObjectName(344) == "铜钱镖" && catalog.GetObjectName(584) == "金刚武神咒", "real names remain simplified");
                    var document = PalSaveDocument.Load(save);
                    Check(sourceSave.AsSpan().SequenceEqual(document.ToArray()), "accepted edition loads original bytes");
                    var result = document.Save();
                    Check(sourceSave.AsSpan().SequenceEqual(File.ReadAllBytes(save)), "no-op save exact");
                    Check(result.BackupPath is not null && sourceSave.AsSpan().SequenceEqual(File.ReadAllBytes(result.BackupPath)), "no-op backup exact");
                }
                else
                {
                    Reject(() => GeshiSaveContract.ValidateResources(Path.Combine(root, "PAL")), "mixed resource identity");
                    Reject(() => PalResourceCatalog.Load(root), "mixed catalog");
                    Reject(() => PalSaveDocument.Load(save), "mixed document load");
                    Reject(() => openDocument.Save(), "mixed resource change blocks already-open document save");
                    Check(sourceSave.AsSpan().SequenceEqual(File.ReadAllBytes(save)) && openDocument.IsDirty, "rejected save preserves disk and unsaved edits");
                    Check(!Directory.Exists(Path.Combine(root, "backup")), "rejection occurs before backup/write");
                }
            }
            for (int file = 0; file < Names.Length; file++)
            {
                string root = Fixture(output, "changed-" + Names[file], sources[2], sourceSave);
                string save = Path.Combine(root, "save", "3.rpg");
                var document = PalSaveDocument.Load(save);
                document.Cash++;
                byte[] changed = (byte[])sources[2][file].Clone(); changed[changed.Length - 1] ^= 1;
                File.WriteAllBytes(Path.Combine(root, "PAL", Names[file]), changed);
                Reject(() => PalSaveDocument.Load(save), "unknown digest blocks load: " + Names[file]);
                Reject(() => document.Save(), "unknown digest blocks save: " + Names[file]);
                Check(sourceSave.AsSpan().SequenceEqual(File.ReadAllBytes(save)), "unknown digest leaves target unchanged");
            }
            for (int edition = 0; edition < locations.Length; edition++)
            for (int file = 0; file < Names.Length; file++)
                Check(sources[edition][file].AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(locations[edition].ResourceDirectory, Names[file]))), "source resource unchanged");
            Check(sourceSave.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(locations[2].SaveDirectory, "3.rpg"))), "source save unchanged");
            Console.WriteLine($"PASS {checks} real-resource edition matrix/load/save checks; no GUI or game started.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static string Fixture(string output, string name, byte[][] resources, byte[] save)
    {
        string root = Path.Combine(output, name);
        Directory.CreateDirectory(Path.Combine(root, "PAL"));
        Directory.CreateDirectory(Path.Combine(root, "save"));
        File.WriteAllText(Path.Combine(root, "sdlpal.cfg"), "GamePath=./PAL/\r\nSavePath=./\r\n");
        File.WriteAllBytes(Path.Combine(root, "pal.exe"), []); // Marker only; never executed.
        for (int i = 0; i < Names.Length; i++) File.WriteAllBytes(Path.Combine(root, "PAL", Names[i]), resources[i]);
        File.WriteAllBytes(Path.Combine(root, "save", "3.rpg"), save);
        return root;
    }
    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        checks++;
    }
    private static void Reject(Action action, string label)
    {
        try { action(); } catch (InvalidDataException) { checks++; return; }
        throw new InvalidOperationException("Expected InvalidDataException: " + label);
    }
}
