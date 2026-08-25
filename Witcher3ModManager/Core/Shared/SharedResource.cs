using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Shared;

/// <summary>
/// One folder claimed by one component (mod/dlc) in one target domain. A component that
/// owns both a mod folder and a dlc folder contributes two separate claims.
/// </summary>
public sealed record FolderClaim(string ComponentId, TargetDomain Domain, string Folder);

/// <summary>
/// Read-only reference count for a single (domain, folder). It is the skeleton for shared
/// resource ownership: Enable/Disable/Delete can decide purely from RefCount instead of
/// rescanning the live mod set or special-casing a mod name. This is descriptive only and
/// never touches the file system or user data.
/// </summary>
public sealed record FolderReference(
    string Folder,
    TargetDomain Domain,
    int RefCount,
    IReadOnlyList<string> Owners)
{
    public bool IsShared => RefCount > 1;

    public bool IsOwnedBy(string componentId) =>
        Owners.Any(o => string.Equals(o, componentId, StringComparison.OrdinalIgnoreCase));
}