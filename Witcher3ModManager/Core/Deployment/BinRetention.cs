namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// What a mod claims for the bin domain, and whether it is currently enabled.
/// </summary>
public sealed record BinClaim(string ModName, bool IsEnabled, IReadOnlyList<string> BinFiles);

/// <summary>
/// A disabled mod whose bin files are still sitting in the game directory.
/// </summary>
public sealed record BinRetention(string ModName, IReadOnlyList<string> RetainedFiles);

/// <summary>
/// Finds bin files that outlive the mod that put them there.
///
/// Disabling a mod never removes its bin files (by design: several mods may write the same
/// path, so deleting one mod's copy could break another). The consequence is that a disabled
/// mod can still be affecting the game. This reports that fact for display; it deliberately
/// does not fold those mods into conflict analysis, which stays about what is enabled.
///
/// Pure: the caller supplies the "does this file exist in the game directory" check, so the
/// rule is testable without touching a real installation.
/// </summary>
public static class BinRetentionInspector
{
    public static IReadOnlyList<BinRetention> FindRetained(
        IEnumerable<BinClaim>? claims,
        Func<string, bool>? fileExistsInGame)
    {
        if (claims is null || fileExistsInGame is null) return Array.Empty<BinRetention>();

        var retained = new List<BinRetention>();
        foreach (var claim in claims)
        {
            if (claim.IsEnabled) continue;
            if (claim.BinFiles is null || claim.BinFiles.Count == 0) continue;

            var present = claim.BinFiles
                .Where(f => !string.IsNullOrWhiteSpace(f) && fileExistsInGame(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (present.Count > 0)
                retained.Add(new BinRetention(claim.ModName, present));
        }

        return retained
            .OrderBy(r => r.ModName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
