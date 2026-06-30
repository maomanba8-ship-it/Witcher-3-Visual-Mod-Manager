using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Witcher3ModManager.Models;
using Witcher3ModManager.Services;

namespace Witcher3ModManager.Views;

public partial class PotentialOverlapDialog : Window
{
    public PotentialOverlapDialog(Dictionary<CategoryType, List<string>> overlaps)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        TitleText.Text = TranslationService.Instance.T("potential.title");
        CloseBtn.Content = TranslationService.Instance.T("conflict.close");

        var total = overlaps.Values.Sum(v => v.Count * (v.Count - 1) / 2);
        SummaryText.Text = string.Format(TranslationService.Instance.T("overlap.summary"), total, overlaps.Values.SelectMany(v => v).Distinct().Count());

        foreach (var kvp in overlaps)
        {
            var catDisplay = kvp.Key switch
            {
                CategoryType.Geralt => TranslationService.Instance.T("category.geralt"),
                CategoryType.Yennefer => TranslationService.Instance.T("category.yennefer"),
                CategoryType.Triss => TranslationService.Instance.T("category.triss"),
                CategoryType.Graphics => TranslationService.Instance.T("category.graphics"),
                CategoryType.Characters => TranslationService.Instance.T("category.characters"),
                CategoryType.ArmorWeapons => TranslationService.Instance.T("category.armor_weapons"),
                CategoryType.Gameplay => TranslationService.Instance.T("category.gameplay"),
                CategoryType.Quests => TranslationService.Instance.T("category.quests"),
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
                Text = string.Format(TranslationService.Instance.T("overlap.cat_count"), catDisplay, kvp.Value.Count),
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