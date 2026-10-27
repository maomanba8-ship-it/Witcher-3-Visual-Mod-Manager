using System;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Analysis.Script;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ScriptSymbolConflictAnalyzerTests
{
    [Fact]
    public void A_blob_and_a_loose_script_over_the_same_method_conflict()
    {
        var evidence = ScriptSymbolConflictAnalyzer.Analyze(new[]
        {
            new ComponentSymbols("a", new[]
            {
                new ScriptSymbol(
                    "W3DamageManagerProcessor", "ProcessActionReaction",
                    ScriptSymbolKind.ClassFunction, ScriptSymbolOrigin.Rsblob)
            }),
            new ComponentSymbols("b", new[]
            {
                new ScriptSymbol(
                    "W3DamageManagerProcessor", "ProcessActionReaction",
                    ScriptSymbolKind.Wrap, ScriptSymbolOrigin.LooseScript)
            })
        });

        var conflict = Assert.Single(evidence);
        Assert.Equal(ConflictKind.ScriptOverride, conflict.Kind);
        Assert.Equal("W3DamageManagerProcessor::ProcessActionReaction", conflict.NormalizedRelativePath);
        Assert.Equal(new[] { "a", "b" }, conflict.ComponentIdentities);
        Assert.Equal(ScriptSymbolConflictAnalyzer.SuggestAction, conflict.SuggestAction);
    }

    [Fact]
    public void Disjoint_symbols_are_not_a_conflict()
    {
        var evidence = ScriptSymbolConflictAnalyzer.Analyze(new[]
        {
            new ComponentSymbols("a", new[]
            {
                new ScriptSymbol("CR4Player", "OnSpawned", ScriptSymbolKind.Wrap, ScriptSymbolOrigin.LooseScript)
            }),
            new ComponentSymbols("b", new[]
            {
                new ScriptSymbol("CR4Player", "OnOpenMap", ScriptSymbolKind.Replace, ScriptSymbolOrigin.Rsblob)
            })
        });

        Assert.Empty(evidence);
    }

    [Fact]
    public void The_same_component_twice_is_not_a_conflict()
    {
        var symbols = new[]
        {
            new ScriptSymbol("CR4Player", "OnSpawned", ScriptSymbolKind.Wrap, ScriptSymbolOrigin.LooseScript)
        };

        var evidence = ScriptSymbolConflictAnalyzer.Analyze(new[]
        {
            new ComponentSymbols("a", symbols),
            new ComponentSymbols("a", symbols)
        });

        Assert.Empty(evidence);
    }

    [Fact]
    public void Class_and_method_names_are_compared_case_insensitively()
    {
        var evidence = ScriptSymbolConflictAnalyzer.Analyze(new[]
        {
            new ComponentSymbols("a", new[]
            {
                new ScriptSymbol("CR4Player", "OnSpawned", ScriptSymbolKind.Wrap, ScriptSymbolOrigin.LooseScript)
            }),
            new ComponentSymbols("b", new[]
            {
                new ScriptSymbol("cr4player", "onspawned", ScriptSymbolKind.ClassFunction, ScriptSymbolOrigin.Rsblob)
            })
        });

        Assert.Single(evidence);
    }

    [Fact]
    public void Identical_script_content_is_not_a_conflict()
    {
        var symbols = new[]
        {
            new ScriptSymbol("CR4Player", "OnSpawned", ScriptSymbolKind.ClassFunction, ScriptSymbolOrigin.Rsblob)
        };

        var evidence = ScriptSymbolConflictAnalyzer.Analyze(new[]
        {
            new ComponentSymbols("a", symbols, Fingerprint: "same"),
            new ComponentSymbols("b", symbols, Fingerprint: "same")
        });

        Assert.Empty(evidence);
    }

    [Fact]
    public void Wraps_alone_are_not_a_conflict()
    {
        // The engine chains every @wrapMethod in load order, so three mods wrapping the same
        // method is normal. Reporting it was the false positive that flooded the conflict list.
        var wrap = new[]
        {
            new ScriptSymbol("CR4Player", "OnSpawned", ScriptSymbolKind.Wrap, ScriptSymbolOrigin.LooseScript)
        };

        var evidence = ScriptSymbolConflictAnalyzer.Analyze(new[]
        {
            new ComponentSymbols("a", wrap),
            new ComponentSymbols("b", wrap),
            new ComponentSymbols("c", wrap)
        });

        Assert.Empty(evidence);
    }

    [Fact]
    public void Unknown_fingerprints_keep_the_conflict()
    {
        var symbols = new[]
        {
            new ScriptSymbol("CR4Player", "OnSpawned", ScriptSymbolKind.ClassFunction, ScriptSymbolOrigin.Rsblob)
        };

        var evidence = ScriptSymbolConflictAnalyzer.Analyze(new[]
        {
            new ComponentSymbols("a", symbols),
            new ComponentSymbols("b", symbols)
        });

        Assert.Single(evidence);
    }
}
