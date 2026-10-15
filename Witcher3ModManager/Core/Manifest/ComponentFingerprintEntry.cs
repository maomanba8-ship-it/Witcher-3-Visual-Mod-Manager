using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Manifest;

/// <summary>
/// One component fingerprint as recorded at import time. Folder is the final storage folder
/// name, i.e. the same name the record exposes through GameModFolders / GameDlcFolders, so
/// the owners of a shared folder can be compared by (Domain, Folder). This is descriptive
/// only: it never drives deployment decisions. An empty or missing fingerprint means the
/// history was never recorded and must be treated as unknown, not as a match.
/// </summary>
public sealed record ComponentFingerprintEntry(
    TargetDomain Domain,
    string Folder,
    string Fingerprint,
    int FileCount);
