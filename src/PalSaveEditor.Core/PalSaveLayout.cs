namespace PalSaveEditor.Core;

/// <summary>
/// Byte layout shared by PAL DOS, PAL 98 and the PAL 98 Dream 2.20 port.
/// Values are little-endian and structures are packed on two-byte boundaries.
/// The event-object tail is deliberately opaque and is always preserved.
/// </summary>
public static class PalSaveLayout
{
    public const int HeaderSize = 44;
    public const int CashOffset = 40;

    public const int PartyOffset = 44;
    public const int PartyEntrySize = 10;
    public const int PartyCapacity = 5;
    public const int FollowerCapacity = 2;

    public const int TrailOffset = 94;
    public const int TrailEntrySize = 6;

    public const int ExperienceOffset = 124;
    public const int ExperienceCategoryCount = 8;
    public const int ExperienceEntrySize = 8;
    public const int RoleCount = 6;

    public const int PlayerRolesOffset = 508;
    public const int RoleArraySize = RoleCount * sizeof(ushort);
    public const int EquipmentCount = 6;
    public const int MagicCapacity = 32;
    public const int ElementCount = 5;

    public const int PoisonOffset = 1408;
    public const int InventoryOffset = 1728;
    public const int InventoryCapacity = 256;
    public const int InventoryEntrySize = 6;
    public const int SceneOffset = 3264;
    public const int SceneCount = 300;
    public const int SceneEntrySize = 8;
    public const int ObjectOffset = 5664;
    public const int DosObjectRecordSize = 12;
    public const int WinObjectRecordSize = 14;
    public const int ObjectCount = 600;
    public const int EventObjectRecordSize = 32;
    public const int DosEventObjectOffset = ObjectOffset + DosObjectRecordSize * ObjectCount;
    public const int WinEventObjectOffset = ObjectOffset + WinObjectRecordSize * ObjectCount;

    public const int SavedTimesOffset = 0;
    public const int ViewportXOffset = 2;
    public const int ViewportYOffset = 4;
    public const int PartyMaxIndexOffset = 6;
    public const int SceneNumberOffset = 8;
    public const int PaletteOffset = 10;
    public const int PartyDirectionOffset = 12;
    public const int MusicNumberOffset = 14;
    public const int BattleMusicNumberOffset = 16;
    public const int BattleFieldNumberOffset = 18;
    public const int ScreenWaveOffset = 20;
    public const int BattleSpeedOffset = 22;
    public const int CollectValueOffset = 24;
    public const int LayerOffset = 26;
    public const int ChaseRangeOffset = 28;
    public const int ChaseCyclesOffset = 30;
    public const int FollowerOffset = 32;
    public static int PartyRoleOffset(int partyIndex) => PartyOffset + partyIndex * PartyEntrySize;
    public static int PartyRecordOffset(int queueIndex) => PartyOffset + queueIndex * PartyEntrySize;

    public static int ExperienceValueOffset(int category, int role) =>
        ExperienceOffset + (category * RoleCount + role) * ExperienceEntrySize;

    public static int RoleFieldOffset(RoleField field, int role) =>
        PlayerRolesOffset + GetRoleFieldBase(field) + role * sizeof(ushort);

    public static int EquipmentOffset(int equipmentSlot, int role) =>
        PlayerRolesOffset + 11 * RoleArraySize + (equipmentSlot * RoleCount + role) * sizeof(ushort);

    public static int MagicOffset(int magicSlot, int role) =>
        PlayerRolesOffset + 384 + (magicSlot * RoleCount + role) * sizeof(ushort);

    public static int InventorySlotOffset(int slot) => InventoryOffset + slot * InventoryEntrySize;

    private static int GetRoleFieldBase(RoleField field) => field switch
    {
        RoleField.Avatar => 0 * RoleArraySize,
        RoleField.BattleSprite => 1 * RoleArraySize,
        RoleField.MapSprite => 2 * RoleArraySize,
        RoleField.NameWordId => 3 * RoleArraySize,
        RoleField.AttackAll => 4 * RoleArraySize,
        RoleField.Level => 6 * RoleArraySize,
        RoleField.MaxHp => 7 * RoleArraySize,
        RoleField.MaxMp => 8 * RoleArraySize,
        RoleField.Hp => 9 * RoleArraySize,
        RoleField.Mp => 10 * RoleArraySize,
        RoleField.Attack => 17 * RoleArraySize,
        RoleField.MagicPower => 18 * RoleArraySize,
        RoleField.Defense => 19 * RoleArraySize,
        RoleField.Dexterity => 20 * RoleArraySize,
        RoleField.FleeRate => 21 * RoleArraySize,
        RoleField.PoisonResistance => 22 * RoleArraySize,
        RoleField.WindResistance => 23 * RoleArraySize,
        RoleField.ThunderResistance => 24 * RoleArraySize,
        RoleField.WaterResistance => 25 * RoleArraySize,
        RoleField.FireResistance => 26 * RoleArraySize,
        RoleField.EarthResistance => 27 * RoleArraySize,
        RoleField.WalkFrames => 64 * RoleArraySize,
        RoleField.CooperativeMagic => 65 * RoleArraySize,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };
}

public enum RoleField
{
    Avatar,
    BattleSprite,
    MapSprite,
    NameWordId,
    AttackAll,
    Level,
    MaxHp,
    MaxMp,
    Hp,
    Mp,
    Attack,
    MagicPower,
    Defense,
    Dexterity,
    FleeRate,
    PoisonResistance,
    WindResistance,
    ThunderResistance,
    WaterResistance,
    FireResistance,
    EarthResistance,
    WalkFrames,
    CooperativeMagic,
}
