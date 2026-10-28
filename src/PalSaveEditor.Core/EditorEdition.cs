namespace PalSaveEditor.Core;

/// <summary>The dedicated executable has no runtime switch back to PALDLL.</summary>
public static class EditorEdition
{
#if GESHI_EDITION
    public static bool IsGeshi => true;
    public const string ProductName = "SDLPal 存档编辑器";
#else
    public static bool IsGeshi => false;
    public const string ProductName = "仙剑98编辑器";
#endif
    public static int MaximumPartyCount => IsGeshi ? 5 : 3;
}
