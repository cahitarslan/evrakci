using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Evrakci.App.Services;
using Evrakci.Core;

namespace Evrakci.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        CultureInfo.DefaultThreadCurrentCulture = tr;
        CultureInfo.DefaultThreadCurrentUICulture = tr;
        Thread.CurrentThread.CurrentCulture = tr;
        Thread.CurrentThread.CurrentUICulture = tr;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(tr.IetfLanguageTag)));

        // Brand typeface everywhere: set on every window and popup root as it appears, so all text inside inherits it
        // (a type's default value would not be inherited). Explicit fonts (icons, document previews) still win.
        var sans = (FontFamily)Resources["BrandSans"];
        void UseBrandFont(object sender, RoutedEventArgs _) => ((FrameworkElement)sender).SetCurrentValue(TextElement.FontFamilyProperty, sans);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(UseBrandFont));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => ThemeService.UseBrandBackground((Window)s)));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(UseBrandFont));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(UseBrandFont));

        DispatcherUnhandledException += OnUnhandled;
        TempFiles.CleanupStale();
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Length > 0) window.OpenFromCommandLine(e.Args[0]);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TempFiles.CleanupSession();
        base.OnExit(e);
    }

    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Never show raw exception messages: they may contain document text or values.
        var message = e.Exception is EvrakciException ex
            ? ex.Message
            : $"Beklenmeyen bir hata oluştu ({e.Exception.GetType().Name}). İşleminiz yarıda kalmış olabilir; verilerinizi kaydedip uygulamayı yeniden başlatın.";
        e.Handled = true;
        _ = Dialogs.Error(message);
    }
}
