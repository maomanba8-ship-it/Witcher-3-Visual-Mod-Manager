namespace Witcher3ModManager.Core.Analysis.Script;

/// <summary>What a script symbol does to the game's class.</summary>
public enum ScriptSymbolKind
{
    /// <summary>A method of a class the script redefines (compiled blobs only).</summary>
    ClassFunction,

    /// <summary>A global function.</summary>
    GlobalFunction,

    /// <summary>Replaces the whole function.</summary>
    Replace,

    /// <summary>Adds a method that did not exist.</summary>
    Add,

    /// <summary>Wraps an existing method.</summary>
    Wrap,

    /// <summary>Adds a field to a class.</summary>
    AddProperty
}

/// <summary>Where the symbol was found.</summary>
public enum ScriptSymbolOrigin
{
    /// <summary>Inside <c>content\precompiled.rsblob</c>.</summary>
    Rsblob,

    /// <summary>In a loose <c>.ws</c> file.</summary>
    LooseScript
}

/// <summary>
/// One symbol a mod touches. Two mods conflict when their symbols have the same class and method,
/// regardless of which mechanism (compiled blob or loose script) produced them.
/// </summary>
public sealed record ScriptSymbol(
    string ClassName,
    string MethodName,
    ScriptSymbolKind Kind,
    ScriptSymbolOrigin Origin,
    uint Flags = 0);
