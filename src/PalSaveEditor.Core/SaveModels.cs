namespace PalSaveEditor.Core;

public sealed record PartyMember(int PartyIndex, ushort RoleId);

public sealed record Follower(int FollowerIndex, ushort SpriteId);

public sealed record RoleSnapshot(
    int RoleId,
    ushort NameWordId,
    string DisplayName,
    ushort Level,
    ushort Experience,
    ushort MaxHp,
    ushort Hp,
    ushort MaxMp,
    ushort Mp,
    ushort Attack,
    ushort MagicPower,
    ushort Defense,
    ushort Dexterity,
    ushort FleeRate,
    ushort PoisonResistance,
    ushort CooperativeMagic);

public sealed record InventoryEntry(int Slot, ushort ItemId, ushort Amount, ushort AmountInUse, string DisplayName);

public sealed record MagicEntry(int Slot, ushort MagicId, string DisplayName);

public sealed record EquipmentEntry(int Slot, ushort ItemId, string DisplayName);

public sealed record SaveWriteResult(string TargetPath, string? BackupPath, int ByteLength);
