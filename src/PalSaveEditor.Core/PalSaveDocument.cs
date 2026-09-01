using System.Buffers.Binary;

namespace PalSaveEditor.Core;

public sealed class PalSaveDocument
{
    private readonly byte[] _originalBytes;
    private readonly byte[] _bytes;
    private ExtendedRoleMagicState _extendedMagics;
    private ExtendedRoleMagicState _originalExtendedMagics;

    private PalSaveDocument(
        string path,
        byte[] bytes,
        SaveFormat format,
        SaveFormatDetection detection,
        PalResourceCatalog? catalog)
    {
        Path = System.IO.Path.GetFullPath(path);
        _originalBytes = (byte[])bytes.Clone();
        _bytes = bytes;
        Format = format;
        Detection = detection;
        Catalog = catalog;
        HasExtendedMagicSidecar = ExtendedRoleMagicSidecar.TryLoad(
            Path, _bytes, out _extendedMagics, out var sidecarWarning);
        ExtendedMagicSidecarWarning = sidecarWarning;
        _originalExtendedMagics = _extendedMagics.Clone();
    }

    public string Path { get; private set; }
    public SaveFormat Format { get; private set; }
    public SaveFormatDetection Detection { get; private set; }
    public PalResourceCatalog? Catalog { get; private set; }
    public int Length => _bytes.Length;
    public bool IsDirty => !_bytes.AsSpan().SequenceEqual(_originalBytes) ||
        !_extendedMagics.ContentEquals(_originalExtendedMagics);
    public bool HasExtendedMagicSidecar { get; private set; }
    public string? ExtendedMagicSidecarWarning { get; private set; }
    public int MagicCapacity => ExtendedRoleMagicState.CapacityPerRole;
    public int ActiveMagicPage => _extendedMagics.ActivePage;

    public ushort SavedTimes { get => ReadUInt16(PalSaveLayout.SavedTimesOffset); set => WriteUInt16(PalSaveLayout.SavedTimesOffset, value); }
    public ushort ViewportX { get => ReadUInt16(PalSaveLayout.ViewportXOffset); set => WriteUInt16(PalSaveLayout.ViewportXOffset, value); }
    public ushort ViewportY { get => ReadUInt16(PalSaveLayout.ViewportYOffset); set => WriteUInt16(PalSaveLayout.ViewportYOffset, value); }
    public ushort SceneNumber { get => ReadUInt16(PalSaveLayout.SceneNumberOffset); set => WriteUInt16(PalSaveLayout.SceneNumberOffset, value); }
    public ushort MusicNumber { get => ReadUInt16(PalSaveLayout.MusicNumberOffset); set => WriteUInt16(PalSaveLayout.MusicNumberOffset, value); }
    public ushort BattleMusicNumber { get => ReadUInt16(PalSaveLayout.BattleMusicNumberOffset); set => WriteUInt16(PalSaveLayout.BattleMusicNumberOffset, value); }
    public ushort CollectValue { get => ReadUInt16(PalSaveLayout.CollectValueOffset); set => WriteUInt16(PalSaveLayout.CollectValueOffset, value); }
    public uint Cash { get => ReadUInt32(PalSaveLayout.CashOffset); set => WriteUInt32(PalSaveLayout.CashOffset, value); }
    public int PartyCount
    {
        get => Math.Max(1, Math.Min(PalSaveLayout.PartyCapacity,
            ReadUInt16(PalSaveLayout.PartyMaxIndexOffset) + 1));
        private set
        {
            if (value is < 1 or > PalSaveLayout.PartyCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            WriteUInt16(PalSaveLayout.PartyMaxIndexOffset, checked((ushort)(value - 1)));
        }
    }

    public static PalSaveDocument Load(
        string path,
        SaveFormat requestedFormat = SaveFormat.Auto,
        string? gameDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("存档路径不能为空。", nameof(path));
        }
        var fullPath = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        PalResourceCatalog? catalog = null;
        if (!string.IsNullOrWhiteSpace(gameDirectory))
        {
            catalog = PalResourceCatalog.Load(gameDirectory!);
        }
        else
        {
            catalog = PalResourceCatalog.TryDiscover(fullPath);
        }

        var detection = SaveFormatDetector.Detect(
            bytes.Length,
            catalog?.WordDatByteLength,
            catalog?.ObjectRecordSize ?? 0,
            catalog?.EventObjectBytes ?? 0);
        var format = requestedFormat == SaveFormat.Auto ? detection.Format : requestedFormat;
        ValidateFormat(bytes.Length, format);
        return new(fullPath, bytes, format, detection, catalog);
    }

    public void SetFormat(SaveFormat format)
    {
        if (format == SaveFormat.Auto)
        {
            format = Detection.Format;
        }

        ValidateFormat(_bytes.Length, format);
        Format = format;
    }

    public void SetCatalog(PalResourceCatalog catalog)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        SaveFormatDetection detection = SaveFormatDetector.Detect(
            _bytes.Length,
            catalog.WordDatByteLength,
            catalog.ObjectRecordSize,
            catalog.EventObjectBytes);
        Catalog = catalog;
        Detection = detection;
        if (detection.Format is SaveFormat.Dream220Dos or SaveFormat.Dream220Win95)
        {
            Format = detection.Format;
        }
    }

    public IReadOnlyList<PartyMember> GetParty()
    {
        var result = new List<PartyMember>(PartyCount);
        for (var i = 0; i < PartyCount; i++)
        {
            result.Add(new(i, ReadUInt16(PalSaveLayout.PartyRoleOffset(i))));
        }

        return result;
    }

    public void SetParty(IReadOnlyList<ushort> roleIds)
    {
        if (roleIds is null)
        {
            throw new ArgumentNullException(nameof(roleIds));
        }
        if (roleIds.Count is < 1 or > PalSaveLayout.PartyCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(roleIds), "队伍人数必须为 1 到 5。 ");
        }

        if (roleIds.Any(id => id >= PalSaveLayout.RoleCount))
        {
            throw new ArgumentOutOfRangeException(nameof(roleIds), "角色编号必须为 0 到 5。 ");
        }

        if (roleIds.Distinct().Count() != roleIds.Count)
        {
            throw new InvalidDataException("队伍中不能有重复角色。");
        }

        var oldPartyCount = PartyCount;
        var followerRecords = ReadFollowerRecords(oldPartyCount);
        if (roleIds.Count + followerRecords.Count > PalSaveLayout.PartyCapacity)
        {
            throw new InvalidDataException(
                $"正式队员 {roleIds.Count} 人与随从 {followerRecords.Count} 人合计超过 {PalSaveLayout.PartyCapacity} 条队列容量。");
        }

        var partyRecords = new List<byte[]>(roleIds.Count);
        for (var i = 0; i < roleIds.Count; i++)
        {
            var record = i < oldPartyCount ? ReadQueueRecord(i) : new byte[PalSaveLayout.PartyEntrySize];
            BinaryPrimitives.WriteUInt16LittleEndian(record, roleIds[i]);
            partyRecords.Add(record);
        }

        WriteQueue(partyRecords, followerRecords);
        PartyCount = roleIds.Count;
    }

    public int FollowerCount
    {
        get
        {
            var count = ReadUInt16(PalSaveLayout.FollowerOffset);
            if (count > PalSaveLayout.FollowerCapacity || PartyCount + count > PalSaveLayout.PartyCapacity)
            {
                throw new InvalidDataException(
                    $"随从数量或队列范围无效：正式队员 {PartyCount}，随从 {count}，队列容量 {PalSaveLayout.PartyCapacity}。");
            }

            return count;
        }
    }

    public IReadOnlyList<Follower> GetFollowers()
    {
        var count = FollowerCount;
        var result = new List<Follower>(count);
        for (var i = 0; i < count; i++)
        {
            var spriteId = ReadUInt16(PalSaveLayout.PartyRoleOffset(PartyCount + i));
            if (spriteId == 0)
            {
                throw new InvalidDataException($"随从 {i + 1} 的 MGO 形象编号为 0，与随从数量不一致。");
            }
            result.Add(new(i, spriteId));
        }
        return result;
    }

    public void SetFollowers(IReadOnlyList<ushort> spriteIds)
    {
        if (spriteIds is null)
        {
            throw new ArgumentNullException(nameof(spriteIds));
        }
        if (spriteIds.Count > PalSaveLayout.FollowerCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(spriteIds), $"随从人数最多为 {PalSaveLayout.FollowerCapacity}。");
        }
        if (PartyCount + spriteIds.Count > PalSaveLayout.PartyCapacity)
        {
            throw new InvalidDataException(
                $"正式队员 {PartyCount} 人与随从 {spriteIds.Count} 人合计超过 {PalSaveLayout.PartyCapacity} 条队列容量。");
        }
        if (spriteIds.Any(id => id == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(spriteIds), "随从 MGO 形象编号必须为 1 到 65535。");
        }

        var partyRecords = Enumerable.Range(0, PartyCount).Select(ReadQueueRecord).ToList();
        var existingFollowers = ReadFollowerRecords(PartyCount);
        var usedExistingFollowers = new bool[existingFollowers.Count];
        var followerRecords = new List<byte[]>(spriteIds.Count);
        for (var i = 0; i < spriteIds.Count; i++)
        {
            var existingIndex = -1;
            if (i < existingFollowers.Count &&
                !usedExistingFollowers[i] &&
                BinaryPrimitives.ReadUInt16LittleEndian(existingFollowers[i]) == spriteIds[i])
            {
                existingIndex = i;
            }
            else
            {
                for (var candidate = 0; candidate < existingFollowers.Count; candidate++)
                {
                    if (!usedExistingFollowers[candidate] &&
                        BinaryPrimitives.ReadUInt16LittleEndian(existingFollowers[candidate]) == spriteIds[i])
                    {
                        existingIndex = candidate;
                        break;
                    }
                }
            }
            if (existingIndex < 0 && i < existingFollowers.Count && !usedExistingFollowers[i])
            {
                existingIndex = i;
            }

            var record = existingIndex >= 0
                ? existingFollowers[existingIndex]
                : new byte[PalSaveLayout.PartyEntrySize];
            if (existingIndex >= 0)
            {
                usedExistingFollowers[existingIndex] = true;
            }
            BinaryPrimitives.WriteUInt16LittleEndian(record, spriteIds[i]);
            followerRecords.Add(record);
        }

        WriteQueue(partyRecords, followerRecords);
        WriteUInt16(PalSaveLayout.FollowerOffset, checked((ushort)spriteIds.Count));
    }

    public RoleSnapshot GetRole(int roleId)
    {
        ValidateRole(roleId);
        var nameWordId = GetRoleField(roleId, RoleField.NameWordId);
        return new(
            roleId,
            nameWordId,
            Catalog?.GetRoleName(roleId, nameWordId) ?? $"角色 {roleId}",
            GetRoleField(roleId, RoleField.Level),
            GetExperience(roleId),
            GetRoleField(roleId, RoleField.MaxHp),
            GetRoleField(roleId, RoleField.Hp),
            GetRoleField(roleId, RoleField.MaxMp),
            GetRoleField(roleId, RoleField.Mp),
            GetRoleField(roleId, RoleField.Attack),
            GetRoleField(roleId, RoleField.MagicPower),
            GetRoleField(roleId, RoleField.Defense),
            GetRoleField(roleId, RoleField.Dexterity),
            GetRoleField(roleId, RoleField.FleeRate),
            GetRoleField(roleId, RoleField.PoisonResistance),
            GetRoleField(roleId, RoleField.CooperativeMagic));
    }

    public ushort GetRoleField(int roleId, RoleField field)
    {
        ValidateRole(roleId);
        return ReadUInt16(PalSaveLayout.RoleFieldOffset(field, roleId));
    }

    public void SetRoleField(int roleId, RoleField field, ushort value)
    {
        ValidateRole(roleId);
        WriteUInt16(PalSaveLayout.RoleFieldOffset(field, roleId), value);
    }

    public short GetRoleSignedField(int roleId, RoleField field)
    {
        ValidateRole(roleId);
        return unchecked((short)ReadUInt16(PalSaveLayout.RoleFieldOffset(field, roleId)));
    }

    public void SetRoleSignedField(int roleId, RoleField field, short value)
    {
        ValidateRole(roleId);
        WriteUInt16(PalSaveLayout.RoleFieldOffset(field, roleId), unchecked((ushort)value));
    }

    public ushort GetExperience(int roleId, int category = 0)
    {
        ValidateRole(roleId);
        if ((uint)category >= PalSaveLayout.ExperienceCategoryCount)
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        return ReadUInt16(PalSaveLayout.ExperienceValueOffset(category, roleId));
    }

    public void SetExperience(int roleId, ushort value, bool applyToAllCategories = false)
    {
        ValidateRole(roleId);
        var count = applyToAllCategories ? PalSaveLayout.ExperienceCategoryCount : 1;
        for (var category = 0; category < count; category++)
        {
            WriteUInt16(PalSaveLayout.ExperienceValueOffset(category, roleId), value);
        }
    }

    public IReadOnlyList<MagicEntry> GetMagics(int roleId)
    {
        ValidateRole(roleId);
        var result = new List<MagicEntry>();
        for (var slot = 0; slot < ExtendedRoleMagicState.CapacityPerRole; slot++)
        {
            var id = _extendedMagics.Roles[roleId][slot];
            if (id != 0)
            {
                result.Add(new(slot, id, Catalog?.GetObjectName(id) ?? $"法术 #{id}"));
            }
        }

        return result;
    }

    public void AddMagic(int roleId, ushort magicId)
    {
        ValidateRole(roleId);
        if (magicId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(magicId));
        }

        // Runtime script removals deliberately preserve their physical slot,
        // so a valid 999-slot sidecar can contain holes before an existing
        // copy of the same magic.  Search the complete role table for the
        // duplicate before choosing the first empty slot.
        for (var slot = 0; slot < ExtendedRoleMagicState.CapacityPerRole; slot++)
        {
            if (_extendedMagics.Roles[roleId][slot] == magicId)
            {
                return;
            }
        }

        for (var slot = 0; slot < ExtendedRoleMagicState.CapacityPerRole; slot++)
        {
            if (_extendedMagics.Roles[roleId][slot] == 0)
            {
                _extendedMagics.Roles[roleId][slot] = magicId;
                _extendedMagics.ProjectActivePage(_bytes);
                return;
            }
        }

        throw new InvalidOperationException("该角色的 999 个法术槽已满。");
    }

    public void RemoveMagic(int roleId, int magicSlot)
    {
        ValidateRole(roleId);
        if ((uint)magicSlot >= ExtendedRoleMagicState.CapacityPerRole)
        {
            throw new ArgumentOutOfRangeException(nameof(magicSlot));
        }

        var values = new List<ushort>(ExtendedRoleMagicState.CapacityPerRole);
        for (var slot = 0; slot < ExtendedRoleMagicState.CapacityPerRole; slot++)
        {
            if (slot != magicSlot)
            {
                var value = _extendedMagics.Roles[roleId][slot];
                if (value != 0)
                {
                    values.Add(value);
                }
            }
        }

        for (var slot = 0; slot < ExtendedRoleMagicState.CapacityPerRole; slot++)
        {
            _extendedMagics.Roles[roleId][slot] =
                slot < values.Count ? values[slot] : (ushort)0;
        }
        _extendedMagics.ProjectActivePage(_bytes);
    }

    public IReadOnlyList<EquipmentEntry> GetEquipment(int roleId)
    {
        ValidateRole(roleId);
        var result = new List<EquipmentEntry>(PalSaveLayout.EquipmentCount);
        for (var slot = 0; slot < PalSaveLayout.EquipmentCount; slot++)
        {
            var itemId = ReadUInt16(PalSaveLayout.EquipmentOffset(slot, roleId));
            result.Add(new(slot, itemId, itemId == 0 ? "（无）" : Catalog?.GetObjectName(itemId) ?? $"物品 #{itemId}"));
        }

        return result;
    }

    public void SetEquipment(int roleId, int slot, ushort itemId)
    {
        ValidateRole(roleId);
        if ((uint)slot >= PalSaveLayout.EquipmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        WriteUInt16(PalSaveLayout.EquipmentOffset(slot, roleId), itemId);
    }

    public IReadOnlyList<InventoryEntry> GetInventory(bool includeEmpty = false)
    {
        var result = new List<InventoryEntry>();
        for (var slot = 0; slot < PalSaveLayout.InventoryCapacity; slot++)
        {
            var offset = PalSaveLayout.InventorySlotOffset(slot);
            var itemId = ReadUInt16(offset);
            if (itemId == 0 && !includeEmpty)
            {
                continue;
            }

            result.Add(new(
                slot,
                itemId,
                ReadUInt16(offset + 2),
                ReadUInt16(offset + 4),
                itemId == 0 ? "（空）" : Catalog?.GetObjectName(itemId) ?? $"物品 #{itemId}"));
        }

        return result;
    }

    public void SetInventorySlot(int slot, ushort itemId, ushort amount, ushort amountInUse = 0)
    {
        ValidateInventorySlot(slot);
        if (itemId == 0 || amount == 0)
        {
            ClearInventorySlot(slot);
            return;
        }

        if (amountInUse > amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amountInUse), "使用中的数量不能大于总数量。");
        }

        var duplicate = GetInventory().FirstOrDefault(entry => entry.ItemId == itemId && entry.Slot != slot);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"物品 #{itemId} 已在槽 {duplicate.Slot} 中，不能重复建立背包槽。");
        }

        var offset = PalSaveLayout.InventorySlotOffset(slot);
        WriteUInt16(offset, itemId);
        WriteUInt16(offset + 2, amount);
        WriteUInt16(offset + 4, amountInUse);
        CompressInventory();
    }

    public int AddInventoryItem(ushort itemId, ushort amount)
    {
        if (itemId == 0 || amount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId));
        }

        var entries = GetInventory();
        var existing = entries.FirstOrDefault(entry => entry.ItemId == itemId);
        if (existing is not null)
        {
            var combined = checked((ushort)Math.Min(ushort.MaxValue, existing.Amount + amount));
            SetInventorySlot(existing.Slot, itemId, combined, existing.AmountInUse);
            return existing.Slot;
        }

        var occupied = entries.Select(entry => entry.Slot).ToHashSet();
        var freeSlot = Enumerable.Range(0, PalSaveLayout.InventoryCapacity)
            .Where(slot => !occupied.Contains(slot))
            .DefaultIfEmpty(-1)
            .First();
        if (freeSlot < 0)
        {
            throw new InvalidOperationException("背包的 256 个槽已满。");
        }

        SetInventorySlot(freeSlot, itemId, amount);
        return freeSlot;
    }

    public void ClearInventorySlot(int slot)
    {
        ValidateInventorySlot(slot);
        _bytes.AsSpan(PalSaveLayout.InventorySlotOffset(slot), PalSaveLayout.InventoryEntrySize).Clear();
        CompressInventory();
    }

    public byte[] ToArray() => (byte[])_bytes.Clone();

    public SaveWriteResult Save(string? targetPath = null, bool createBackup = true)
    {
        if (!string.IsNullOrWhiteSpace(ExtendedMagicSidecarWarning) &&
            _extendedMagics.HasExtendedPayload)
        {
            throw new InvalidOperationException(
                "当前扩展法术槽 sidecar 需要先使用仙剑98存档检查工具修复；编辑器不会覆盖它。");
        }

        _extendedMagics.ProjectActivePage(_bytes);
        var destination = System.IO.Path.GetFullPath(targetPath ?? Path);
        bool savesToOriginalPath = string.Equals(
            destination,
            System.IO.Path.GetFullPath(Path),
            StringComparison.OrdinalIgnoreCase);
        var directory = System.IO.Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("无法确定目标目录。");
        Directory.CreateDirectory(directory);
        bool destinationExisted = File.Exists(destination);
        string? rollbackPath = destinationExisted
            ? createBackup
                ? BuildBackupPath(destination)
                : System.IO.Path.Combine(
                    directory,
                    $".{System.IO.Path.GetFileName(destination)}.{Guid.NewGuid():N}.rollback")
            : null;
        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(_bytes, 0, _bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (destinationExisted)
            {
                File.Replace(temporaryPath, destination, rollbackPath!, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, destination);
            }

            byte[] persisted = File.ReadAllBytes(destination);
            if (!persisted.AsSpan().SequenceEqual(_bytes))
            {
                throw new IOException("保存后的文件与待写入数据不一致。 ");
            }

            if (ExtendedRoleMagicSidecar.SupportsPath(destination))
            {
                if (_extendedMagics.HasExtendedPayload)
                {
                    ExtendedRoleMagicSidecar.WriteAtomically(
                        destination, persisted, _extendedMagics);
                    HasExtendedMagicSidecar = true;
                    ExtendedMagicSidecarWarning = null;
                }
                else
                {
                    HasExtendedMagicSidecar = false;
                    if (!savesToOriginalPath)
                    {
                        ExtendedMagicSidecarWarning = null;
                    }
                }
            }
            else
            {
                HasExtendedMagicSidecar = false;
                ExtendedMagicSidecarWarning = _extendedMagics.HasExtendedPayload
                    ? "仅 1.RPG 到 5.RPG 会保存 999 槽扩展数据；当前文件只保存原生页的 32 槽。"
                    : null;
            }

            if (destinationExisted && !createBackup)
            {
                File.Delete(rollbackPath!);
                rollbackPath = null;
            }
        }
        catch
        {
            if (rollbackPath is not null && File.Exists(rollbackPath))
            {
                File.Copy(rollbackPath, destination, overwrite: true);
                if (!createBackup)
                {
                    File.Delete(rollbackPath);
                    rollbackPath = null;
                }
            }
            else if (!destinationExisted && File.Exists(destination))
            {
                File.Delete(destination);
            }
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        Path = destination;
        Array.Copy(_bytes, _originalBytes, _bytes.Length);
        _originalExtendedMagics = _extendedMagics.Clone();

        return new(destination, createBackup ? rollbackPath : null, _bytes.Length);
    }

    private static void ValidateFormat(int length, SaveFormat format)
    {
        var minimum = format is SaveFormat.PalDos or SaveFormat.Dream220Dos
            ? PalSaveLayout.DosEventObjectOffset
            : PalSaveLayout.WinEventObjectOffset;
        if (length < minimum)
        {
            throw new InvalidDataException(
                $"{format.GetDisplayName()} 至少需要 {minimum:N0} 字节，当前文件仅 {length:N0} 字节。");
        }

        if ((length - minimum) % 32 != 0)
        {
            throw new InvalidDataException(
                $"文件长度不符合 {format.GetDisplayName()} 的固定前缀和 32 字节事件记录边界。");
        }
    }

    private static string BuildBackupPath(string destination)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = $"{destination}.bak-{stamp}";
        var suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = $"{destination}.bak-{stamp}-{suffix++}";
        }

        return candidate;
    }

    private void CompressInventory()
    {
        var entries = new List<(ushort Item, ushort Amount, ushort InUse)>();
        for (var slot = 0; slot < PalSaveLayout.InventoryCapacity; slot++)
        {
            var offset = PalSaveLayout.InventorySlotOffset(slot);
            var item = ReadUInt16(offset);
            var amount = ReadUInt16(offset + 2);
            if (item != 0 && amount != 0)
            {
                entries.Add((item, amount, ReadUInt16(offset + 4)));
            }
        }

        _bytes.AsSpan(PalSaveLayout.InventoryOffset, PalSaveLayout.InventoryCapacity * PalSaveLayout.InventoryEntrySize).Clear();
        for (var slot = 0; slot < entries.Count; slot++)
        {
            var offset = PalSaveLayout.InventorySlotOffset(slot);
            WriteUInt16(offset, entries[slot].Item);
            WriteUInt16(offset + 2, entries[slot].Amount);
            WriteUInt16(offset + 4, entries[slot].InUse);
        }
    }

    private List<byte[]> ReadFollowerRecords(int partyCount)
    {
        var count = FollowerCount;
        var result = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(ReadQueueRecord(partyCount + i));
        }
        return result;
    }

    private byte[] ReadQueueRecord(int queueIndex)
    {
        if ((uint)queueIndex >= PalSaveLayout.PartyCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(queueIndex));
        }
        return _bytes.AsSpan(
            PalSaveLayout.PartyRecordOffset(queueIndex),
            PalSaveLayout.PartyEntrySize).ToArray();
    }

    private void WriteQueue(IReadOnlyList<byte[]> partyRecords, IReadOnlyList<byte[]> followerRecords)
    {
        if (partyRecords.Count + followerRecords.Count > PalSaveLayout.PartyCapacity)
        {
            throw new InvalidDataException("正式队员与随从超过共享队列容量。");
        }

        foreach (var record in partyRecords.Concat(followerRecords))
        {
            if (record.Length != PalSaveLayout.PartyEntrySize)
            {
                throw new InvalidDataException("队伍记录长度无效。");
            }
        }

        _bytes.AsSpan(
            PalSaveLayout.PartyOffset,
            PalSaveLayout.PartyCapacity * PalSaveLayout.PartyEntrySize).Clear();
        var queueIndex = 0;
        foreach (var record in partyRecords.Concat(followerRecords))
        {
            record.CopyTo(_bytes, PalSaveLayout.PartyRecordOffset(queueIndex++));
        }
    }

    private ushort ReadUInt16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(offset, sizeof(ushort)));
    private uint ReadUInt32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(offset, sizeof(uint)));
    private void WriteUInt16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(offset, sizeof(ushort)), value);
    private void WriteUInt32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(offset, sizeof(uint)), value);

    private static void ValidateRole(int roleId)
    {
        if ((uint)roleId >= PalSaveLayout.RoleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(roleId));
        }
    }

    private static void ValidateInventorySlot(int slot)
    {
        if ((uint)slot >= PalSaveLayout.InventoryCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }
}
