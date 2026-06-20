using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Views;

public partial class OrphanDialog : Window
{
    private readonly List<OrphanModInfo> _orphans;
    private readonly List<CheckBox> _checkBoxes = new();
    public List<OrphanModInfo> SelectedOrphans { get; private set; } = new();

    public OrphanDialog(List<OrphanModInfo> orphans)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        _orphans = orphans;
        TitleText.Text = "未管理 Mod (" + orphans.Count + ")";
        BuildOrphanList();
        UpdateSelectionCount();
    }

        private void BuildOrphanList()
    {
        OrphanListPanel.Children.Clear();
        _checkBoxes.Clear();

        foreach (var orphan in _orphans)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#1C1F2A")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox
            {
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Tag = orphan
            };
            cb.Checked += (s, e) => UpdateSelectionCount();
            cb.Unchecked += (s, e) => UpdateSelectionCount();
            _checkBoxes.Add(cb);
            Grid.SetColumn(cb, 0);
            grid.Children.Add(cb);

            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            
            // Display base name (not modXXX or dlcXXX)
            var displayName = orphan.HasPair 
                ? GetBaseName(orphan.FolderName) 
                : orphan.FolderName;
            
            var nameText = new TextBlock
            {
                Text = displayName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#E2E4E9"))
            };
            
            var subText = new TextBlock
            {
                Text = orphan.HasPair
                    ? orphan.DisplayTotalSize + "  |  " + orphan.FolderName + " + " + orphan.PairedOrphan!.FolderName
                    : orphan.DisplaySize + "  |  " + orphan.DisplayLocation,
                FontSize = 11,
                Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#5E626E")),
                Margin = new Thickness(0, 2, 0, 0)
            };
            infoStack.Children.Add(nameText);
            infoStack.Children.Add(subText);
            Grid.SetColumn(infoStack, 1);
            grid.Children.Add(infoStack);

            // Status + type badges
            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (orphan.HasPair)
            {
                var pairBadge = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#201C6B9E")),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 6, 0)
                };
                pairBadge.Child = new TextBlock
                {
                    Text = "Mod + DLC",
                    FontSize = 10,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C6B9E"))
                };
                rightStack.Children.Add(pairBadge);
            }

            var statusBadge = new Border
            {
                Background = orphan.IsEnabled
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#206BAF7B"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20E0556A")),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2)
            };
            statusBadge.Child = new TextBlock
            {
                Text = orphan.IsEnabled ? "\u5df2\u542f\u7528" : "\u5df2\u505c\u7528",
                FontSize = 10,
                Foreground = orphan.IsEnabled
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6BAF7B"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0556A"))
            };
            rightStack.Children.Add(statusBadge);

            Grid.SetColumn(rightStack, 2);
            grid.Children.Add(rightStack);

            card.Child = grid;
            OrphanListPanel.Children.Add(card);
        }
    }

    private static string GetBaseName(string folderName)
    {
        var name = folderName.StartsWith("~") ? folderName[1..] : folderName;
        if (name.StartsWith("mod", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            return name[3..];
        if (name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            return name[3..];
        return name;
    }

    private void UpdateSelectionCount()
    {
        var selected = _checkBoxes.Count(cb => cb.IsChecked == true);
        SelectionCount.Text = "已选 " + selected + " / " + _orphans.Count;
    }

    private void SelectAll_Changed(object sender, RoutedEventArgs e)
    {
        var isChecked = SelectAllCheckBox.IsChecked == true;
        foreach (var cb in _checkBoxes)
            cb.IsChecked = isChecked;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        SelectedOrphans = _checkBoxes
            .Where(cb => cb.IsChecked == true)
            .Select(cb => (OrphanModInfo)cb.Tag)
            .ToList();

        if (SelectedOrphans.Count == 0)
        {
            MessageBox.Show("请至少选择一个 Mod。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}