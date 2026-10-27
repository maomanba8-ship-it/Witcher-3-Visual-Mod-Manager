using System.Linq;
using Witcher3ModManager.Core.Analysis.Script;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ScriptAnnotationReaderTests
{
    [Fact]
    public void Reads_wrap_annotation_with_target_class()
    {
        const string source = """
            @wrapMethod( W3DamageManagerProcessor ) function ProcessActionReaction( wasFrozen : bool, wasAlive : bool )
            {
                wrappedMethod( wasFrozen, wasAlive );
            }
            """;

        var symbol = Assert.Single(ScriptAnnotationReader.Parse(source));

        Assert.Equal("W3DamageManagerProcessor", symbol.ClassName);
        Assert.Equal("ProcessActionReaction", symbol.MethodName);
        Assert.Equal(ScriptSymbolKind.Wrap, symbol.Kind);
        Assert.Equal(ScriptSymbolOrigin.LooseScript, symbol.Origin);
    }

    [Fact]
    public void Reads_add_replace_and_field_forms()
    {
        const string source = """
            @addMethod( CR4Player ) function CSR_RememberSettings()
            @replaceMethod( W3DamageManagerProcessor ) function ProcessAction( action : W3DamageAction )
            @addField( CR4Player ) var csrMenuHaze : bool;
            """;

        var symbols = ScriptAnnotationReader.Parse(source);

        Assert.Equal(3, symbols.Count);
        Assert.Contains(symbols, s => s.Kind == ScriptSymbolKind.Add && s.MethodName == "CSR_RememberSettings");
        Assert.Contains(symbols, s => s.Kind == ScriptSymbolKind.Replace && s.MethodName == "ProcessAction");
        Assert.Contains(symbols, s => s.Kind == ScriptSymbolKind.AddProperty && s.MethodName == "csrMenuHaze");
    }

    [Fact]
    public void Annotation_may_sit_on_the_line_before_the_function()
    {
        const string source = """
            @wrapMethod( W3QuenEntityStateShieldActive )
            function OnEnterState( prevStateName : name )
            {
            }
            """;

        var symbol = Assert.Single(ScriptAnnotationReader.Parse(source));

        Assert.Equal("W3QuenEntityStateShieldActive", symbol.ClassName);
        Assert.Equal("OnEnterState", symbol.MethodName);
    }

    [Fact]
    public void Commented_out_annotations_are_ignored()
    {
        const string source = """
            // @wrapMethod( W3DamageManagerProcessor ) function ProcessAction()
            /*
               @addMethod( CR4Player ) function CSR_Sweep()
            */
            """;

        Assert.Empty(ScriptAnnotationReader.Parse(source));
    }

    [Fact]
    public void Repeating_the_same_annotation_yields_one_symbol()
    {
        const string source = """
            @wrapMethod( CR4Player ) function OnSpawned( spawnData : SEntitySpawnData )
            @wrapMethod( CR4Player ) function OnSpawned( spawnData : SEntitySpawnData )
            """;

        var symbol = Assert.Single(ScriptAnnotationReader.Parse(source));

        Assert.Equal("OnSpawned", symbol.MethodName);
    }
}
