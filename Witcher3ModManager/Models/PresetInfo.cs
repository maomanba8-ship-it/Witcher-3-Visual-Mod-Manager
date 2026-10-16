namespace Witcher3ModManager.Models;

public class PresetInfo
{
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Which shape <see cref="ModStates"/> uses. 0 (absent in an older file) means the keys are
    /// component folders; 1 means they are package ids, which is what a preset should describe:
    /// a folder can be renamed, a record keeps its identity.
    /// </summary>
    public int Version { get; set; }

    /// <summary>package id (version 1) or component folder (version 0) -> isEnabled mapping</summary>
    public Dictionary<string, bool> ModStates { get; set; } = new();
}
