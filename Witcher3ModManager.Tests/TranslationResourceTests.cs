using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Witcher3ModManager.Tests;

/// <summary>
/// Guards the translation tables against the three defects that made the UI show the wrong
/// language: a key defined twice inside one table (the later line silently wins), a key missing
/// from one of the two languages, and a key the code asks for that exists in neither.
/// </summary>
public class TranslationResourceTests
{
    private static readonly Regex EntryPattern = new(@"^\s*\[""([A-Za-z0-9_.]+)""\]\s*=", RegexOptions.Compiled);
    private static readonly Regex UsagePattern = new(@"\.T\(""([A-Za-z0-9_.]+)""\)", RegexOptions.Compiled);

    [Fact]
    public void No_key_is_defined_twice_inside_one_language_table()
    {
        var (zh, en) = ReadTranslationTables();

        var duplicated = zh.Duplicates.Concat(en.Duplicates).Distinct().OrderBy(k => k).ToList();

        Assert.Empty(duplicated);
    }

    [Fact]
    public void Both_languages_define_exactly_the_same_keys()
    {
        var (zh, en) = ReadTranslationTables();

        var onlyInChinese = zh.Keys.Except(en.Keys).OrderBy(k => k).ToList();
        var onlyInEnglish = en.Keys.Except(zh.Keys).OrderBy(k => k).ToList();

        Assert.Empty(onlyInChinese);
        Assert.Empty(onlyInEnglish);
    }

    [Fact]
    public void Every_key_the_code_asks_for_exists_in_both_languages()
    {
        // A missing key makes T() return the raw key name, which is how "category.other" once
        // showed up verbatim in the English UI.
        var (zh, en) = ReadTranslationTables();
        var appRoot = FindAppProjectRoot();

        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles(appRoot))
            foreach (Match m in UsagePattern.Matches(File.ReadAllText(file)))
                used.Add(m.Groups[1].Value);

        Assert.NotEmpty(used);
        Assert.Empty(used.Where(k => !zh.Keys.Contains(k)).ToList());
        Assert.Empty(used.Where(k => !en.Keys.Contains(k)).ToList());
    }

    private static (Table zh, Table en) ReadTranslationTables()
    {
        var source = File.ReadAllLines(Path.Combine(FindAppProjectRoot(), "Services", "TranslationService.cs"));
        var zhStart = FindBlockStart(source, "\"zh-CN\" => new Dictionary");
        var enStart = FindBlockStart(source, "\"en\" => new Dictionary");

        return (ReadBlock(source, zhStart, enStart - 1), ReadBlock(source, enStart, source.Length));
    }

    private static int FindBlockStart(string[] lines, string marker)
    {
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].Contains(marker, StringComparison.Ordinal))
                return i + 1;
        throw new InvalidOperationException($"Translation table not found: {marker}");
    }

    private static Table ReadBlock(string[] lines, int firstLine, int lastLine)
    {
        var table = new Table();
        for (var i = firstLine - 1; i < lastLine && i < lines.Length; i++)
        {
            var match = EntryPattern.Match(lines[i]);
            if (!match.Success) continue;
            table.Add(match.Groups[1].Value);
        }
        return table;
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        Path.GetFileName(f) != "TranslationService.cs");

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

    private sealed class Table
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        public List<string> Duplicates { get; } = new();
        public IReadOnlyCollection<string> Keys => _keys;

        public void Add(string key)
        {
            if (!_keys.Add(key)) Duplicates.Add(key);
        }
    }
}
