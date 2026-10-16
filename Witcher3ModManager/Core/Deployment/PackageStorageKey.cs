namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// The name a package's copies live under: its record id when it has one, its display name
/// otherwise.
///
/// A display name is not an identity: two mods can share one, and renaming a record makes its
/// copies unreachable. The record id survives both, which is what a folder of per-package copies
/// needs. The name stays as the fallback for records old enough to have no id.
/// </summary>
public static class PackageStorageKey
{
    public static string For(string? packageId, string? displayName)
    {
        var id = (packageId ?? "").Trim();
        if (id.Length > 0) return id;

        var name = (displayName ?? "").Trim();
        return name.Length > 0 ? name : "unknown";
    }
}
