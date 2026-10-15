using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Witcher3ModManager.Tests;

/// <summary>
/// Guards against user-facing dialog text that is hardcoded instead of translated. The delete
/// confirmation once read "Delete [modX]? This cannot be undone." with an English "Delete"
/// button while the rest of the window was Chinese, because those literals never went through
/// TranslationService. The key-existence guard cannot see this: a literal is not a key.
/// </summary>
public class LocalizedUiTextTests
{
    private static readonly Regex DialogCall =
        new(@"(MessageBox|ConfirmDialog|InputDialog)\.Show\(", RegexOptions.Compiled);
    private static readonly Regex Literal =
        new("\"([^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);
    private static readonly Regex EscapeOrGlyph =
        new(@"\\u[0-9A-Fa-f]{4}|\\.", RegexOptions.Compiled);
    private static readonly Regex Wordish =
        new(@"[A-Za-z]{4,}", RegexOptions.Compiled);

    [Fact]
    public void Dialog_text_is_never_a_hardcoded_literal()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles(FindAppProjectRoot()))
        {
            var lines = File.ReadAllLines(file);
            var insideDialogCall = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (DialogCall.IsMatch(line)) insideDialogCall = true;
                if (!insideDialogCall) continue;

                foreach (Match literal in Literal.Matches(line))
                {
                    if (IsTranslationKeyArgument(line, literal.Index)) continue;

                    // Icon glyphs such as "\uE74D" carry no words of their own.
                    var words = EscapeOrGlyph.Replace(literal.Value, "");
                    if (Wordish.IsMatch(words))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1} {literal.Value}");
                }

                if (Regex.IsMatch(line, @"\);\s*$")) insideDialogCall = false;
            }
        }

        Assert.Empty(offenders);
    }

    private static bool IsTranslationKeyArgument(string line, int quoteIndex)
    {
        var start = Math.Max(0, quoteIndex - 3);
        return line.Substring(start, quoteIndex - start).EndsWith("T(", StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    private static string FindAppProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Witcher3ModManager", "AppDataPath.cs");
            if (File.Exists(candidate)) return Path.Combine(dir.FullName, "Witcher3ModManager");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("App project root not found. Run tests from the repo checkout.");
    }
}
