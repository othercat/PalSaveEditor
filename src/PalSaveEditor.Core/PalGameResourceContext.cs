using System.Security.Cryptography;
using System.Text.Json;

namespace PalSaveEditor.Core;

/// <summary>
/// Resolves the resource set that PALDLL actually exposes for the active game
/// profile. A present but invalid current.json is an error: silently falling
/// back to the classic root resources would attach the wrong story layout to a
/// save and make a later write unsafe.
/// </summary>
public sealed record PalGameResourceContext(
    string GameDirectory,
    string ResourceDirectory,
    string? ProfileId,
    string? ProfileVersion,
    string? ProfileDisplayName,
    string? DescriptorPath,
    string? DescriptorSha256)
{
    public bool IsActiveProfile => !string.IsNullOrWhiteSpace(ProfileId);

    public string DescribeResource(string fileName)
    {
        string path = Path.Combine(ResourceDirectory, fileName);
        return IsActiveProfile
            ? $"PALDLL active profile {ProfileId}@{ProfileVersion} ({ProfileDisplayName}) -> {path}"
            : path;
    }
}

public static class PalGameResourceContextResolver
{
    private const string PointerSchema = "PAL98.EffectiveGameProfilePointer.v1";
    private const string DescriptorSchema = "PAL98.GameProfile.v1";

    public static PalGameResourceContext Resolve(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new ArgumentException("游戏资料目录不能为空。", nameof(gameDirectory));
        }

        string requestedDirectory = Path.GetFullPath(gameDirectory);
        string profilesDirectory = Path.Combine(requestedDirectory, "palmod", "Profiles");
        string pointerPath = Path.Combine(profilesDirectory, "current.json");
        if (!File.Exists(pointerPath))
        {
            return new PalGameResourceContext(
                requestedDirectory,
                requestedDirectory,
                null,
                null,
                null,
                null,
                null);
        }

        using JsonDocument pointerDocument = ParseJson(pointerPath, "active profile pointer");
        JsonElement pointer = pointerDocument.RootElement;
        RequireValue(pointer, "schema", pointerPath, PointerSchema);
        string profileId = RequireString(pointer, "profile_id", pointerPath);
        string profileVersion = RequireString(pointer, "profile_version", pointerPath);
        string descriptorSha256 = RequireSha256(pointer, "descriptor_sha256", pointerPath);
        string stagingRelativePath = RequireString(pointer, "staging_relative_path", pointerPath);

        string stagedDirectory = ResolveContainedPath(
            profilesDirectory,
            stagingRelativePath,
            "active profile staging directory");
        string descriptorPath = Path.Combine(stagedDirectory, "manifest", "game-profile.json");
        if (!File.Exists(descriptorPath))
        {
            throw new FileNotFoundException("active profile descriptor 不存在。", descriptorPath);
        }
        VerifyFileIdentity(descriptorPath, null, descriptorSha256, "active profile descriptor");

        using JsonDocument descriptorDocument = ParseJson(descriptorPath, "active profile descriptor");
        JsonElement descriptor = descriptorDocument.RootElement;
        RequireValue(descriptor, "schema", descriptorPath, DescriptorSchema);
        RequireValue(descriptor, "profile_id", descriptorPath, profileId);
        RequireValue(descriptor, "profile_version", descriptorPath, profileVersion);
        string displayName = RequireString(descriptor, "display_name", descriptorPath);

        if (!descriptor.TryGetProperty("resource_set", out JsonElement resourceSet) ||
            resourceSet.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{descriptorPath} 缺少 resource_set 数组。");
        }

        string wordPath = ValidateRequiredResource(stagedDirectory, resourceSet, "WORD.DAT");
        string sssPath = ValidateRequiredResource(stagedDirectory, resourceSet, "SSS.MKF");
        string resourcesDirectory = Path.GetDirectoryName(wordPath)
            ?? throw new InvalidDataException("active profile WORD.DAT 没有有效父目录。");
        if (!string.Equals(resourcesDirectory, Path.GetDirectoryName(sssPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("active profile 的 WORD.DAT 与 SSS.MKF 不在同一资源目录。");
        }

        return new PalGameResourceContext(
            requestedDirectory,
            resourcesDirectory,
            profileId,
            profileVersion,
            displayName,
            descriptorPath,
            descriptorSha256);
    }

    private static string ValidateRequiredResource(
        string stagedDirectory,
        JsonElement resourceSet,
        string kind)
    {
        JsonElement? match = null;
        foreach (JsonElement item in resourceSet.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("kind", out JsonElement kindElement) &&
                string.Equals(kindElement.GetString(), kind, StringComparison.OrdinalIgnoreCase))
            {
                match = item;
                break;
            }
        }

        if (match is null)
        {
            throw new InvalidDataException($"active profile descriptor 未声明 {kind}。");
        }

        string relativePath = RequireString(match.Value, "relative_path", $"resource_set[{kind}]");
        string sha256 = RequireSha256(match.Value, "sha256", $"resource_set[{kind}]");
        if (!match.Value.TryGetProperty("size_bytes", out JsonElement sizeElement) ||
            !sizeElement.TryGetInt64(out long expectedLength) || expectedLength < 0)
        {
            throw new InvalidDataException($"resource_set[{kind}] 的 size_bytes 无效。");
        }

        string path = ResolveContainedPath(stagedDirectory, relativePath, kind);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"active profile 的 {kind} 不存在。", path);
        }
        VerifyFileIdentity(path, expectedLength, sha256, kind);
        return path;
    }

    private static JsonDocument ParseJson(string path, string label)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{label} JSON 无效：{path}", ex);
        }
    }

    private static string ResolveContainedPath(string root, string relativePath, string label)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"{label} 必须是非空相对路径：{relativePath}");
        }

        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        string prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                        Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{label} 越出允许目录：{relativePath}");
        }
        return fullPath;
    }

    private static string RequireString(JsonElement element, string property, string source)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"{source} 缺少非空字符串 {property}。");
        }
        return value.GetString()!;
    }

    private static string RequireSha256(JsonElement element, string property, string source)
    {
        string value = RequireString(element, property, source).ToLowerInvariant();
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"{source} 的 {property} 不是 SHA-256。");
        }
        return value;
    }

    private static void RequireValue(JsonElement element, string property, string source, string expected)
    {
        string actual = RequireString(element, property, source);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{source} 的 {property}={actual}，预期 {expected}。");
        }
    }

    private static void VerifyFileIdentity(string path, long? expectedLength, string expectedSha256, string label)
    {
        var info = new FileInfo(path);
        if (expectedLength is not null && info.Length != expectedLength.Value)
        {
            throw new InvalidDataException(
                $"{label} 长度不匹配：实际 {info.Length:N0}，descriptor {expectedLength.Value:N0}。");
        }

        using FileStream stream = File.OpenRead(path);
        using SHA256 sha256 = SHA256.Create();
        string actual = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{label} SHA-256 不匹配：{path}");
        }
    }
}
