using System.IO;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Keeps a copy of the library's data files before they are emptied.
///
/// Clearing the library is a decision the user makes on purpose, but it is still a decision made
/// quickly sometimes. Leaving the previous file beside the new empty one costs a few kilobytes and
/// makes the reset reversible by hand, without any automatic restore path that could later fire at
/// the wrong moment.
/// </summary>
public static class LibraryReset
{
    public const string PreservedSuffix = ".cleared-";

    public static (IReadOnlyList<string> Copies, IReadOnlyList<string> Problems) Preserve(
        IEnumerable<string?>? files,
        DateTime at)
    {
        var copies = new List<string>();
        var problems = new List<string>();
        var stamp = at.ToString("yyyyMMdd-HHmmss");

        foreach (var file in files ?? Array.Empty<string?>())
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) continue;

            var target = file + PreservedSuffix + stamp;
            try
            {
                File.Copy(file, target, overwrite: true);
                copies.Add(target);
            }
            catch (Exception ex)
            {
                problems.Add($"{file}: {ex.Message}");
            }
        }

        return (copies, problems);
    }
}
