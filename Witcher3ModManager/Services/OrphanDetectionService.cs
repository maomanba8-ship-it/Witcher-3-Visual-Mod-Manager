using System.IO;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class OrphanDetectionService
{
    private static readonly HashSet<string> OfficialDlcs = new(StringComparer.OrdinalIgnoreCase)
    {
        "dlc1","dlc2","dlc3","dlc4","dlc5","dlc6","dlc7","dlc8","dlc9","dlc10",
        "dlc11","dlc12","dlc13","dlc14","dlc15","dlc16","dlc17","dlc18","dlc19","dlc20",
        "bob","ep1"
    };

    public List<OrphanModInfo> ScanOrphans(string gamePath, IReadOnlyList<ModEntry> knownMods)
    {
        var orphans = new List<OrphanModInfo>();
        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            return orphans;

        var knownModFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownDlcFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in knownMods)
        {
            foreach (var f in mod.GameModFolders)
                if (!string.IsNullOrEmpty(f)) knownModFolders.Add(f);
            foreach (var f in mod.GameDlcFolders)
                if (!string.IsNullOrEmpty(f)) knownDlcFolders.Add(f);
        }

        // Scan mods/
        var modsRoot = Path.Combine(gamePath, "mods");
        if (Directory.Exists(modsRoot))
        {
            foreach (var dir in Directory.GetDirectories(modsRoot))
            {
                var name = Path.GetFileName(dir);
                var cleanName = name.StartsWith("~") ? name[1..] : name;
                if (knownModFolders.Contains(cleanName)) continue;
                if (!IsModFolder(dir)) continue;
                orphans.Add(BuildOrphanInfo(dir, name, false));
            }
        }

        // Scan dlc/
        var dlcRoot = Path.Combine(gamePath, "dlc");
        if (Directory.Exists(dlcRoot))
        {
            foreach (var dir in Directory.GetDirectories(dlcRoot))
            {
                var name = Path.GetFileName(dir);
                if (OfficialDlcs.Contains(name)) continue;
                var cleanName = name.StartsWith("~") ? name[1..] : name;
                if (knownDlcFolders.Contains(cleanName)) continue;
                if (!IsModFolder(dir)) continue;
                orphans.Add(BuildOrphanInfo(dir, name, true));
            }
        }

        // Pair modXXX + dlcXXX into single orphan entries
        PairModDlcOrphans(orphans);

        return orphans;
    }

    private static bool IsModFolder(string path)
        => Core.Deployment.GameFolderShape.IsModFolder(path);

    private static OrphanModInfo BuildOrphanInfo(string fullPath, string folderName, bool isInDlc)
    {
        long size = 0;
        int count = 0;
        try
        {
            var files = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories);
            count = files.Length;
            size = files.Sum(f => new FileInfo(f).Length);
        }
        catch { }

        return new OrphanModInfo
        {
            FolderName = folderName,
            FullPath = fullPath,
            IsEnabled = !folderName.StartsWith("~"),
            IsInDlcFolder = isInDlc,
            SizeBytes = size,
            FileCount = count
        };
    }

    private static string GetBaseName(string folderName)
    {
        var name = folderName.StartsWith("~") ? folderName[1..] : folderName;
        if (name.StartsWith("mod", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            return name[3..];
        if (name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            return name[3..];
        return name;
    }

    private static void PairModDlcOrphans(List<OrphanModInfo> orphans)
    {
        var modOrphans = orphans.Where(o => !o.IsInDlcFolder).ToList();
        var dlcOrphans = orphans.Where(o => o.IsInDlcFolder).ToList();

        foreach (var modOrphan in modOrphans)
        {
            var modBase = GetBaseName(modOrphan.FolderName);
            var match = dlcOrphans.FirstOrDefault(d =>
                string.Equals(GetBaseName(d.FolderName), modBase, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                modOrphan.PairedOrphan = match;
                orphans.Remove(match);
                dlcOrphans.Remove(match);
            }
        }
    }
}
