namespace PalSaveEditor.Core;

public enum SaveFormat
{
    Auto = 0,
    PalDos,
    PalWin95,
    Dream220Dos,
    Dream220Win95,
}

public static class SaveFormatExtensions
{
    public static string GetDisplayName(this SaveFormat format) => format switch
    {
        SaveFormat.Auto => "自动识别",
        SaveFormat.PalDos => "仙剑 DOS（原版布局）",
        SaveFormat.PalWin95 => "仙剑 98（Win95 布局）",
        SaveFormat.Dream220Dos => "梦幻 2.20（DOS 兼容布局）",
        SaveFormat.Dream220Win95 => "梦幻2.2显血版（PALDLL / Win95 布局）",
        _ => format.ToString(),
    };
}
