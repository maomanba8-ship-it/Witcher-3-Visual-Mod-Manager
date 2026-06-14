using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Views;

public partial class PotentialOverlapDialog : Window
{
    public PotentialOverlapDialog(Dictionary<CategoryType, List<string>> overlaps)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        var total = overlaps.Values.Sum(v => v.Count * (v.Count - 1) / 2);
        SummaryText.Text = $"检测到 {total} 组潜在覆盖，" +
                          $"涉及 {overlaps.Values.SelectMany(v => v).Distinct().Count()} 个模组。\n" +
                          "同分类模组可能修改同一游戏内容，游戏将根据优先级(Priority)决定最终效果。";

        foreach (var kvp in overlaps)
        {
            var catDisplay = kvp.Key switch
            {
                CategoryType.Geralt => "杰洛特",
                CategoryType.Yennefer => "叶奈法",
                CategoryType.Triss => "特莉丝",
                CategoryType.Graphics => "图形",
                CategoryType.Characters => "角色",
                CategoryType.ArmorWeapons => "护甲武器",
                CategoryType.Gameplay => "玩法",
                CategoryType.Quests => "任务",
                CategoryType.UI => "UI",
                _ => kvp.Key.ToString()
            };

            var card = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#1C1F2A")),
                CornerRadius = new System.Windows.CornerRadius(4),
                Padding = new System.Windows.Thickness(12, 10, 12, 10),
                Margin = new System.Windows.Thickness(0, 0, 0, 6),
                BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#2A2E3C")),
                BorderThickness = new System.Windows.Thickness(1)
            };

            var stack = new System.Windows.Controls.StackPanel();
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"\uE74B {catDisplay}（{kvp.Value.Count} 个模组）",
                FontSize = 12,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#C9A96E"))
            });
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = string.Join(" \u2194 ", kvp.Value),
                FontSize = 11,
                Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#9A9EAA")),
                Margin = new System.Windows.Thickness(0, 4, 0, 0)
            });

            card.Child = stack;
            OverlapStackPanel.Children.Add(card);
        }
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}