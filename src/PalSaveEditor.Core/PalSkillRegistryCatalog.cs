using System.Security.Cryptography;
using System.Text.Json;

namespace PalSaveEditor.Core;

public sealed record PalRegisteredSkill(
    string LogicalId,
    string DisplayName,
    string SkillSetId,
    string SkillSetDisplayName,
    string SourceSetId,
    ushort ObjectId,
    bool DefaultRandomCandidate,
    bool Deprecated);

public sealed record PalSkillRegistryResolution(
    string RegistryId,
    string RegistryVersion,
    string RegistrySha256,
    IReadOnlyList<string> SourceSetIds,
    IReadOnlyList<PalRegisteredSkill> Skills,
    string Evidence);

/// <summary>
/// Reads the independent logical-skill registry and resolves only mappings that
/// belong to the currently loaded resource set. It never reads ConfigTool's
/// random-skill selection, so save editing remains independent from gameplay
/// candidate-pool exclusions.
/// </summary>
public sealed class PalSkillRegistryCatalog
{
    public const string RegistrySchema = "PAL98.SkillRegistry.v1";
    public const string ContentCatalogSchema = "PAL98.ContentCatalog.v1";
    public const string RelativeRegistryPath = "palmod/PAL98_SKILL_REGISTRY.v1.json";
    public const string ClassicSkillSetId = "skill-set.pal98-classic.random-candidates";
    public const string ComposedHunqian167SkillSetId =
        "skill-set.hunqian-167-composed.random-candidates";

    private sealed record ResourceIdentity(string Kind, string Sha256);
    private sealed record SourceSet(
        string Id,
        IReadOnlyList<string> ProfileIds,
        string InventoryState,
        int ObjectRecordSize,
        int ObjectRecordCount,
        IReadOnlyList<ResourceIdentity> Resources);
    private sealed record SkillSet(
        string Id,
        string DisplayName,
        IReadOnlyList<string> SourceSetIds,
        IReadOnlyList<string> MemberLogicalIds,
        string SelectionState);
    private sealed record SourceBinding(
        string SourceSetId,
        ushort ObjectId,
        int ObjectRecordSize,
        ushort? Pal98TargetObjectId);
    private sealed record Skill(
        string LogicalId,
        string DisplayName,
        string OwnerSourceSetId,
        bool DefaultRandomCandidate,
        bool Deprecated,
        IReadOnlyList<SourceBinding> Bindings);

    private readonly IReadOnlyList<SourceSet> _sourceSets;
    private readonly IReadOnlyList<SkillSet> _skillSets;
    private readonly IReadOnlyDictionary<string, Skill> _skills;

    private PalSkillRegistryCatalog(
        string registryId,
        string registryVersion,
        string registrySha256,
        IReadOnlyList<SourceSet> sourceSets,
        IReadOnlyList<SkillSet> skillSets,
        IReadOnlyDictionary<string, Skill> skills)
    {
        RegistryId = registryId;
        RegistryVersion = registryVersion;
        RegistrySha256 = registrySha256;
        _sourceSets = sourceSets;
        _skillSets = skillSets;
        _skills = skills;
    }

    public string RegistryId { get; }
    public string RegistryVersion { get; }
    public string RegistrySha256 { get; }

    public static string ResolvePath(string gameDirectory)
    {
        string nested = Path.Combine(
            gameDirectory,
            RelativeRegistryPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(nested))
        {
            return nested;
        }

        string direct = Path.Combine(gameDirectory, "PAL98_SKILL_REGISTRY.v1.json");
        return File.Exists(direct) ? direct : nested;
    }

    public static PalSkillRegistryCatalog Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 256,
        });
        JsonElement root = RequireObject(document.RootElement, "skill registry");
        RequireValue(root, "schema", RegistrySchema, "skill registry");
        string registryId = RequireString(root, "registry_id", "skill registry");
        string registryVersion = RequireString(root, "registry_version", "skill registry");

        var sourceSets = new List<SourceSet>();
        var sourceSetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement sourceElement in RequireArray(root, "source_sets", "skill registry"))
        {
            string id = RequireString(sourceElement, "source_set_id", "source set");
            if (!sourceSetIds.Add(id))
            {
                throw new InvalidDataException($"skill registry 含重复 source_set_id：{id}");
            }
            JsonElement shape = RequireProperty(sourceElement, "table_shape", JsonValueKind.Object, id);
            var resources = new List<ResourceIdentity>();
            foreach (JsonElement resource in RequireArray(sourceElement, "resources", id))
            {
                resources.Add(new ResourceIdentity(
                    RequireString(resource, "kind", id),
                    RequireSha256(resource, "sha256", id)));
            }
            sourceSets.Add(new SourceSet(
                id,
                ReadStringArray(sourceElement, "profile_ids", id),
                RequireString(sourceElement, "inventory_state", id),
                RequireInt32(shape, "object_record_size", id),
                RequireInt32(shape, "object_records", id),
                resources));
        }

        var skillSets = new List<SkillSet>();
        var skillSetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement setElement in RequireArray(root, "skill_sets", "skill registry"))
        {
            string id = RequireString(setElement, "skill_set_id", "skill set");
            if (!skillSetIds.Add(id))
            {
                throw new InvalidDataException($"skill registry 含重复 skill_set_id：{id}");
            }
            skillSets.Add(new SkillSet(
                id,
                RequireString(setElement, "display_name", id),
                ReadStringArray(setElement, "source_set_ids", id),
                ReadStringArray(setElement, "member_logical_ids", id),
                RequireString(setElement, "selection_state", id)));
        }

        var skills = new Dictionary<string, Skill>(StringComparer.Ordinal);
        foreach (JsonElement skillElement in RequireArray(root, "skills", "skill registry"))
        {
            string logicalId = RequireString(skillElement, "logical_id", "skill");
            var bindings = new List<SourceBinding>();
            foreach (JsonElement binding in RequireArray(skillElement, "source_bindings", logicalId))
            {
                int objectId = RequireInt32(binding, "object_id", logicalId);
                int? targetId = ReadNullableInt32(binding, "pal98_target_object_id", logicalId);
                if ((uint)objectId > ushort.MaxValue ||
                    (targetId is not null && (uint)targetId.Value > ushort.MaxValue))
                {
                    throw new InvalidDataException($"{logicalId} 的对象号超出 16 位存档范围。");
                }
                bindings.Add(new SourceBinding(
                    RequireString(binding, "source_set_id", logicalId),
                    (ushort)objectId,
                    RequireInt32(binding, "object_record_size", logicalId),
                    targetId is null ? null : (ushort)targetId.Value));
            }

            var skill = new Skill(
                logicalId,
                RequireString(skillElement, "display_name", logicalId),
                RequireString(skillElement, "owner_source_set_id", logicalId),
                RequireBoolean(skillElement, "default_random_candidate", logicalId),
                RequireBoolean(skillElement, "deprecated", logicalId),
                bindings);
            if (skills.ContainsKey(logicalId))
            {
                throw new InvalidDataException($"skill registry 含重复 logical_id：{logicalId}");
            }
            skills.Add(logicalId, skill);
        }

        return new PalSkillRegistryCatalog(
            registryId,
            registryVersion,
            HashBytes(bytes),
            sourceSets,
            skillSets,
            skills);
    }

    /// <summary>
    /// Resolves the authoritative magic list from an active profile's verified
    /// CONTENT.CATALOG. Unlike random selection, this includes every learnable
    /// and grantable skill, including static-verified extended object IDs.
    /// </summary>
    public static PalSkillRegistryResolution ResolveContentCatalog(
        PalResourceCatalog resources)
    {
        if (resources is null)
        {
            throw new ArgumentNullException(nameof(resources));
        }

        string? path = resources.ResourceContext.ContentCatalogPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "active profile 未提供已校验的 CONTENT.CATALOG。",
                path);
        }

        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 256,
        });
        JsonElement root = RequireObject(document.RootElement, "content catalog");
        RequireValue(root, "schema", ContentCatalogSchema, "content catalog");
        string catalogId = RequireString(root, "catalog_id", "content catalog");
        string catalogVersion = RequireString(root, "catalog_version", "content catalog");
        RequireValue(
            root,
            "profile_id",
            resources.ActiveProfileId
                ?? throw new InvalidDataException("CONTENT.CATALOG 只能用于 active profile。"),
            "content catalog");
        RequireValue(
            root,
            "profile_version",
            resources.ActiveProfileVersion
                ?? throw new InvalidDataException("active profile 缺少版本。"),
            "content catalog");

        var skills = new List<PalRegisteredSkill>();
        var logicalIds = new HashSet<string>(StringComparer.Ordinal);
        var objectIds = new HashSet<ushort>();
        var sourceSetIds = new List<string>();
        var seenSourceSetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement magic in RequireArray(root, "magics", "content catalog"))
        {
            string logicalId = RequireString(magic, "logical_id", "content catalog magic");
            if (!logicalIds.Add(logicalId))
            {
                throw new InvalidDataException($"CONTENT.CATALOG 含重复 logical_id：{logicalId}");
            }

            int rawObjectId = RequireInt32(magic, "object_id", logicalId);
            if (rawObjectId == 0 || rawObjectId > ushort.MaxValue ||
                rawObjectId >= resources.RuntimeObjectRecordCount)
            {
                throw new InvalidDataException(
                    $"{logicalId} 的对象号 {rawObjectId} 越出 active profile 对象表 " +
                    $"0..{Math.Max(0, resources.RuntimeObjectRecordCount - 1)}。");
            }
            ushort objectId = (ushort)rawObjectId;
            if (!objectIds.Add(objectId))
            {
                throw new InvalidDataException($"CONTENT.CATALOG 含重复技能对象号：{objectId}");
            }

            string status = RequireString(magic, "status", logicalId);
            bool supportedStatus = status is "pal98-runtime-verified" or "pal98-static-verified";
            bool learnable = RequireBoolean(magic, "learnable", logicalId);
            bool randomizable = RequireBoolean(magic, "randomizable", logicalId);
            bool grantable = RequireBoolean(magic, "grantable", logicalId);
            IReadOnlyList<string> exclusions = ReadStringArray(magic, "exclusions", logicalId);

            JsonElement.ArrayEnumerator mappings = RequireArray(
                magic,
                "source_mappings",
                logicalId);
            string? sourceSetId = null;
            foreach (JsonElement mapping in mappings)
            {
                string current = RequireString(mapping, "source_set_id", logicalId);
                _ = RequireInt32(mapping, "object_id", logicalId);
                sourceSetId ??= current;
                if (seenSourceSetIds.Add(current))
                {
                    sourceSetIds.Add(current);
                }
            }
            if (sourceSetId is null)
            {
                throw new InvalidDataException($"{logicalId} 没有 source_mappings。 ");
            }

            if (!supportedStatus || !learnable || !grantable ||
                exclusions.Contains("not-learnable", StringComparer.Ordinal) ||
                exclusions.Contains("not-grantable", StringComparer.Ordinal))
            {
                continue;
            }

            (string skillSetId, string skillSetDisplayName) = ResolveContentSkillSet(
                logicalId,
                sourceSetId);
            skills.Add(new PalRegisteredSkill(
                logicalId,
                RequireString(magic, "display_name", logicalId),
                skillSetId,
                skillSetDisplayName,
                sourceSetId,
                objectId,
                randomizable,
                exclusions.Contains("deprecated", StringComparer.Ordinal)));
        }

        if (skills.Count == 0)
        {
            throw new InvalidDataException(
                "active profile CONTENT.CATALOG 没有可写入存档的 learnable/grantable 技能。 ");
        }

        return new PalSkillRegistryResolution(
            catalogId,
            catalogVersion,
            HashBytes(bytes),
            sourceSetIds,
            skills,
            "active-profile-content-catalog-sha256");
    }

    public PalSkillRegistryResolution Resolve(PalResourceCatalog resources)
    {
        if (resources is null)
        {
            throw new ArgumentNullException(nameof(resources));
        }
        var matched = ResolveSourceSets(resources, out string evidence);
        if (matched.Count == 0)
        {
            throw new InvalidDataException(
                "独立技能注册表无法将当前 WORD/SSS/DATA 或 active profile 精确对应到技能来源。");
        }

        var matchedIds = new HashSet<string>(
            matched.Select(source => source.Id),
            StringComparer.Ordinal);
        var result = new List<PalRegisteredSkill>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SkillSet set in _skillSets)
        {
            if (set.SelectionState == "unavailable" ||
                !set.SourceSetIds.Any(matchedIds.Contains))
            {
                continue;
            }
            foreach (string logicalId in set.MemberLogicalIds)
            {
                if (!seen.Add(logicalId) || !_skills.TryGetValue(logicalId, out Skill? skill))
                {
                    continue;
                }
                SourceBinding? binding = skill.Bindings.FirstOrDefault(item =>
                    matchedIds.Contains(item.SourceSetId));
                if (binding is null)
                {
                    continue;
                }

                ushort? objectId = binding.ObjectRecordSize == resources.ObjectRecordSize
                    ? binding.ObjectId
                    : resources.ObjectRecordSize == PalSaveLayout.WinObjectRecordSize
                        ? binding.Pal98TargetObjectId
                        : null;
                if (objectId is null || objectId.Value >= resources.WordCount)
                {
                    continue;
                }
                result.Add(new PalRegisteredSkill(
                    skill.LogicalId,
                    skill.DisplayName,
                    set.Id,
                    set.DisplayName,
                    binding.SourceSetId,
                    objectId.Value,
                    skill.DefaultRandomCandidate,
                    skill.Deprecated));
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("当前资源身份已识别，但注册表没有可写入该存档的技能映射。");
        }
        return new PalSkillRegistryResolution(
            RegistryId,
            RegistryVersion,
            RegistrySha256,
            matched.Select(source => source.Id).ToArray(),
            result,
            evidence);
    }

    private IReadOnlyList<SourceSet> ResolveSourceSets(
        PalResourceCatalog resources,
        out string evidence)
    {
        Dictionary<string, string> hashes = HashAvailableResources(
            resources.SourceDirectory);
        List<SourceSet> exact = _sourceSets.Where(source =>
            source.Resources.Count >= 2 &&
            source.Resources.All(resource =>
                hashes.TryGetValue(resource.Kind, out string? actual) &&
                string.Equals(actual, resource.Sha256, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (exact.Count != 0)
        {
            evidence = "exact-resource-sha256";
            return exact;
        }

        string? activeProfileId = resources.ActiveProfileId;
        if (!string.IsNullOrWhiteSpace(activeProfileId))
        {
            List<SourceSet> profile = _sourceSets.Where(source =>
                source.InventoryState == "exact-pal98-snapshot" &&
                source.ObjectRecordSize == resources.ObjectRecordSize &&
                source.ObjectRecordCount == resources.WordCount &&
                source.ProfileIds.Any(id =>
                    string.Equals(activeProfileId, id, StringComparison.Ordinal) ||
                    activeProfileId!.StartsWith(id + ".", StringComparison.Ordinal)))
                .ToList();
            if (profile.Count != 0)
            {
                evidence = "active-profile-and-table-shape";
                return profile;
            }
        }

        evidence = "none";
        return Array.Empty<SourceSet>();
    }

    private static Dictionary<string, string> HashAvailableResources(string directory)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string kind in new[] { "SSS.MKF", "DATA.MKF", "WORD.DAT" })
        {
            string path = Path.Combine(directory, kind);
            if (File.Exists(path))
            {
                using FileStream stream = File.OpenRead(path);
                using SHA256 sha256 = SHA256.Create();
                result[kind] = BitConverter.ToString(sha256.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }
        return result;
    }

    private static (string Id, string DisplayName) ResolveContentSkillSet(
        string logicalId,
        string sourceSetId)
    {
        if (logicalId.StartsWith("skill.pal98.classic.", StringComparison.Ordinal))
        {
            return (ClassicSkillSetId, "仙剑98原版技能池");
        }
        if (logicalId.StartsWith("skill.hunqian167.", StringComparison.Ordinal))
        {
            return (ComposedHunqian167SkillSetId, "魂牵梦萦1.67技能池");
        }
        return (
            $"skill-set.content-catalog.{sourceSetId}",
            $"扩展技能池（{sourceSetId}）");
    }

    private static JsonElement RequireObject(JsonElement value, string source)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{source} 必须是 JSON 对象。");
        }
        return value;
    }

    private static JsonElement RequireProperty(
        JsonElement owner,
        string property,
        JsonValueKind kind,
        string source)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) || value.ValueKind != kind)
        {
            throw new InvalidDataException($"{source} 缺少 {kind} 字段 {property}。");
        }
        return value;
    }

    private static JsonElement.ArrayEnumerator RequireArray(
        JsonElement owner,
        string property,
        string source) =>
        RequireProperty(owner, property, JsonValueKind.Array, source).EnumerateArray();

    private static string RequireString(JsonElement owner, string property, string source)
    {
        JsonElement value = RequireProperty(owner, property, JsonValueKind.String, source);
        string? text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException($"{source}.{property} 不能为空。");
        }
        return text!;
    }

    private static string RequireSha256(JsonElement owner, string property, string source)
    {
        string value = RequireString(owner, property, source).ToLowerInvariant();
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"{source}.{property} 不是 SHA-256。");
        }
        return value;
    }

    private static int RequireInt32(JsonElement owner, string property, string source)
    {
        JsonElement value = RequireProperty(owner, property, JsonValueKind.Number, source);
        if (!value.TryGetInt32(out int result) || result < 0)
        {
            throw new InvalidDataException($"{source}.{property} 不是非负整数。");
        }
        return result;
    }

    private static int? ReadNullableInt32(JsonElement owner, string property, string source)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result) || result < 0)
        {
            throw new InvalidDataException($"{source}.{property} 不是 null 或非负整数。");
        }
        return result;
    }

    private static bool RequireBoolean(JsonElement owner, string property, string source)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) ||
            (value.ValueKind != JsonValueKind.True &&
             value.ValueKind != JsonValueKind.False))
        {
            throw new InvalidDataException($"{source}.{property} 不是布尔值。");
        }
        return value.GetBoolean();
    }

    private static IReadOnlyList<string> ReadStringArray(
        JsonElement owner,
        string property,
        string source)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in RequireArray(owner, property, source))
        {
            if (item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString()) ||
                !seen.Add(item.GetString()!))
            {
                throw new InvalidDataException($"{source}.{property} 含空值、重复值或非字符串。");
            }
            result.Add(item.GetString()!);
        }
        return result;
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
            throw new InvalidDataException($"{source}.{property}={actual}，预期 {expected}。");
        }
    }

    private static string HashBytes(byte[] bytes)
    {
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(bytes))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }
}
