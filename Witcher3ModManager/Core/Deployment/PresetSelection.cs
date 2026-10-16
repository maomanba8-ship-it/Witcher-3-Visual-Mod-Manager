namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Reads one preset's answer about one package.
///
/// A preset is a statement about packages, so it has to name them the way the library does: by
/// record. The presets written before that named folders instead, and those files are still out
/// there, so the older shape is read as what it is rather than guessed at from how a key looks —
/// the preset says which of the two it is.
/// </summary>
public static class PresetSelection
{
    /// <summary>The version new presets are written with: keys are package ids.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// What this preset wants for one package, or null when it says nothing about it. Folders are
    /// only consulted for presets written before packages had ids.
    /// </summary>
    public static bool? Wanted(
        string? packageId,
        IEnumerable<string>? folders,
        IReadOnlyDictionary<string, bool>? states,
        int version)
    {
        if (states is null || states.Count == 0) return null;

        if (version >= CurrentVersion && !string.IsNullOrWhiteSpace(packageId))
            return states.TryGetValue(packageId!, out var byPackage) ? byPackage : null;

        foreach (var folder in folders ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            if (states.TryGetValue(folder, out var byFolder)) return byFolder;
        }

        return null;
    }
}
