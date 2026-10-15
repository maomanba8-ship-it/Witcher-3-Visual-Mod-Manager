using System;
using System.Collections.Generic;
using System.Linq;

namespace Witcher3ModManager.Core.Shared;

/// <summary>
/// What the recorded import-time fingerprints say about one shared folder.
/// </summary>
public enum SharedContentVerdict
{
    /// <summary>Not enough recorded history to say anything. Never shown as a problem.</summary>
    Unknown,

    /// <summary>Every owner recorded the same content.</summary>
    Identical,

    /// <summary>Owners recorded different content for the same folder.</summary>
    Differs
}

/// <summary>
/// Compares the fingerprints that the owners of one shared folder recorded when they were
/// imported. Pure: it only reads the values handed to it.
///
/// Only a folder whose every owner recorded a fingerprint can be judged, and the judgement is
/// the whole point of recording them: a shared folder lives on disk as a single copy, so the
/// disk can no longer tell whether the owners originally shipped the same bytes. An owner with
/// no record (imported before fingerprints existed) makes the verdict Unknown rather than a
/// match, because unknown history must never be presented as agreement.
/// </summary>
public static class SharedFingerprintComparison
{
    public static SharedContentVerdict Compare(IEnumerable<string?>? fingerprintsPerOwner)
    {
        if (fingerprintsPerOwner is null) return SharedContentVerdict.Unknown;

        var fingerprints = fingerprintsPerOwner.ToList();
        if (fingerprints.Count < 2) return SharedContentVerdict.Unknown;
        if (fingerprints.Any(string.IsNullOrEmpty)) return SharedContentVerdict.Unknown;

        var distinct = fingerprints
            .Select(f => f!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinct.Count > 1 ? SharedContentVerdict.Differs : SharedContentVerdict.Identical;
    }
}
