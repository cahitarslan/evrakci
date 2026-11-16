using Velopack;

namespace Evrakci.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack runs the app's install, update and uninstall hooks here and exits when it handled one.
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
