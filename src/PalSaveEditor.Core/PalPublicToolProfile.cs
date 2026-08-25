namespace PalSaveEditor.Core;

/// <summary>
/// Sanitized profile facts shared by the public save editor and checker.
/// This contract deliberately contains no PALDLL hooks, fixed addresses,
/// authentication material, local paths, or original resource bytes.
/// </summary>
public sealed record PalPublicToolCredit(int Order, string Name, string Role, string Basis);

public sealed record PalPublicToolProfile(
    string Schema,
    string ProfileId,
    string ProfileVersion,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<PalPublicToolCredit> Credits,
    SaveFormat SaveFormat,
    int ObjectRecordSize,
    int SavedObjectRecordCount,
    int ResourceObjectRecordCount,
    int EventObjectRecordCount,
    int ExpectedSaveLength,
    int WordDatByteLength)
{
    public void ValidateDescriptor(string displayName, string saveNamespace)
    {
        if (!string.Equals(displayName, DisplayName, StringComparison.Ordinal) ||
            !string.Equals(saveNamespace, ProfileId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{ProfileId}@{ProfileVersion} 的公开 profile 名称或 save namespace 不匹配。");
        }
    }

    public void ValidateResources(
        int wordDatByteLength,
        int objectRecordSize,
        int resourceObjectRecordCount,
        int eventObjectBytes)
    {
        int expectedEventBytes = checked(EventObjectRecordCount * PalSaveLayout.EventObjectRecordSize);
        if (wordDatByteLength != WordDatByteLength ||
            objectRecordSize != ObjectRecordSize ||
            resourceObjectRecordCount != ResourceObjectRecordCount ||
            eventObjectBytes != expectedEventBytes ||
            ExpectedSaveLength != PalSaveLayout.WinEventObjectOffset + expectedEventBytes)
        {
            throw new InvalidDataException(
                $"{ProfileId}@{ProfileVersion} 的 WORD、对象表或事件区不符合公开存档合同。");
        }
    }
}

public static class PalPublicToolProfiles
{
    public const string Schema = "PAL98.PublicToolProfile.v1";
    private const string DrawCardProfileSegment = ".drawcard.";

    public static readonly PalPublicToolProfile Dream220Visible = new(
        Schema,
        "pal98.dream220.compat",
        "1.0.18",
        "梦幻2.2显血版",
        ["Dream2.20", "仙剑梦幻2.20", "主播粉丝梦幻2.2显血版"],
        [
            new(1, "主播粉丝", "author", "user-specified"),
            new(2, "孙小柔", "author", "user-specified"),
            new(3, "othercat", "author", "user-specified"),
        ],
        SaveFormat.Dream220Win95,
        PalSaveLayout.WinObjectRecordSize,
        PalSaveLayout.ObjectCount,
        589,
        5_369,
        SaveFormatDetector.KnownDream220Win95Length,
        5_890);

    public static PalPublicToolProfile? Find(string? profileId, string? profileVersion)
    {
        if (!string.Equals(profileVersion, Dream220Visible.ProfileVersion, StringComparison.Ordinal))
        {
            return null;
        }

        if (string.Equals(profileId, Dream220Visible.ProfileId, StringComparison.Ordinal))
        {
            return Dream220Visible;
        }

        if (!IsDrawCardDerivedProfileId(profileId, Dream220Visible.ProfileId))
        {
            return null;
        }

        return Dream220Visible with
        {
            ProfileId = profileId!,
            DisplayName = Dream220Visible.DisplayName + " + 抽卡",
        };
    }

    private static bool IsDrawCardDerivedProfileId(string? profileId, string baseProfileId)
    {
        string prefix = baseProfileId + DrawCardProfileSegment;
        if (profileId is null || profileId.Length != prefix.Length + 12 ||
            !profileId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char character in profileId.AsSpan(prefix.Length))
        {
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
