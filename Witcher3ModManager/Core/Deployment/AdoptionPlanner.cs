using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>One file an adopted component put in the game's bin directory, relative to that folder.</summary>
public sealed record AdoptedFile(string RelativePath, string Hash);

/// <summary>
/// A folder that is already in the game directory and is being taken into the library. The
/// fingerprint describes what is there now, which is what makes the folder recognisable later as
/// "ours" rather than as somebody else's content.
/// </summary>
public sealed record AdoptionComponent(
    TargetDomain Domain,
    string Folder,
    string Fingerprint,
    IReadOnlyList<AdoptedFile>? BinFiles = null);

/// <summary>Why something could not be adopted as it stands.</summary>
public enum AdoptionProblem
{
    /// <summary>Another package already claims this path in the index.</summary>
    AlreadyOwned,

    /// <summary>What is there could not be fingerprinted, so ownership could not be recorded.</summary>
    Unreadable
}

/// <summary>One thing that was left out, and who owns it when that is the reason.</summary>
public sealed record AdoptionWarning(AdoptionProblem Problem, string Subject, string? Owner = null);

/// <summary>
/// What taking a package into the library would record: the components, and anything that had to
/// be left out. Data only — nothing here reads the disk, and nothing here writes the record.
/// </summary>
public sealed record AdoptionPlan(
    string PackageId,
    string DisplayName,
    IReadOnlyList<ComponentRecord> Components,
    IReadOnlyList<AdoptionWarning> Warnings);

/// <summary>
/// Turns what was found in the game directory into index components.
///
/// Adoption is the answer to mods the manager never installed: they are already there, so the only
/// honest thing to do is describe them. Copying them into the storage directory would double the
/// disk usage of every mod the user installed by hand, and deleting them is the opposite of what
/// they asked for.
///
/// What this deliberately does not do is claim a path somebody else already owns: two components
/// claiming one folder is exactly the state that makes a later rollback delete the wrong content.
/// </summary>
public static class AdoptionPlanner
{
    public static AdoptionPlan Build(
        string packageId,
        string displayName,
        IEnumerable<AdoptionComponent>? found,
        LibraryIndex? index)
    {
        var components = new List<ComponentRecord>();
        var warnings = new List<AdoptionWarning>();
        var binFiles = new List<AdoptedFile>();

        foreach (var component in found ?? Array.Empty<AdoptionComponent>())
        {
            var folder = (component.Folder ?? "").Trim();
            if (folder.Length == 0) continue;

            var relativePath = RelativePathOf(component.Domain, folder);
            var owner = index?.OwnerOf(relativePath);

            if (owner is not null && !string.Equals(owner.ComponentId, ComponentIdOf(packageId, component),
                    StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new AdoptionWarning(AdoptionProblem.AlreadyOwned, relativePath,
                    OwnerNameOf(index, owner)));
                continue;
            }

            if (string.IsNullOrWhiteSpace(component.Fingerprint))
            {
                // Without a fingerprint there is nothing to compare against later, so recording the
                // folder would only produce a target that can never be shown to match.
                warnings.Add(new AdoptionWarning(AdoptionProblem.Unreadable, relativePath));
                continue;
            }

            components.Add(new ComponentRecord(
                ComponentIdOf(packageId, component),
                component.Domain,
                folder,
                new[]
                {
                    new TargetRecord(relativePath, TargetKind.Folder, component.Fingerprint, DateTime.Now)
                }));

            foreach (var file in component.BinFiles ?? Array.Empty<AdoptedFile>())
            {
                var binPath = BinRelativePathOf(file.RelativePath);
                if (binPath.Length == 0) continue;

                var binOwner = index?.OwnerOf(binPath);
                if (binOwner is not null && !string.Equals(binOwner.ComponentId,
                        ComponentIdOf(packageId, TargetDomain.Bin, ""), StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(new AdoptionWarning(AdoptionProblem.AlreadyOwned, binPath,
                        OwnerNameOf(index, binOwner)));
                    continue;
                }

                binFiles.Add(file with { RelativePath = binPath });
            }
        }

        if (binFiles.Count > 0)
        {
            components.Add(new ComponentRecord(
                ComponentIdOf(packageId, TargetDomain.Bin, ""),
                TargetDomain.Bin,
                "",
                binFiles.Select(f => new TargetRecord(f.RelativePath, TargetKind.File, f.Hash, DateTime.Now))
                    .ToList()));
        }

        return new AdoptionPlan(packageId, displayName ?? "", components, warnings);
    }

    /// <summary>"mods/modA", "dlc/dlcA" or "bin/config/a.xml" — the shape the index records.</summary>
    public static string RelativePathOf(TargetDomain domain, string folder) =>
        domain switch
        {
            TargetDomain.Dlc => "dlc/" + folder,
            TargetDomain.Bin => BinRelativePathOf(folder),
            _ => "mods/" + folder
        };

    private static string BinRelativePathOf(string? relativePath) =>
        BinLedger.Normalize("bin/" + (relativePath ?? ""));

    private static string ComponentIdOf(string packageId, AdoptionComponent component) =>
        ComponentIdOf(packageId, component.Domain, component.Folder);

    private static string ComponentIdOf(string packageId, TargetDomain domain, string folder) =>
        ComponentRecord.MakeId(packageId, domain, folder);

    private static string? OwnerNameOf(LibraryIndex? index, ComponentRecord owner) =>
        index?.Packages
            .FirstOrDefault(p => (p.Components ?? Array.Empty<ComponentRecord>())
                .Any(c => string.Equals(c.ComponentId, owner.ComponentId, StringComparison.OrdinalIgnoreCase)))
            ?.DisplayName;
}
