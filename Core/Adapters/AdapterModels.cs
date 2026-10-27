using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ShiroBot.Adapters;

/// <summary>
/// The platform families an adapter is built on, read from the ShiroBot.Model.* assemblies its DLL references
/// (ShiroBot.Model.QQ → "QQ"). An adapter's own platform names the protocol (Milky, OneBot); the model says which
/// chat platform that is, so the Dashboard can show "QQ · Milky" next to "Telegram" and "Discord".
/// </summary>
internal static class AdapterModels
{
    private const string ModelPrefix = "ShiroBot.Model.";
    private static readonly ConcurrentDictionary<string, (DateTime WrittenAt, string[] Models)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Model names the DLL references, without reading or running its code; empty when unreadable.</summary>
    public static IReadOnlyList<string> Of(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath)) return [];
        var writtenAt = File.GetLastWriteTimeUtc(assemblyPath);
        if (Cache.TryGetValue(assemblyPath, out var cached) && cached.WrittenAt == writtenAt) return cached.Models;

        string[] models;
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            models = metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
                .Where(name => name.StartsWith(ModelPrefix, StringComparison.Ordinal) && name.Length > ModelPrefix.Length)
                .Select(name => name[ModelPrefix.Length..])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            models = [];
        }

        Cache[assemblyPath] = (writtenAt, models);
        return models;
    }
}
