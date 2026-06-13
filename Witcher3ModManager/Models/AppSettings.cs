namespace Witcher3ModManager.Models;

public class AppSettings
{
    public string BackgroundImagePath { get; set; } = "";
    public string Witcher3GamePath { get; set; } = "";
    public string SevenZipPath { get; set; } = @"D:\7-Zip\7z.exe";
    public string Language { get; set; } = "zh-CN";
    public bool AutoBackup { get; set; } = true;
    public string ModStoragePath { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public string LaunchArgs { get; set; } = "";
    public string ScriptMergerPath { get; set; } = "";
    public string NexusModsUrl { get; set; } = "https://www.nexusmods.com/witcher3";
    public string LastUsedCategory { get; set; } = "All";
}
