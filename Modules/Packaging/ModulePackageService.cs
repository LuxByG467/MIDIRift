using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MIDIRift.Modules.Packaging;

public sealed class ModulePackageService : IModulePackageService
{
    private static readonly HashSet<string> ExecutableExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe", ".so", ".dylib", ".jar", ".dex"
        };

    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ModulePackageInspection Inspect(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("MIDIRift module package was not found.", packagePath);

        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Package does not contain manifest.json at its root.");

        ModuleManifest manifest;
        using (var stream = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<ModuleManifest>(stream, _json)
                ?? throw new InvalidDataException("manifest.json is empty or invalid.");
        }

        ValidateManifest(manifest);

        var effective = manifest.Modules
            .SelectMany(m => m.Permissions ?? Array.Empty<ModulePermission>())
            .Distinct()
            .OrderBy(p => p)
            .ToArray();

        bool executable = archive.Entries.Any(e =>
            ExecutableExtensions.Contains(Path.GetExtension(e.FullName)));

        string actualHash = ComputeSha256(packagePath);
        bool hashMatches = string.IsNullOrWhiteSpace(manifest.Sha256) ||
            string.Equals(NormalizeHash(manifest.Sha256), actualHash, StringComparison.OrdinalIgnoreCase);

        var warnings = new List<string>();
        if (!hashMatches)
            warnings.Add("Package SHA-256 does not match the manifest.");
        if (executable)
            warnings.Add("Executable payload detected; Stage H will not load it in-process.");
        if (effective.Contains(ModulePermission.Network))
            warnings.Add("Package requests network access; future host mediation is required.");
        if (effective.Contains(ModulePermission.FileWrite))
            warnings.Add("Package requests file-write access; future host mediation is required.");

        return new ModulePackageInspection(
            manifest,
            effective,
            hashMatches,
            !string.IsNullOrWhiteSpace(manifest.Signature),
            executable,
            warnings);
    }

    private static void ValidateManifest(ModuleManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.MidiriftApi);

        if (manifest.Modules is null || manifest.Modules.Count == 0)
            throw new InvalidDataException("A module package must declare at least one module.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in manifest.Modules)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(module.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(module.Name);
            if (!ids.Add(module.Id))
                throw new InvalidDataException($"Duplicate module id '{module.Id}'.");
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string NormalizeHash(string hash) =>
        hash.Replace("sha256:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "")
            .Trim();
}
