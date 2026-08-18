namespace PalSaveEditor.Core;

public sealed record SaveFormatDetection(
    SaveFormat Format,
    string Reason,
    bool IsHeuristic,
    IReadOnlyList<SaveFormat> CompatibleFormats);

public static class SaveFormatDetector
{
    public const int KnownPal98Length = 176_528;
    public const int KnownPalDosLength = 183_488;
    public const int KnownDream220Length = 184_672;
    public const int KnownDream220Win95Length = 185_872;

    public static SaveFormatDetection Detect(
        int fileLength,
        int? wordDatLength = null,
        int objectRecordSize = 0,
        int eventObjectBytes = 0)
    {
        if (fileLength < PalSaveLayout.DosEventObjectOffset)
        {
            throw new InvalidDataException(
                $"文件仅 {fileLength:N0} 字节，小于可编辑 PAL 存档的 DOS 固定区域 {PalSaveLayout.DosEventObjectOffset:N0} 字节。");
        }

        if (wordDatLength == 5_892 && objectRecordSize == PalSaveLayout.DosObjectRecordSize &&
            (eventObjectBytes == 0 || fileLength == PalSaveLayout.DosEventObjectOffset + eventObjectBytes))
        {
            return new(
                SaveFormat.Dream220Dos,
                "配套 WORD.DAT、DOS 对象表和事件区均符合梦幻 2.20 资源。",
                false,
                [SaveFormat.Dream220Dos, SaveFormat.PalDos]);
        }

        if (wordDatLength == 5_650 && objectRecordSize == PalSaveLayout.WinObjectRecordSize &&
            eventObjectBytes == 171_808 && fileLength == PalSaveLayout.WinEventObjectOffset + eventObjectBytes)
        {
            return new(
                SaveFormat.Dream220Win95,
                "存档采用 Win95 固定区，配套对象表和事件区符合 PALDLL 梦幻 2.20 移植版。",
                false,
                [SaveFormat.Dream220Win95, SaveFormat.PalWin95]);
        }

        return fileLength switch
        {
            KnownPalDosLength => new(
                SaveFormat.PalDos,
                "文件长度匹配已验证的 DOS 原版存档。",
                true,
                [SaveFormat.PalDos]),
            KnownPal98Length => new(
                SaveFormat.PalWin95,
                "文件长度匹配已验证的 98 柔情版存档。",
                true,
                [SaveFormat.PalWin95]),
            KnownDream220Length => new(
                SaveFormat.Dream220Dos,
                "文件长度匹配已验证的梦幻 2.20 DOS 存档；建议同时选择游戏资料目录复核。",
                true,
                [SaveFormat.Dream220Dos, SaveFormat.PalDos]),
            KnownDream220Win95Length => new(
                SaveFormat.Dream220Win95,
                "文件长度匹配已验证的 PALDLL 梦幻 2.20 移植版存档；建议同时选择游戏资料目录复核。",
                true,
                [SaveFormat.Dream220Win95, SaveFormat.PalWin95]),
            _ when (fileLength - PalSaveLayout.DosEventObjectOffset) % 32 == 0 => new(
                SaveFormat.PalDos,
                "固定前缀后为完整的 32 字节 DOS 事件记录；若这是梦幻 2.20，请选择其游戏资料目录复核。",
                true,
                [SaveFormat.PalDos, SaveFormat.Dream220Dos]),
            _ when (fileLength - PalSaveLayout.WinEventObjectOffset) % 32 == 0 => new(
                SaveFormat.PalWin95,
                "固定前缀后为完整的 32 字节 Win95 事件记录；PALDLL 梦幻 2.20 移植版也使用此布局。",
                true,
                [SaveFormat.PalWin95, SaveFormat.Dream220Win95]),
            _ => throw new InvalidDataException(
                "文件长度既不符合 DOS（12 字节对象记录），也不符合 Win95（14 字节对象记录）的 32 字节事件边界。"),
        };
    }
}
