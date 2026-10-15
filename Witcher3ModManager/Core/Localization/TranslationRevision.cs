using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Witcher3ModManager.Core.Localization;

/// <summary>
/// Fingerprint of a built-in translation table. The app caches each language as a JSON file the
/// user may edit, and that cache used to win forever: once written, later wording fixes and the
/// version string never reached the UI again. Pairing the cache with this fingerprint lets the
/// app tell "the built-ins still match the cache" from "the built-ins moved on".
///
/// The digest is order independent, so reordering the table is not treated as a change, and it
/// ignores nothing else, so any real edit is.
/// </summary>
public static class TranslationRevision
{
    public static string Compute(IEnumerable<KeyValuePair<string, string>>? strings)
    {
        if (strings is null) return "";

        var lines = strings
            .Select(kv => kv.Key + "\u0000" + (kv.Value ?? ""))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        if (lines.Count == 0) return "";

        var payload = string.Join("\n", lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
