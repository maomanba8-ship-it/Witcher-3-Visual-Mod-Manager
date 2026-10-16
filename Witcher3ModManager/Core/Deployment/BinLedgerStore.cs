using System.Text.Json;
using Witcher3ModManager.Core.Storage;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Keeps the bin ledger in one file under the data directory.
///
/// Losing this file does not break the game directory: it only means bin files can no longer be
/// put back, because what was there before is no longer known. That is why a damaged file is
/// preserved rather than silently replaced with an empty ledger — an empty one would be written
/// over the real thing on the next save, and every restore would then answer "no record".
/// </summary>
public sealed class BinLedgerStore
{
    private readonly string _path;

    public BinLedgerStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public string Path => _path;

    public TextFileOutcome LastLoadOutcome { get; private set; } = TextFileOutcome.Missing;

    /// <summary>Where a damaged file was kept, when one was.</summary>
    public string? DamagedCopyPath { get; private set; }

    /// <summary>Set when the last save failed; the caller decides how to report it.</summary>
    public string? LastSaveError { get; private set; }

    public BinLedger Load()
    {
        var load = TextFileRecovery.Load(_path, LooksLikeALedger);
        LastLoadOutcome = load.Outcome;
        DamagedCopyPath = load.Detail;

        if (load.Text is null) return BinLedger.Empty;

        try
        {
            var ledger = JsonSerializer.Deserialize<BinLedger>(load.Text);
            return ledger is null || ledger.Version > BinLedger.CurrentVersion
                ? BinLedger.Empty
                : ledger;
        }
        catch
        {
            return BinLedger.Empty;
        }
    }

    public bool Save(BinLedger? ledger)
    {
        try
        {
            var json = JsonSerializer.Serialize(ledger ?? BinLedger.Empty,
                new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteAllText(_path, json);
            LastSaveError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastSaveError = ex.Message;
            return false;
        }
    }

    private static bool LooksLikeALedger(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BinLedger>(json) is { Version: > 0 };
        }
        catch
        {
            return false;
        }
    }
}
