using System.Text.Json;

namespace PalSaveEditor.Core;

public sealed record LearnedMagicProfileMigrationCatalog(
    int RuntimeObjectCount,
    HashSet<ushort> CurrentSkillObjectIds,
    IReadOnlyDictionary<ushort, ushort> LegacyToCurrentObjectIds,
    int HistoricalCatalogCount,
    int SkippedHistoricalCatalogCount,
    string? InferredHistoricalProfileVersion,
    string MigrationEvidence);

public sealed record LearnedMagicProfileMigrationResult(
    int Migrated,
    int RemovedRetired,
    int RemovedOutOfRange,
    int RemovedDuplicates,
    int PreservedUnknownInRange)
{
    public int ChangedCount => Migrated + RemovedRetired +
        RemovedOutOfRange + RemovedDuplicates;
    public bool Changed => ChangedCount != 0;
}

public static class LearnedMagicProfileMigration
{
    private const string ContentCatalogSchema = "PAL98.ContentCatalog.v1";
    private const long MaximumCatalogBytes = 4L * 1024L * 1024L;

    public static LearnedMagicProfileMigrationCatalog Resolve(
        PalResourceCatalog resources,
        string? savePath = null)
    {
        if (resources is null)
        {
            throw new ArgumentNullException(nameof(resources));
        }
        if (!resources.IsActiveProfile ||
            string.IsNullOrWhiteSpace(resources.ActiveProfileId) ||
            string.IsNullOrWhiteSpace(resources.ActiveProfileVersion) ||
            string.IsNullOrWhiteSpace(
                resources.ResourceContext.ContentCatalogPath))
        {
            throw new InvalidOperationException(
                "已学仙术 Profile 迁移只适用于带 CONTENT.CATALOG 的 active profile。");
        }

        PalSkillRegistryResolution currentResolution =
            PalSkillRegistryCatalog.ResolveContentCatalog(resources);
        var currentByLogicalId = currentResolution.Skills.ToDictionary(
            skill => skill.LogicalId,
            skill => skill.ObjectId,
            StringComparer.Ordinal);
        HashSet<ushort> currentObjectIds = currentResolution.Skills
            .Select(skill => skill.ObjectId)
            .ToHashSet();

        string currentCatalogPath =
            resources.ResourceContext.ContentCatalogPath!;
        using JsonDocument currentDocument = ParseCatalog(currentCatalogPath);
        JsonElement currentRoot = currentDocument.RootElement;
        string saveNamespace = RequireString(
            currentRoot, "save_namespace", currentCatalogPath);

        string profilesRoot = Path.Combine(
            resources.ResourceContext.GameDirectory,
            "palmod",
            "Profiles");
        string profileRoot = Path.Combine(
            profilesRoot,
            resources.ActiveProfileId!);
        var candidates = new Dictionary<ushort, ushort>();
        var ambiguous = new HashSet<ushort>();
        var versionCatalogs = new List<HistoricalCatalog>
        {
            new(
                resources.ActiveProfileVersion!,
                File.GetLastWriteTimeUtc(currentCatalogPath),
                currentResolution.Skills.ToDictionary(
                    skill => skill.ObjectId,
                    skill => skill.LogicalId)),
        };
        int historicalCatalogCount = 0;
        int skippedHistoricalCatalogCount = 0;
        if (Directory.Exists(profileRoot) &&
            !IsReparsePoint(new DirectoryInfo(profileRoot)))
        {
            int visited = 0;
            foreach (string versionDirectory in
                     Directory.EnumerateDirectories(profileRoot))
            {
                if (++visited > 128)
                {
                    skippedHistoricalCatalogCount++;
                    break;
                }
                var versionInfo = new DirectoryInfo(versionDirectory);
                if (IsReparsePoint(versionInfo) ||
                    string.Equals(
                        versionInfo.Name,
                        resources.ActiveProfileVersion,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string path = Path.Combine(
                    versionDirectory,
                    "palmod",
                    "profile",
                    "content-catalog.json");
                try
                {
                    if (!File.Exists(path) ||
                        new FileInfo(path).Length is <= 0 or > MaximumCatalogBytes ||
                        !HasNoReparsePoints(versionDirectory, path))
                    {
                        skippedHistoricalCatalogCount++;
                        continue;
                    }
                    using JsonDocument document = ParseCatalog(path);
                    JsonElement root = document.RootElement;
                    RequireValue(root, "schema", ContentCatalogSchema, path);
                    RequireValue(
                        root, "profile_id", resources.ActiveProfileId!, path);
                    RequireValue(
                        root, "profile_version", versionInfo.Name, path);
                    RequireValue(root, "save_namespace", saveNamespace, path);
                    historicalCatalogCount++;
                    var logicalByObjectId = new Dictionary<ushort, string>();

                    foreach (JsonElement magic in RequireArray(
                                 root, "magics", path))
                    {
                        if (!IsPersistableMagic(magic, path))
                        {
                            continue;
                        }
                        string logicalId = RequireString(
                            magic, "logical_id", path);
                        int rawObjectId = RequireNonnegativeInt32(
                            magic, "object_id", path);
                        if (rawObjectId is <= 0 or > ushort.MaxValue)
                        {
                            continue;
                        }
                        ushort legacyObjectId = (ushort)rawObjectId;
                        if (logicalByObjectId.TryGetValue(
                                legacyObjectId, out string? existingLogicalId) &&
                            !string.Equals(
                                existingLogicalId, logicalId,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                $"{path} 的技能对象号 {legacyObjectId} 重复映射。");
                        }
                        logicalByObjectId[legacyObjectId] = logicalId;
                        if (currentObjectIds.Contains(legacyObjectId))
                        {
                            continue;
                        }
                        ushort target = currentByLogicalId.TryGetValue(
                            logicalId, out ushort currentObjectId)
                            ? currentObjectId
                            : (ushort)0;
                        if (candidates.TryGetValue(
                                legacyObjectId, out ushort existing) &&
                            existing != target)
                        {
                            ambiguous.Add(legacyObjectId);
                        }
                        else
                        {
                            candidates[legacyObjectId] = target;
                        }
                    }
                    versionCatalogs.Add(new HistoricalCatalog(
                        versionInfo.Name,
                        File.GetLastWriteTimeUtc(path),
                        logicalByObjectId));
                }
                catch (Exception ex) when (ex is IOException or
                    UnauthorizedAccessException or InvalidDataException or
                    JsonException)
                {
                    // Historical staging is optional evidence. A malformed or
                    // unavailable old version must never weaken validation of
                    // the active, descriptor-verified catalog.
                    skippedHistoricalCatalogCount++;
                }
            }
        }

        foreach (ushort objectId in ambiguous)
        {
            candidates.Remove(objectId);
        }
        string? inferredHistoricalProfileVersion = null;
        string migrationEvidence = "unambiguous-profile-history";
        if (!string.IsNullOrWhiteSpace(savePath) && File.Exists(savePath))
        {
            DateTime cutoff = File.GetLastWriteTimeUtc(savePath)
                .AddSeconds(2);
            HistoricalCatalog? selected = versionCatalogs
                .Where(catalog => catalog.LastWriteUtc <= cutoff)
                .OrderByDescending(catalog => catalog.LastWriteUtc)
                .FirstOrDefault();
            if (selected is not null && string.Equals(
                    selected.ProfileVersion,
                    resources.ActiveProfileVersion,
                    StringComparison.Ordinal))
            {
                migrationEvidence = "active-profile-at-save-time";
            }
            else if (selected is not null)
            {
                inferredHistoricalProfileVersion = selected.ProfileVersion;
                migrationEvidence =
                    "save-last-write-before-immutable-profile-staging";
                foreach (KeyValuePair<ushort, string> mapping in
                         selected.LogicalIdsByObjectId)
                {
                    ushort legacyObjectId = mapping.Key;
                    string logicalId = mapping.Value;
                    if (currentObjectIds.Contains(legacyObjectId))
                    {
                        continue;
                    }
                    candidates[legacyObjectId] =
                        currentByLogicalId.TryGetValue(
                            logicalId, out ushort currentObjectId)
                            ? currentObjectId
                            : (ushort)0;
                }
            }
        }
        return new LearnedMagicProfileMigrationCatalog(
            resources.RuntimeObjectRecordCount,
            currentObjectIds,
            candidates,
            historicalCatalogCount,
            skippedHistoricalCatalogCount,
            inferredHistoricalProfileVersion,
            migrationEvidence);
    }

    public static LearnedMagicProfileMigrationResult Apply(
        ExtendedRoleMagicState state,
        LearnedMagicProfileMigrationCatalog catalog)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }
        if (catalog.RuntimeObjectCount is <= 0 or > 0x8000)
        {
            throw new InvalidDataException("active profile 对象表容量无效。");
        }

        int migrated = 0;
        int removedRetired = 0;
        int removedOutOfRange = 0;
        int removedDuplicates = 0;
        int preservedUnknownInRange = 0;
        foreach (ushort[] role in state.Roles)
        {
            ushort[] transformed = (ushort[])role.Clone();
            var seen = new HashSet<ushort>();
            bool removedFromRole = false;
            for (int slot = 0; slot < role.Length; slot++)
            {
                ushort savedObjectId = role[slot];
                if (savedObjectId == 0)
                {
                    continue;
                }
                ushort replacement = savedObjectId;
                bool keep = true;
                if (catalog.CurrentSkillObjectIds.Contains(savedObjectId))
                {
                    // The active profile owns this exact id.
                }
                else if (catalog.LegacyToCurrentObjectIds.TryGetValue(
                             savedObjectId, out ushort currentObjectId))
                {
                    if (currentObjectId != 0 &&
                        currentObjectId < catalog.RuntimeObjectCount &&
                        catalog.CurrentSkillObjectIds.Contains(currentObjectId))
                    {
                        replacement = currentObjectId;
                        migrated++;
                    }
                    else if (currentObjectId == 0)
                    {
                        keep = false;
                        removedRetired++;
                    }
                }
                else if (savedObjectId >= catalog.RuntimeObjectCount)
                {
                    keep = false;
                    removedOutOfRange++;
                }
                else
                {
                    preservedUnknownInRange++;
                }

                if (!keep)
                {
                    transformed[slot] = 0;
                    removedFromRole = true;
                    continue;
                }
                transformed[slot] = replacement;
                if (!seen.Add(replacement))
                {
                    transformed[slot] = 0;
                    removedFromRole = true;
                    removedDuplicates++;
                }
            }

            if (removedFromRole)
            {
                int write = 0;
                foreach (ushort magic in transformed)
                {
                    if (magic != 0)
                    {
                        role[write++] = magic;
                    }
                }
                Array.Clear(role, write, role.Length - write);
            }
            else
            {
                Array.Copy(transformed, role, role.Length);
            }
        }

        var result = new LearnedMagicProfileMigrationResult(
            migrated,
            removedRetired,
            removedOutOfRange,
            removedDuplicates,
            preservedUnknownInRange);
        if (result.Changed && state.HasRandomLevelProgress)
        {
            state.HasRandomLevelProgress = false;
            Array.Clear(
                state.RandomLevelAppliedThroughLevel,
                0,
                state.RandomLevelAppliedThroughLevel.Length);
        }
        return result;
    }

    private static JsonDocument ParseCatalog(string path)
    {
        return JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 256,
            });
    }

    private static bool IsPersistableMagic(JsonElement magic, string source)
    {
        string status = RequireString(magic, "status", source);
        bool supportedStatus = status is "pal98-runtime-verified" or
            "pal98-static-verified";
        bool learnable = RequireBoolean(magic, "learnable", source);
        bool grantable = RequireBoolean(magic, "grantable", source);
        HashSet<string> exclusions = RequireArray(magic, "exclusions", source)
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : throw new InvalidDataException(
                    $"{source}.exclusions 含非字符串。"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
        return supportedStatus && learnable && grantable &&
            !exclusions.Contains("not-learnable") &&
            !exclusions.Contains("not-grantable");
    }

    private static bool IsReparsePoint(FileSystemInfo info) =>
        (info.Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool HasNoReparsePoints(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(path);
        string prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        DirectoryInfo? current = new FileInfo(fullPath).Directory;
        while (current is not null &&
               current.FullName.StartsWith(
                   prefix, StringComparison.OrdinalIgnoreCase))
        {
            if (IsReparsePoint(current))
            {
                return false;
            }
            current = current.Parent;
        }
        return current is not null &&
            string.Equals(
                current.FullName.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                fullRoot,
                StringComparison.OrdinalIgnoreCase) &&
            !IsReparsePoint(current);
    }

    private static JsonElement.ArrayEnumerator RequireArray(
        JsonElement owner,
        string property,
        string source)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"{source} 缺少数组字段 {property}。");
        }
        return value.EnumerateArray();
    }

    private static string RequireString(
        JsonElement owner,
        string property,
        string source)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException(
                $"{source} 缺少字符串字段 {property}。");
        }
        return value.GetString()!;
    }

    private static int RequireNonnegativeInt32(
        JsonElement owner,
        string property,
        string source)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int result) || result < 0)
        {
            throw new InvalidDataException(
                $"{source}.{property} 不是非负整数。");
        }
        return result;
    }

    private static bool RequireBoolean(
        JsonElement owner,
        string property,
        string source)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) ||
            (value.ValueKind != JsonValueKind.True &&
             value.ValueKind != JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"{source}.{property} 不是布尔值。");
        }
        return value.GetBoolean();
    }

    private static void RequireValue(
        JsonElement owner,
        string property,
        string expected,
        string source)
    {
        string actual = RequireString(owner, property, source);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{source}.{property}={actual}，预期 {expected}。");
        }
    }

    private sealed record HistoricalCatalog(
        string ProfileVersion,
        DateTime LastWriteUtc,
        IReadOnlyDictionary<ushort, string> LogicalIdsByObjectId);
}
