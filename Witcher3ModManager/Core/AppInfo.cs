namespace Witcher3ModManager.Core;

/// <summary>
/// Single source of truth for the application version. Everything that displays a version
/// reads it from here, so a release changes one line instead of hunting literals across the
/// UI and the localisation tables. Packaging metadata (csproj, installer, release file names)
/// is wired to this in the release-pipeline task, not by hand.
/// </summary>
public static class AppInfo
{
    /// <summary>Bare semantic version, without the leading "v".</summary>
    public const string Version = "0.9.6";

    /// <summary>Version as shown to users.</summary>
    public const string DisplayVersion = "v" + Version;
}
