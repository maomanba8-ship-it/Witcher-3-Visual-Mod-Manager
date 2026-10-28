using System;
using System.IO;
using Witcher3ModManager.Core.Analysis.Script;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ScriptDependencyInspectorTests
{
    [Fact]
    public void A_referenced_class_makes_the_other_mod_a_dependent()
    {
        var core = NewDir();
        var provider = NewDir();
        try
        {
            Write(core, "flyingMountsCore.ws", "class FMCController extends IScriptable\n{\n}\n");
            Write(provider, "fmcRoyalWyvernProvider.ws",
                "@wrapMethod(FMCController)\nfunction FMC_Test()\n{\n    var c : FMCController;\n}\n");

            var dependency = Assert.Single(
                ScriptDependencyInspector.FindDependents(core, new[] { ("modFlyingMountsRoyalWyvern", provider) }));

            Assert.Equal("modFlyingMountsRoyalWyvern", dependency.DependentModName);
            Assert.Equal("FMCController", dependency.ClassName);
        }
        finally
        {
            Directory.Delete(core, true);
            Directory.Delete(provider, true);
        }
    }

    [Fact]
    public void Commented_references_and_similar_names_do_not_count()
    {
        var core = NewDir();
        var provider = NewDir();
        try
        {
            Write(core, "core.ws", "class FMCController\n{\n}\n");
            Write(provider, "provider.ws",
                "// FMCController is used elsewhere\n@wrapMethod(FMCController2)\nfunction Test()\n{\n}\n");

            Assert.Empty(ScriptDependencyInspector.FindDependents(core, new[] { ("modA", provider) }));
        }
        finally
        {
            Directory.Delete(core, true);
            Directory.Delete(provider, true);
        }
    }

    [Fact]
    public void A_dependent_is_reported_once_per_class()
    {
        var core = NewDir();
        var provider = NewDir();
        try
        {
            Write(core, "core.ws", "class FMCController\n{\n}\n");
            Write(provider, "a.ws", "var c : FMCController;\n");
            Write(provider, "b.ws", "@wrapMethod(FMCController)\nfunction Test()\n{\n}\n");

            Assert.Single(ScriptDependencyInspector.FindDependents(core, new[] { ("modA", provider) }));
        }
        finally
        {
            Directory.Delete(core, true);
            Directory.Delete(provider, true);
        }
    }

    [Fact]
    public void Classes_in_comments_are_not_definitions()
    {
        var root = NewDir();
        try
        {
            Write(root, "a.ws", "// class Fake\n/* class AlsoFake */\nclass Real\n{\n}\n");

            Assert.Equal(new[] { "Real" }, ScriptDependencyInspector.ClassesDefinedIn(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w3mm-dep-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string root, string name, string text)
    {
        var dir = Path.Combine(root, "content", "scripts", "local");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), text);
    }
}
