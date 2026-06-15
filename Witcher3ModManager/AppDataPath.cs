namespace Witcher3ModManager;

public static class AppDataPath
{
#if DEBUG
    public const string FolderName = "Witcher3ModManager_Test";
#else
    public const string FolderName = "Witcher3ModManager";
#endif
}
