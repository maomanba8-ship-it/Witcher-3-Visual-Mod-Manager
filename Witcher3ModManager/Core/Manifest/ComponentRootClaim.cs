using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Manifest;

/// <summary>
/// One component identified by an absolute storage root rather than by a folder name under the
/// mods root. The bin domain needs this: a record stores its own bin storage directory with the
/// files already laid out relative to the game's bin folder, so there is no single mods root to
/// resolve a folder name against. It is a plain input record and never touches the file system.
/// </summary>
public sealed record ComponentRootClaim(
    string ComponentId,
    TargetDomain Domain,
    string RootPath);
