using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CSVEditor.Views;

/// <summary>
/// 轻量输入对话框：单字段（重命名列）或双字段（跳转到行列）。
/// </summary>
public partial class PromptWindow : Window
{
    private PromptWindow()
    {
        InitializeComponent();
    }

    public string FirstValue => FirstBox.Text.Trim();

    public string SecondValue => SecondBox.Text.Trim();

    /// <summary>单字段输入。</summary>
    public static string? Show(
        Window? owner,
        string title,
        string label,
        string initialValue,
        string confirmText = "确定")
    {
        var dialog = Create(owner, title, label, initialValue, confirmText);
        dialog.SecondRow.Visibility = Visibility.Collapsed;
        return dialog.ShowDialog() == true ? dialog.FirstValue : null;
    }

    /// <summary>双字段输入（例如“行号 / 列号”）。</summary>
    public static (string First, string Second)? ShowPair(
        Window? owner,
        string title,
        string firstLabel,
        string firstValue,
        string secondLabel,
        string secondValue,
        string confirmText = "确定")
    {
        var dialog = Create(owner, title, firstLabel, firstValue, confirmText);
        dialog.SecondLabel.Text = secondLabel;
        dialog.SecondBox.Text = secondValue;
        return dialog.ShowDialog() == true
            ? (dialog.FirstValue, dialog.SecondValue)
            : null;
    }

    private static PromptWindow Create(
        Window? owner,
        string title,
        string label,
        string initialValue,
        string confirmText)
    {
        var dialog = new PromptWindow();

        if (owner is not null && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        dialog.TitleText.Text = title;
        dialog.Title = title;
        dialog.FirstLabel.Text = label;
        dialog.FirstBox.Text = initialValue;
        dialog.ConfirmButton.Content = confirmText;
        dialog.Loaded += (_, _) =>
        {
            dialog.FirstBox.Focus();
            dialog.FirstBox.SelectAll();
        };

        return dialog;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void SecondBox_TextChanged(object sender, TextChangedEventArgs e)
    {
    }
}
