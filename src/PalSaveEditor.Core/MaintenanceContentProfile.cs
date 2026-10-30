using System.Security.Cryptography;

namespace PalSaveEditor.Core;

// Immutable per-document contract. A switch in the launcher must never change
// the meaning of an already-open save. Refs othercat/sdlpal-lostedit#120 #121.
public sealed record MaintenanceContentProfile(string Id, string Name, int ObjectCount,
    int EventCount, int MagicCount, int[]? ItemIds = null, int[]? MagicIds = null)
{
    public int MinimumSaveLength => GeshiSaveContract.EventOffset + EventCount * 32;
    public bool IsItem(int id) => ItemIds is null ? GeshiSaveContract.IsItem(id) : Array.IndexOf(ItemIds, id) >= 0;
    public bool IsMagic(int id) => MagicIds is null ? GeshiSaveContract.IsMagic(id) : Array.IndexOf(MagicIds, id) >= 0;
    public static readonly MaintenanceContentProfile Geshi = new("geshi-easy", "隔世追凶·简单", 588, 5632, 108);
    private static readonly (MaintenanceContentProfile Profile, string[] Hashes)[] Dreams =
    [
        (new("dream222-normal", "仙剑梦幻2.2.2 普通", 589, 5369, 114,
            [61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293, 294, 564, 565, 579],
            [295, 296, 297, 298, 299, 300, 301, 302, 303, 304, 305, 306, 307, 308, 309, 310, 312, 313, 314, 315, 316, 317, 318, 319, 320, 321, 322, 323, 325, 326, 327, 328, 329, 330, 332, 333, 334, 335, 337, 338, 339, 340, 341, 342, 344, 348, 349, 350, 351, 352, 353, 355, 356, 357, 358, 359, 362, 363, 364, 365, 367, 368, 369, 371, 372, 373, 374, 375, 376, 377, 379, 380, 381, 382, 383, 384, 385, 390, 566, 567, 568, 569, 570, 571, 580]),
            ["5e272f8d12e38a7ec3dbb7ed8a61710e3a6d526daaa5978fefd7069b0e22e57b", "0bc9b33ea9b7d35e7ff93aab2ac49a5c5b12a087c9242673a16027b1ab03c8b6", "be2375c1d3b4300b124cc051aea940059fb6a25953f7448ab9a68367c7f34df1", "db43c384bf3e568c18bc26f491b599cca35db5a1710bccc66e4e612031933e59"]),
        (new("dream222-hard", "仙剑梦幻2.2.2 高难", 589, 5369, 114,
            [61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293, 294, 564, 565, 579],
            [295, 296, 297, 298, 299, 300, 301, 302, 303, 304, 305, 306, 307, 308, 309, 310, 312, 313, 314, 315, 316, 317, 318, 319, 320, 321, 322, 323, 325, 326, 327, 328, 329, 330, 332, 333, 334, 335, 337, 338, 339, 340, 341, 342, 344, 348, 349, 350, 351, 352, 353, 355, 356, 357, 358, 359, 362, 363, 364, 365, 367, 368, 369, 371, 372, 373, 374, 375, 376, 377, 379, 380, 381, 382, 383, 384, 385, 390, 566, 567, 568, 569, 570, 571, 580]),
            ["5e272f8d12e38a7ec3dbb7ed8a61710e3a6d526daaa5978fefd7069b0e22e57b", "f8d08926a6ec406143b733c2082d678fd7b7a49f9c877f5812910ee06b51e3f3", "be2375c1d3b4300b124cc051aea940059fb6a25953f7448ab9a68367c7f34df1", "db43c384bf3e568c18bc26f491b599cca35db5a1710bccc66e4e612031933e59"]),
        (new("dreamspace39x-hard", "梦时空3.9x 高难", 589, 5369, 115,
            [61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293, 294, 564, 565, 579],
            [295, 296, 297, 298, 299, 300, 301, 302, 303, 304, 305, 306, 307, 308, 309, 310, 312, 313, 314, 315, 316, 317, 318, 319, 320, 321, 322, 323, 325, 326, 327, 328, 329, 330, 332, 333, 334, 335, 337, 338, 339, 340, 341, 342, 344, 348, 349, 350, 351, 352, 353, 355, 356, 357, 358, 359, 362, 363, 364, 365, 367, 368, 369, 371, 372, 373, 374, 375, 377, 379, 380, 381, 382, 383, 384, 385, 390, 395, 396, 566, 567, 568, 569, 570, 571, 580, 588]),
            ["64641ce5984f82f208a42d633aa84981155267a59ab6e78c445859b67a32e8af", "9bff164acc41c953427d8a1611030ce3f635331099c5cc01ce21212f306bbbd7", "2717fd9f663462ad022f3feb42f4b9cc6441dcc76c29798a51196bc69c73ca21", "aa97fbde3511651cf36b1e6602fc28c40dc473992bce6253b8a61b3bf7b2d538"]),
    ];
    internal static MaintenanceContentProfile? FindDream(string directory)
    {
        string[] names = ["SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG"];
        var hashes = new List<string>();
        foreach (string name in names)
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) return null;
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            hashes.Add(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant());
        }
        return Dreams.FirstOrDefault(d => d.Hashes.SequenceEqual(hashes)).Profile;
    }
    public static MaintenanceContentProfile Resolve(string directory)
    {
        var dream = FindDream(directory);
        if (dream is not null) return dream;
        GeshiSaveContract.GetResourceCodePage(directory);
        return Geshi;
    }
    internal static void ValidateLocation(string path, PalResourceCatalog catalog)
    {
        var root = GeshiSaveContract.FindGameRoot(path);
        if (root is null) return; // An explicitly selected external copy has no embedded identity.
        var location = GeshiSaveContract.Locate(root);
        string marker = Path.Combine(location.SaveDirectory, ".content-package");
        if (location.ContentScopedSave && !File.Exists(marker))
            throw new InvalidDataException("内容存档目录缺少身份标识，请用配置工具重新准备内容。");
        if (File.Exists(marker) && File.ReadAllText(marker).TrimEnd('\r', '\n') != catalog.ContentProfile?.Id)
            throw new InvalidDataException("存档目录标识与当前内容不一致，未打开或写入存档。");
        string full = Path.GetFullPath(path);
        string profiles = Path.Combine(root, "profiles") + Path.DirectorySeparatorChar;
        string legacy = Path.Combine(root, "save") + Path.DirectorySeparatorChar;
        if ((full.StartsWith(profiles, StringComparison.OrdinalIgnoreCase) || full.StartsWith(legacy, StringComparison.OrdinalIgnoreCase)) &&
            !full.StartsWith(location.SaveDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("该存档属于其他内容目录。请在配置工具切换到对应内容后再打开，未修改存档。");
        if (Resolve(location.ResourceDirectory).Id != catalog.ContentProfile?.Id)
            throw new InvalidDataException("当前游戏内容已切换，请重新打开对应存档。");
    }
}
