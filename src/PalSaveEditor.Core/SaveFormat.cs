namespace PalSaveEditor.Core;

public enum SaveFormat
{
    Auto = 0,
    PalDos,
    PalWin95,
    Dream220Dos,
}

public static class SaveFormatExtensions
{
    public static string GetDisplayName(this SaveFormat format) => format switch
    {
        SaveFormat.Auto => "自动识别",
        SaveFormat.PalDos => "仙剑奇侠传 DOS",
        SaveFormat.PalWin95 => "仙剑奇侠传 98 柔情版",
        SaveFormat.Dream220Dos => "仙剑 DOS·梦幻 2.20",
        _ => format.ToString(),
    };
}
