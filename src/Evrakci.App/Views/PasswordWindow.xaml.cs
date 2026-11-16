using System.Windows;
using Wpf.Ui.Controls;

namespace Evrakci.App.Views;

public partial class PasswordWindow : FluentWindow
{
    readonly bool _forSave;
    string? _result;

    PasswordWindow(bool forSave, string? fileName)
    {
        _forSave = forSave;
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        Bar.Title = forSave ? "Dışa aktar" : "Şifreli dosya";
        Heading.Text = forSave ? "Dışa aktarılan kopyaya şifre koyulsun mu?" : "Şifreyi girin";
        Explain.Text = forSave
            ? "Dosya TCKN, IBAN gibi kişisel bilgiler içerir. Şifre koyarsanız dosya şifrelenir (AES-256). Şifreyi unutursanız dosya bir daha açılamaz."
            : $"“{fileName}” şifreli. Açmak için kaydederken koyduğunuz şifreyi girin.";
        RepeatPanel.Visibility = forSave ? Visibility.Visible : Visibility.Collapsed;
        NoPassword.Visibility = forSave ? Visibility.Visible : Visibility.Collapsed;
        Ok.Content = forSave ? "Şifreyle kaydet" : "Aç";
        Loaded += (_, _) => First.Focus();
        Validate();
    }

    /// <summary>Null: cancelled. Empty: save without password.</summary>
    public static string? AskToSave()
    {
        var w = new PasswordWindow(true, null);
        return w.ShowDialog() == true ? w._result : null;
    }

    public static string? AskToOpen(string fileName)
    {
        var w = new PasswordWindow(false, fileName);
        return w.ShowDialog() == true ? w._result : null;
    }

    void OnChanged(object sender, RoutedEventArgs e) => Validate();

    void Validate()
    {
        string? problem = null;
        var a = First.Password ?? "";
        if (_forSave)
        {
            if (a.Length > 0 && a.Length < 6) problem = "Şifre en az 6 karakter olmalı.";
            else if (a.Length > 0 && a != (Second.Password ?? "")) problem = "Şifreler aynı değil.";
        }
        Problem.Text = problem ?? "";
        Ok.IsEnabled = a.Length > 0 && problem is null;
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        _result = First.Password;
        DialogResult = true;
    }

    void OnNoPassword(object sender, RoutedEventArgs e)
    {
        _result = "";
        DialogResult = true;
    }

    void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
