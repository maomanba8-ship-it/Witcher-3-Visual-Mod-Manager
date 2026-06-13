namespace Witcher3ModManager.Models;

public class PresetInfo
{
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>mod name -> isEnabled mapping</summary>
    public Dictionary<string, bool> ModStates { get; set; } = new();
}
