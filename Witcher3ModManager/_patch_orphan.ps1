$file = "Z:\GGGGGGGGGeam\MOD\借鉴MOD开发\Witcher3ModManager\Witcher3ModManager\Views\ModListPage.xaml.cs"
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

# Chinese chars
$ch_find = [char]0x53D1 + [char]0x73B0
$ch_find2 = [char]0x53D1 + [char]0x73B0
$ch_ge = [char]0x4E2A
$ch_wei = [char]0x672A + [char]0x88AB + [char]0x7BA1 + [char]0x7406 + [char]0x7684
$ch_quan = [char]0x5168 + [char]0x90E8 + [char]0x5BFC + [char]0x5165
$ch_cha = [char]0x67E5 + [char]0x770B + [char]0x8BE6 + [char]0x60C5
$ch_hu = [char]0x5FFD + [char]0x7565
$ch_cg = [char]0x6210 + [char]0x529F + [char]0x5BFC + [char]0x5165
$ch_guer = [char]0x5B64 + [char]0x513F
$ch_guer_dr = [char]0x5B64 + [char]0x513F + " Mod " + [char]0x5BFC + [char]0x5165
$ch_yixia = [char]0x4EE5 + [char]0x4E0B + " Mod " + [char]0x5DF2 + [char]0x5B89 + [char]0x88C5 + [char]0x5728 + [char]0x6E38 + [char]0x620F + [char]0x76EE + [char]0x5F55 + [char]0x4E2D + [char]0xFF0C + [char]0x4F46 + [char]0x5C1A + [char]0x672A + [char]0x88AB + [char]0x7BA1 + [char]0x7406 + [char]0x5668 + [char]0x7BA1 + [char]0x7406 + [char]0xFF1A
$ch_wei_xiang = [char]0x672A + [char]0x7BA1 + [char]0x7406 + " Mod " + [char]0x8BE6 + [char]0x60C5
$ch_yiqi = [char]0x5DF2 + [char]0x542F + [char]0x7528
$ch_ting = [char]0x5DF2 + [char]0x505C + [char]0x7528

# 1. Add OrphanModInfo using
$content = $content -replace '(using Witcher3ModManager\.Services;)', "using Witcher3ModManager.Models;`n`$1"

# 2. Add _orphans field after StatusUpdated event
$content = $content -replace '(public event Action<int, int, int, int>\? StatusUpdated;)', "`$1`n    private List<OrphanModInfo> _orphans = new();"

# 3. Add SetOrphans method after UpdateStatusBar()
$setOrphans = @"
`n
    public void SetOrphans(List<OrphanModInfo> orphans)
    {
        _orphans = orphans ?? new();
        if (_orphans.Count == 0)
        {
            OrphanBanner.Visibility = Visibility.Collapsed;
            return;
        }
        OrphanBannerText.Text = $ch_find + $" {_orphans.Count} $ch_ge$ch_wei Mod";
        OrphanBanner.Visibility = Visibility.Visible;
    }
"@
$content = $content -replace '(UpdateStatusBar\(\);\s*\})', "`$1$setOrphans"

# 4. Add orphan handlers before the last closing brace
$handlers = @"

    #region Orphan Handlers
    private void OrphanImportAll_Click(object sender, RoutedEventArgs e)
    {
        if (_storage == null || _orphans.Count == 0) return;
        var imported = 0;
        foreach (var orphan in _orphans)
        {
            try
            {
                _storage.ImportOrphanMod(orphan);
                imported++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Orphan import failed: {orphan.FolderName} - {ex.Message}");
            }
        }
        _orphans.Clear();
        OrphanBanner.Visibility = Visibility.Collapsed;
        RefreshModList();
        MessageBox.Show($ch_cg + $" {imported} $ch_ge Mod。", $ch_guer_dr, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OrphanViewDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_orphans.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($ch_yixia);
        sb.AppendLine();
        foreach (var o in _orphans)
        {
            var status = o.IsEnabled ? $ch_yiqi : $ch_ting;
            sb.AppendLine($"  {o.FolderName}  [{status}]  {o.DisplaySize}  {o.DisplayLocation}");
        }
        MessageBox.Show(sb.ToString(), $ch_wei_xiang + $" ({_orphans.Count})", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OrphanIgnore_Click(object sender, RoutedEventArgs e)
    {
        _orphans.Clear();
        OrphanBanner.Visibility = Visibility.Collapsed;
    }
    #endregion
"@

$content = $content -replace "(\r?\n\}\s*)$", "$handlers`n}"

[System.IO.File]::WriteAllText($file, $content, [System.Text.UTF8Encoding]::new($false))
Write-Output "CS updated"
