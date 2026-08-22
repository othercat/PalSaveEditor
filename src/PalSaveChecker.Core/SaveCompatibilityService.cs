using System.IO.Compression;
using System.Text;
using PalSaveEditor.Core;

namespace PalSaveChecker.Core;

public enum SaveCheckStatus
{
    Missing,
    Clean,
    Polluted,
    Incompatible,
    Unreadable,
}

public sealed record SaveCheckItem(
    string FileName,
    SaveCheckStatus Status,
    bool Repairable,
    int DefinitionMismatchCount,
    int InvalidScriptCount,
    string Risk,
    string? Error = null);

public sealed record SaveCheckReport(
    string GameRoot,
    string? ReferenceDescription,
    string? ReferenceError,
    IReadOnlyList<SaveCheckItem> Saves)
{
    public bool CanRepair => ReferenceError is null && Saves.Any(item => item.Status == SaveCheckStatus.Polluted);
    public bool HasProblems => ReferenceError is not null || Saves.Any(item =>
        item.Status is SaveCheckStatus.Polluted or SaveCheckStatus.Incompatible or SaveCheckStatus.Unreadable);
}

public sealed record SaveRepairItem(
    string FileName,
    bool Success,
    string Message,
    string? BackupPath = null);

public sealed record SaveRepairReport(
    SaveCheckReport Before,
    SaveCheckReport After,
    IReadOnlyList<SaveRepairItem> Results)
{
    public bool HasFailures => Results.Any(item => !item.Success) || After.HasProblems;
}

public static class GameDirectoryLocator
{
    public static string Resolve(string executableDirectory)
    {
        if (string.IsNullOrWhiteSpace(executableDirectory))
        {
            throw new ArgumentException("程序目录不能为空。", nameof(executableDirectory));
        }

        var directory = new DirectoryInfo(Path.GetFullPath(executableDirectory));
        return directory.Name.Equals("tools", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null
            ? directory.Parent.FullName
            : directory.FullName;
    }
}

public sealed class SaveCompatibilityService
{
    internal const int SaveObjectTableOffset = 0x1620;
    internal const int ObjectRecordSize = 14;
    internal const int MaximumSavedObjectCount = 600;
    private const int ScriptRecordSize = 8;
    private const int EventRecordSize = 32;
    private const int SaveEventObjectOffset = 14_064;
    private const int SssEventChunkIndex = 0;
    private const int SssObjectChunkIndex = 2;
    private const int SssScriptChunkIndex = 4;

    public SaveCheckReport Check(string gameRoot)
    {
        string fullRoot = Path.GetFullPath(gameRoot);
        if (!TryLoadReference(fullRoot, out ReferenceData? reference, out string? description, out string? error))
        {
            return new SaveCheckReport(fullRoot, description, error, InspectWithoutReference(fullRoot));
        }

        return CheckWithReference(fullRoot, reference!, description!);
    }

    public SaveRepairReport Repair(string gameRoot, bool keepBackup = true)
    {
        string fullRoot = Path.GetFullPath(gameRoot);
        SaveCheckReport before = Check(fullRoot);
        var results = new List<SaveRepairItem>();
        if (!TryLoadReference(fullRoot, out ReferenceData? reference, out string? description, out string? referenceError))
        {
            results.Add(new SaveRepairItem("-", false, referenceError ?? "无法读取参考资源。"));
            return new SaveRepairReport(before, before, results);
        }

        foreach (SaveCheckItem item in before.Saves)
        {
            if (item.Status is SaveCheckStatus.Incompatible or SaveCheckStatus.Unreadable)
            {
                results.Add(new SaveRepairItem(item.FileName, false, item.Error ?? "存档无法读取。"));
                continue;
            }
            if (item.Status != SaveCheckStatus.Polluted)
            {
                continue;
            }

            string savePath = Path.Combine(fullRoot, item.FileName);
            try
            {
                byte[] original = ReadAllBytesShared(savePath);
                Analysis analysis = Analyze(original, reference!);
                if (!analysis.Repairable)
                {
                    results.Add(new SaveRepairItem(item.FileName, false, analysis.Error ?? "无法安全修复。"));
                    continue;
                }

                byte[] repaired = RepairBytes(original, reference!, analysis);
                Analysis verification = Analyze(repaired, reference!);
                if (!verification.IsClean)
                {
                    results.Add(new SaveRepairItem(item.FileName, false, "候选修复仍未通过一致性检查，原文件未改动。"));
                    continue;
                }

                string rollbackPath = ReplaceWithRollback(savePath, repaired, keepBackup);
                try
                {
                    Analysis diskVerification = Analyze(ReadAllBytesShared(savePath), reference!);
                    if (!diskVerification.IsClean)
                    {
                        RestoreRollback(rollbackPath, savePath, keepBackup);
                        results.Add(new SaveRepairItem(
                            item.FileName,
                            false,
                            "落盘复核失败，已从回滚副本恢复原存档。",
                            keepBackup ? rollbackPath : null));
                        continue;
                    }

                    if (!keepBackup)
                    {
                        File.Delete(rollbackPath);
                    }

                    results.Add(new SaveRepairItem(
                        item.FileName,
                        true,
                        keepBackup ? "修复完成并已创建备份。" : "修复完成；未保留备份。",
                        keepBackup ? rollbackPath : null));
                }
                catch
                {
                    if (File.Exists(rollbackPath))
                    {
                        RestoreRollback(rollbackPath, savePath, keepBackup);
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                results.Add(new SaveRepairItem(item.FileName, false, $"修复失败：{ex.Message}"));
            }
        }

        SaveCheckReport after = CheckWithReference(fullRoot, reference!, description!);
        return new SaveRepairReport(before, after, results);
    }

    internal static SaveCheckItem InspectBytes(string fileName, byte[] bytes, byte[] sssBytes)
    {
        ReferenceData reference = ReferenceData.Parse(sssBytes);
        return ToCheckItem(fileName, Analyze(bytes, reference));
    }

    internal static byte[] RepairBytesForTest(byte[] bytes, byte[] sssBytes)
    {
        ReferenceData reference = ReferenceData.Parse(sssBytes);
        Analysis analysis = Analyze(bytes, reference);
        if (!analysis.Repairable)
        {
            throw new InvalidDataException(analysis.Error ?? "无法安全修复。 ");
        }
        return RepairBytes(bytes, reference, analysis);
    }

    private static SaveCheckReport CheckWithReference(string root, ReferenceData reference, string description)
    {
        var saves = new List<SaveCheckItem>(5);
        for (int slot = 1; slot <= 5; slot++)
        {
            string fileName = $"{slot}.RPG";
            string path = Path.Combine(root, fileName);
            if (!File.Exists(path))
            {
                saves.Add(new SaveCheckItem(fileName, SaveCheckStatus.Missing, false, 0, 0, "未找到；不会处理。"));
                continue;
            }

            try
            {
                saves.Add(ToCheckItem(fileName, Analyze(ReadAllBytesShared(path), reference)));
            }
            catch (Exception ex)
            {
                saves.Add(new SaveCheckItem(fileName, SaveCheckStatus.Unreadable, false, 0, 0,
                    "无法确认安全性，请勿覆盖该存档。", ex.Message));
            }
        }

        return new SaveCheckReport(root, description, null, saves);
    }

    private static IReadOnlyList<SaveCheckItem> InspectWithoutReference(string root)
    {
        var saves = new List<SaveCheckItem>(5);
        for (int slot = 1; slot <= 5; slot++)
        {
            string fileName = $"{slot}.RPG";
            string path = Path.Combine(root, fileName);
            saves.Add(File.Exists(path)
                ? new SaveCheckItem(fileName, SaveCheckStatus.Unreadable, false, 0, 0,
                    "缺少对应补丁的 SSS.MKF，无法判断或修复。")
                : new SaveCheckItem(fileName, SaveCheckStatus.Missing, false, 0, 0, "未找到；不会处理。"));
        }
        return saves;
    }

    private static SaveCheckItem ToCheckItem(string fileName, Analysis analysis)
    {
        if (!analysis.Repairable)
        {
            SaveCheckStatus status = analysis.IsLayoutMismatch
                ? SaveCheckStatus.Incompatible
                : SaveCheckStatus.Unreadable;
            string failureRisk = analysis.IsLayoutMismatch
                ? "剧情版本或事件流程布局不匹配；不能用对象字段修复代替剧情状态迁移。"
                : "存档结构异常，自动修复可能破坏进度。";
            return new SaveCheckItem(fileName, status, false,
                analysis.DefinitionMismatchCount, analysis.InvalidScriptCount,
                failureRisk, analysis.Error);
        }
        if (analysis.IsClean)
        {
            return new SaveCheckItem(fileName, SaveCheckStatus.Clean, false, 0, 0,
                "未发现对象定义或脚本索引污染。 ");
        }

        string risk = analysis.DefinitionMismatchCount > 0
            ? "对象定义已偏离当前补丁，可能导致人物、物品、敌人或中毒/受伤脚本乱跳，严重时会崩溃。"
            : "发现超出当前脚本表范围的索引，可能跳入无关剧情或触发 Error 6/9。";
        return new SaveCheckItem(fileName, SaveCheckStatus.Polluted, true,
            analysis.DefinitionMismatchCount, analysis.InvalidScriptCount, risk);
    }

    private static Analysis Analyze(byte[] saveBytes, ReferenceData reference)
    {
        int expectedLength = checked(SaveEventObjectOffset + reference.EventObjectBytes);
        if (saveBytes.Length != expectedLength)
        {
            int actualEventBytes = saveBytes.Length - SaveEventObjectOffset;
            string actualRecords = actualEventBytes >= 0 && actualEventBytes % EventRecordSize == 0
                ? (actualEventBytes / EventRecordSize).ToString("N0")
                : "非整记录";
            int missingRecords = actualEventBytes >= 0 && actualEventBytes < reference.EventObjectBytes &&
                                 actualEventBytes % EventRecordSize == 0
                ? (reference.EventObjectBytes - actualEventBytes) / EventRecordSize
                : 0;
            string difference = missingRecords > 0 ? $"，缺少 {missingRecords:N0} 条" : string.Empty;
            return Analysis.Unrepairable(
                $"文件 {saveBytes.Length:N0} 字节；当前资源要求 Win95/PALDLL 存档 {expectedLength:N0} 字节" +
                $"（固定区 {SaveEventObjectOffset:N0} + {reference.EventObjectBytes / EventRecordSize:N0} 条事件记录），" +
                $"当前仅对应 {actualRecords} 条{difference}。缺失或多出的剧情事件状态无法从 SSS.MKF 自动重建。",
                isLayoutMismatch: true);
        }

        int requiredLength = checked(SaveObjectTableOffset + reference.ObjectCount * ObjectRecordSize);
        if (saveBytes.Length < requiredLength)
        {
            return Analysis.Unrepairable($"文件仅 {saveBytes.Length} 字节，小于当前补丁所需的对象区边界 {requiredLength} 字节。 ");
        }

        var fieldRepairs = new HashSet<(int ObjectId, int Field)>();
        int definitions = 0;
        int scripts = 0;

        for (int objectId = 0; objectId < reference.ObjectCount; objectId++)
        {
            int saveOffset = SaveObjectTableOffset + objectId * ObjectRecordSize;
            int referenceOffset = objectId * ObjectRecordSize;
            int[] stableFields = StableFields(objectId);
            var mismatchedStableFields = new List<int>();
            foreach (int field in stableFields)
            {
                if (ReadUInt16(saveBytes, saveOffset + field * 2) ==
                    ReadUInt16(reference.ObjectBytes, referenceOffset + field * 2))
                {
                    continue;
                }

                mismatchedStableFields.Add(field);
                definitions++;
            }

            if (mismatchedStableFields.Count > 0 && objectId < 0x003D)
            {
                // Player-object script fields can legitimately advance, so do
                // not blindly replace the whole record. A stable mismatch is
                // proof of corruption; repair the contiguous field span
                // bounded by the first and last mismatched stable fields. This
                // recovers in-range script garbage inside a sprite overwrite
                // while preserving clean fields beyond a partial final record.
                int first = mismatchedStableFields.Min();
                int last = mismatchedStableFields.Max();
                for (int field = first; field <= last; field++)
                {
                    fieldRepairs.Add((objectId, field));
                }
            }
            else
            {
                foreach (int field in mismatchedStableFields)
                {
                    fieldRepairs.Add((objectId, field));
                }
            }

            foreach (int field in ScriptFields(objectId))
            {
                ushort script = ReadUInt16(saveBytes, saveOffset + field * 2);
                if (script == 0 || script < reference.ScriptCount)
                {
                    continue;
                }

                scripts++;
                fieldRepairs.Add((objectId, field));
            }
        }

        return new Analysis(definitions, scripts, fieldRepairs, null);
    }

    private static byte[] RepairBytes(byte[] original, ReferenceData reference, Analysis analysis)
    {
        byte[] repaired = (byte[])original.Clone();
        foreach ((int objectId, int field) in analysis.FieldRepairs)
        {
            int source = objectId * ObjectRecordSize + field * 2;
            int target = SaveObjectTableOffset + objectId * ObjectRecordSize + field * 2;
            repaired[target] = reference.ObjectBytes[source];
            repaired[target + 1] = reference.ObjectBytes[source + 1];
        }
        return repaired;
    }

    private static int[] StableFields(int objectId) => objectId switch
    {
        < 0x003D => [0, 1, 4, 5, 6],
        < 0x0127 => [0, 1, 5, 6],
        < 0x018E => [0, 1, 4, 5, 6],
        < 0x0227 => [0, 1, 5, 6],
        _ => [0, 1, 3, 5, 6],
    };

    private static int[] ScriptFields(int objectId) => objectId switch
    {
        < 0x003D => [2, 3],
        < 0x0127 => [2, 3, 4, 5],
        < 0x018E => [2, 3, 4],
        < 0x0227 => [2, 3, 4],
        _ => [2, 4],
    };

    private static bool TryLoadReference(
        string root,
        out ReferenceData? reference,
        out string? description,
        out string? error)
    {
        reference = null;
        description = null;
        error = null;
        try
        {
            PalGameResourceContext resourceContext = PalGameResourceContextResolver.Resolve(root);
            if (resourceContext.IsActiveProfile)
            {
                string activeSssPath = Path.Combine(resourceContext.ResourceDirectory, "SSS.MKF");
                reference = ReferenceData.Parse(ReadAllBytesShared(activeSssPath));
                description = resourceContext.DescribeResource("SSS.MKF");
                return true;
            }

            string configPath = Path.Combine(root, "config.ini");
            if (!File.Exists(configPath))
            {
                error = $"未找到 {configPath}。";
                return false;
            }

            string defaultPatch = ReadDefaultPatch(configPath);
            byte[] sssBytes;
            if (string.IsNullOrWhiteSpace(defaultPatch))
            {
                string sssPath = Path.Combine(root, "SSS.MKF");
                if (!File.Exists(sssPath))
                {
                    error = "DefaultPatch 为空，且游戏目录没有 SSS.MKF。";
                    return false;
                }
                sssBytes = ReadAllBytesShared(sssPath);
                description = sssPath;
            }
            else
            {
                if (!IsSafePatchName(defaultPatch))
                {
                    error = $"DefaultPatch 名称不安全或不是单一文件名：{defaultPatch}";
                    return false;
                }
                if (!TryReadPatchSss(root, defaultPatch, out sssBytes, out description, out error))
                {
                    return false;
                }
            }

            reference = ReferenceData.Parse(sssBytes);
            return true;
        }
        catch (Exception ex)
        {
            error = $"读取当前补丁资源失败：{ex.Message}";
            return false;
        }
    }

    private static string ReadDefaultPatch(string configPath)
    {
        string section = string.Empty;
        foreach (string rawLine in ReadConfigText(configPath).Split(
            new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
            {
                continue;
            }
            if (line[0] == '[' && line[line.Length - 1] == ']')
            {
                section = line.Substring(1, line.Length - 2).Trim();
                continue;
            }
            if (!section.Equals("Patch", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            int equals = line.IndexOf('=');
            if (equals <= 0 || !line.Substring(0, equals).Trim().Equals("DefaultPatch", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string value = line.Substring(equals + 1).Trim();
            int comment = value.IndexOf(';');
            if (comment >= 0)
            {
                value = value.Substring(0, comment).Trim();
            }
            return value.Trim().Trim('"');
        }
        return string.Empty;
    }

    private static string ReadConfigText(string configPath)
    {
        byte[] bytes = ReadAllBytesShared(configPath);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936).GetString(bytes);
        }
    }

    private static bool IsSafePatchName(string value) =>
        value.IndexOf("..", StringComparison.Ordinal) < 0 &&
        value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0 &&
        !Path.IsPathRooted(value);

    private static bool TryReadPatchSss(
        string root,
        string defaultPatch,
        out byte[] bytes,
        out string? description,
        out string? error)
    {
        bytes = [];
        description = null;
        error = null;
        string patchesRoot = Path.Combine(root, "patches");
        if (!Directory.Exists(patchesRoot))
        {
            error = $"未找到补丁目录 {patchesRoot}。";
            return false;
        }

        string patchStem = defaultPatch.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? defaultPatch.Substring(0, defaultPatch.Length - 4)
            : defaultPatch;
        string? zipPath = Directory.EnumerateFiles(patchesRoot, "*.zip", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Equals(patchStem, StringComparison.OrdinalIgnoreCase));
        if (zipPath is not null)
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(candidate =>
                Path.GetFileName(candidate.FullName).Equals("SSS.MKF", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                error = $"补丁 {Path.GetFileName(zipPath)} 内没有 SSS.MKF。";
                return false;
            }
            using Stream stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            bytes = memory.ToArray();
            description = $"{zipPath} -> {entry.FullName}";
            return true;
        }

        string? directory = Directory.EnumerateDirectories(patchesRoot, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path).Equals(patchStem, StringComparison.OrdinalIgnoreCase));
        if (directory is not null)
        {
            string? sssPath = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(path).Equals("SSS.MKF", StringComparison.OrdinalIgnoreCase));
            if (sssPath is not null)
            {
                bytes = ReadAllBytesShared(sssPath);
                description = sssPath;
                return true;
            }
        }

        error = $"找不到 DefaultPatch={defaultPatch} 对应的 ZIP/目录或其中的 SSS.MKF。";
        return false;
    }

    private static string ReplaceWithRollback(string path, byte[] bytes, bool keepBackup)
    {
        string directory = Path.GetDirectoryName(path) ?? throw new IOException("存档目录无效。 ");
        string rollback;
        if (keepBackup)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            rollback = Path.Combine(directory, $"{Path.GetFileName(path)}.bak-{timestamp}");
            for (int suffix = 1; File.Exists(rollback); suffix++)
            {
                rollback = Path.Combine(directory, $"{Path.GetFileName(path)}.bak-{timestamp}-{suffix}");
            }
        }
        else
        {
            rollback = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.rollback");
        }

        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            using (FileStream stream = new(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Flush(flushToDisk: true);
            }
            File.Replace(temporary, path, rollback, ignoreMetadataErrors: true);
            return rollback;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void RestoreRollback(string rollbackPath, string savePath, bool keepBackup)
    {
        File.Copy(rollbackPath, savePath, overwrite: true);
        if (!keepBackup)
        {
            File.Delete(rollbackPath);
        }
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > int.MaxValue)
        {
            throw new IOException("文件过大。 ");
        }
        var bytes = new byte[(int)stream.Length];
        ReadExactly(stream, bytes);
        return bytes;
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        int readTotal = 0;
        while (readTotal < buffer.Length)
        {
            int read = stream.Read(buffer, readTotal, buffer.Length - readTotal);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            readTotal += read;
        }
    }

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        (ushort)(bytes[offset] | bytes[offset + 1] << 8);

    private sealed record Analysis(
        int DefinitionMismatchCount,
        int InvalidScriptCount,
        HashSet<(int ObjectId, int Field)> FieldRepairs,
        string? Error,
        bool IsLayoutMismatch = false)
    {
        public bool Repairable => Error is null;
        public bool IsClean => Repairable && DefinitionMismatchCount == 0 && InvalidScriptCount == 0;

        public static Analysis Unrepairable(string error, bool isLayoutMismatch = false) =>
            new(0, 0, [], error, isLayoutMismatch);
    }

    private sealed record ReferenceData(
        byte[] ObjectBytes,
        int ObjectCount,
        int ScriptCount,
        int EventObjectBytes)
    {
        public static ReferenceData Parse(byte[] bytes)
        {
            if (bytes.Length < 24)
            {
                throw new InvalidDataException("SSS.MKF 太短。 ");
            }
            uint firstOffset = ReadUInt32(bytes, 0);
            if (firstOffset < 24 || firstOffset > bytes.Length || firstOffset % 4 != 0)
            {
                throw new InvalidDataException("SSS.MKF 索引表无效。 ");
            }
            int chunkCount = checked((int)(firstOffset / 4) - 1);
            if (chunkCount <= SssScriptChunkIndex)
            {
                throw new InvalidDataException("SSS.MKF 缺少对象或脚本块。 ");
            }

            (int eventStart, int eventEnd) = ReadChunkBounds(bytes, chunkCount, SssEventChunkIndex);
            (int objectStart, int objectEnd) = ReadChunkBounds(bytes, chunkCount, SssObjectChunkIndex);
            (int scriptStart, int scriptEnd) = ReadChunkBounds(bytes, chunkCount, SssScriptChunkIndex);
            int objectLength = objectEnd - objectStart;
            int scriptLength = scriptEnd - scriptStart;
            int eventLength = eventEnd - eventStart;
            if (eventLength <= 0 || eventLength % EventRecordSize != 0)
            {
                throw new InvalidDataException("SSS.MKF 事件记录宽度不匹配仙剑 98。 ");
            }
            if (objectLength <= 0 || objectLength % ObjectRecordSize != 0 ||
                scriptLength <= 0 || scriptLength % ScriptRecordSize != 0)
            {
                throw new InvalidDataException("SSS.MKF 对象或脚本记录宽度不匹配仙剑 98。 ");
            }
            int objectCount = objectLength / ObjectRecordSize;
            if (objectCount <= 0 || objectCount > MaximumSavedObjectCount)
            {
                throw new InvalidDataException($"SSS.MKF 对象数 {objectCount} 超出存档容量。 ");
            }
            return new ReferenceData(bytes.AsSpan(objectStart, objectLength).ToArray(),
                objectCount, scriptLength / ScriptRecordSize, eventLength);
        }

        private static (int Start, int End) ReadChunkBounds(byte[] bytes, int chunkCount, int index)
        {
            if (index < 0 || index >= chunkCount)
            {
                throw new InvalidDataException("SSS.MKF 块索引越界。 ");
            }
            uint rawStart = ReadUInt32(bytes, index * 4);
            uint rawEnd = ReadUInt32(bytes, (index + 1) * 4);
            if (rawStart > int.MaxValue || rawEnd > int.MaxValue || rawStart > rawEnd || rawEnd > bytes.Length)
            {
                throw new InvalidDataException("SSS.MKF 块边界无效。 ");
            }
            return ((int)rawStart, (int)rawEnd);
        }

        private static uint ReadUInt32(byte[] bytes, int offset) =>
            (uint)bytes[offset] |
            (uint)bytes[offset + 1] << 8 |
            (uint)bytes[offset + 2] << 16 |
            (uint)bytes[offset + 3] << 24;
    }
}
