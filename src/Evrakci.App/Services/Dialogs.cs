using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace Evrakci.App.Services;

public enum Choice { Primary, Secondary, Cancel }

public static class Dialogs
{
    public static async Task Error(string message, string title = "Bir sorun oluştu") =>
        await Show(title, message, "Tamam", null, null, ControlAppearance.Primary);

    public static async Task Info(string message, string title = "Bilgi") =>
        await Show(title, message, "Tamam", null, null, ControlAppearance.Primary);

    public static async Task<bool> Confirm(string message, string title, string yes = "Evet", string no = "Vazgeç",
        bool danger = false) =>
        await Show(title, message, yes, null, no, danger ? ControlAppearance.Danger : ControlAppearance.Primary) == Choice.Primary;

    public static Task<Choice> Ask(string message, string title, string primary, string secondary, string cancel = "Vazgeç") =>
        Show(title, message, primary, secondary, cancel, ControlAppearance.Primary);

    static async Task<Choice> Show(string title, string message, string primary, string? secondary, string? close,
        ControlAppearance appearance)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            PrimaryButtonText = primary,
            PrimaryButtonAppearance = appearance,
            IsSecondaryButtonEnabled = secondary is not null,
            SecondaryButtonText = secondary ?? "",
            IsCloseButtonEnabled = close is not null,
            CloseButtonText = close ?? "",
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
                    ?? (Application.Current?.MainWindow is { IsVisible: true } w ? w : null),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var result = await box.ShowDialogAsync();
        return result switch
        {
            MessageBoxResult.Primary => Choice.Primary,
            MessageBoxResult.Secondary => Choice.Secondary,
            _ => Choice.Cancel,
        };
    }
}
